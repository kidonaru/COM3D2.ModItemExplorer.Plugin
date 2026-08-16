using System;

namespace COM3D2.ModItemExplorer.Plugin
{
    /// <summary>
    /// SceneEditor プラグインのシーンプリセットプロバイダ規約用の属性。
    /// アセンブリ参照を避けるため、各プラグインが同名（短名一致）で自前定義する
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class ScenePresetProviderAttribute : Attribute
    {
    }

    /// <summary>
    /// 自前配置モデルの状態をシーンプリセット規約で公開するプロバイダ
    /// （規約の詳細は docs/external-plugin-api.md 参照）
    /// </summary>
    [ScenePresetProvider]
    public static class ModelPlacementPresetProvider
    {
        public static string PresetProviderId => "ModItemExplorer.ModelPlacement";

        public static string PresetProviderDisplayName => "モデル配置 (ModItemExplorer)";

        /// <summary>読込トグルなど幅の狭い場所で使われる短縮名</summary>
        public static string PresetProviderShortDisplayName => "モデル";

        /// <summary>現在の自前配置モデル一式を XML で返す。配置なし・失敗時は null</summary>
        public static string CapturePresetXml()
        {
            return SelfModelPlacer.instance.GetPlacementXml();
        }

        /// <summary>XML を現在のシーンへ反映する（既存の自前配置分は置き換え）</summary>
        public static bool ApplyPresetXml(string xml)
        {
            return Refresh(SelfModelPlacer.instance.ApplyPlacementXml(xml));
        }

        /// <summary>
        /// SceneCapture プリセット XML のうち Models セクションを反映する
        /// （既存の自前配置分は置き換え）。担当セクションが無ければ配置には触れず true
        /// </summary>
        public static bool ApplySceneCaptureXml(string xml)
        {
            return Refresh(SceneCaptureImporter.Apply(xml));
        }

        /// <summary>
        /// 配置が入れ替わってもモデル一覧の項目は自動では追従しないため、適用後に作り直す
        /// </summary>
        private static bool Refresh(bool applied)
        {
            if (applied)
            {
                ModItemManager.instance.UpdateModelItems();
            }
            return applied;
        }
    }
}
