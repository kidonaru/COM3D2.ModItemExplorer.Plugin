using System;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.ModItemExplorer.Plugin
{
    /// <summary>レイヤー切替行の設定。状態は持たず、取得・変更はデリゲートで注入する</summary>
    public struct ModelLayerRowOption
    {
        /// <summary>行頭のラベル。null なら「レイヤー」</summary>
        public string label;
        public float labelWidth;
        /// <summary>行の高さ。0 なら 20</summary>
        public float height;
        /// <summary>ラベルのスタイル。null なら既定</summary>
        public GUIStyle labelStyle;
        public Func<ModelLayerType> getLayerType;
        public Action<ModelLayerType> setLayerType;
    }

    /// <summary>
    /// 配置モデルのレイヤー (Default/Charactor) の切替行。
    /// モデル操作ウィンドウ・SceneEditor Inspector への委譲描画・設定タブで共通に使う
    /// </summary>
    public static class ModelLayerRowDrawer
    {
        private static readonly ModelLayerType[] Types =
            { ModelLayerType.Default, ModelLayerType.Charactor };

        private static readonly string[] Names = { "Default", "Charactor" };

        /// <summary>切替ボタンの幅。最長の「Charactor」が収まる幅にする</summary>
        public static readonly float ButtonWidth = 80f;

        public static void Draw(GUIView view, ModelLayerRowOption option)
        {
            var height = option.height > 0f ? option.height : 20f;

            view.BeginHorizontal();
            {
                view.DrawLabel(option.label ?? "レイヤー", option.labelWidth, height,
                    style: option.labelStyle);

                var current = option.getLayerType();
                for (var i = 0; i < Types.Length; i++)
                {
                    var layerType = Types[i];
                    view.DrawToggle(Names[i], current == layerType, ButtonWidth, height,
                        // 選択中の項目を再度押しても解除しない (表示対象行と同じ規約)
                        on => { if (on) option.setLayerType(layerType); });
                }
            }
            view.EndLayout();
        }
    }
}
