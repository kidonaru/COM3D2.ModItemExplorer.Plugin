using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.ModItemExplorer.Plugin
{
    /// <summary>
    /// ギズモの表示対象 (すべて/選択中) の切替行。設定は配置モデル全体で共有される。
    /// モデル操作ウィンドウと SceneEditor Inspector への委譲描画で共通に使う
    /// </summary>
    public static class GizmoTargetRowDrawer
    {
        private static readonly SelfModelPlacer.GizmoTargetType[] Types =
        {
            SelfModelPlacer.GizmoTargetType.All,
            SelfModelPlacer.GizmoTargetType.Selected,
        };

        private static readonly string[] Names = { "すべて表示", "選択中" };

        /// <summary>表示対象を選ぶボタンの幅。最長の「すべて表示」が収まる幅にする</summary>
        public static readonly float ButtonWidth = 80f;

        public static void Draw(GUIView view, float labelWidth, float height, GUIStyle labelStyle = null)
        {
            var placer = SelfModelPlacer.instance;

            view.BeginHorizontal();
            {
                view.DrawLabel("表示対象", labelWidth, height, style: labelStyle);

                var current = placer.gizmoTargetType;
                for (var i = 0; i < Types.Length; i++)
                {
                    var targetType = Types[i];
                    view.DrawToggle(Names[i], current == targetType, ButtonWidth, height,
                        // 選択中の項目を再度押しても解除しない（ギズモ行と同じ規約）
                        on => { if (on) placer.gizmoTargetType = targetType; });
                }
            }
            view.EndLayout();
        }
    }
}
