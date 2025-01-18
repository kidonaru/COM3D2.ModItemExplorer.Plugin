using System;
using System.Collections.Generic;
using System.IO;
using COM3D2.MotionTimelineEditor;
using UnityEngine.SceneManagement;

namespace COM3D2.ModItemExplorer.Plugin
{
    public class TempPresetManager : ManagerBase
    {
        private Dictionary<Maid, List<CharacterMgr.Preset>> _tempPresetsMap = new Dictionary<Maid, List<CharacterMgr.Preset>>();

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

        public List<CharacterMgr.Preset> GetPresets(Maid maid)
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
            var binaryReader = new BinaryReader(new MemoryStream(buffer));
            var preset = characterMgr.PresetLoad(binaryReader, string.Empty);
            binaryReader.Close();

            preset.strFileName = DateTime.Now.ToString("MM-dd HH.mm.ss");

            GetPresets(maid).Insert(0, preset);
        }

        public override void OnChangedSceneLevel(Scene scene, LoadSceneMode sceneMode)
        {
            if (plugin.isEnable)
            {
                return;
            }

            foreach (var presets in _tempPresetsMap.Values)
            {
                foreach (var preset in presets)
                {
                    UnityEngine.Object.Destroy(preset.texThum);
                }
            }

            _tempPresetsMap.Clear();
        }
    }
}