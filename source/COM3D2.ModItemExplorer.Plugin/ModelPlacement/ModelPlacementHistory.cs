using System;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.ModItemExplorer.Plugin
{
    /// <summary>
    /// 自前配置モデルの操作を SceneEditor の操作履歴へ積む。
    /// 状態のスナップショットと復元はプリセットと同じ経路（ModelPlacementPresetItem）を使う。
    /// SceneEditor が無い環境では HistoryClient 側で無視される
    /// </summary>
    public class ModelPlacementHistory
    {
        /// <summary>Transform の変化とみなす最小差。GUI の数値入力の丸め誤差を拾わない程度に取る</summary>
        private const float TransformEpsilon = 1e-4f;

        private readonly SelfModelPlacer _placer;

        /// <summary>
        /// 一括復元や undo/redo の適用中に個別操作を再登録しないための抑止フラグ。
        /// ホストは undo/redo 中の Register を受け付けないため、ここで止めておく
        /// </summary>
        private bool _suppressed;

        /// <summary>
        /// 配置全体の世代。プリセット読込などで全モデルを入れ替えるたびに進める。
        /// 生成/削除エントリはこの値を canApply で照合し、世代をまたいだ適用を防ぐ
        /// （入れ替え前のモデルを現在の配置へ復活させてしまうため）
        /// </summary>
        private int _generation;

        /// <summary>
        /// モデルごとの「最後に履歴へ積んだ状態」。ドラッグ中はここを更新せずに
        /// 差分を溜め込み、マウス解放時に 1 件へまとめて登録する
        /// </summary>
        private readonly Dictionary<StudioModelStatWrapper, ModelPlacementPresetItem> _baselines
            = new Dictionary<StudioModelStatWrapper, ModelPlacementPresetItem>();

        /// <summary>前フレームの編集モード。モード復帰時に基準を取り直すために持つ</summary>
        private bool _wasEditing;

        public ModelPlacementHistory(SelfModelPlacer placer)
        {
            _placer = placer;
        }

        /// <summary>履歴登録を止めて処理を実行する（一括復元・undo/redo の適用用）</summary>
        public void RunSuppressed(Action action)
        {
            var previous = _suppressed;
            _suppressed = true;
            try
            {
                action();
            }
            finally
            {
                _suppressed = previous;
            }
        }

        /// <summary>
        /// 履歴に積む価値のある操作なら現在状態を控えて返す。
        /// 抑止中・履歴機能なし・破棄済みモデルなら null（呼び出し側は登録を諦める）
        /// </summary>
        public ModelPlacementPresetItem TryCaptureState(StudioModelStatWrapper model)
        {
            if (_suppressed || !HistoryClient.isAvailable)
            {
                return null;
            }

            return _placer.BuildPresetItem(model);
        }

        /// <summary>モデルの現在状態を基準として控え直す（以後の差分検出の起点になる）</summary>
        public void Rebase(StudioModelStatWrapper model)
        {
            if (model == null)
            {
                return;
            }

            var state = _placer.BuildPresetItem(model);
            if (state == null)
            {
                _baselines.Remove(model);
                return;
            }

            _baselines[model] = state;
        }

        public void Forget(StudioModelStatWrapper model)
        {
            if (model != null)
            {
                _baselines.Remove(model);
            }
        }

        /// <summary>
        /// 配置全体を入れ替えたことを通知する。世代が進むため、
        /// これ以前に積んだ生成/削除エントリは canApply が false になって適用されなくなる
        /// </summary>
        public void InvalidateAll()
        {
            _generation++;
            _baselines.Clear();
        }

        /// <summary>
        /// 毎フレーム呼ぶ。ギズモ・スライダー・数値入力いずれの Transform 変更も、
        /// 操作が確定した（マウスを離した）時点で 1 件の履歴にまとめる
        /// </summary>
        public void UpdateTransformAggregation(
            List<StudioModelStatWrapper> models, bool isEditing)
        {
            // 編集モード外はギズモも編集 UI も動かないため、毎フレームの状態生成ごと省く。
            // 復帰時は基準を取り直し、モード外での変化（履歴・プリセット適用分）を拾わない
            if (!isEditing)
            {
                _wasEditing = false;
                return;
            }

            if (!_wasEditing)
            {
                _wasEditing = true;
                RebaseAll(models);
                return;
            }

            // ここで HistoryClient.isAvailable は見ない。
            // 未接続時は毎回アセンブリを走査するため、毎フレーム呼ぶと逆に高くつく
            // （登録自体は HistoryClient.Register 側で無視される）

            // ドラッグ中は確定していないため、差分を溜めたまま次フレームへ持ち越す
            var settled = !Input.GetMouseButton(0) && !ModelGizmoManager.instance.isDragging;

            foreach (var model in models)
            {
                var current = _placer.BuildPresetItem(model);
                if (current == null)
                {
                    continue;
                }

                ModelPlacementPresetItem baseline;
                if (!_baselines.TryGetValue(model, out baseline))
                {
                    _baselines[model] = current;
                    continue;
                }

                string changeLabel;
                if (!TryBuildTransformChangeLabel(baseline, current, out changeLabel) || !settled)
                {
                    continue;
                }

                RegisterTransform(model, baseline, current, changeLabel);
                _baselines[model] = current;
            }

            RemoveStaleBaselines(models);
        }

        private void RebaseAll(List<StudioModelStatWrapper> models)
        {
            _baselines.Clear();
            foreach (var model in models)
            {
                Rebase(model);
            }
        }

        /// <summary>
        /// 一覧から消えたモデルの基準を捨てる。undo/redo の再生成では件数が変わらないまま
        /// インスタンスだけ入れ替わるため、件数比較では判定できない
        /// </summary>
        private void RemoveStaleBaselines(List<StudioModelStatWrapper> models)
        {
            List<StudioModelStatWrapper> stale = null;
            foreach (var model in _baselines.Keys)
            {
                if (!models.Contains(model))
                {
                    if (stale == null)
                    {
                        stale = new List<StudioModelStatWrapper>();
                    }
                    stale.Add(model);
                }
            }

            if (stale == null)
            {
                return;
            }

            foreach (var model in stale)
            {
                _baselines.Remove(model);
            }
        }

        /// <summary>
        /// 配置したモデルを履歴に積む。state は TryCaptureState で控えたもの
        /// </summary>
        public void RegisterCreate(StudioModelStatWrapper model, ModelPlacementPresetItem state)
        {
            if (state == null)
            {
                return;
            }

            _baselines[model] = state;
            RegisterExistence("モデル配置: " + ResolveDisplayName(state, model?.displayName), state, model);
        }

        /// <summary>
        /// state と fallbackName は削除前に控えておくこと
        /// （破棄後は BuildPresetItem も displayName も読めないため）
        /// </summary>
        public void RegisterDelete(ModelPlacementPresetItem state, string fallbackName)
        {
            if (state == null)
            {
                return;
            }

            RegisterExistence("モデル削除: " + ResolveDisplayName(state, fallbackName), state, null);
        }

        /// <summary>
        /// 生成/削除の履歴。undo/redo で再生成するたびにラッパーが変わるため、
        /// クロージャ間で共有するローカル変数に現在のインスタンスを持たせる
        /// </summary>
        private void RegisterExistence(
            string description, ModelPlacementPresetItem state, StudioModelStatWrapper model)
        {
            // null は「今は存在しない」。create/delete 両方から書き換える
            var current = model;
            var generation = _generation;

            Action create = () =>
            {
                if (current != null)
                {
                    return;
                }

                RunSuppressed(() =>
                {
                    current = _placer.RestoreModel(state);
                    Rebase(current);
                });
                ModItemManager.instance.UpdateModelItems();
            };

            Action delete = () =>
            {
                if (current == null)
                {
                    return;
                }

                RunSuppressed(() =>
                {
                    Forget(current);
                    _placer.DeleteModel(current);
                    current = null;
                });
                ModItemManager.instance.UpdateModelItems();
            };

            var created = model != null;
            HistoryClient.Register(
                description,
                created ? delete : create,
                created ? create : delete,
                () => generation == _generation);
        }

        /// <summary>
        /// 表示状態の変化を 1 件登録する（呼び出し元が無変化時は呼ばない）
        /// </summary>
        public void RegisterVisible(
            StudioModelStatWrapper model, ModelPlacementPresetItem state, bool visible)
        {
            if (state == null)
            {
                return;
            }

            _baselines[model] = state;

            var description = (visible ? "モデル表示: " : "モデル非表示: ")
                + ResolveDisplayName(state, model?.displayName);
            HistoryClient.Register(
                description,
                () => RunSuppressed(() => _placer.SetVisible(model, !visible)),
                () => RunSuppressed(() => _placer.SetVisible(model, visible)),
                () => IsAlive(model));
        }

        /// <summary>
        /// アタッチ先の変更。Attach は位置・回転もリセットするため、
        /// before/after とも Transform ごと復元する
        /// </summary>
        public void RegisterAttach(StudioModelStatWrapper model, ModelPlacementPresetItem before)
        {
            if (before == null)
            {
                return;
            }

            var after = _placer.BuildPresetItem(model);
            if (after == null)
            {
                return;
            }

            _baselines[model] = after;

            var description = "モデルアタッチ: " + ResolveDisplayName(after, model?.displayName)
                + " → " + GetAttachLabel(after);
            HistoryClient.Register(
                description,
                () => ApplyAttachState(model, before),
                () => ApplyAttachState(model, after),
                () => IsAlive(model));
        }

        private void ApplyAttachState(StudioModelStatWrapper model, ModelPlacementPresetItem state)
        {
            RunSuppressed(() =>
            {
                _placer.RestoreAttachState(model, state);
                _placer.ApplyTransform(model, state);
                Rebase(model);
            });
        }

        private void RegisterTransform(
            StudioModelStatWrapper model,
            ModelPlacementPresetItem before,
            ModelPlacementPresetItem after,
            string changeLabel)
        {
            var description = "モデル" + changeLabel + ": " + ResolveDisplayName(after, model?.displayName);
            HistoryClient.Register(
                description,
                () => ApplyTransformState(model, before),
                () => ApplyTransformState(model, after),
                () => IsAlive(model));
        }

        private void ApplyTransformState(StudioModelStatWrapper model, ModelPlacementPresetItem state)
        {
            RunSuppressed(() =>
            {
                _placer.ApplyTransform(model, state);
                // 戻した状態を基準にし直さないと、次フレームの差分検出が逆方向の履歴を積んでしまう
                Rebase(model);
            });
        }

        /// <summary>
        /// Transform に変化があれば true を返し、label に変化した成分名を組み立てる
        /// </summary>
        private static bool TryBuildTransformChangeLabel(
            ModelPlacementPresetItem a, ModelPlacementPresetItem b, out string label)
        {
            var changes = new List<string>(3);
            if (!IsSame(a.posX, b.posX) || !IsSame(a.posY, b.posY) || !IsSame(a.posZ, b.posZ))
            {
                changes.Add("移動");
            }
            if (!IsSame(a.rotX, b.rotX) || !IsSame(a.rotY, b.rotY) || !IsSame(a.rotZ, b.rotZ))
            {
                changes.Add("回転");
            }
            if (!IsSame(a.sclX, b.sclX) || !IsSame(a.sclY, b.sclY) || !IsSame(a.sclZ, b.sclZ))
            {
                changes.Add("拡縮");
            }

            label = string.Join("・", changes.ToArray());
            return changes.Count > 0;
        }

        private static bool IsSame(float a, float b)
        {
            return Mathf.Abs(a - b) < TransformEpsilon;
        }

        /// <summary>履歴エントリの canApply 用。破棄済みモデルへの適用を飛ばす</summary>
        private static bool IsAlive(StudioModelStatWrapper model)
        {
            return model?.obj as GameObject != null;
        }

        /// <summary>アイテム名を優先し、menu が引けないときはモデル名にフォールバックする</summary>
        private static string ResolveDisplayName(ModelPlacementPresetItem state, string fallbackName)
        {
            var menu = ModItemManager.instance.GetMenu(state.fileName);
            if (menu != null && !string.IsNullOrEmpty(menu.name))
            {
                return menu.name;
            }
            return !string.IsNullOrEmpty(fallbackName) ? fallbackName : state.fileName;
        }

        private static string GetAttachLabel(ModelPlacementPresetItem state)
        {
            var point = SelfModelPlacer.AttachPoints.Find(p => p.boneName == state.attachBoneName);
            return point != null ? point.displayName : "なし";
        }
    }
}
