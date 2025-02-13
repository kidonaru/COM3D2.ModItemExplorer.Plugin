using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using COM3D2.MotionTimelineEditor;
using UnityEngine.SceneManagement;

namespace COM3D2.ModItemExplorer.Plugin
{
    public class TempPreset
    {
        public CharacterMgr.Preset preset;
        public XmlDocument xmlMemory;
        public long lastWriteAt;
    }

    public class TempPresetManager : ManagerBase
    {
        private Dictionary<Maid, List<TempPreset>> _tempPresetsMap = new Dictionary<Maid, List<TempPreset>>();

        private static TempPresetManager _instance;
        public static TempPresetManager instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new TempPresetManager();
                }
                return _instance;
            }
        }

        public List<TempPreset> GetTempPresets(Maid maid)
        {
            return _tempPresetsMap.GetOrCreate(maid);
        }

        public void SavePresetCache(Maid maid, CharacterMgr.PresetType presetType)
        {
            if (maid == null)
            {
                return;
            }

            byte[] buffer = characterMgr.PresetSaveNotWriteFile(maid, presetType);
            var xmlMemory = ExPresetWrapper.xmlMemory;
            var binaryReader = new BinaryReader(new MemoryStream(buffer));
            var preset = characterMgr.PresetLoad(binaryReader, string.Empty);
            binaryReader.Close();

            var now = DateTime.Now;

            preset.strFileName = now.ToString("MM-dd HH.mm.ss");
            var lastWriteAt = now.Ticks;

            var tempPreset = new TempPreset
            {
                preset = preset,
                xmlMemory = xmlMemory,
                lastWriteAt = lastWriteAt
            };

            MTEUtils.LogDebug("SavePresetCache: strFileName={0} xmlMemory={1}", preset.strFileName, xmlMemory);

            GetTempPresets(maid).Insert(0, tempPreset);
        }

        public override void OnChangedSceneLevel(Scene scene, LoadSceneMode sceneMode)
        {
            if (plugin.isEnable)
            {
                return;
            }

            foreach (var tempPresets in _tempPresetsMap.Values)
            {
                foreach (var tempPreset in tempPresets)
                {
                    UnityEngine.Object.Destroy(tempPreset.preset.texThum);
                }
            }

            _tempPresetsMap.Clear();
        }
    }
}