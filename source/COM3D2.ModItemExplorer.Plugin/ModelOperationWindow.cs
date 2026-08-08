using System;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace COM3D2.ModItemExplorer.Plugin
{
    /// <summary>
    /// 配置モデルの操作ウィンドウ。編集モードがモデルのときのみ表示される
    /// </summary>
    public class ModelOperationWindow : IWindow
    {
        public readonly static int WINDOW_ID = 582880;

        /// <summary>ウィンドウの最小サイズ。Transform 行が折り返さない幅を下限にする</summary>
        public readonly static int MIN_WINDOW_WIDTH = 380;
        public readonly static int MIN_WINDOW_HEIGHT = 320;

        public readonly static int HEADER_HEIGHT = 20;

        /// <summary>リサイズグリップを置く下端の高さ</summary>
        public readonly static int FOOTER_HEIGHT = 20;

        public readonly static int ROW_HEIGHT = 20;

        /// <summary>モデル一覧の行の高さ。サムネを載せるため通常行より大きくする</summary>
        public readonly static int MODEL_ROW_HEIGHT = 36;

        /// <summary>ウィンドウを縮めてもモデル一覧に最低限残す高さ</summary>
        public readonly static int MIN_MODEL_LIST_HEIGHT = MODEL_ROW_HEIGHT * 2;

        /// <summary>
        /// モデル一覧より下に確保する行数（ギズモ + Transform 5行）。
        /// モデル未選択時は Transform が案内ラベル1行に縮むが、選択のたびに一覧の高さが
        /// 変わると操作しづらいため、常に最大の行数分を確保する
        /// </summary>
        private readonly static int BOTTOM_ROW_COUNT = 6;

        /// <summary>GUIView.DrawHorizontalLine が描く区切り線の高さ</summary>
        private readonly static int HORIZONTAL_LINE_HEIGHT = 1;

        /// <summary>アイコン相当の小さなボタン（表示トグル・削除）の幅</summary>
        public readonly static int BUTTON_WIDTH = 20;

        /// <summary>全行共通のラベル幅。列を揃えるためどの行もこの幅を使う</summary>
        public readonly static int LABEL_WIDTH = 70;

        /// <summary>ウィンドウ内のタブ</summary>
        private enum TabType
        {
            操作,
            プリセット,
        }

        private TabType _tabType = TabType.操作;

        /// <summary>ユーザーによる表示切替。設定には保存せずセッション内のみ保持する</summary>
        private bool _userVisible = true;

        public void ToggleVisible()
        {
            _userVisible = !_userVisible;
        }

        /// <summary>ドラッグラベルの感度（1pxあたりの増減量）</summary>
        private const float PositionSensitivity = 0.01f;
        private const float RotationSensitivity = 1f;
        private const float ScaleSensitivity = 0.01f;

        /// <summary>連動時に比率計算をあきらめる拡縮値のしきい値</summary>
        private const float ScaleLinkEpsilon = 0.0001f;

        /// <summary>拡縮の XYZ を連動させるか。設定には保存せずセッション内のみ保持する</summary>
        private bool _scaleLinked = false;

        private static readonly string[] AxisNames = { "X", "Y", "Z" };

        private static WindowManager windowManager => WindowManager.instance;
        private static ModItemManager modItemManager => ModItemManager.instance;
        private static TextureManager textureManager => TextureManager.instance;
        private static SelfModelPlacer placer => SelfModelPlacer.instance;
        private static Config config => ConfigManager.instance.config;

        public int windowIndex { get; set; }
        public bool isShowWnd { get; set; }

        private Rect _windowRect;
        public Rect windowRect
        {
            get => _windowRect;
            set => _windowRect = value;
        }

        /// <summary>操作対象のモデル。実体は SelfModelPlacer が持つ</summary>
        public StudioModelStatWrapper selectedModel
        {
            get => placer.selectedModel;
            set => placer.selectedModel = value;
        }

        private GUIComboBox<SelfModelPlacer.AttachPoint> _attachPointComboBox
            = new GUIComboBox<SelfModelPlacer.AttachPoint>
        {
            items = SelfModelPlacer.AttachPoints,
            getName = (point, _) => point.displayName,
            buttonSize = new Vector2(150, 20),
        };

        private string _presetName = "";

        private List<string> _presetNames = new List<string>();

        /// <summary>プリセット一覧の再読込が必要か。毎フレームのディレクトリ走査を避けるためのフラグ</summary>
        private bool _presetNamesDirty = true;

        private GUIView _rootView = new GUIView();
        private GUIView _headerView = new GUIView();
        private GUIView _contentView = new GUIView();
        private GUIView _footerView = new GUIView();
        private bool _initializedGUI = false;

        private int _windowWidth = MIN_WINDOW_WIDTH;
        private int _windowHeight = MIN_WINDOW_HEIGHT;

        private GUIView.DragInfo _windowSizeDragInfo = new GUIView.DragInfo();

        public GUIStyle gsWin => GUIView.gsWin;

        public ModelOperationWindow()
        {
            this.windowIndex = 0;
            this.isShowWnd = false;
            this.windowRect = new Rect(
                Screen.width - _windowWidth - 30,
                Screen.height - _windowHeight - 100,
                _windowWidth,
                _windowHeight);
        }

        public void Init()
        {
        }

        public void InitView()
        {
            _rootView.Init(0, 0, _windowWidth, _windowHeight);
            _headerView.Init(0, 0, _windowWidth, HEADER_HEIGHT);
            _contentView.Init(0, HEADER_HEIGHT, _windowWidth,
                _windowHeight - HEADER_HEIGHT - FOOTER_HEIGHT);
            _footerView.Init(0, _windowHeight - FOOTER_HEIGHT, _windowWidth, FOOTER_HEIGHT);

            _headerView.parent = _rootView;
            _contentView.parent = _rootView;
            _footerView.parent = _rootView;
        }

        public void Update()
        {
            // ギズモ回転のオイラー角正規化はウィンドウ非表示中も回す
            // （他の編集モードでもギズモ自体は操作できるため）
            placer.Update();

            var isModelMode = windowManager.modItemWindow != null
                && windowManager.modItemWindow.isModelMode;

            // ギズモはウィンドウの開閉と独立。編集モード中は出しっぱなしにする
            placer.isModelEditMode = isModelMode;

            var showWnd = isModelMode && _userVisible;

            // 表示のたびに一覧を取り直す（外部でファイルが増減していても追従させる）
            if (showWnd && !isShowWnd)
            {
                _presetNamesDirty = true;
            }

            isShowWnd = showWnd;
        }

        public void Close()
        {
            isShowWnd = false;
            _userVisible = false;

            // プラグイン無効化時にも呼ばれるため、ギズモとハイライトをここで片付ける
            placer.isModelEditMode = false;
        }

        public void OnLoad()
        {
            MTEUtils.AdjustWindowPosition(ref _windowRect);
        }

        public void OnScreenSizeChanged()
        {
            ClampWindowSize();
            MTEUtils.AdjustWindowPosition(ref _windowRect);
        }

        public void OnChangedSceneLevel(Scene scene, LoadSceneMode sceneMode)
        {
            selectedModel = null;
        }

        public void InitGUI()
        {
            if (_initializedGUI)
            {
                return;
            }
            _initializedGUI = true;

            // 画面解像度が変わった後でも収まるよう、保存値は読み込み時点で丸める
            ClampWindowSize();

            _windowWidth = config.modelOperationWindowWidth;
            _windowHeight = config.modelOperationWindowHeight;
            _windowRect.width = _windowWidth;
            _windowRect.height = _windowHeight;

            InitView();

            if (config.modelOperationWindowPosX != -1 && config.modelOperationWindowPosY != -1)
            {
                _windowRect.x = config.modelOperationWindowPosX;
                _windowRect.y = config.modelOperationWindowPosY;
            }

            MTEUtils.AdjustWindowPosition(ref _windowRect);
        }

        public void OnGUI()
        {
            if (!isShowWnd)
            {
                return;
            }

            InitGUI();

            if (_windowWidth != config.modelOperationWindowWidth ||
                _windowHeight != config.modelOperationWindowHeight)
            {
                _windowWidth = config.modelOperationWindowWidth;
                _windowHeight = config.modelOperationWindowHeight;
                _windowRect.width = _windowWidth;
                _windowRect.height = _windowHeight;
                InitView();
            }

            windowRect = GUI.Window(WINDOW_ID, windowRect, DrawWindow, "モデル操作", gsWin);
            MTEUtils.ResetInputOnScroll(windowRect);

            if (config.modelOperationWindowPosX != (int)windowRect.x ||
                config.modelOperationWindowPosY != (int)windowRect.y)
            {
                config.modelOperationWindowPosX = (int)windowRect.x;
                config.modelOperationWindowPosY = (int)windowRect.y;
            }
        }

        private void DrawWindow(int id)
        {
            _rootView.ResetLayout();

            DrawHeader();
            DrawContent();
            DrawResizeGrip();

            _rootView.DrawComboBox();

            // リサイズ中にウィンドウ移動が同時に走ると位置とサイズが競合するため抑止する
            if (!_windowSizeDragInfo.isDragging)
            {
                GUI.DragWindow();
            }
        }

        /// <summary>
        /// 右下のリサイズグリップ。実サイズは config 経由で OnGUI が反映する
        /// </summary>
        private void DrawResizeGrip()
        {
            var view = _footerView;
            view.ResetLayout();

            // フッター内の右端に合わせるため、padding/margin は入れない
            view.padding = Vector2.zero;
            view.margin = 0;

            view.BeginLayout(GUIView.LayoutDirection.Free);

            view.currentPos.x = _windowWidth - FOOTER_HEIGHT;

            view.DrawDraggableButton("□", FOOTER_HEIGHT, FOOTER_HEIGHT,
                _windowSizeDragInfo,
                new Vector2(_windowWidth, _windowHeight),
                null,
                value =>
                {
                    config.modelOperationWindowWidth = (int)value.x;
                    config.modelOperationWindowHeight = (int)value.y;

                    ClampWindowSize();

                    config.dirty = true;
                });
        }

        private void ClampWindowSize()
        {
            config.modelOperationWindowWidth = Mathf.Clamp(
                config.modelOperationWindowWidth, MIN_WINDOW_WIDTH, Screen.width);
            config.modelOperationWindowHeight = Mathf.Clamp(
                config.modelOperationWindowHeight, MIN_WINDOW_HEIGHT, Screen.height);
        }

        private void DrawHeader()
        {
            var view = _headerView;
            view.ResetLayout();

            view.padding = Vector2.zero;

            view.BeginLayout(GUIView.LayoutDirection.Free);

            view.currentPos.x = _windowWidth - 20;

            if (view.DrawButton("x", 20, 20))
            {
                // isShowWnd は Update() が毎フレーム計算し直すため、こちらを落とす
                _userVisible = false;
            }
        }

        private void DrawContent()
        {
            var view = _contentView;
            view.ResetLayout();
            view.SetEnabled(!view.IsComboBoxFocused());

            _tabType = view.DrawTabs(_tabType, 80, ROW_HEIGHT);

            if (_tabType == TabType.操作)
            {
                DrawModelList(view);

                view.DrawHorizontalLine();

                DrawGizmoRow(view);
                DrawTransform(view);
            }
            else
            {
                DrawPreset(view);
            }
        }

        /// <summary>
        /// 名前付きプリセットの保存・読込・削除。対象は自前配置分のモデル全体
        /// </summary>
        private void DrawPreset(GUIView view)
        {
            if (_presetNamesDirty)
            {
                _presetNamesDirty = false;
                _presetNames = placer.GetPresetNames();
            }

            view.BeginHorizontal();
            {
                view.DrawLabel("名前", LABEL_WIDTH, ROW_HEIGHT, style: GUIView.gsLabelRight);

                view.DrawTextField(new GUIView.TextFieldOption
                {
                    value = _presetName,
                    width = 200,
                    hiddenButton = true,
                    onChanged = value => _presetName = value,
                });

                if (view.DrawButton("保存", 50, ROW_HEIGHT, enabled: _presetName.Trim().Length > 0))
                {
                    placer.SavePreset(_presetName);
                    _presetNamesDirty = true;
                }
            }
            view.EndLayout();

            view.DrawHorizontalLine();

            // 最後の要素なので高さ -1（残り全部）でウィンドウの伸縮に追従させる
            view.BeginScrollView(-1, -1, GUIView.AutoScrollViewRect, false, true);

            if (_presetNames.Count == 0)
            {
                view.DrawLabel("プリセットがありません", -1, ROW_HEIGHT);
            }

            foreach (var name in _presetNames)
            {
                view.BeginHorizontal();
                {
                    // クリックで名前欄に反映し、同名上書き保存をしやすくする
                    view.DrawLabel(name, 200, ROW_HEIGHT,
                        onClickAction: () => _presetName = name);

                    if (view.DrawButton("読込", 50, ROW_HEIGHT))
                    {
                        // 選択の解除は SelfModelPlacer.LoadPreset が行う
                        placer.LoadPreset(name);
                        modItemManager.UpdateModelItems();
                    }

                    if (view.DrawButton("削除", 50, ROW_HEIGHT))
                    {
                        placer.DeletePreset(name);
                        _presetNamesDirty = true;
                    }
                }
                view.EndLayout();

                if (_presetNamesDirty)
                {
                    // 削除で一覧が変わったため、このフレームの列挙を打ち切る
                    break;
                }
            }

            view.EndScrollView();
        }

        /// <summary>
        /// モデル一覧に割り当てる高さ。下に並ぶ固定行を残して余りを全部与える
        /// </summary>
        private float GetModelListHeight(GUIView view)
        {
            // 区切り線 + ギズモ行 + Transform 各行。いずれも後ろに margin が付く
            var bottomHeight = HORIZONTAL_LINE_HEIGHT + view.margin
                + (ROW_HEIGHT + view.margin) * BOTTOM_ROW_COUNT;

            // padding.y は上下2回分引く（GetDrawRect の高さ自動計算と同じ規約に合わせる）
            var height = view.viewRect.height - view.currentPos.y
                - view.padding.y * 2 - view.margin - bottomHeight;

            return Mathf.Max(height, MIN_MODEL_LIST_HEIGHT);
        }

        /// <summary>
        /// 配置中のモデル一覧。表示切替・選択・削除を行う
        /// </summary>
        private void DrawModelList(GUIView view)
        {
            var models = placer.modelList;

            var listTop = view.currentPos.y;
            var listHeight = GetModelListHeight(view);

            // BeginScrollView は padding を無視して currentPos をそのまま絶対座標に使うため、
            // 通常の縦フローに合わせるには padding.y 分を自前で足しておく必要がある
            view.currentPos.y += view.padding.y;

            view.BeginScrollView(-1, listHeight, GUIView.AutoScrollViewRect, false, true);

            if (models.Count == 0)
            {
                view.DrawLabel("配置モデルがありません", -1, ROW_HEIGHT);
            }

            // 削除ボタンを右端に寄せるため、名前ラベルで行の余りを埋める
            var rowWidth = view.viewRect.width - view.padding.x * 2;
            var nameWidth = rowWidth
                - (BUTTON_WIDTH + view.margin) * 2
                - (MODEL_ROW_HEIGHT + view.margin);

            foreach (var model in models)
            {
                var menu = modItemManager.GetMenu(model.infoWrapper?.fileName);

                view.BeginHorizontal();
                {
                    // ボタン類は行の高さまで引き伸ばさず、通常の高さのまま縦中央に置く
                    var buttonOffsetY = (MODEL_ROW_HEIGHT - ROW_HEIGHT) * 0.5f;

                    view.currentPos.y += buttonOffsetY;
                    view.DrawToggle(model.visible, BUTTON_WIDTH, ROW_HEIGHT,
                        value => placer.SetVisible(model, value));
                    view.currentPos.y -= buttonOffsetY;

                    DrawModelThumb(view, menu);

                    // 選択状態は文字色で表す（トグルを2つ並べると視覚ノイズになるため）
                    var selected = model == selectedModel;
                    view.DrawLabel(GetModelDisplayName(model, menu), nameWidth, MODEL_ROW_HEIGHT,
                        textColor: selected ? Color.green : Color.white,
                        // 選択済みのモデルを再度押したときは選択を解除する
                        onClickAction: () => selectedModel = selected ? null : model);

                    view.currentPos.y += buttonOffsetY;
                    var deleted = view.DrawButton("x", BUTTON_WIDTH, ROW_HEIGHT);
                    view.currentPos.y -= buttonOffsetY;

                    if (deleted)
                    {
                        // 選択の解除は SelfModelPlacer.DeleteModel が行う
                        placer.DeleteModel(model);
                        modItemManager.UpdateModelItems();
                    }
                }
                view.EndLayout();
            }

            view.EndScrollView();

            // EndScrollView は currentPos にビュー絶対座標を書き戻すため、
            // そのままだと後続の描画が viewRect.y + padding.y 分だけ下にずれる。
            // 一覧の直下（次の縦フロー位置）へ戻す
            view.currentPos.y = listTop + listHeight + view.margin;
        }

        /// <summary>
        /// モデルのサムネ。menu 未解決やアイコン未設定でも列がずれないよう領域だけは必ず消費する
        /// </summary>
        private void DrawModelThumb(GUIView view, MenuInfo menu)
        {
            var thumb = menu != null ? textureManager.GetTexture(menu.iconName, menu.iconData) : null;
            if (thumb == null)
            {
                view.DrawEmpty(MODEL_ROW_HEIGHT, MODEL_ROW_HEIGHT);
                return;
            }

            view.DrawTexture(thumb, MODEL_ROW_HEIGHT, MODEL_ROW_HEIGHT);
        }

        /// <summary>
        /// 一覧に出すモデル名。menu のアイテム名を優先し、同一アイテムの複数配置は連番で区別する。
        /// menu が引けないときはファイル名ベースの名前にフォールバックする
        /// </summary>
        private string GetModelDisplayName(StudioModelStatWrapper model, MenuInfo menu)
        {
            if (menu == null || string.IsNullOrEmpty(menu.name))
            {
                return model.displayName;
            }

            return model.group == 0 ? menu.name : menu.name + " (" + model.group + ")";
        }

        /// <summary>
        /// ギズモの操作種別。dragType は配置モデル全体で共有される
        /// </summary>
        private void DrawGizmoRow(GUIView view)
        {
            view.BeginHorizontal();
            {
                view.DrawLabel("ギズモ", LABEL_WIDTH, ROW_HEIGHT, style: GUIView.gsLabelRight);

                view.DrawToggle("なし", placer.dragType == SelfModelPlacer.GizmoDragType.None,
                    60, ROW_HEIGHT,
                    _ => placer.dragType = SelfModelPlacer.GizmoDragType.None);
                view.DrawToggle("移動", placer.dragType == SelfModelPlacer.GizmoDragType.Move,
                    60, ROW_HEIGHT,
                    _ => placer.dragType = SelfModelPlacer.GizmoDragType.Move);
                view.DrawToggle("回転", placer.dragType == SelfModelPlacer.GizmoDragType.Rotate,
                    60, ROW_HEIGHT,
                    _ => placer.dragType = SelfModelPlacer.GizmoDragType.Rotate);
                view.DrawToggle("拡縮", placer.dragType == SelfModelPlacer.GizmoDragType.Scale,
                    60, ROW_HEIGHT,
                    _ => placer.dragType = SelfModelPlacer.GizmoDragType.Scale);
            }
            view.EndLayout();
        }

        /// <summary>
        /// 選択中モデルの位置・回転・拡縮を編集する行
        /// </summary>
        private void DrawTransform(GUIView view)
        {
            var model = selectedModel;
            var go = model?.obj as GameObject;
            if (go == null)
            {
                view.DrawLabel("モデルを選択してください", -1, ROW_HEIGHT);
                return;
            }

            var cache = view.GetTransformCache(go.transform);

            DrawVector3Row(view, "位置", PositionSensitivity, cache.position,
                (value, _) => { cache.position = value; cache.Apply(); },
                () => { cache.position = Vector3.zero; cache.Apply(); });

            // 回転は SelfModelPlacer のオイラー角キャッシュを使う。
            // ギズモ操作分も軸単位で足し込まれるため、ハンドル操作が該当軸の数値だけを動かす
            DrawVector3Row(view, "回転", RotationSensitivity, placer.GetEulerAngles(model),
                (value, _) => placer.SetEulerAngles(model, value),
                () => placer.SetEulerAngles(model, Vector3.zero));

            DrawVector3Row(view, "拡縮", ScaleSensitivity, cache.scale,
                (value, index) => ApplyScale(cache, value, index),
                () => { cache.scale = Vector3.one; cache.Apply(); });

            DrawScaleLinkRow(view);

            DrawAttachRow(view, model);
        }

        /// <summary>
        /// アタッチ先の選択行。対象メイドは編集中のメイド固定
        /// </summary>
        private void DrawAttachRow(GUIView view, StudioModelStatWrapper model)
        {
            view.BeginHorizontal();
            {
                view.DrawLabel("アタッチ", LABEL_WIDTH, ROW_HEIGHT, style: GUIView.gsLabelRight);

                // 現在のアタッチ状態をコンボに反映する（見つからなければ「なし」）
                var state = placer.GetAttachState(model);
                var boneName = state != null ? state.boneName : null;
                _attachPointComboBox.currentIndex = Mathf.Max(0,
                    SelfModelPlacer.AttachPoints.FindIndex(p => p.boneName == boneName));

                _attachPointComboBox.onSelected = (point, _) =>
                    placer.Attach(model, modItemManager.currentMaid, point);
                _attachPointComboBox.DrawButton(view);
            }
            view.EndLayout();
        }

        /// <summary>
        /// 拡縮の連動トグル。オンにすると 1 軸の変更を他軸へ比率で波及させる。
        /// 拡縮行に並べると幅が足りないため独立した行にしている
        /// </summary>
        private void DrawScaleLinkRow(GUIView view)
        {
            view.BeginHorizontal();
            {
                // 拡縮行と列を揃えるためラベル幅分を空ける
                view.DrawLabel("", LABEL_WIDTH, ROW_HEIGHT);

                view.DrawToggle("XYZ連動", _scaleLinked, 100, ROW_HEIGHT,
                    value => _scaleLinked = value);
            }
            view.EndLayout();
        }

        /// <summary>
        /// 拡縮を適用する。連動中は変更軸の変化率を全軸に掛けて、元の比率を保ったまま拡縮する
        /// </summary>
        private void ApplyScale(TransformCache cache, Vector3 value, int index)
        {
            if (_scaleLinked)
            {
                var oldValue = cache.scale[index];
                if (Mathf.Abs(oldValue) > ScaleLinkEpsilon)
                {
                    value = cache.scale * (value[index] / oldValue);
                }
                else
                {
                    // 0 付近は比率が求まらないため、このときだけ全軸を同じ値にそろえる
                    value = Vector3.one * value[index];
                }
            }

            cache.scale = value;
            cache.Apply();
        }

        /// <summary>
        /// ラベル + XYZ（ドラッグラベル + 数値入力）+ リセットボタンの1行を描画。
        /// onChanged には変更後の値と、変更された軸のインデックスを渡す（連動処理で使う）
        /// </summary>
        private void DrawVector3Row(
            GUIView view,
            string label,
            float dragSensitivity,
            Vector3 value,
            Action<Vector3, int> onChanged,
            Action onReset)
        {
            view.BeginHorizontal();
            {
                view.DrawLabel(label, LABEL_WIDTH, ROW_HEIGHT, style: GUIView.gsLabelRight);

                for (var i = 0; i < 3; i++)
                {
                    var index = i;

                    view.DrawDragLabel(AxisNames[index], 14, ROW_HEIGHT, dragSensitivity, delta =>
                    {
                        value[index] += delta;
                        onChanged(value, index);
                    });

                    // FloatFieldOption.label はフィールド左のラベル描画も兼ねるため、
                    // XYZ を並べるここでは label を渡さずキャッシュだけ自前で取る
                    var fieldCache = view.GetFieldCache(label + index, FloatFieldType.F3);
                    fieldCache.UpdateValue(value[index]);

                    view.DrawFloatField(new GUIView.FloatFieldOption
                    {
                        value = value[index],
                        width = 62,
                        height = ROW_HEIGHT,
                        fieldCache = fieldCache,
                        onChanged = newValue =>
                        {
                            value[index] = newValue;
                            onChanged(value, index);
                        },
                    });
                }

                if (view.DrawButton("R", 20, ROW_HEIGHT))
                {
                    onReset();
                }
            }
            view.EndLayout();
        }
    }
}
