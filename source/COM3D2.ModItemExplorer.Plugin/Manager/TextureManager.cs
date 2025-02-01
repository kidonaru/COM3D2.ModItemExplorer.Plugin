using System.Collections.Generic;
using System.Threading;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace COM3D2.ModItemExplorer.Plugin
{
    public class TextureManager : ManagerBase
    {
        private Dictionary<string, TextureResource> _textureFileCache = new Dictionary<string, TextureResource>(1024);
        private Dictionary<string, Texture2D> _textureCache = new Dictionary<string, Texture2D>(1024);
        private HashSet<string> _requestTextureFileNameSet = new HashSet<string>();

        private static TextureManager _instance;
        public static TextureManager instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new TextureManager();
                }
                return _instance;
            }
        }

        public Texture2D GetTexture(string fileName, byte[] textureData = null)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                return null;
            }

            // テクスチャ生成済みの場合はキャッシュから取得
            if (_textureCache.TryGetValue(fileName, out var texture))
            {
                return texture;
            }

            // テクスチャデータがある場合はテクスチャ生成
            if (textureData != null && textureData.Length > 0)
            {
                texture = new Texture2D(1, 1);
                texture.LoadImage(textureData);
                texture.wrapMode = TextureWrapMode.Clamp;
                _textureCache[fileName] = texture;
                return texture;
            }

            TextureResource textureFile = null;
            bool fileLoaded = false;

            // テクスチャファイルがロード済みか確認
            lock (_textureFileCache)
            {
                if (_textureFileCache.TryGetValue(fileName, out textureFile))
                {
                    fileLoaded = true;
                    _textureFileCache.Remove(fileName);
                }
            }

            // テクスチャファイルがロード済みの場合はテクスチャ生成
            if (fileLoaded)
            {
                if (textureFile != null)
                {
                    texture = textureFile.CreateTexture2D();
                }
                _textureCache[fileName] = texture;
                MTEUtils.LogDebug("[TextureManager] CreateTexture: " + fileName);
                return texture;
            }

            // テクスチャファイルがロードされていない場合はロードリクエスト
            lock (_requestTextureFileNameSet)
            {
                _requestTextureFileNameSet.Add(fileName);
            }

            StartTextureLoadThread();

            MTEUtils.LogDebug("[ModMenuItemManager] RequestTextureLoad: " + fileName);

            return null;
        }

        private Thread _textureLoadThread;
        private volatile bool _isTextureLoadThreadRunning = false;
        private AutoResetEvent _textureLoadEvent = new AutoResetEvent(false);

        private void StartTextureLoadThread()
        {
            if (_textureLoadThread != null && _textureLoadThread.IsAlive)
            {
                _textureLoadEvent.Set();
                return;
            }

            _isTextureLoadThreadRunning = true;
            _textureLoadThread = new Thread(() =>
            {
                var requestedFileNames = new List<string>();

                while (_isTextureLoadThreadRunning)
                {
                    requestedFileNames.Clear();

                    lock (_requestTextureFileNameSet)
                    {
                        if (_requestTextureFileNameSet.Count > 0)
                        {
                            requestedFileNames.AddRange(_requestTextureFileNameSet);
                        }
                    }

                    if (requestedFileNames.Count == 0)
                    {
                        _textureLoadEvent.WaitOne();
                        continue;
                    }

                    foreach (var fileName in requestedFileNames)
                    {
                        var textureFile = TextureLoader.LoadTextureFile(fileName);

                        lock (_textureFileCache)
                        {
                            _textureFileCache[fileName] = textureFile;
                        }

                        lock (_requestTextureFileNameSet)
                        {
                            _requestTextureFileNameSet.Remove(fileName);
                        }
                    }
                }
            });

            _textureLoadThread.Start();
        }

        public override void OnChangedSceneLevel(Scene scene, LoadSceneMode sceneMode)
        {
            if (plugin.isEnable)
            {
                return;
            }

            lock (_textureFileCache)
            {
                _textureFileCache.Clear();
            }
            lock (_requestTextureFileNameSet)
            {
                _requestTextureFileNameSet.Clear();
            }

            foreach (var texture in _textureCache.Values)
            {
                if (texture != null)
                {
                    Object.Destroy(texture);
                }
            }
            _textureCache.Clear();
        }
    }
}