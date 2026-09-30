using System.Collections;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;

namespace COM3D2.ModItemExplorer.Plugin
{
    public class MaidManagerWrapper
    {
        private MaidManagerField maidManagerField = new MaidManagerField();
        private MaidCacheField maidCacheField = new MaidCacheField();

        private object _maidManager = null;
        public object maidManager
        {
            get
            {
                if (_maidManager == null)
                {
                    _maidManager = maidManagerField.instance.GetValue(null, null);
                }
                return _maidManager;
            }
        }

        public IList maidCachesOriginal
        {
            get => (IList)maidManagerField.maidCaches.GetValue(maidManager);
        }

        public int maidSlotNo
        {
            get => (int)maidManagerField.maidSlotNo.GetValue(maidManager, null);
        }

        private List<MaidCacheWrapper> _maidCaches = new List<MaidCacheWrapper>();
        public List<MaidCacheWrapper> maidCaches
        {
            get
            {
                try
                {
                    if (!initialized)
                    {
                        return null;
                    }

                    var maidCachesOriginal = this.maidCachesOriginal;
                    _maidCaches.Clear();
                    foreach (var maidCache in maidCachesOriginal)
                    {
                        var maidCacheWrapper = maidCacheField.ConvertToWrapper(maidCache);
                        _maidCaches.Add(maidCacheWrapper);
                    }

                    return _maidCaches;
                }
                catch (System.Exception e)
                {
                    MTEUtils.LogException(e);
                    return null;
                }
            }
        }

        private static MaidManagerWrapper _instance = null;
        public static MaidManagerWrapper instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new MaidManagerWrapper();
                    _instance.Init();
                }
                return _instance;
            }
        }

        public bool initialized { get; private set; } = false;

        // 型やメンバーが見つからないのはバージョン差による恒久的な問題なので、以降は再試行しない
        private bool _incompatible = false;

        public bool Init()
        {
            if (initialized || _incompatible)
            {
                return initialized;
            }

            // SceneEditor 不在やロード順が自分より後の場合は見つからない。次回の IsValid で再試行する
            var assembly = SceneEditorAssembly.Find();
            if (assembly == null)
            {
                return false;
            }

            // レイヤー情報の読み書きができないとラッパーが既定値のまま毎フレーム差し替わるので、併せて接続条件にする
            if (!maidManagerField.Init(assembly) || !maidCacheField.Init(assembly) ||
                !AnimationLayerInfoField.instance.initialized)
            {
                _incompatible = true;
                MTEUtils.LogWarning("MaidManagerWrapper: SceneEditor のメイド情報に接続できませんでした");
                return false;
            }

            initialized = true;

            return true;
        }

        /// <summary>未接続なら接続を試みてから結果を返す</summary>
        public bool IsValid()
        {
            return Init();
        }
    }
}