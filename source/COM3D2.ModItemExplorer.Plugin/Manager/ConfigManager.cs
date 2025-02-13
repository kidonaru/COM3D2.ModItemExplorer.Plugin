using System;
using System.IO;
using System.Xml.Serialization;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.ModItemExplorer.Plugin
{
    public class ConfigManager : ManagerBase
    {
        private Config _config = new Config();
        public Config config => _config;

        private static ConfigManager _instance = null;
        public static ConfigManager instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new ConfigManager();
                }
                return _instance;
            }
        }

        private bool _isLoaded = false;

        private ConfigManager()
        {
        }

        public override void Init()
        {
            if (!_isLoaded)
            {
                LoadConfigXml();
                SaveConfigXml();
            }
        }

        public override void Update()
        {
            if (config.dirty && Input.GetMouseButtonUp(0))
            {
                SaveConfigXml();
            }
        }

        public void LoadConfigXml()
        {
            try
            {
                var path = PluginUtils.ConfigPath;
                if (!File.Exists(path))
                {
                    return;
                }

                var serializer = new XmlSerializer(typeof(Config));
                using (var stream = new FileStream(path, FileMode.Open))
                {
                    _config = (Config)serializer.Deserialize(stream);
                    _config.ConvertVersion();
                }

                _isLoaded = true;
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }
        }

        public void SaveConfigXml()
        {
            MTEUtils.LogDebug("設定保存中...");
            try
            {
                config.dirty = false;

                var path = PluginUtils.ConfigPath;
                var serializer = new XmlSerializer(typeof(Config));
                using (var stream = new FileStream(path, FileMode.Create))
                {
                    serializer.Serialize(stream, config);
                }
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }
        }

        public void ResetConfig()
        {
            _config = new Config();
            SaveConfigXml();
        }
    }
}