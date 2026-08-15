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
            var tempPreset = CapturePreset(maid, presetType);
            if (tempPreset == null)
            {
                return;
            }

            MTEUtils.LogDebug("SavePresetCache: strFileName={0} xmlMemory={1}",
                tempPreset.preset.strFileName, tempPreset.xmlMemory);

            GetTempPresets(maid).Insert(0, tempPreset);
        }

        /// <summary>
        /// 操作履歴用にメイドの全状態を控える。体型・顔・ExPreset まで含むため、
        /// 装備しか見ない MaidPropsSnapshot では戻せないプリセット適用の undo に使う。
        /// 一覧には出さないのでサムネは即破棄する（Unity のテクスチャは GC されず、
        /// 履歴の件数分そのまま残ってしまうため）
        /// </summary>
        public TempPreset CaptureHistorySnapshot(Maid maid)
        {
            var tempPreset = CapturePreset(maid, CharacterMgr.PresetType.All);

            var texThum = tempPreset?.preset?.texThum;
            if (texThum != null)
            {
                UnityEngine.Object.Destroy(texThum);
                tempPreset.preset.texThum = null;
            }

            return tempPreset;
        }

        private TempPreset CapturePreset(Maid maid, CharacterMgr.PresetType presetType)
        {
            if (maid == null)
            {
                return null;
            }

            byte[] buffer = characterMgr.PresetSaveNotWriteFile(maid, presetType);
            var xmlMemory = ExPresetWrapper.xmlMemory;
            var binaryReader = new BinaryReader(new MemoryStream(buffer));
#if COM3D25
            // COM3D2.5 で PresetLoad(BinaryReader, string) は static メソッドに変更された
            var preset = CharacterMgr.PresetLoad(binaryReader, string.Empty);
#else
            var preset = characterMgr.PresetLoad(binaryReader, string.Empty);
#endif
            binaryReader.Close();

            var now = DateTime.Now;

            preset.strFileName = now.ToString("MM-dd HH.mm.ss");
            var lastWriteAt = now.Ticks;

            return new TempPreset
            {
                preset = preset,
                xmlMemory = xmlMemory,
                lastWriteAt = lastWriteAt
            };
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