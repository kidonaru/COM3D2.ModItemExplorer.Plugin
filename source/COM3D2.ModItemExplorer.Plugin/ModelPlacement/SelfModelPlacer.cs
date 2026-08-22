using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using UnityEngine.Rendering;

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
        public const float GizmoScale = 0.5f;

        /// <summary>選択中モデルのハイライト色。元色との間を往復させる</summary>
        private static readonly Color HighlightColor = new Color(0.4f, 1f, 0.4f, 1f);

        /// <summary>ハイライトの明滅周期(秒)</summary>
        private const float HighlightCycle = 1.2f;

        private static ModItemManager modItemManager => ModItemManager.instance;

        private static Config config => ConfigManager.instance.config;

        private static SelfModelPlacer _instance = null;
        public static SelfModelPlacer instance
            => _instance ?? (_instance = new SelfModelPlacer());

        // Config から復元した表示対象をギズモ側の初期状態にも反映しておく。
        // 復元前の既定値を掴まないよう、初回アクセスは ConfigManager.Init() より後である必要がある
        // (現状 ModItemExplorer.Initialize が configManager.Init() を最初に呼ぶため満たされている)
        private SelfModelPlacer()
        {
            ApplyGizmoTarget();
            ModelGizmoManager.instance.onGrabbed = OnGizmoGrabbed;
        }

        /// <summary>
        /// 掴んだギズモのモデルを選択状態へ合わせる。
        /// 管理外・逆引き不能なモデルでは現在の選択を保つ (掴んだ操作で選択が消えると困るため)。
        /// カメラは寄せない (掴んだ瞬間に視点が動くと操作が破綻するため)
        /// </summary>
        private void OnGizmoGrabbed(GameObject go)
        {
            var model = FindModelByGameObject(go);
            if (model == null)
            {
                return;
            }

            SetSelectedModel(model, focus: false);
        }

        private readonly List<StudioModelStatWrapper> _models = new List<StudioModelStatWrapper>();

        // Mesh / Material は GameObject を Destroy しても解放されないため、モデルごとに追跡して明示破棄する
        private readonly Dictionary<StudioModelStatWrapper, List<UnityEngine.Object>> _disposables
            = new Dictionary<StudioModelStatWrapper, List<UnityEngine.Object>>();

        private GameObject _parentGo = null;

        private ModelPlacementHistory _history = null;

        /// <summary>配置操作を SceneEditor の操作履歴へ積む窓口</summary>
        public ModelPlacementHistory history
            => _history ?? (_history = new ModelPlacementHistory(this));

        /// <summary>アタッチ先ボーンの定義</summary>
        public class AttachPoint
        {
            public string displayName;

            /// <summary>アタッチ先のボーン名。null は「なし」（ワールド配置）</summary>
            public string boneName;
        }

        /// <summary>アタッチ中のモデルの状態。復元用にメイドの guid とボーン名を持つ</summary>
        public class AttachState
        {
            public string maidGuid;
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

        /// <summary>
        /// ギズモを表示する対象。多数配置するとギズモが重なって選びづらくなるため、
        /// 選択中のモデルだけに絞れるようにしている。設定は Config に永続化する
        /// </summary>
        public GizmoTargetType gizmoTargetType
        {
            get => config.gizmoTargetType;
            set
            {
                if (config.gizmoTargetType == value)
                {
                    return;
                }

                config.gizmoTargetType = value;
                config.dirty = true;
                ApplyGizmoTarget();
            }
        }

        /// <summary>現在の表示対象設定と選択状態をギズモへ反映する</summary>
        private void ApplyGizmoTarget()
        {
            var selectedOnly = gizmoTargetType == GizmoTargetType.Selected;
            ModelGizmoManager.instance.SetVisibleTarget(
                selectedOnly, selectedOnly ? selectedModel?.obj as GameObject : null);
        }

        private bool _useLocalSpace = true;

        /// <summary>ギズモの軸空間 (true = Local)。SceneEditor 在席時は GizmoToolClient と双方向同期する</summary>
        public bool useLocalSpace
        {
            get => _useLocalSpace;
            set
            {
                if (_useLocalSpace == value)
                {
                    return;
                }

                _useLocalSpace = value;
                ModelGizmoManager.instance.SetUseLocalSpace(value);
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
                if (go == null && _selectedModel != null)
                {
                    // DeleteModel を通らずに破棄された場合もここで検知する。
                    // ギズモの表示対象は破棄済みの GameObject を持ち続けるため併せて更新する
                    // (ApplyGizmoTarget から再入するが、この時点で _selectedModel は null なので再帰しない)
                    _selectedModel = null;
                    ApplyGizmoTarget();
                }
                return _selectedModel;
            }
            set => SetSelectedModel(value, focus: true);
        }

        /// <summary>
        /// 選択中モデルを切り替える。focus = true なら SceneEditor の SceneView カメラを対象へ寄せる
        /// </summary>
        private void SetSelectedModel(StudioModelStatWrapper value, bool focus)
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

            var previousGo = _selectedModel?.obj as GameObject;

            // 同期が SceneEditor の onSelectionChanged 経由で OnHostSelectionChanged として
            // 戻ってきたときに同値判定で止まるよう、同期より先に代入しておく
            _selectedModel = value;
            RefreshHighlight();
            ApplyGizmoTarget();
            SyncSelectionToHost(previousGo, focus);
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

        // ---- SceneEditor の選択連携 ----

        // 登録の再試行間隔 (フレーム)。ホスト型の解決は毎フレーム行うほど安くはない
        private const int SelectionRetryIntervalFrames = 60;
        // int.MinValue だと frame - _lastSelectionAttemptFrame がオーバーフローして負になり、
        // リトライガードが恒久的に成立して一度も登録を試行しなくなる
        private int _lastSelectionAttemptFrame = -SelectionRetryIntervalFrames;

        // ModelSelectClient へ購読済みか。接続の再試行と初期同期はクライアント側が持つため、
        // ここでの購読は 1 回きりでよい。
        // このクラスはプラグイン常駐のシングルトンで破棄されないため Unsubscribe は行わない
        // (Inspector / ModelProvider の登録も同様に解除しない)
        private bool _modelSelectSubscribed;

        /// <summary>
        /// SceneEditor への接続（選択中モデルの購読と Inspector 描画・モデル提供の登録）を行う。
        /// Inspector / ModelProvider のホストは後からロードされる可能性があるため、
        /// そろうまで一定間隔で再試行する（ModelGizmoManager のホスト登録と同じパターン）
        /// </summary>
        private void TryRegisterHostConnections()
        {
            if (_modelSelectSubscribed && _inspectorHandle != null && _modelProviderHandle != null)
            {
                return;
            }

            var frame = Time.frameCount;
            if (frame - _lastSelectionAttemptFrame < SelectionRetryIntervalFrames)
            {
                return;
            }
            _lastSelectionAttemptFrame = frame;

            TryRegisterInspector();
            TryRegisterModelProvider();

            // 購読はホスト不在でも保持され、接続できた時点で現在の選択が 1 回プッシュされる。
            // ホスト側は選択オブジェクトを「提供中モデルの一覧」から逆引きしてモデルへ写像するため、
            // モデル提供の登録が済むまで購読しない (済む前に購読すると初期同期で
            // 自分のモデルが解決されず null が流れる)
            if (!_modelSelectSubscribed && _modelProviderHandle != null)
            {
                ModelSelectClient.Subscribe(OnHostSelectionChanged);
                _modelSelectSubscribed = true;
            }
        }

        // SceneEditor の InspectorHost へ登録済みか。SceneEditor は後からロードされる可能性があるため
        // 成功するまで再試行する (選択購読の再試行間隔に相乗りする)
        private object _inspectorHandle;

        // SceneEditor の ModelProviderHost へ登録済みか。SceneEditor は後からロードされる
        // 可能性があるため成功するまで再試行する (選択購読の再試行間隔に相乗りする)
        private object _modelProviderHandle;
        private ModelInspectorDrawer _inspectorDrawer;

        /// <summary>
        /// Inspector への委譲描画が実際に効いているか。
        /// InspectorHostClient.isAvailable はホスト側 API を解決できたかを表すだけで、
        /// 登録自体は失敗しうる。委譲を前提に自前の UI を隠す側はこちらを見ること
        /// </summary>
        public bool isInspectorRegistered => _inspectorHandle != null;

        /// <summary>
        /// SceneEditor Inspector へ管理モデルの委譲描画を登録する。
        /// SceneEditor 不在時は InspectorHostClient が無効を返すため何もしない
        /// </summary>
        private void TryRegisterInspector()
        {
            if (_inspectorHandle != null || !InspectorHostClient.isAvailable)
            {
                return;
            }

            if (_inspectorDrawer == null)
            {
                _inspectorDrawer = new ModelInspectorDrawer();
            }

            _inspectorHandle = InspectorHostClient.Register(
                "ModItemExplorer",
                _inspectorDrawer.CanDraw,
                _inspectorDrawer.Draw,
                // ヘッダー行を自前のスクロールビュー内へ描き、内容と一緒にスクロールさせる。
                // 対応していない旧ホストへは Register 側が従来どおりの登録へ倒す
                drawsHeader: true);

            if (_inspectorHandle != null)
            {
                MTEUtils.LogDebug("SelfModelPlacer: InspectorHost へ登録しました");
            }
        }

        /// <summary>
        /// SceneEditor のボーン編集等へ配置モデルの一覧を提供する。
        /// SceneEditor 不在時は ModelProviderClient が無効を返すため何もしない
        /// </summary>
        private void TryRegisterModelProvider()
        {
            if (_modelProviderHandle != null || !ModelProviderClient.isAvailable)
            {
                return;
            }

            _modelProviderHandle = ModelProviderClient.Register(
                "ModItemExplorer",
                GetProvidedModels,
                GetProvidedModelName);

            if (_modelProviderHandle != null)
            {
                MTEUtils.LogDebug("SelfModelPlacer: ModelProviderHost へ登録しました");
            }
        }

        /// <summary>配置中モデルのルート GameObject 一覧 (MTE 配置分も含む)</summary>
        private List<GameObject> GetProvidedModels()
        {
            var result = new List<GameObject>();
            foreach (var model in ModelPlacerManager.instance.modelList)
            {
                var go = model.obj as GameObject;
                if (go != null)
                {
                    result.Add(go);
                }
            }
            return result;
        }

        /// <summary>
        /// GameObject からモデルの表示名を逆引きする。管理外なら null (ホスト側が GO 名で表示)。
        /// 名前はモデル操作ウィンドウの一覧と同じ規則で解決する
        /// </summary>
        private string GetProvidedModelName(GameObject go)
        {
            if (go == null)
            {
                return null;
            }

            foreach (var model in ModelPlacerManager.instance.modelList)
            {
                if ((model.obj as GameObject) == go)
                {
                    return modItemManager.GetModelDisplayName(model);
                }
            }
            return null;
        }

        /// <summary>
        /// 選択状態を SceneEditor へ反映する。Inspector に選択として表示されるが、
        /// ギズモは常に ModelGizmoManager 側を使うため showGizmo = false で抑止する。
        /// focus = true なら SceneView のカメラを選択対象へ寄せる。
        /// SceneEditor 不在・連携設定 OFF のときは反映されないが、こちらの選択はそのまま保持する
        /// </summary>
        private void SyncSelectionToHost(GameObject previousGo, bool focus)
        {
            var go = _selectedModel?.obj as GameObject;
            if (go != null)
            {
                ModelSelectClient.TrySelectModel(go, showGizmo: false, focus: focus);
            }
            else if (previousGo != null && ModelSelectClient.selectedModel == previousGo)
            {
                // 自分が選ばせたモデルだけ解除する。
                // SceneEditor 側でユーザーが選び直した別モデルの選択は奪わない
                ModelSelectClient.TrySelectModel(null);
            }
        }

        /// <summary>
        /// SceneEditor 側の選択中モデルの変化を追従する。自プラグイン管理のモデルなら選択し、
        /// それ以外（他プラグインのモデル・モデル以外への切替・選択解除）なら選択を外す。
        /// 自分の TrySelectModel もエコーされるが、SetSelectedModel の同値判定で止まる
        /// </summary>
        private void OnHostSelectionChanged(GameObject go)
        {
            // 例外はクライアント側でも握り潰されるが、ログを自前の文脈付きで残すため
            // ここでも捕捉する
            try
            {
                // ホスト発の選択はホスト側でカメラ制御済みなので、こちらからフォーカスし直さない
                SetSelectedModel(FindModelByGameObject(go), focus: false);
            }
            catch (Exception e)
            {
                MTEUtils.LogWarning("SceneEditor の選択変更の反映に失敗しました");
                MTEUtils.LogException(e);
            }
        }

        /// <summary>配置済みモデルを GameObject から逆引きする。管理外なら null</summary>
        public StudioModelStatWrapper FindModelByGameObject(GameObject go)
        {
            if (go == null)
            {
                return null;
            }

            foreach (var model in _models)
            {
                if ((model.obj as GameObject) == go)
                {
                    return model;
                }
            }
            return null;
        }

        /// <summary>
        /// 配置中のモデル一覧。呼び出し側の走査中に増減しても壊れないようコピーを返す。
        /// 要素の Wrapper は配置中ずっと同一インスタンスであること（ツリー側が参照比較で追従するため）
        /// </summary>
        public List<StudioModelStatWrapper> modelList
            => new List<StudioModelStatWrapper>(_models);

        /// <summary>配置中のモデル数。有無の判定だけならコピーを作らずに済む</summary>
        public int modelCount => _models.Count;

        /// <summary>
        /// .menu アイテムをシーンに配置し、生成したモデルを返す（失敗時は null）。
        /// group は呼び出し側の採番をヒントとして受け取るが、名前の一意性は内部で採り直して保証する
        /// </summary>
        public StudioModelStatWrapper CreateModel(string fileName, int group, bool visible)
        {
            GameObject modelGo = null;
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

                return RegisterCreatedModel(modelGo, fileName, group, visible, disposables);
            }
            catch (Exception e)
            {
                MTEUtils.LogWarning("モデルの配置に失敗しました。{0}", fileName);
                MTEUtils.LogException(e);

                // 登録前に失敗した分は誰からも参照されず削除もできなくなるため、ここで片付ける
                if (modelGo != null)
                {
                    UnityEngine.Object.Destroy(modelGo);
                }
                DestroyAll(disposables);

                return null;
            }
        }

        /// <summary>
        /// nei 由来の背景オブジェクト (.asset_bg) をシーンに配置し、生成したモデルを返す（失敗時は null）。
        /// ラッパー生成以降は CreateModel と同じ扱いにする
        /// </summary>
        public StudioModelStatWrapper CreateBgObject(string assetBundleName, int group, bool visible)
        {
            GameObject modelGo = null;

            try
            {
                var prefab = BgObjectAssetLoader.LoadPrefab(assetBundleName);
                if (prefab == null)
                {
                    return null;
                }

                modelGo = UnityEngine.Object.Instantiate(prefab);
                SetLayerRecursively(modelGo, GetModelLayer());

                var fileName = assetBundleName + BgObjectAssetLoader.AssetBgExtension;

                // Mesh/Material はアセットバンドル所有のため破棄対象に積まない。
                // 破棄すると同じバンドルから作った他インスタンスまで壊れる
                return RegisterCreatedModel(
                    modelGo, fileName, group, visible, new List<UnityEngine.Object>());
            }
            catch (Exception e)
            {
                MTEUtils.LogWarning("背景オブジェクトの配置に失敗しました。{0}", assetBundleName);
                MTEUtils.LogException(e);

                if (modelGo != null)
                {
                    UnityEngine.Object.Destroy(modelGo);
                }

                return null;
            }
        }

        /// <summary>
        /// 生成済みの modelGo をラッパーで包んでシーンへ据え、配置中モデルとして登録する。
        /// menu 経路 (CreateModel) と .asset_bg 経路 (CreateBgObject) の共通後半部分。
        /// 途中で失敗した場合はラッパーだけ片付けて呼び出し側へ投げ返す
        /// (modelGo と disposables の後始末は生成した側の責務)
        /// </summary>
        private StudioModelStatWrapper RegisterCreatedModel(
            GameObject modelGo,
            string fileName,
            int group,
            bool visible,
            List<UnityEngine.Object> disposables)
        {
            // ギズモ操作でモデル内部の Transform を壊さないよう、ラッパー越しに動かす
            var resolvedGroup = ResolveGroup(fileName, group);
            var modelName = GetModelName(fileName, resolvedGroup);
            var wrapperGo = new GameObject(modelName);

            try
            {
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

                history.RegisterCreate(wrapper, history.TryCaptureState(wrapper));

                return wrapper;
            }
            catch
            {
                UnityEngine.Object.Destroy(wrapperGo);
                throw;
            }
        }

        /// <summary>
        /// GameObject とその全子孫のレイヤーを設定する。
        /// menu 経路 (ModelMeshLoader) が生成したボーンごとにレイヤーを設定しているのに合わせる
        /// </summary>
        private static void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
            {
                SetLayerRecursively(child.gameObject, layer);
            }
        }

        /// <summary>
        /// 毎フレーム呼ぶ。ギズモ操作による回転をオイラー角キャッシュに軸単位で足し込み、
        /// 正規化した回転を書き戻す
        /// </summary>
        public void Update()
        {
            TryRegisterHostConnections();
            UpdateGizmoKeyInput();
            UpdateGizmoToolSync();
            ModelGizmoManager.instance.Update();

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

            // オイラー角の確定後に見ないと、回転の差分を過渡状態のまま拾ってしまう
            history.UpdateTransformAggregation(_models, isModelEditMode);

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
        /// 現在のアタッチ先が AttachPoints の何番目か。未アタッチ・該当なしは 0 (なし)
        /// </summary>
        public int GetAttachPointIndex(StudioModelStatWrapper model)
        {
            var state = GetAttachState(model);
            var boneName = state != null ? state.boneName : null;
            return Mathf.Max(0, AttachPoints.FindIndex(p => p.boneName == boneName));
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

            // Attach は位置・回転もリセットするため、履歴の控えは Transform ごと取る
            var historyState = history.TryCaptureState(model);

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
                    maidGuid = maid.status.guid,
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

            history.RegisterAttach(model, historyState);
        }

        /// <summary>
        /// 保存データのアタッチ状態を復元する。未アタッチならワールド配置へ戻す
        /// </summary>
        internal void RestoreAttachState(StudioModelStatWrapper model, ModelPlacementPresetItem item)
        {
            if (string.IsNullOrEmpty(item.attachMaidGuid) || string.IsNullOrEmpty(item.attachBoneName))
            {
                Attach(model, null, null);
                return;
            }

            var maid = FindAttachTargetMaid(item.attachMaidGuid);
            var point = AttachPoints.Find(p => p.boneName == item.attachBoneName);
            if (maid == null || point == null)
            {
                MTEUtils.LogWarning("アタッチ先が見つからないためワールド配置に戻します。{0}", item.attachBoneName);
                Attach(model, null, null);
                return;
            }

            Attach(model, maid, point);
        }

        /// <summary>
        /// guid からアタッチ先メイドを引く。別セーブのプリセットなど guid が一致しない場合は
        /// 現在の対象メイドで代替する（見つからなければ null）
        /// </summary>
        private static Maid FindAttachTargetMaid(string maidGuid)
        {
            var maid = GameMain.Instance.CharacterMgr.GetMaid(maidGuid);
            if (maid != null)
            {
                return maid;
            }

            var currentMaid = modItemManager.currentMaid;
            if (currentMaid != null)
            {
                MTEUtils.LogWarning("アタッチ先メイドが見つからないため現在の対象メイドに割り当てます。{0}", maidGuid);
            }

            return currentMaid;
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

            if (model.visible == visible)
            {
                return;
            }

            var historyState = history.TryCaptureState(model);

            model.visible = visible;

            var go = model.obj as GameObject;
            if (go != null)
            {
                go.SetActive(visible);
            }

            history.RegisterVisible(model, historyState, visible);
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

            // 破棄すると状態を読めなくなるため、履歴用の控えは削除前に取る
            var historyState = history.TryCaptureState(model);
            var historyName = model.displayName;

            if (selectedModel == model)
            {
                selectedModel = null;
            }

            try
            {
                var go = model.obj as GameObject;
                if (go != null)
                {
                    ModelGizmoManager.instance.RemoveGizmo(go);
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
            history.Forget(model);

            history.RegisterDelete(historyState, historyName);
        }

        /// <summary>
        /// 配置したモデルを全て破棄する。シーンをまたぐと参照が無効になるため遷移時に呼ぶ
        /// </summary>
        public void DeleteAll()
        {
            selectedModel = null;

            // 一括破棄は 1 体ずつ履歴に積んでも戻せないため登録しない。
            // 世代を進めて、既存の生成/削除エントリが復活させに来ないようにする
            history.RunSuppressed(() =>
            {
                foreach (var model in modelList)
                {
                    DeleteModel(model);
                }
            });
            history.InvalidateAll();

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
                var preset = BuildPreset();

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

                var restored = ApplyPreset(preset);

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
        /// 現在の自前配置分の配置内容をプリセットデータとして取り出す
        /// </summary>
        private ModelPlacementPreset BuildPreset()
        {
            var preset = new ModelPlacementPreset();

            foreach (var model in _models)
            {
                var item = BuildPresetItem(model);
                if (item != null)
                {
                    preset.items.Add(item);
                }
            }

            return preset;
        }

        /// <summary>
        /// モデル1体分の状態を取り出す。破棄済みなら null。
        /// プリセット保存と操作履歴のスナップショットで同じ形式を使う
        /// </summary>
        internal ModelPlacementPresetItem BuildPresetItem(StudioModelStatWrapper model)
        {
            var go = model?.obj as GameObject;
            if (go == null)
            {
                return null;
            }

            var t = go.transform;
            var attach = GetAttachState(model);
            // 回転はキャッシュ値を保存し、UI に表示している数値と一致させる
            var euler = GetEulerAngles(model);
            return new ModelPlacementPresetItem
            {
                fileName = model.infoWrapper?.fileName,
                group = model.group,
                visible = model.visible,
                // UI・アタッチと揃えるためローカル系で保存する
                posX = t.localPosition.x, posY = t.localPosition.y, posZ = t.localPosition.z,
                rotX = euler.x, rotY = euler.y, rotZ = euler.z,
                sclX = t.localScale.x, sclY = t.localScale.y, sclZ = t.localScale.z,
                attachMaidGuid = attach?.maidGuid,
                attachBoneName = attach?.boneName,
            };
        }

        /// <summary>
        /// プリセットデータを現在のシーンに反映する。既存の自前配置分は置き換える。
        /// 個別失敗はスキップし、実際に復元できた数を返す
        /// </summary>
        internal int ApplyPreset(ModelPlacementPreset preset)
        {
            DeleteAll();

            // 旧形式はアタッチ先がスロット番号のため復元できない。黙って世界配置に落ちると気付けないので知らせる
            if (preset.version < ModelPlacementPreset.CurrentVersion)
            {
                MTEUtils.LogWarning(
                    "旧形式(version {0})の配置プリセットのため、アタッチ情報は復元されません", preset.version);
            }

            // プリセット復元は全体の入れ替えなので、1 体ずつは履歴に積まない
            var restored = 0;
            history.RunSuppressed(() =>
            {
                foreach (var item in preset.items)
                {
                    if (RestoreModel(item) != null)
                    {
                        restored++;
                    }
                }
            });

            // 復元直後はどれも選択していない状態にする
            selectedModel = null;

            return restored;
        }

        /// <summary>配置データのファイル名が背景オブジェクト (.asset_bg) を指すか</summary>
        internal static bool IsBgObjectFileName(string fileName)
        {
            return !string.IsNullOrEmpty(fileName)
                && fileName.EndsWith(
                    BgObjectAssetLoader.AssetBgExtension, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>"xxx.asset_bg" からアセットバンドル名 "xxx" を取り出す</summary>
        private static string GetAssetBundleName(string fileName)
        {
            return Path.GetFileNameWithoutExtension(fileName);
        }

        /// <summary>
        /// 保存データからモデル1体を復元する。失敗時は null。
        /// プリセット復元と操作履歴の undo/redo で同じ経路を使う
        /// </summary>
        internal StudioModelStatWrapper RestoreModel(ModelPlacementPresetItem item)
        {
            // 保存時と同じ生成経路を再実行してから Transform を適用する。
            // 背景オブジェクトは fileName の拡張子で見分ける
            // (ModItemManager.GetMenu が .menu/.mod を拡張子で見分けているのと同じ流儀)
            var wrapper = IsBgObjectFileName(item.fileName)
                ? CreateBgObject(GetAssetBundleName(item.fileName), item.group, item.visible)
                : CreateModel(item.fileName, item.group, item.visible);
            if (wrapper?.obj as GameObject == null)
            {
                return null;
            }

            RestoreAttachState(wrapper, item);

            // Attach はローカル位置・回転をリセットするため、必ずアタッチの後に適用する
            ApplyTransform(wrapper, item);
            return wrapper;
        }

        /// <summary>
        /// 保存データの位置・回転・拡縮をモデルへ適用する（アタッチ先は変更しない）
        /// </summary>
        internal void ApplyTransform(StudioModelStatWrapper model, ModelPlacementPresetItem item)
        {
            var go = model?.obj as GameObject;
            if (go == null)
            {
                return;
            }

            var t = go.transform;
            t.localPosition = new Vector3(item.posX, item.posY, item.posZ);
            // 回転はキャッシュ経由で適用し、保存した数値がそのまま UI に出るようにする
            SetEulerAngles(model, new Vector3(item.rotX, item.rotY, item.rotZ));
            t.localScale = new Vector3(item.sclX, item.sclY, item.sclZ);
        }

        /// <summary>
        /// 現在の自前配置分の配置内容を XML 文字列として取得する（外部プラグイン連携用）。
        /// フォーマットは名前付きプリセットの xml と同一。失敗時は null
        /// </summary>
        public string GetPlacementXml()
        {
            try
            {
                var serializer = new XmlSerializer(typeof(ModelPlacementPreset));
                using (var writer = new StringWriter())
                {
                    serializer.Serialize(writer, BuildPreset());
                    return writer.ToString();
                }
            }
            catch (Exception e)
            {
                MTEUtils.LogWarning("配置内容のXML化に失敗しました");
                MTEUtils.LogException(e);
                return null;
            }
        }

        /// <summary>
        /// XML 文字列から配置を復元する（外部プラグイン連携用）。既存の自前配置分は置き換える
        /// </summary>
        public bool ApplyPlacementXml(string xml)
        {
            try
            {
                if (string.IsNullOrEmpty(xml))
                {
                    MTEUtils.LogWarning("配置XMLが空です");
                    return false;
                }

                ModelPlacementPreset preset;
                var serializer = new XmlSerializer(typeof(ModelPlacementPreset));
                using (var reader = new StringReader(xml))
                {
                    preset = (ModelPlacementPreset)serializer.Deserialize(reader);
                }

                var restored = ApplyPreset(preset);
                MTEUtils.Log("配置XMLを反映しました。({0}/{1}体)", restored, preset.items.Count);
                return true;
            }
            catch (Exception e)
            {
                MTEUtils.LogWarning("配置XMLの反映に失敗しました");
                MTEUtils.LogException(e);
                return false;
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
                var nprApplied = false;
                var nprReflectionApplied = false;

                foreach (var change in script.materialChanges)
                {
                    if (change.materialNo < 0 || change.materialNo >= materials.Length)
                    {
                        continue;
                    }

                    // LoadMaterial は欠損時に NDebug.Assert へ落ちるため、事前に存在を確かめる。
                    // MOD の mate は GameUty.FileSystem 側には無いので Mod 側も見る MTEUtils を使う
                    if (!MTEUtils.IsExistentFile(change.fileName))
                    {
                        MTEUtils.LogWarning("mateファイルが見つかりません。{0}", change.fileName);
                        continue;
                    }

                    // NPR 用の mate は ImportCM ではシェーダーを解決できないため NPRShader 側に投げる。
                    // NPRShader 未導入なら null が返るので通常ロードにフォールバックする
                    var material = NprShaderLoader.IsNprMaterial(change.fileName)
                        ? NprShaderLoader.LoadMaterial(change.fileName)
                        : null;
                    var nprLoaded = material != null;
                    if (material == null)
                    {
                        material = ImportCM.LoadMaterial(change.fileName, null);
                        if (material == null)
                        {
                            continue;
                        }
                    }

                    materials[change.materialNo] = material;
                    disposables.Add(material);
                    nprApplied |= nprLoaded;
                    nprReflectionApplied |= nprLoaded && NprShaderLoader.IsReflectionMaterial(change.fileName);
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

                // NPR シェーダーは接線を前提に法線を作るため、NPRShader 側の配置オブジェクト処理と同じく再計算する
                if (nprApplied && smr.sharedMesh != null)
                {
                    smr.sharedMesh.RecalculateTangents();
                }

                // ModelMeshLoader は移植元に倣ってリフレクションプローブを切っているが、
                // _Reflection_ 系の NPR シェーダーはプローブが無いと反射が出ないので有効化する
                if (nprReflectionApplied)
                {
                    smr.reflectionProbeUsage = ReflectionProbeUsage.Simple;
                }
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
        /// ゲーム本体の GizmoRender はメインカメラ前提で SceneView 上では操作できないため、
        /// カメラ非依存の TransformGizmo を ModelGizmoManager 経由で使う
        /// </summary>
        private void AddGizmo(GameObject target)
        {
            ModelGizmoManager.instance.AddGizmo(target);
        }

        /// <summary>
        /// 配置済み全モデルのギズモに現在の操作種別を反映
        /// </summary>
        private void ApplyDragType()
        {
            ModelGizmoManager.instance.SetToolAndVisible(
                ToGizmoTool(_dragType), _isModelEditMode && _dragType != GizmoDragType.None);
        }

        public static GizmoTool ToGizmoTool(GizmoDragType dragType)
        {
            switch (dragType)
            {
                case GizmoDragType.Move: return GizmoTool.Move;
                case GizmoDragType.Rotate: return GizmoTool.Rotate;
                case GizmoDragType.Scale: return GizmoTool.Scale;
                default: return GizmoTool.None;
            }
        }

        /// <summary>キー入力でギズモの操作種別を切り替える</summary>
        private void UpdateGizmoKeyInput()
        {
            if (!_isModelEditMode)
            {
                return;
            }

            if (config.GetKeyDown(KeyBindType.GizmoMove))
            {
                dragType = GizmoDragType.Move;
            }
            if (config.GetKeyDown(KeyBindType.GizmoRotate))
            {
                dragType = GizmoDragType.Rotate;
            }
            if (config.GetKeyDown(KeyBindType.GizmoScale))
            {
                dragType = GizmoDragType.Scale;
            }
        }

        // 前回同期時点の値。SceneEditor とどちら側が変更したかの判別に使う
        private GizmoDragType _lastSyncedDragType;
        private bool _lastSyncedUseLocalSpace;
        private GizmoTargetType _lastSyncedGizmoTargetType;
        private bool _gizmoToolSyncStarted;

        // ホスト型が未解決の間の再試行間隔 (フレーム)。
        // ホスト型の解決は毎フレーム行うほど安くはない (TryRegisterHostConnections と同じパターン)
        private const int GizmoToolSyncRetryIntervalFrames = 60;
        private int _lastGizmoToolSyncAttemptFrame = -GizmoToolSyncRetryIntervalFrames;

        /// <summary>
        /// ギズモ操作設定を SceneEditor (GizmoRenderer) と双方向同期する。
        /// SceneEditor 側にイベントが無いため毎フレームのポーリングで追従し、
        /// 前回同期値との差分でどちらが動いたかを判別する (同値なら no-op でループしない)。
        /// 両側が同フレームに変わった場合は MTE 側を優先する
        /// </summary>
        private void UpdateGizmoToolSync()
        {
            if (!_gizmoToolSyncStarted)
            {
                var frame = Time.frameCount;
                if (frame - _lastGizmoToolSyncAttemptFrame < GizmoToolSyncRetryIntervalFrames)
                {
                    return;
                }
                _lastGizmoToolSyncAttemptFrame = frame;
            }

            if (!GizmoToolClient.isAvailable)
            {
                return;
            }

            // 取得に失敗すると GizmoToolClient は無効へ倒れつつ既定値を返すため、
            // 読み出した後にもう一度確認する。既定値 (なし / Local) を SceneEditor の現在値と
            // 取り違えて MTE 側のギズモ設定を書き換えないようにする
            var hostTool = GizmoToolClient.tool;
            var hostUseLocalSpace = GizmoToolClient.useLocalSpace;
            if (!GizmoToolClient.isAvailable)
            {
                return;
            }

            if (!_gizmoToolSyncStarted)
            {
                // 初回は SceneEditor 側の現在値へ合わせる (SceneEditor を正とする)
                _gizmoToolSyncStarted = true;
                dragType = FromGizmoTool(hostTool);
                useLocalSpace = hostUseLocalSpace;
                _lastSyncedDragType = dragType;
                _lastSyncedUseLocalSpace = useLocalSpace;

                // 表示対象は旧版 SceneEditor に無いため、扱えるときだけ合わせる。
                // 扱えない間は MIE 自身の Config が正のまま
                if (GizmoToolClient.isTargetTypeAvailable)
                {
                    gizmoTargetType = GizmoToolClient.targetType;
                }
                _lastSyncedGizmoTargetType = gizmoTargetType;
                return;
            }

            if (dragType != _lastSyncedDragType)
            {
                GizmoToolClient.tool = ToGizmoTool(dragType);
            }
            else
            {
                dragType = FromGizmoTool(hostTool);
            }
            _lastSyncedDragType = dragType;

            if (useLocalSpace != _lastSyncedUseLocalSpace)
            {
                GizmoToolClient.useLocalSpace = useLocalSpace;
            }
            else
            {
                useLocalSpace = hostUseLocalSpace;
            }
            _lastSyncedUseLocalSpace = useLocalSpace;

            if (GizmoToolClient.isTargetTypeAvailable)
            {
                if (gizmoTargetType != _lastSyncedGizmoTargetType)
                {
                    GizmoToolClient.targetType = gizmoTargetType;
                }
                else
                {
                    gizmoTargetType = GizmoToolClient.targetType;
                }
                _lastSyncedGizmoTargetType = gizmoTargetType;
            }
        }

        public static GizmoDragType FromGizmoTool(GizmoTool tool)
        {
            switch (tool)
            {
                case GizmoTool.Move: return GizmoDragType.Move;
                case GizmoTool.Rotate: return GizmoDragType.Rotate;
                case GizmoTool.Scale: return GizmoDragType.Scale;
                default: return GizmoDragType.None;
            }
        }
    }
}
