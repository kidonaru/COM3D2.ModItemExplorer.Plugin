using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.ModItemExplorer.Plugin
{
    /// <summary>
    /// MotionTimelineEditor に頼らず、プラグイン単体で .menu アイテムをシーンに配置する。
    /// モデルはラッパー GameObject でくるんでギズモを付け、共通の配置親の下にぶら下げる。
    /// </summary>
    public class SelfModelPlacer
    {
        /// <summary>配置プラグイン名。UI のコンボボックスと振り分けの両方で使う</summary>
        public const string PluginName = "ModItemExplorer";

        private const string ParentObjectName = "ModItemExplorer Model Parent";

        /// <summary>ギズモの大きさ。既定倍率では配置モデルに対して大きすぎるため縮めている</summary>
        private const float GizmoScale = 0.5f;

        /// <summary>選択中モデルのハイライト色。元色との間を往復させる</summary>
        private static readonly Color HighlightColor = new Color(0.4f, 1f, 0.4f, 1f);

        /// <summary>ハイライトの明滅周期(秒)</summary>
        private const float HighlightCycle = 1.2f;

        private static SelfModelPlacer _instance = null;
        public static SelfModelPlacer instance
            => _instance ?? (_instance = new SelfModelPlacer());

        private readonly List<StudioModelStatWrapper> _models = new List<StudioModelStatWrapper>();

        // Mesh / Material は GameObject を Destroy しても解放されないため、モデルごとに追跡して明示破棄する
        private readonly Dictionary<StudioModelStatWrapper, List<UnityEngine.Object>> _disposables
            = new Dictionary<StudioModelStatWrapper, List<UnityEngine.Object>>();

        private GameObject _parentGo = null;

        /// <summary>アタッチ先ボーンの定義</summary>
        public class AttachPoint
        {
            public string displayName;

            /// <summary>アタッチ先のボーン名。null は「なし」（ワールド配置）</summary>
            public string boneName;
        }

        /// <summary>アタッチ中のモデルの状態。復元用にスロット番号とボーン名を持つ</summary>
        public class AttachState
        {
            public int maidSlotNo;
            public string boneName;
        }

        /// <summary>
        /// 定番のアタッチポイント。ボーン名は COM3D2.5 実機で GetBone が解決することを確認済み
        /// </summary>
        public static readonly List<AttachPoint> AttachPoints = new List<AttachPoint>
        {
            new AttachPoint { displayName = "なし", boneName = null },
            new AttachPoint { displayName = "頭", boneName = "Bip01 Head" },
            new AttachPoint { displayName = "首", boneName = "Bip01 Neck" },
            new AttachPoint { displayName = "胸", boneName = "Bip01 Spine1a" },
            new AttachPoint { displayName = "骨盤", boneName = "Bip01 Pelvis" },
            new AttachPoint { displayName = "左肩", boneName = "Bip01 L UpperArm" },
            new AttachPoint { displayName = "右肩", boneName = "Bip01 R UpperArm" },
            new AttachPoint { displayName = "左肘", boneName = "Bip01 L Forearm" },
            new AttachPoint { displayName = "右肘", boneName = "Bip01 R Forearm" },
            new AttachPoint { displayName = "左手", boneName = "Bip01 L Hand" },
            new AttachPoint { displayName = "右手", boneName = "Bip01 R Hand" },
            new AttachPoint { displayName = "左腿", boneName = "Bip01 L Thigh" },
            new AttachPoint { displayName = "右腿", boneName = "Bip01 R Thigh" },
            new AttachPoint { displayName = "左膝", boneName = "Bip01 L Calf" },
            new AttachPoint { displayName = "右膝", boneName = "Bip01 R Calf" },
            new AttachPoint { displayName = "左足", boneName = "Bip01 L Foot" },
            new AttachPoint { displayName = "右足", boneName = "Bip01 R Foot" },
        };

        private readonly Dictionary<StudioModelStatWrapper, AttachState> _attachStates
            = new Dictionary<StudioModelStatWrapper, AttachState>();

        /// <summary>
        /// モデルごとのオイラー角キャッシュ。
        /// ギズモは回転をローカル軸で右から合成するため、Unity のオイラー合成順(Z→X→Y)では
        /// Z 軸ハンドル以外の操作で全成分が変動して見える。そこで1フレーム分の回転差分を
        /// 軸単位でオイラー角に足し込み、その値を正として書き戻すことで
        /// 「X ハンドルの操作は X の数値だけを動かす」挙動にする
        /// </summary>
        private class RotationCache
        {
            public Quaternion rotation;
            public Vector3 eulerAngles;
        }

        private readonly Dictionary<StudioModelStatWrapper, RotationCache> _rotationCaches
            = new Dictionary<StudioModelStatWrapper, RotationCache>();

        /// <summary>ハイライト中のマテリアルと、書き戻し用の元の色</summary>
        private class HighlightTarget
        {
            public Material material;
            public Color originalColor;
        }

        private readonly List<HighlightTarget> _highlightTargets = new List<HighlightTarget>();

        /// <summary>ギズモの操作種別。None はギズモ自体を隠す</summary>
        public enum GizmoDragType
        {
            None,
            Move,
            Rotate,
            Scale,
        }

        private GizmoDragType _dragType = GizmoDragType.Move;

        /// <summary>
        /// ギズモの操作種別。誤操作防止のため移動/回転/拡縮は排他で1つだけ有効にする
        /// </summary>
        public GizmoDragType dragType
        {
            get => _dragType;
            set
            {
                if (_dragType == value)
                {
                    return;
                }

                _dragType = value;
                ApplyDragType();
            }
        }

        private StudioModelStatWrapper _selectedModel = null;

        /// <summary>
        /// 操作対象として選択中のモデル。破棄済みなら null に戻す。
        /// UI 側（ModelOperationWindow）はこの値を参照するだけにして、選択の実体をここに一元化する
        /// </summary>
        public StudioModelStatWrapper selectedModel
        {
            get
            {
                var go = _selectedModel?.obj as GameObject;
                if (go == null)
                {
                    _selectedModel = null;
                }
                return _selectedModel;
            }
            set
            {
                // 他プラグイン配置分は対象外。ハイライトでマテリアルを書き換えてしまうため弾く
                if (value != null && !Owns(value))
                {
                    return;
                }

                // 破棄済み判定のある getter ではなくフィールドと比べる。
                // 破棄済みモデルから null への切替もハイライト解除として通す必要があるため
                if (_selectedModel == value)
                {
                    return;
                }

                _selectedModel = value;
                RefreshHighlight();
            }
        }

        private bool _isModelEditMode = false;

        /// <summary>
        /// モデル編集モード中か。ModelOperationWindow が毎フレーム代入する。
        /// ギズモの表示条件であり、ハイライトの有効条件も兼ねる。
        /// 操作ウィンドウの開閉とは独立させる（閉じていてもギズモは操作できるべきなので）
        /// </summary>
        public bool isModelEditMode
        {
            get => _isModelEditMode;
            set
            {
                if (_isModelEditMode == value)
                {
                    return;
                }

                _isModelEditMode = value;
                ApplyDragType();
                RefreshHighlight();
            }
        }

        /// <summary>
        /// 選択モデル配下の _Color を持つマテリアルを記録する。
        /// マテリアルはモデルごとに生成されるため、書き換えても他モデルには波及しない
        /// </summary>
        private void BeginHighlight(StudioModelStatWrapper model)
        {
            var go = model?.obj as GameObject;
            if (go == null)
            {
                return;
            }

            foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
            {
                // materials は複製を作ってしまうため sharedMaterials を使う
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material == null || !material.HasProperty("_Color"))
                    {
                        continue;
                    }

                    _highlightTargets.Add(new HighlightTarget
                    {
                        material = material,
                        originalColor = material.GetColor("_Color"),
                    });
                }
            }
        }

        /// <summary>
        /// 記録した元の色を書き戻す。モデル破棄が先行してマテリアルが消えている場合はスキップする
        /// </summary>
        private void EndHighlight()
        {
            foreach (var target in _highlightTargets)
            {
                if (target.material != null)
                {
                    target.material.SetColor("_Color", target.originalColor);
                }
            }

            _highlightTargets.Clear();
        }

        /// <summary>
        /// 選択状態と編集モードからハイライト対象を取り直す。
        /// 旧対象の色は必ず書き戻してから張り直すので、解除漏れの経路を作らない
        /// </summary>
        private void RefreshHighlight()
        {
            EndHighlight();

            if (_isModelEditMode)
            {
                BeginHighlight(selectedModel);
            }
        }

        /// <summary>
        /// ハイライト色を毎フレーム更新する。
        /// アルファは元の値のままにする（Lighted_Trans で透明度が明滅するのを避けるため）
        /// </summary>
        private void UpdateHighlight()
        {
            if (_highlightTargets.Count == 0)
            {
                return;
            }

            var t = (Mathf.Sin(Time.time * Mathf.PI * 2f / HighlightCycle) + 1f) * 0.5f;

            foreach (var target in _highlightTargets)
            {
                if (target.material == null)
                {
                    continue;
                }

                var color = Color.Lerp(target.originalColor, HighlightColor, t);
                color.a = target.originalColor.a;
                target.material.SetColor("_Color", color);
            }
        }

        /// <summary>
        /// 配置中のモデル一覧。呼び出し側の走査中に増減しても壊れないようコピーを返す。
        /// 要素の Wrapper は配置中ずっと同一インスタンスであること（ツリー側が参照比較で追従するため）
        /// </summary>
        public List<StudioModelStatWrapper> modelList
            => new List<StudioModelStatWrapper>(_models);

        /// <summary>
        /// .menu アイテムをシーンに配置し、生成したモデルを返す（失敗時は null）。
        /// group は呼び出し側の採番をヒントとして受け取るが、名前の一意性は内部で採り直して保証する
        /// </summary>
        public StudioModelStatWrapper CreateModel(string fileName, int group, bool visible)
        {
            GameObject modelGo = null;
            GameObject wrapperGo = null;
            var disposables = new List<UnityEngine.Object>();

            try
            {
                var script = ModelMenuScript.Load(fileName);
                if (script == null || string.IsNullOrEmpty(script.modelFileName))
                {
                    MTEUtils.LogWarning("menuの解析に失敗しました。{0}", fileName);
                    return null;
                }

                modelGo = ModelMeshLoader.LoadMesh(script.modelFileName, GetModelLayer(), disposables);
                if (modelGo == null)
                {
                    DestroyAll(disposables);
                    return null;
                }

                ApplyMenuChanges(modelGo, script, disposables);

                // ギズモ操作でモデル内部の Transform を壊さないよう、ラッパー越しに動かす
                var resolvedGroup = ResolveGroup(fileName, group);
                var modelName = GetModelName(fileName, resolvedGroup);
                wrapperGo = new GameObject(modelName);
                wrapperGo.transform.SetParent(GetOrCreateParent().transform, false);
                wrapperGo.transform.position = GetDefaultPosition();
                modelGo.transform.SetParent(wrapperGo.transform, false);
                modelGo.transform.localPosition = Vector3.zero;
                modelGo.transform.localRotation = Quaternion.identity;
                modelGo.transform.localScale = Vector3.one;

                AddGizmo(wrapperGo);
                wrapperGo.SetActive(visible);

                var wrapper = new StudioModelStatWrapper
                {
                    original = null,
                    group = resolvedGroup,
                    name = modelName,
                    displayName = modelName,
                    obj = wrapperGo,
                    pluginName = PluginName,
                    visible = visible,
                    infoWrapper = new OfficialObjectInfoWrapper
                    {
                        fileName = fileName,
                        label = fileName,
                    },
                };

                _models.Add(wrapper);
                _disposables[wrapper] = disposables;

                // 配置直後は操作対象にする（3D 上のハイライトと一覧の選択表示を一致させる）
                selectedModel = wrapper;

                return wrapper;
            }
            catch (Exception e)
            {
                MTEUtils.LogWarning("モデルの配置に失敗しました。{0}", fileName);
                MTEUtils.LogException(e);

                // 登録前に失敗した分は誰からも参照されず削除もできなくなるため、ここで片付ける
                if (wrapperGo != null)
                {
                    UnityEngine.Object.Destroy(wrapperGo);
                }
                else if (modelGo != null)
                {
                    UnityEngine.Object.Destroy(modelGo);
                }
                DestroyAll(disposables);

                return null;
            }
        }

        /// <summary>
        /// 毎フレーム呼ぶ。ギズモ操作による回転をオイラー角キャッシュに軸単位で足し込み、
        /// 正規化した回転を書き戻す
        /// </summary>
        public void Update()
        {
            foreach (var model in _models)
            {
                var go = model.obj as GameObject;
                if (go == null)
                {
                    continue;
                }

                var t = go.transform;
                var cache = GetOrCreateRotationCache(model, t);

                // 前フレームから変わっていなければ何もしない（== は近似比較）
                if (t.localRotation == cache.rotation)
                {
                    continue;
                }

                // ギズモの軸ハンドルは1フレームでは単一ローカル軸の微小回転を右から掛けるため、
                // ローカル差分のオイラー角はほぼ該当軸成分のみになる。これを足し込むことで
                // ±90°を跨いでも数値が飛ばない連続的なオイラー角が得られる
                var delta = (Quaternion.Inverse(cache.rotation) * t.localRotation).eulerAngles;
                cache.eulerAngles += NormalizeEuler(delta);
                cache.rotation = Quaternion.Euler(cache.eulerAngles);
                t.localRotation = cache.rotation;
            }

            UpdateHighlight();
        }

        /// <summary>モデルのオイラー角（キャッシュ値）。UI 表示・編集はこれを使う</summary>
        public Vector3 GetEulerAngles(StudioModelStatWrapper model)
        {
            var go = model?.obj as GameObject;
            if (go == null)
            {
                return Vector3.zero;
            }
            return GetOrCreateRotationCache(model, go.transform).eulerAngles;
        }

        /// <summary>モデルのオイラー角を設定し、Transform に反映する</summary>
        public void SetEulerAngles(StudioModelStatWrapper model, Vector3 eulerAngles)
        {
            var go = model?.obj as GameObject;
            if (go == null)
            {
                return;
            }

            var cache = GetOrCreateRotationCache(model, go.transform);
            cache.eulerAngles = eulerAngles;
            cache.rotation = Quaternion.Euler(eulerAngles);
            go.transform.localRotation = cache.rotation;
        }

        private RotationCache GetOrCreateRotationCache(StudioModelStatWrapper model, Transform t)
        {
            RotationCache cache;
            if (!_rotationCaches.TryGetValue(model, out cache))
            {
                cache = new RotationCache
                {
                    rotation = t.localRotation,
                    eulerAngles = t.localEulerAngles,
                };
                _rotationCaches[model] = cache;
            }
            return cache;
        }

        /// <summary>各成分を -180〜180 に正規化する</summary>
        private static Vector3 NormalizeEuler(Vector3 euler)
        {
            euler.x = Mathf.DeltaAngle(0f, euler.x);
            euler.y = Mathf.DeltaAngle(0f, euler.y);
            euler.z = Mathf.DeltaAngle(0f, euler.z);
            return euler;
        }

        /// <summary>
        /// モデルのアタッチ状態を返す。未アタッチなら null
        /// </summary>
        public AttachState GetAttachState(StudioModelStatWrapper model)
        {
            AttachState state;
            return model != null && _attachStates.TryGetValue(model, out state) ? state : null;
        }

        /// <summary>
        /// モデルをメイドのボーンにアタッチする。point.boneName が null ならワールドに戻す。
        /// 切替時はローカル位置・回転をリセットしてボーン直上に置く
        /// </summary>
        public void Attach(StudioModelStatWrapper model, Maid maid, AttachPoint point)
        {
            if (!Owns(model))
            {
                return;
            }

            var go = model.obj as GameObject;
            if (go == null)
            {
                return;
            }

            Transform parent;
            if (point != null && point.boneName != null)
            {
                var bone = maid != null ? maid.body0.GetBone(point.boneName) : null;
                if (bone == null)
                {
                    MTEUtils.LogWarning("アタッチ先ボーンが見つかりません。{0}", point.boneName);
                    return;
                }

                parent = bone;
                _attachStates[model] = new AttachState
                {
                    maidSlotNo = maid.ActiveSlotNo,
                    boneName = point.boneName,
                };
            }
            else
            {
                parent = GetOrCreateParent().transform;
                _attachStates.Remove(model);
            }

            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            // 拡縮はアタッチ後も見た目を保ちたいため維持する
            // 回転はキャッシュ経由でリセットし、UI 表示との整合を保つ
            SetEulerAngles(model, Vector3.zero);
        }

        /// <summary>
        /// 自前で配置したモデルかどうか
        /// </summary>
        public bool Owns(StudioModelStatWrapper model)
        {
            return model != null && model.pluginName == PluginName;
        }

        /// <summary>
        /// 配置モデルの表示状態を切り替える。自前配置分でなければ何もしない
        /// （MTE 側モデルの visible は MTE の管轄のため触らない）
        /// </summary>
        public void SetVisible(StudioModelStatWrapper model, bool visible)
        {
            if (!Owns(model))
            {
                return;
            }

            model.visible = visible;

            var go = model.obj as GameObject;
            if (go != null)
            {
                go.SetActive(visible);
            }
        }

        /// <summary>
        /// 配置したモデルを破棄する。自前配置分でなければ何もしない
        /// </summary>
        public void DeleteModel(StudioModelStatWrapper model)
        {
            if (!Owns(model))
            {
                return;
            }

            if (selectedModel == model)
            {
                selectedModel = null;
            }

            try
            {
                var go = model.obj as GameObject;
                if (go != null)
                {
                    UnityEngine.Object.Destroy(go);
                }

                List<UnityEngine.Object> disposables;
                if (_disposables.TryGetValue(model, out disposables))
                {
                    DestroyAll(disposables);
                    _disposables.Remove(model);
                }
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }

            _models.Remove(model);
            _attachStates.Remove(model);
            _rotationCaches.Remove(model);
        }

        /// <summary>
        /// 配置したモデルを全て破棄する。シーンをまたぐと参照が無効になるため遷移時に呼ぶ
        /// </summary>
        public void DeleteAll()
        {
            selectedModel = null;

            foreach (var model in modelList)
            {
                DeleteModel(model);
            }

            _models.Clear();
            _disposables.Clear();
            _attachStates.Clear();
            _rotationCaches.Clear();

            if (_parentGo != null)
            {
                UnityEngine.Object.Destroy(_parentGo);
            }
            _parentGo = null;
        }

        private static string GetPresetPath(string name)
            => MTEUtils.CombinePaths(PluginUtils.ModelPresetDirPath, name + ".xml");

        /// <summary>ファイル名に使えない文字を除去する</summary>
        private static string SanitizePresetName(string name)
        {
            if (name == null)
            {
                return string.Empty;
            }

            foreach (var c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c.ToString(), "");
            }

            return name.Trim();
        }

        /// <summary>
        /// 保存済みプリセット名の一覧（拡張子なし・名前順）
        /// </summary>
        public List<string> GetPresetNames()
        {
            try
            {
                var names = new List<string>();
                foreach (var path in Directory.GetFiles(PluginUtils.ModelPresetDirPath, "*.xml"))
                {
                    names.Add(Path.GetFileNameWithoutExtension(path));
                }

                names.Sort();
                return names;
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
                return new List<string>();
            }
        }

        /// <summary>
        /// 名前付きプリセットを削除する
        /// </summary>
        public void DeletePreset(string name)
        {
            try
            {
                var safeName = SanitizePresetName(name);
                if (safeName.Length == 0)
                {
                    return;
                }

                var path = GetPresetPath(safeName);
                if (File.Exists(path))
                {
                    File.Delete(path);
                    MTEUtils.Log("配置プリセットを削除しました。{0}", safeName);
                }
            }
            catch (Exception e)
            {
                MTEUtils.LogWarning("配置プリセットの削除に失敗しました");
                MTEUtils.LogException(e);
            }
        }

        /// <summary>
        /// 自前配置分の配置内容を名前付きプリセットとして保存する
        /// </summary>
        public void SavePreset(string name)
        {
            var safeName = SanitizePresetName(name);
            if (safeName.Length == 0)
            {
                MTEUtils.LogWarning("プリセット名が空です");
                return;
            }

            var path = GetPresetPath(safeName);
            var tempPath = path + ".tmp";

            try
            {
                var preset = new ModelPlacementPreset();

                foreach (var model in _models)
                {
                    var go = model.obj as GameObject;
                    if (go == null)
                    {
                        continue;
                    }

                    var t = go.transform;
                    var attach = GetAttachState(model);
                    // 回転はキャッシュ値を保存し、UI に表示している数値と一致させる
                    var euler = GetEulerAngles(model);
                    preset.items.Add(new ModelPlacementPresetItem
                    {
                        fileName = model.infoWrapper?.fileName,
                        group = model.group,
                        visible = model.visible,
                        // UI・アタッチと揃えるためローカル系で保存する
                        posX = t.localPosition.x, posY = t.localPosition.y, posZ = t.localPosition.z,
                        rotX = euler.x, rotY = euler.y, rotZ = euler.z,
                        sclX = t.localScale.x, sclY = t.localScale.y, sclZ = t.localScale.z,
                        attachMaidSlotNo = attach != null ? attach.maidSlotNo : -1,
                        attachBoneName = attach?.boneName,
                    });
                }

                // 書き込み途中の例外で既存ファイルを壊さないよう、一時ファイル経由で置き換える
                var serializer = new XmlSerializer(typeof(ModelPlacementPreset));
                using (var stream = new FileStream(tempPath, FileMode.Create))
                {
                    serializer.Serialize(stream, preset);
                }

                if (File.Exists(path))
                {
                    File.Delete(path);
                }
                File.Move(tempPath, path);

                MTEUtils.Log("配置プリセットを保存しました。{0} ({1}体)", safeName, preset.items.Count);
            }
            catch (Exception e)
            {
                MTEUtils.LogWarning("配置プリセットの保存に失敗しました");
                MTEUtils.LogException(e);

                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch (Exception e2)
                {
                    MTEUtils.LogException(e2);
                }
            }
        }

        /// <summary>
        /// 名前付きプリセットから配置を復元する。既存の自前配置分は置き換える
        /// </summary>
        public bool LoadPreset(string name)
        {
            try
            {
                var safeName = SanitizePresetName(name);
                var path = GetPresetPath(safeName);
                if (safeName.Length == 0 || !File.Exists(path))
                {
                    MTEUtils.LogWarning("配置プリセットがありません。{0}", path);
                    return false;
                }

                ModelPlacementPreset preset;
                var serializer = new XmlSerializer(typeof(ModelPlacementPreset));
                using (var stream = new FileStream(path, FileMode.Open))
                {
                    preset = (ModelPlacementPreset)serializer.Deserialize(stream);
                }

                DeleteAll();

                var restored = 0;
                foreach (var item in preset.items)
                {
                    // 保存時と同じ生成経路を再実行してから Transform を適用する
                    var wrapper = CreateModel(item.fileName, item.group, item.visible);
                    var go = wrapper?.obj as GameObject;
                    if (go == null)
                    {
                        continue;
                    }

                    RestoreAttach(wrapper, item);

                    // Attach はローカル位置・回転をリセットするため、必ずアタッチの後に適用する
                    var t = go.transform;
                    t.localPosition = new Vector3(item.posX, item.posY, item.posZ);
                    // 回転はキャッシュ経由で適用し、保存した数値がそのまま UI に出るようにする
                    SetEulerAngles(wrapper, new Vector3(item.rotX, item.rotY, item.rotZ));
                    t.localScale = new Vector3(item.sclX, item.sclY, item.sclZ);
                    restored++;
                }

                // 復元直後はどれも選択していない状態にする
                selectedModel = null;

                // 個別失敗はスキップされるため、実際に復元できた数を報告する
                MTEUtils.Log("配置プリセットを復元しました。{0} ({1}/{2}体)", safeName, restored, preset.items.Count);
                return true;
            }
            catch (Exception e)
            {
                MTEUtils.LogWarning("配置プリセットの復元に失敗しました");
                MTEUtils.LogException(e);
                return false;
            }
        }

        /// <summary>
        /// プリセットのアタッチ情報を復元する。メイド不在時はワールド配置のままにする
        /// </summary>
        private void RestoreAttach(StudioModelStatWrapper wrapper, ModelPlacementPresetItem item)
        {
            if (item.attachMaidSlotNo < 0 || string.IsNullOrEmpty(item.attachBoneName))
            {
                return;
            }

            var maid = GameMain.Instance.CharacterMgr.GetMaid(item.attachMaidSlotNo);
            var point = AttachPoints.Find(p => p.boneName == item.attachBoneName);
            if (maid == null || point == null)
            {
                MTEUtils.LogWarning("アタッチ先が見つからないためワールド配置に戻します。{0}", item.attachBoneName);
                return;
            }

            Attach(wrapper, maid, point);
        }

        /// <summary>
        /// menu のマテリアル変更・テクスチャ変更を適用し、生成したリソースを disposables に積む
        /// </summary>
        private static void ApplyMenuChanges(
            GameObject modelGo,
            ModelMenuScript script,
            List<UnityEngine.Object> disposables)
        {
            if (script.materialChanges.Count == 0 && script.textureChanges.Count == 0)
            {
                return;
            }

            foreach (var smr in modelGo.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                // sharedMaterials を使う。materials は複製を作るので追跡対象とインスタンスがずれる
                var materials = smr.sharedMaterials;

                foreach (var change in script.materialChanges)
                {
                    if (change.materialNo < 0 || change.materialNo >= materials.Length)
                    {
                        continue;
                    }

                    // LoadMaterial は欠損時に NDebug.Assert へ落ちるため、事前に存在を確かめる
                    if (!GameUty.FileSystem.IsExistentFile(change.fileName))
                    {
                        MTEUtils.LogWarning("mateファイルが見つかりません。{0}", change.fileName);
                        continue;
                    }

                    var material = ImportCM.LoadMaterial(change.fileName, null);
                    if (material == null)
                    {
                        continue;
                    }

                    materials[change.materialNo] = material;
                    disposables.Add(material);
                }

                foreach (var change in script.textureChanges)
                {
                    if (change.materialNo < 0 || change.materialNo >= materials.Length)
                    {
                        continue;
                    }

                    var material = materials[change.materialNo];
                    if (material == null || !material.HasProperty(change.propName))
                    {
                        continue;
                    }

                    // CreateTexture と違い TryCreateTexture は欠損時に null を返す（Assert に落ちない）
                    var texture = ImportCM.TryCreateTexture(change.fileName);
                    if (texture == null)
                    {
                        MTEUtils.LogWarning("texファイルが読み込めません。{0}", change.fileName);
                        continue;
                    }

                    material.SetTexture(change.propName, texture);
                    disposables.Add(texture);
                }

                smr.sharedMaterials = materials;
            }
        }

        private static void DestroyAll(List<UnityEngine.Object> disposables)
        {
            foreach (var obj in disposables)
            {
                if (obj != null)
                {
                    UnityEngine.Object.Destroy(obj);
                }
            }
            disposables.Clear();
        }

        /// <summary>
        /// 同一 menu 内で未使用の最小 group を返す。
        /// 呼び出し側（ModItemManager.CreateModel）は最初に一致したモデルで打ち切るため
        /// 3個目以降が既存の番号と衝突する。名前の一意性はツリーのキーに直結するのでここで採り直す
        /// </summary>
        private int ResolveGroup(string fileName, int hint)
        {
            var used = new HashSet<int>();
            foreach (var model in _models)
            {
                if (model.infoWrapper?.fileName == fileName)
                {
                    used.Add(model.group);
                }
            }

            if (!used.Contains(hint))
            {
                return hint;
            }

            // 0 の次は 2 から（"名前 (1)" は使わない既存の採番規則に合わせる）
            if (!used.Contains(0))
            {
                return 0;
            }
            for (int group = 2; ; group++)
            {
                if (!used.Contains(group))
                {
                    return group;
                }
            }
        }

        private static string GetModelName(string fileName, int group)
        {
            var baseName = Path.GetFileNameWithoutExtension(fileName);
            return group == 0 ? baseName : baseName + " (" + group + ")";
        }

        /// <summary>
        /// モデルを載せるレイヤー。名前解決に失敗したら Character の既定値にフォールバックする
        /// </summary>
        private static int GetModelLayer()
        {
            var layer = LayerMask.NameToLayer("Character");
            return layer >= 0 ? layer : 10;
        }

        /// <summary>
        /// 配置初期位置を返す。カメラの注視点（CameraMain.GetTargetPos）の真下、床の高さに置く。
        /// 原点固定だと画面外に配置されて見えず、カメラ前方に置くと注視点からずれるため
        /// </summary>
        private static Vector3 GetDefaultPosition()
        {
            try
            {
                var pos = GameMain.Instance.MainCamera.GetTargetPos();
                pos.y = 0f;
                return pos;
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
                return Vector3.zero;
            }
        }

        private GameObject GetOrCreateParent()
        {
            if (_parentGo == null)
            {
                _parentGo = new GameObject(ParentObjectName);
            }
            return _parentGo;
        }

        /// <summary>
        /// 操作ギズモを付ける。種別は dragType に従い排他。
        /// GizmoRenderTarget ではなく GizmoRender 派生の ModelGizmoRender を使う。GizmoRenderTarget の Update は
        /// new で基底を隠蔽していて base.Update() を呼ばないため、ドラッグ判定フラグが立たず操作できない
        /// </summary>
        private void AddGizmo(GameObject target)
        {
            var gizmo = target.AddComponent<ModelGizmoRender>();
            gizmo.offsetScale = GizmoScale;
            ApplyDragType(gizmo);
        }

        /// <summary>
        /// 配置済み全モデルのギズモに現在の操作種別を反映
        /// </summary>
        private void ApplyDragType()
        {
            foreach (var model in _models)
            {
                var go = model.obj as GameObject;
                var gizmo = go != null ? go.GetComponent<GizmoRender>() : null;
                if (gizmo == null)
                {
                    continue;
                }

                ApplyDragType(gizmo);
            }
        }

        private void ApplyDragType(GizmoRender gizmo)
        {
            gizmo.eAxis = _dragType == GizmoDragType.Move;
            gizmo.eRotate = _dragType == GizmoDragType.Rotate;
            gizmo.eScal = _dragType == GizmoDragType.Scale;

            // GizmoRender は Visible=false で描画も操作判定も止まる。
            // 「なし」と編集モード外はこれでまとめて切る
            gizmo.Visible = _isModelEditMode && _dragType != GizmoDragType.None;
        }
    }
}
