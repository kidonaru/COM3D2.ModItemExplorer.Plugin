using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
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

        public bool Init()
        {
            var assemblyPath = Path.GetFullPath(MTEUtils.CombinePaths(
                "Sybaris", "UnityInjector", "COM3D2.MotionTimelineEditor.Plugin.dll"));
            if (!File.Exists(assemblyPath))
            {
                MTEUtils.LogWarning("MotionTimelineEditor.Plugin" + " not found");
                return false;
            }

            var assembly = Assembly.LoadFile(assemblyPath);

            if (!maidManagerField.Init(assembly))
            {
                return false;
            }

            if (!maidCacheField.Init(assembly))
            {
                return false;
            }

            initialized = true;

            return true;
        }

        public bool IsValid()
        {
            return initialized;
        }
    }
}