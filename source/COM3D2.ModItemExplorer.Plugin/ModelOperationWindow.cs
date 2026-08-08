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
        public readonly static int WINDOW_WIDTH = 380;
        public readonly static int WINDOW_HEIGHT = 360;
        public readonly static int HEADER_HEIGHT = 20;
        public readonly static int MODEL_LIST_HEIGHT = 120;
        public readonly static int ROW_HEIGHT = 20;

        /// <summary>全行共通のラベル幅。列を揃えるためどの行もこの幅を使う</summary>
        public readonly static int LABEL_WIDTH = 70;

        /// <summary>プリセット一覧の表示高さ</summary>
        public readonly static int PRESET_LIST_HEIGHT = 250;

        /// <summary>ウィンドウ内のタブ</summary>
        private enum TabType
        {
            操作,
            プリセット,
        }

        private TabType _tabType = TabType.操作;

        /// <summary>ドラッグラベルの感度（1pxあたりの増減量）</summary>
        private const float PositionSensitivity = 0.01f;
        private const float RotationSensitivity = 1f;
        private const float ScaleSensitivity = 0.01f;

        private static readonly string[] AxisNames = { "X", "Y", "Z" };

        private static WindowManager windowManager => WindowManager.instance;
        private static ModItemManager modItemManager => ModItemManager.instance;
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

        /// <summary>操作対象のモデル。破棄済みなら null に戻す</summary>
        private StudioModelStatWrapper _selectedModel;
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
            set => _selectedModel = value;
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
        private GUIView _contentView = new GUIView();
        private bool _initializedGUI = false;

        public GUIStyle gsWin => GUIView.gsWin;

        public ModelOperationWindow()
        {
            this.windowIndex = 0;
            this.isShowWnd = false;
            this.windowRect = new Rect(
                Screen.width - WINDOW_WIDTH - 30,
                Screen.height - WINDOW_HEIGHT - 100,
                WINDOW_WIDTH,
                WINDOW_HEIGHT);
        }

        public void Init()
        {
        }

        public void InitView()
        {
            _rootView.Init(0, 0, WINDOW_WIDTH, WINDOW_HEIGHT);
            _contentView.Init(0, HEADER_HEIGHT, WINDOW_WIDTH, WINDOW_HEIGHT - HEADER_HEIGHT);

            _contentView.parent = _rootView;
        }

        public void Update()
        {
            // ギズモ回転のオイラー角正規化はウィンドウ非表示中も回す
            // （他の編集モードでもギズモ自体は操作できるため）
            placer.Update();

            // 編集モードがモデルの間だけ表示する
            var showWnd = windowManager.modItemWindow != null
                && windowManager.modItemWindow.isModelMode;

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
        }

        public void OnLoad()
        {
            MTEUtils.AdjustWindowPosition(ref _windowRect);
        }

        public void OnScreenSizeChanged()
        {
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

            DrawContent();

            _rootView.DrawComboBox();

            GUI.DragWindow();
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

            view.BeginScrollView(-1, PRESET_LIST_HEIGHT, GUIView.AutoScrollViewRect, false, true);

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
                        placer.LoadPreset(name);
                        modItemManager.UpdateModelItems();
                        selectedModel = null;
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
        /// 配置中のモデル一覧。表示切替・選択・削除を行う
        /// </summary>
        private void DrawModelList(GUIView view)
        {
            var models = placer.modelList;

            view.BeginScrollView(-1, MODEL_LIST_HEIGHT, GUIView.AutoScrollViewRect, false, true);

            if (models.Count == 0)
            {
                view.DrawLabel("配置モデルがありません", -1, ROW_HEIGHT);
            }

            foreach (var model in models)
            {
                view.BeginHorizontal();
                {
                    view.DrawToggle(model.visible, 20, ROW_HEIGHT,
                        value => placer.SetVisible(model, value));

                    // 選択状態は文字色で表す（トグルを2つ並べると視覚ノイズになるため）
                    var selected = model == selectedModel;
                    view.DrawLabel(model.displayName, 270, ROW_HEIGHT,
                        textColor: selected ? Color.green : Color.white,
                        onClickAction: () => selectedModel = model);

                    if (view.DrawButton("x", 20, ROW_HEIGHT))
                    {
                        placer.DeleteModel(model);
                        if (model == selectedModel)
                        {
                            selectedModel = null;
                        }
                        modItemManager.UpdateModelItems();
                    }
                }
                view.EndLayout();
            }

            view.EndScrollView();
        }

        /// <summary>
        /// ギズモの操作種別。dragType は配置モデル全体で共有される
        /// </summary>
        private void DrawGizmoRow(GUIView view)
        {
            view.BeginHorizontal();
            {
                view.DrawLabel("ギズモ", LABEL_WIDTH, ROW_HEIGHT, style: GUIView.gsLabelRight);

                view.DrawToggle("移動", placer.dragType == SelfModelPlacer.GizmoDragType.Move,
                    60, ROW_HEIGHT,
                    _ => placer.dragType = SelfModelPlacer.GizmoDragType.Move);
                view.DrawToggle("回転", placer.dragType == SelfModelPlacer.GizmoDragType.Rotate,
                    60, ROW_HEIGHT,
                    _ => placer.dragType = SelfModelPlacer.GizmoDragType.Rotate);
                view.DrawToggle("拡縮", placer.dragType == SelfModelPlacer.GizmoDragType.Scale,
                    60, ROW_HEIGHT,
                    _ => placer.dragType = SelfModelPlacer.GizmoDragType.Scale);

                // dragType は配置モデル全体に一括で効くことを明示する
                view.DrawLabel("(全モデル共通)", -1, ROW_HEIGHT, textColor: Color.gray);
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
                value => { cache.position = value; cache.Apply(); },
                () => { cache.position = Vector3.zero; cache.Apply(); });

            // 回転は SelfModelPlacer のオイラー角キャッシュを使う。
            // ギズモ操作分も軸単位で足し込まれるため、ハンドル操作が該当軸の数値だけを動かす
            DrawVector3Row(view, "回転", RotationSensitivity, placer.GetEulerAngles(model),
                value => placer.SetEulerAngles(model, value),
                () => placer.SetEulerAngles(model, Vector3.zero));

            DrawVector3Row(view, "拡縮", ScaleSensitivity, cache.scale,
                value => { cache.scale = value; cache.Apply(); },
                () => { cache.scale = Vector3.one; cache.Apply(); });

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
        /// ラベル + XYZ（ドラッグラベル + 数値入力）+ リセットボタンの1行を描画
        /// </summary>
        private void DrawVector3Row(
            GUIView view,
            string label,
            float dragSensitivity,
            Vector3 value,
            Action<Vector3> onChanged,
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
                        onChanged(value);
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
                            onChanged(value);
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
