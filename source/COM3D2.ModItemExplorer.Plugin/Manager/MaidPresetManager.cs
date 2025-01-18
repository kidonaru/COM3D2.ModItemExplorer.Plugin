using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace COM3D2.ModItemExplorer.Plugin
{
    public class MaidPresetManager : ManagerBase
    {
        private Dictionary<string, CharacterMgr.Preset> _presetCache = new Dictionary<string, CharacterMgr.Preset>();
        private Dictionary<string, PresetData> _presetDataCache = new Dictionary<string, PresetData>();

        private static MaidPresetManager _instance;
        public static MaidPresetManager instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new MaidPresetManager();
                }
                return _instance;
            }
        }

        public CharacterMgr.Preset GetPreset(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                return null;
            }
            if (_presetCache.TryGetValue(filePath, out var preset))
            {
                return preset;
            }

            PresetData presetData = null;
            bool dataLoaded = false;

            // ロード済みか確認
            lock (_presetDataCache)
            {
                if (_presetDataCache.TryGetValue(filePath, out presetData))
                {
                    dataLoaded = true;
                    _presetDataCache.Remove(filePath);
                }
            }

            // ロード済みの場合はPreset生成
            if (dataLoaded)
            {
                if (presetData != null)
                {
                    preset = presetData.preset;
                    preset.texThum = new Texture2D(1, 1);
                    preset.texThum.LoadImage(presetData.textureBytes);
                    preset.texThum.wrapMode = TextureWrapMode.Clamp;
                }
                _presetCache[filePath] = preset;
                return preset;
            }

            // ロードされていない場合はロードリクエスト
            lock (_requestPresetFilePathSet)
            {
                _requestPresetFilePathSet.Add(filePath);
            }

            StartPresetLoadThread();

            MTEUtils.LogDebug("[MaidPresetManager] RequestLoad: " + filePath);

            return null;
        }

        private Thread _presetLoadThread;
        private volatile bool _isPresetLoadThreadRunning = false;
        private AutoResetEvent _presetLoadEvent = new AutoResetEvent(false);
        private HashSet<string> _requestPresetFilePathSet = new HashSet<string>();

        private void StartPresetLoadThread()
        {
            if (_presetLoadThread != null && _presetLoadThread.IsAlive)
            {
                _presetLoadEvent.Set();
                return;
            }

            _isPresetLoadThreadRunning = true;
            _presetLoadThread = new Thread(() =>
            {
                var requestedFilePaths = new List<string>();

                while (_isPresetLoadThreadRunning)
                {
                    requestedFilePaths.Clear();

                    lock (_requestPresetFilePathSet)
                    {
                        if (_requestPresetFilePathSet.Count > 0)
                        {
                            requestedFilePaths.AddRange(_requestPresetFilePathSet);
                        }
                    }

                    if (requestedFilePaths.Count == 0)
                    {
                        _presetLoadEvent.WaitOne();
                        continue;
                    }

                    foreach (var filePath in requestedFilePaths)
                    {
                        var presetData = PresetLoader.Load(filePath);

                        lock (_presetDataCache)
                        {
                            _presetDataCache[filePath] = presetData;
                        }

                        lock (_requestPresetFilePathSet)
                        {
                            _requestPresetFilePathSet.Remove(filePath);
                        }
                    }
                }
            });

            _presetLoadThread.Start();
        }

        public void ApplyPreset(Maid maid, CharacterMgr.Preset preset)
        {
            if (maid == null || preset == null)
            {
                return;
            }

            characterMgr.PresetSet(maid, preset);
			maid.AllProcPropSeqStart();
        }

        public override void OnChangedSceneLevel(Scene scene, LoadSceneMode sceneMode)
        {
            if (plugin.isEnable)
            {
                return;
            }

            lock (_presetDataCache)
            {
                _presetDataCache.Clear();
            }

            foreach (var preset in _presetCache.Values)
            {
                if (preset.texThum != null)
                {
                    UnityEngine.Object.Destroy(preset.texThum);
                }
            }

            _presetCache.Clear();
        }
    }
}