using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace COM3D2.ModItemExplorer.Plugin
{
    /// <summary>
    /// 配置モデルの操作ウィンドウ。編集モードがモデルのときのみ表示される
    /// </summary>
    public class ModelOperationWindow : DockableWindowBase
    {
        public readonly static int WINDOW_ID = 582880;

        /// <summary>ウィンドウの最小サイズ。Transform 行が折り返さない幅を下限にする</summary>
        public readonly static int MIN_WINDOW_WIDTH = 380;
        public readonly static int MIN_WINDOW_HEIGHT = 340;

        public readonly static int ROW_HEIGHT = 20;

        /// <summary>モデル一覧の行の高さ。サムネを載せるため通常行より大きくする</summary>
        public readonly static int MODEL_ROW_HEIGHT = 36;

        /// <summary>ウィンドウを縮めてもモデル一覧に最低限残す高さ</summary>
        public readonly static int MIN_MODEL_LIST_HEIGHT = MODEL_ROW_HEIGHT * 2;

        /// <summary>
        /// モデル一覧より下に確保する行数（ギズモ + 表示対象 + 位置・回転・拡縮 + アタッチ）。
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

        private static WindowManager windowManager => WindowManager.instance;
        private static ModItemManager modItemManager => ModItemManager.instance;
        private static TextureManager textureManager => TextureManager.instance;
        private static SelfModelPlacer placer => SelfModelPlacer.instance;
        private static Config config => ConfigManager.instance.config;

        protected override int windowId => WINDOW_ID;
        protected override string windowTitle => "モデル操作";
        protected override int minWidth => MIN_WINDOW_WIDTH;
        protected override int minHeight => MIN_WINDOW_HEIGHT;

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
        private GUIView _contentView = new GUIView();

        private int _windowWidth = MIN_WINDOW_WIDTH;
        private int _windowHeight = MIN_WINDOW_HEIGHT;

        public GUIStyle gsWin => GUIView.gsWin;

        public ModelOperationWindow()
        {
            this.windowIndex = 0;
        }

        protected override void LoadPlacement(out int x, out int y, out int width, out int height)
        {
            x = config.modelOperationWindowPosX;
            y = config.modelOperationWindowPosY;
            width = config.modelOperationWindowWidth;
            height = config.modelOperationWindowHeight;
        }

        protected override void StorePlacement(int x, int y, int width, int height)
        {
            config.modelOperationWindowPosX = x;
            config.modelOperationWindowPosY = y;
            config.modelOperationWindowWidth = width;
            config.modelOperationWindowHeight = height;
            config.dirty = true;
        }

        public override void Init()
        {
            base.Init();

            _windowWidth = (int)windowRect.width;
            _windowHeight = (int)windowRect.height;
            InitView();
        }

        protected override void OnSizeChanged(int width, int height)
        {
            _windowWidth = width;
            _windowHeight = height;
            InitView();
        }

        public void InitView()
        {
            var headerHeight = DockableWindowBase.HEADER_HEIGHT;

            _rootView.Init(0, 0, _windowWidth, _windowHeight);
            _contentView.Init(0, headerHeight, _windowWidth, _windowHeight - headerHeight);

            _contentView.parent = _rootView;
        }

        public override void Update()
        {
            base.Update();

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

        public override void Close()
        {
            base.Close();

            // isShowWnd は Update() が毎フレーム計算し直すため、こちらも落とす
            _userVisible = false;

            // プラグイン無効化時にも呼ばれるため、ギズモとハイライトをここで片付ける
            placer.isModelEditMode = false;
        }


        public override void OnChangedSceneLevel(Scene scene, LoadSceneMode sceneMode)
        {
            selectedModel = null;
        }

        protected override void DrawContent()
        {
            _rootView.ResetLayout();

            DrawMainContent();

            ComboBoxPopupWindow.instance.ProcessFocus(_rootView, this);
        }

        private void DrawMainContent()
        {
            var view = _contentView;
            view.ResetLayout();
            view.SetEnabled(!ComboBoxPopupWindow.instance.IsOpenFor(this));

            DrawTabRow(view);

            if (_tabType == TabType.操作)
            {
                DrawModelList(view);

                view.DrawHorizontalLine();

                DrawGizmoRow(view);
                DrawGizmoTargetRow(view);
                DrawTransform(view);
            }
            else
            {
                DrawPreset(view);
            }
        }

        /// <summary>
        /// タブ1つ分の幅。リセットボタンはウィンドウ右端に固定で置くため、
        /// TAB_WIDTH * タブ数 + RESET_BUTTON_WIDTH が MIN_WINDOW_WIDTH に収まる範囲でタブを増やすこと
        /// </summary>
        private readonly static int TAB_WIDTH = 80;

        /// <summary>リセット（全削除）ボタンの幅</summary>
        private readonly static int RESET_BUTTON_WIDTH = 60;

        /// <summary>リセットの確認を待つ秒数。この間に再度押されたら実行する</summary>
        private readonly static float RESET_CONFIRM_DURATION = 3f;

        /// <summary>リセットの確認待ちが切れる時刻。0 なら確認待ちではない</summary>
        private float _resetConfirmTime = 0f;

        /// <summary>
        /// タブ行。右端にはタブと関係なく効くリセット（全削除）ボタンを寄せて置く
        /// </summary>
        private void DrawTabRow(GUIView view)
        {
            view.BeginHorizontal();
            {
                _tabType = view.DrawTabs(_tabType, TAB_WIDTH, ROW_HEIGHT);

                view.currentPos.x = view.viewRect.width - view.padding.x * 2 - RESET_BUTTON_WIDTH;

                DrawResetButton(view);
            }
            view.EndLayout();

            // DrawTabs が単独行のときに末尾へ入れる余白（GUIView.DrawTabs 内の AddSpace(5) と同値）。
            // 横並びにすると x 方向へ消えるため縦に入れ直す
            view.AddSpace(5);
        }

        /// <summary>
        /// 配置モデルを全削除するボタン。DeleteAll は履歴ごと捨てて取り消せないため、
        /// 誤クリックを弾くよう 2 度押しを要求する
        /// </summary>
        private void DrawResetButton(GUIView view)
        {
            var confirming = Time.realtimeSinceStartup < _resetConfirmTime;

            var pressed = view.DrawButton(
                confirming ? "本当に?" : "リセット",
                RESET_BUTTON_WIDTH, ROW_HEIGHT,
                placer.modelCount > 0,
                confirming ? Color.red : (Color?)null);

            if (!pressed)
            {
                return;
            }

            if (!confirming)
            {
                _resetConfirmTime = Time.realtimeSinceStartup + RESET_CONFIRM_DURATION;
                return;
            }

            _resetConfirmTime = 0f;

            // 選択の解除は SelfModelPlacer.DeleteAll が行う
            placer.DeleteAll();
            modItemManager.UpdateModelItems();
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
            // 区切り線 + ギズモ行 + 表示対象行 + Transform 各行。いずれも後ろに margin が付く
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
                        textColor: selected ? GUIView.option.accentColor : Color.white,
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
        /// ギズモの操作種別と軸空間。dragType は配置モデル全体で共有される
        /// </summary>
        private void DrawGizmoRow(GUIView view)
        {
            GizmoToolRowDrawer.Draw(view, new GizmoToolRowOption
            {
                labelWidth = LABEL_WIDTH,
                height = ROW_HEIGHT,
                labelStyle = GUIView.gsLabelRight,
                getTool = () => SelfModelPlacer.ToGizmoTool(placer.dragType),
                setTool = tool => placer.dragType = SelfModelPlacer.FromGizmoTool(tool),
                getUseLocalSpace = () => placer.useLocalSpace,
                setUseLocalSpace = value => placer.useLocalSpace = value,
            });
        }

        private readonly static SelfModelPlacer.GizmoTargetType[] GIZMO_TARGET_TYPES =
        {
            SelfModelPlacer.GizmoTargetType.All,
            SelfModelPlacer.GizmoTargetType.Selected,
        };

        private readonly static string[] GIZMO_TARGET_NAMES = { "すべて表示", "選択中" };

        /// <summary>ギズモの表示対象を選ぶ幅。最長の「すべて表示」が収まる幅にする</summary>
        private readonly static int GIZMO_TARGET_BUTTON_WIDTH = 80;

        /// <summary>
        /// ギズモを表示する対象の切替行。設定は配置モデル全体で共有される
        /// </summary>
        private void DrawGizmoTargetRow(GUIView view)
        {
            view.BeginHorizontal();
            {
                view.DrawLabel("表示対象", LABEL_WIDTH, ROW_HEIGHT, style: GUIView.gsLabelRight);

                var current = placer.gizmoTargetType;
                for (var i = 0; i < GIZMO_TARGET_TYPES.Length; i++)
                {
                    var targetType = GIZMO_TARGET_TYPES[i];
                    view.DrawToggle(GIZMO_TARGET_NAMES[i], current == targetType,
                        GIZMO_TARGET_BUTTON_WIDTH, ROW_HEIGHT,
                        // 選択中の項目を再度押しても解除しない（ギズモ行と同じ規約）
                        on => { if (on) placer.gizmoTargetType = targetType; });
                }
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

            ModelTransformRowDrawer.Draw(view, model, go, LABEL_WIDTH, ROW_HEIGHT,
                labelStyle: GUIView.gsLabelRight);

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

                _attachPointComboBox.currentIndex = placer.GetAttachPointIndex(model);

                _attachPointComboBox.onSelected = (point, _) =>
                    placer.Attach(model, modItemManager.currentMaid, point);
                _attachPointComboBox.DrawButton(view);
            }
            view.EndLayout();
        }
    }
}
