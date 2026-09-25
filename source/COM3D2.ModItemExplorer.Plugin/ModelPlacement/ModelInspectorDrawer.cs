using COM3D2.MotionTimelineEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace COM3D2.ModItemExplorer.Plugin
{
    /// <summary>
    /// SceneEditor Inspector へ委譲描画する MTE 管理モデルの内容。
    /// 行の委譲に対応したホストでは、共通のモデル表示 (ヘッダー・管理行・アタッチ・Transform) を
    /// ホストが描き、こちらは末尾のレイヤー行だけを足す (DrawRows)。
    /// 旧ホストでは内容を丸ごと描く (Draw)。Transform 行・アタッチ行は ModelOperationWindow と同じ部品で描く。
    /// アタッチのドロップダウンは MTE 側の ComboBoxPopupWindow が独立ウィンドウとして
    /// 出すため、ボタン座標をスクリーン座標へ直す基準として SceneEditor のウィンドウ矩形を借りる
    /// </summary>
    public class ModelInspectorDrawer
    {
        private const float LabelWidth = 40f;
        private const float RowHeight = 20f;

        private readonly GUIView _view = new GUIView();

        /// <summary>
        /// DrawRows 用。ホストのスクロールビュー内の座標だけを共有する別ビューで、
        /// Draw 側の _view とは初期化のタイミングが違うため分ける
        /// </summary>
        private readonly GUIView _rowsView = new GUIView();

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
        /// InspectorHost の draw。contentRect は SceneEditor Inspector のウィンドウローカル領域。
        /// ヘッダー行は drawsHeader: true で登録してこちらのスクロールビュー内へ描くため、
        /// そのぶんを引かない領域が渡ってくる (旧ホストではヘッダー行の下の残り領域)
        /// </summary>
        public void Draw(GameObject go, Rect contentRect)
        {
            var model = placer.FindModelByGameObject(go);
            if (model == null)
            {
                return;
            }

            _view.Init(contentRect);

            // 委譲領域は Inspector を縮めると内容より狭くなる。
            // スクロールは委譲先の責務なのでここで自前で掛ける
            _view.BeginScrollView(-1, -1, GUIView.AutoScrollViewRect, false, true);

            // ギズモ行・表示対象行・オブジェクト行の中身を描くのはホスト
            // (SceneEditor Inspector)。内容と一緒にスクロールさせるため、
            // スクロールビューの先頭という位置だけをこちらで決める。
            // 操作結果は GizmoToolClient の同期で placer 側へ反映される。
            // 旧ホストでは 0 が返り、ヘッダーは委譲領域の外へ固定表示される
            var headerHeight = InspectorHostClient.DrawHeader(go, _view.GetDrawRect(-1, 0f));
            if (headerHeight > 0f)
            {
                // DrawHeader はホスト側の別ビューで描くためこちらのレイアウトは進まない
                _view.DrawEmpty(-1, headerHeight);
            }

            ModelTransformRowDrawer.Draw(_view, model, go, LabelWidth, RowHeight);

            DrawAttachRow(model);
            DrawLayerRow(_view, model);

            _view.EndScrollView();

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
        /// InspectorHost の drawRows。ホストが描く共通のモデル表示の末尾へ、
        /// ModItemExplorer 固有のレイヤー行だけを足す。戻り値は使った高さ (末尾の余白を含まない)
        /// </summary>
        public float DrawRows(GameObject go, Rect rect)
        {
            var model = placer.FindModelByGameObject(go);
            if (model == null)
            {
                return 0f;
            }

            // ホストが確保した矩形をそのまま使う (内側で二重に余白を取らない)
            _rowsView.padding = Vector2.zero;
            _rowsView.Init(rect);

            DrawLayerRow(_rowsView, model);

            // EndLayout 後の currentPos.y は最後の要素の下端 + margin なので、
            // ホストが余白を重ねないよう 1 個ぶん差し引いて返す
            return Mathf.Max(0f, _rowsView.currentPos.y - _rowsView.margin);
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
                    placer.AttachFromUI(model, modItemManager.currentMaid, point);
                _attachPointComboBox.DrawButton(_view);
            }
            _view.EndLayout();
        }

        /// <summary>
        /// モデルを載せるレイヤーの切替行
        /// </summary>
        private void DrawLayerRow(GUIView view, StudioModelStatWrapper model)
        {
            ModelLayerRowDrawer.Draw(view, new ModelLayerRowOption
            {
                labelWidth = LabelWidth + 20,
                height = RowHeight,
                getLayerType = () => placer.GetLayerType(model),
                setLayerType = value => placer.SetLayerType(model, value),
            });
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
