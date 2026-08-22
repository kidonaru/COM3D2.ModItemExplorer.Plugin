using COM3D2.MotionTimelineEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace COM3D2.ModItemExplorer.Plugin
{
    /// <summary>
    /// SceneEditor Inspector へ委譲描画する MTE 管理モデルの内容。
    /// ギズモ行・表示対象行・Transform 行・アタッチ行は ModelOperationWindow と同じ部品で描く。
    /// アタッチのドロップダウンは MTE 側の ComboBoxPopupWindow が独立ウィンドウとして
    /// 出すため、ボタン座標をスクリーン座標へ直す基準として SceneEditor のウィンドウ矩形を借りる
    /// </summary>
    public class ModelInspectorDrawer
    {
        private const float LabelWidth = 40f;
        private const float RowHeight = 20f;

        private readonly GUIView _view = new GUIView();

        private readonly GUIComboBox<SelfModelPlacer.AttachPoint> _attachPointComboBox
            = new GUIComboBox<SelfModelPlacer.AttachPoint>
        {
            items = SelfModelPlacer.AttachPoints,
            getName = (point, _) => point.displayName,
            // ラベル + 前後送りボタンと合わせて Inspector 既定幅 (280) に収まるサイズ
            buttonSize = new Vector2(110, 20),
        };

        private readonly HostWindowProxy _hostWindow = new HostWindowProxy();

        private static SelfModelPlacer placer => SelfModelPlacer.instance;
        private static ModItemManager modItemManager => ModItemManager.instance;

        /// <summary>
        /// InspectorHost の canDraw。自プラグイン管理のモデルだけ引き受ける。
        /// ホスト経由の描画は Update とは独立に呼ばれるため、無効化中に委譲描画が
        /// 残らないよう入口でプラグイン有効状態も確認する (ModelGizmoManager と同じパターン)
        /// </summary>
        public bool CanDraw(GameObject go)
        {
            var plugin = ModItemExplorer.instance;
            if (plugin == null || !plugin.isEnable)
            {
                return false;
            }
            return placer.FindModelByGameObject(go) != null;
        }

        /// <summary>
        /// InspectorHost の draw。contentRect は SceneEditor Inspector のウィンドウローカル領域で、
        /// ホストが描くヘッダー行 (アクティブ・名前・フォーカス) を除いた残り領域
        /// </summary>
        public void Draw(GameObject go, Rect contentRect)
        {
            var model = placer.FindModelByGameObject(go);
            if (model == null)
            {
                return;
            }

            _view.Init(contentRect);

            GizmoToolRowDrawer.Draw(_view, new GizmoToolRowOption
            {
                labelWidth = LabelWidth,
                height = RowHeight,
                getTool = () => SelfModelPlacer.ToGizmoTool(placer.dragType),
                setTool = tool => placer.dragType = SelfModelPlacer.FromGizmoTool(tool),
                getUseLocalSpace = () => placer.useLocalSpace,
                setUseLocalSpace = value => placer.useLocalSpace = value,
            });

            GizmoTargetRowDrawer.Draw(_view, LabelWidth, RowHeight);

            ModelTransformRowDrawer.Draw(_view, model, go, LabelWidth, RowHeight);

            DrawAttachRow(model);

            if (InspectorHostClient.isWindowStateAvailable)
            {
                // ボタン押下で _view へ登録されたフォーカスを MTE 側のポップアップへ渡す。
                // ポップアップは MTE のウィンドウとして描かれるため、ボタン座標の基準に
                // ホスト (SceneEditor Inspector) のウィンドウ矩形を渡す
                ComboBoxPopupWindow.instance.ProcessFocus(
                    _view, _hostWindow, () => InspectorHostClient.hostWindowRect);
            }
            else
            {
                // ウィンドウ矩形を取れない旧バージョンの SceneEditor ではドロップダウンの位置を
                // 決められない。開かずに捨てて前後送りボタンだけで選ばせる
                _view.CancelFocusComboBox();
            }
        }

        /// <summary>
        /// アタッチ先の選択行。対象メイドは編集中のメイド固定 (操作ウィンドウと同じ)
        /// </summary>
        private void DrawAttachRow(StudioModelStatWrapper model)
        {
            _view.BeginHorizontal();
            {
                _view.DrawLabel("アタッチ", LabelWidth + 20, RowHeight);

                _attachPointComboBox.currentIndex = placer.GetAttachPointIndex(model);
                _attachPointComboBox.onSelected = (point, _) =>
                    placer.Attach(model, modItemManager.currentMaid, point);
                _attachPointComboBox.DrawButton(_view);
            }
            _view.EndLayout();
        }

        /// <summary>
        /// ComboBoxPopupWindow へ渡すホスト。SceneEditor のウィンドウは MTE 側の
        /// ウィンドウ管理下に無いため、ポップアップの生存判定に使う表示状態だけを
        /// 橋渡しする。他のメンバーは呼ばれないので空実装にしている
        /// </summary>
        private class HostWindowProxy : IGUIWindow
        {
            public int windowIndex { get; set; }

            public bool isShowWnd
            {
                get => InspectorHostClient.isHostWindowVisible;
                set { }
            }

            public Rect windowRect
            {
                get => InspectorHostClient.hostWindowRect;
                set { }
            }

            public void Init() { }
            public void Update() { }
            public void Close() { }
            public void OnLoad() { }
            public void OnScreenSizeChanged() { }
            public void OnChangedSceneLevel(Scene scene, LoadSceneMode sceneMode) { }
            public void OnGUI() { }
        }
    }
}
