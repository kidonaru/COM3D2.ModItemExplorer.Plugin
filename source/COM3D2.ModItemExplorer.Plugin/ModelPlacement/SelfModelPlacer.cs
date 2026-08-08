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
        private const float GizmoScale = 0.25f;

        /// <summary>配置初期位置のカメラからの距離(m)</summary>
        private const float DefaultDistance = 1.5f;

        private static SelfModelPlacer _instance = null;
        public static SelfModelPlacer instance
            => _instance ?? (_instance = new SelfModelPlacer());

        private readonly List<StudioModelStatWrapper> _models = new List<StudioModelStatWrapper>();

        // Mesh / Material は GameObject を Destroy しても解放されないため、モデルごとに追跡して明示破棄する
        private readonly Dictionary<StudioModelStatWrapper, List<UnityEngine.Object>> _disposables
            = new Dictionary<StudioModelStatWrapper, List<UnityEngine.Object>>();

        private GameObject _parentGo = null;

        /// <summary>ギズモの操作種別</summary>
        public enum GizmoDragType
        {
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
        }

        /// <summary>
        /// 配置したモデルを全て破棄する。シーンをまたぐと参照が無効になるため遷移時に呼ぶ
        /// </summary>
        public void DeleteAll()
        {
            foreach (var model in modelList)
            {
                DeleteModel(model);
            }

            _models.Clear();
            _disposables.Clear();

            if (_parentGo != null)
            {
                UnityEngine.Object.Destroy(_parentGo);
            }
            _parentGo = null;
        }

        /// <summary>
        /// 自前配置分の配置内容をプリセット XML に保存する
        /// </summary>
        public void SavePreset()
        {
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
                    preset.items.Add(new ModelPlacementPresetItem
                    {
                        fileName = model.infoWrapper?.fileName,
                        group = model.group,
                        visible = model.visible,
                        posX = t.position.x, posY = t.position.y, posZ = t.position.z,
                        rotX = t.eulerAngles.x, rotY = t.eulerAngles.y, rotZ = t.eulerAngles.z,
                        sclX = t.localScale.x, sclY = t.localScale.y, sclZ = t.localScale.z,
                    });
                }

                var serializer = new XmlSerializer(typeof(ModelPlacementPreset));
                using (var stream = new FileStream(PluginUtils.ModelPresetPath, FileMode.Create))
                {
                    serializer.Serialize(stream, preset);
                }

                MTEUtils.Log("配置プリセットを保存しました。{0}体", preset.items.Count);
            }
            catch (Exception e)
            {
                MTEUtils.LogWarning("配置プリセットの保存に失敗しました");
                MTEUtils.LogException(e);
            }
        }

        /// <summary>
        /// プリセット XML から配置を復元する。既存の自前配置分は置き換える
        /// </summary>
        public void LoadPreset()
        {
            try
            {
                var path = PluginUtils.ModelPresetPath;
                if (!File.Exists(path))
                {
                    MTEUtils.LogWarning("配置プリセットがありません。{0}", path);
                    return;
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

                    var t = go.transform;
                    t.position = new Vector3(item.posX, item.posY, item.posZ);
                    t.eulerAngles = new Vector3(item.rotX, item.rotY, item.rotZ);
                    t.localScale = new Vector3(item.sclX, item.sclY, item.sclZ);
                    restored++;
                }

                // 個別失敗はスキップされるため、実際に復元できた数を報告する
                MTEUtils.Log("配置プリセットを復元しました。{0}/{1}体", restored, preset.items.Count);
            }
            catch (Exception e)
            {
                MTEUtils.LogWarning("配置プリセットの復元に失敗しました");
                MTEUtils.LogException(e);
            }
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
        /// カメラ前方の床上（y=0）の配置初期位置を返す。原点固定だと画面外に配置されて見えないため
        /// </summary>
        private static Vector3 GetDefaultPosition()
        {
            try
            {
                var camTransform = GameMain.Instance.MainCamera.transform;
                var forward = camTransform.forward;
                forward.y = 0f;
                forward = forward.sqrMagnitude > 0.001f ? forward.normalized : Vector3.forward;

                var pos = camTransform.position + forward * DefaultDistance;
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
        /// GizmoRenderTarget ではなく基底の GizmoRender を使う。派生側の Update は new で基底を隠蔽していて
        /// base.Update() を呼ばないため、ドラッグ判定フラグが立たず操作できない
        /// </summary>
        private void AddGizmo(GameObject target)
        {
            var gizmo = target.AddComponent<GizmoRender>();
            gizmo.offsetScale = GizmoScale;
            ApplyDragType(gizmo);
            gizmo.Visible = true;
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
        }
    }
}
