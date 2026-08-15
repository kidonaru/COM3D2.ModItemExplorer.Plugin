using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.ModItemExplorer.Plugin
{
    /// <summary>
    /// 配置モデルの位置・回転・拡縮の 3 行。モデル操作ウィンドウと
    /// EW Inspector への委譲描画で同じ見た目・同じ感度を使うために共通化している
    /// </summary>
    public static class ModelTransformRowDrawer
    {
        /// <summary>ドラッグラベルの感度 (1px あたりの増減量)</summary>
        public const float PositionSensitivity = 0.01f;
        public const float RotationSensitivity = 1f;
        public const float ScaleSensitivity = 0.01f;

        /// <summary>連動時に比率計算をあきらめる拡縮値のしきい値</summary>
        private const float ScaleLinkEpsilon = 0.0001f;

        /// <summary>
        /// 拡縮の XYZ を連動させるか。操作ウィンドウと EW Inspector のどちらから
        /// 切り替えても同じ状態を見せるため共有する。設定には保存せずセッション内のみ保持する。
        /// 書き換えを連動トグルの操作だけに限るため外部へは公開しない
        /// </summary>
        private static bool _scaleLinked = false;

        private static SelfModelPlacer placer => SelfModelPlacer.instance;

        /// <summary>
        /// 3 行をまとめて描画する。拡縮行の末尾には常に XYZ 連動トグルを出す
        /// </summary>
        public static void Draw(
            GUIView view,
            StudioModelStatWrapper model,
            GameObject go,
            float labelWidth,
            float rowHeight,
            GUIStyle labelStyle = null)
        {
            var cache = view.GetTransformCache(go.transform);

            view.DrawVector3Row(new GUIView.Vector3RowOption
            {
                label = "位置",
                labelWidth = labelWidth,
                labelStyle = labelStyle,
                height = rowHeight,
                dragSensitivity = PositionSensitivity,
                value = cache.position,
                onChanged = value => { cache.position = value; cache.Apply(); },
                onReset = () => { cache.position = Vector3.zero; cache.Apply(); },
            });

            // 回転は SelfModelPlacer のオイラー角キャッシュを使う。
            // ギズモ操作分も軸単位で足し込まれるため、ハンドル操作が該当軸の数値だけを動かす
            view.DrawVector3Row(new GUIView.Vector3RowOption
            {
                label = "回転",
                labelWidth = labelWidth,
                labelStyle = labelStyle,
                height = rowHeight,
                dragSensitivity = RotationSensitivity,
                value = placer.GetEulerAngles(model),
                onChanged = value => placer.SetEulerAngles(model, value),
                onReset = () => placer.SetEulerAngles(model, Vector3.zero),
            });

            view.DrawVector3Row(new GUIView.Vector3RowOption
            {
                label = "拡縮",
                labelWidth = labelWidth,
                labelStyle = labelStyle,
                height = rowHeight,
                dragSensitivity = ScaleSensitivity,
                value = cache.scale,
                onChangedAxis = (value, index) => ApplyScale(cache, value, index),
                onReset = () => { cache.scale = Vector3.one; cache.Apply(); },
                linkIcon = PluginInfo.LinkIconTexture,
                linked = _scaleLinked,
                onLinkChanged = value => _scaleLinked = value,
            });
        }

        /// <summary>
        /// 拡縮を適用する。連動中は変更軸の変化率を全軸に掛けて、元の比率を保ったまま拡縮する
        /// </summary>
        private static void ApplyScale(TransformCache cache, Vector3 value, int index)
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
    }
}
