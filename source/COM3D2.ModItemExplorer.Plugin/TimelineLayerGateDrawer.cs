using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.ModItemExplorer.Plugin
{
    /// <summary>
    /// SceneEditor のウィンドウ内レイヤーゲートを本プラグインのウィンドウで再現する。
    /// タイムライン読込中にレイヤー未登録なら注意文と追加ボタンを描き、以降の項目を強制無効にする。
    /// 判定・文言・追加処理は TimelineLayerGateClient 経由で SceneEditor 側に任せる。
    /// SceneEditor 不在・タイムライン未読込では何も描かず従来表示のまま。
    ///
    /// Begin で無効化したら同じ描画パス内で必ず End を呼ぶこと。
    /// SetEnabled はグローバル GUI.enabled を書き換えるため、戻し忘れると
    /// 後に描かれる ComboBoxPopupWindow まで操作できなくなる。
    /// タブのボタンはゲートの対象外にする (無効化すると抜けられなくなる)。
    ///
    /// SceneEditor 側 TimelineLayerGate と異なり、メイド単位レイヤーの分岐や
    /// drawNotice による注意文の重複抑制は持たない (対象レイヤーはメイド非依存かつ
    /// 呼び出しが 1 箇所のみのため)
    /// </summary>
    public static class TimelineLayerGateDrawer
    {
        private const float BUTTON_WIDTH = 220f;

        public static void Begin(GUIView view, string layerName, float rowHeight)
        {
            var state = TimelineLayerGateClient.GetState(layerName);
            switch (state)
            {
                case TimelineLayerGateClient.StateNoTimeline:
                case TimelineLayerGateClient.StateReady:
                    return;

                case TimelineLayerGateClient.StateMissing:
                    DrawMissing(view, layerName, rowHeight);
                    Disable(view);
                    return;

                default:
                    // メイド不在 (本プラグインのレイヤーでは起きない) や未知の値は無効化だけ行う
                    Disable(view);
                    return;
            }
        }

        /// <summary>強制無効を解除して有効へ戻す。冪等</summary>
        public static void End(GUIView view)
        {
            view.forceDisabled = false;
            view.SetEnabled(true);
        }

        private static void DrawMissing(GUIView view, string layerName, float rowHeight)
        {
            view.DrawLabel(TimelineLayerGateClient.GetNoticeText(layerName), -1, rowHeight,
                textColor: Color.yellow);

            // ボタンは強制無効の前に描く (押せる必要がある)
            if (view.DrawButton(TimelineLayerGateClient.GetAddButtonText(layerName), BUTTON_WIDTH, rowHeight))
            {
                TimelineLayerGateClient.AddLayer(layerName);
            }

            view.DrawHorizontalLine(Color.gray);
            view.AddSpace(5);
        }

        private static void Disable(GUIView view)
        {
            view.forceDisabled = true;
            view.SetEnabled(false);
        }
    }
}
