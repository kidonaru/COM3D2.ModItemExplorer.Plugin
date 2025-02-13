using COM3D2.MotionTimelineEditor;
using UnityEngine.SceneManagement;

namespace COM3D2.ModItemExplorer.Plugin
{
    public class ManagerBase : IManager
    {
        protected static ModItemExplorer plugin => ModItemExplorer.instance;
        protected static ConfigManager configManager => ConfigManager.instance;
        protected static WindowManager windowManager => WindowManager.instance;
        protected static MaidPresetManager maidPresetManager => MaidPresetManager.instance;
        protected static TempPresetManager tempPresetManager => TempPresetManager.instance;
        protected static ModelHackManagerWrapper modelHackManager => ModelHackManagerWrapper.instance;
        protected static CharacterMgr characterMgr => GameMain.Instance.CharacterMgr;

        public virtual void Init()
        {
        }

        public virtual void PreUpdate()
        {
        }

        public virtual void Update()
        {
        }

        public virtual void LateUpdate()
        {
        }

        public virtual void OnLoad()
        {
        }

        public virtual void OnPluginDisable()
        {
        }

        public virtual void OnChangedSceneLevel(Scene scene, LoadSceneMode sceneMode)
        {
        }
    }
}