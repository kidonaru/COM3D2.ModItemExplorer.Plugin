using System;
using System.IO;
using System.Reflection;
using COM3D2.MotionTimelineEditor;

namespace COM3D2.ModItemExplorer.Plugin
{
    public static class PluginUtils
    {
        public static readonly string UserDataPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "Config");

        public const string PluginVersion = PluginInfo.PluginVersion;

        public static string ConfigPath
        {
            get => MTEUtils.CombinePaths(UserDataPath, PluginInfo.PluginName + ".xml");
        }

        public static string OfficialNameCsvPath
        {
            get => MTEUtils.CombinePaths(UserDataPath, PluginInfo.PluginName + "_OfficialName.csv");
        }

        public static string MenuCachePath
        {
            get => MTEUtils.CombinePaths(UserDataPath, PluginInfo.PluginName + "_MenuCache.dat");
        }

        /// <summary>選択履歴の保存先。件数が増えても設定XMLを膨らませないよう別ファイルにする</summary>
        public static string ItemHistoryPath
        {
            get => MTEUtils.CombinePaths(UserDataPath, PluginInfo.PluginName + "_History.xml");
        }

        public static string PluginConfigDirPath
        {
            get
            {
                var path = MTEUtils.CombinePaths(UserDataPath, PluginInfo.PluginName);
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }

                return path;
            }
        }

        /// <summary>配置プリセットの保存先フォルダ。名前ごとに1ファイル置く</summary>
        public static string ModelPresetDirPath
        {
            get
            {
                var path = MTEUtils.CombinePaths(PluginConfigDirPath, "ModelPresets");
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }

                return path;
            }
        }
    }
}