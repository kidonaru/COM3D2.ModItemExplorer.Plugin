using System;
using System.IO;
using System.Reflection;
using COM3D2.MotionTimelineEditor;

namespace COM3D2.ModItemExplorer.Plugin
{
    public static class PluginUtils
    {
        //public static readonly string UserDataPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "Config");
        public static readonly string UserDataPath = BepInEx.Paths.ConfigPath;

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
    }
}