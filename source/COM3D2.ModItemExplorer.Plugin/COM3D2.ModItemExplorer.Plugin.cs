using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityInjector;
using UnityInjector.Attributes;
using System.Collections;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using COM3D2.MotionTimelineEditor;

namespace COM3D2.ModItemExplorer.Plugin
{
    public class GUIOption : GUIOptionBase
    {
        public override float keyRepeatTimeFirst => config.keyRepeatTimeFirst;
        public override float keyRepeatTime => config.keyRepeatTime;
        public override bool useHSVColor
        {
            get => config.useHSVColor;
            set
            {
                config.useHSVColor = value;
                config.dirty = true;
            }
        }
        public override Color windowHoverColor => config.windowHoverColor;
        public override Texture2D changeIcon => null;
        public override Texture2D favoriteOffIcon => PluginInfo.FavoriteOffIconTexture;
        public override Texture2D favoriteOnIcon => PluginInfo.FavoriteOnIconTexture;

        private static Config config => ConfigManager.instance.config;
    }

    [
        PluginFilter("COM3D2x64"),
        PluginName(PluginInfo.PluginFullName),
        PluginVersion(PluginInfo.PluginVersion)
    ]
    public class ModItemExplorer : PluginBase
    {
        private bool _isEnable = false;
        public bool isEnable
        {
            get => _isEnable;
            set
            {
                if (_isEnable == value)
                {
                    return;
                }

                _isEnable = value;
                UpdateGearMenu();

                if (value)
                {
                    OnPluginEnable();
                }
                else
                {
                    OnPluginDisable();
                }
            }
        }

        public static ModItemExplorer instance { get; private set; }

        private static ManagerRegistry managerRegistry => ManagerRegistry.instance;
        private static WindowManager windowManager => WindowManager.instance;
        private static ConfigManager configManager => ConfigManager.instance;
        private static Config config => ConfigManager.instance.config;
        private static ModItemManager modItemManager => ModItemManager.instance;

        public ModItemExplorer()
        {
        }

        public void Awake()
        {
            GameObject.DontDestroyOnLoad(this);
            instance = this;
        }

        public void Start()
        {
            try
            {
                Initialize();
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }
        }

        public void Update()
        {
            try
            {
                if (!config.pluginEnabled)
                {
                    return;
                }

                modItemManager.PreUpdate();

                if (config.GetKeyDown(KeyBindType.PluginToggle))
                {
                    isEnable = !isEnable;
                }

                if (isEnable)
                {
                    managerRegistry.Update();
                }
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }
        }

        public void LateUpdate()
        {
            try
            {
                if (!config.pluginEnabled)
                {
                    return;
                }

                if (isEnable)
                {
                    managerRegistry.LateUpdate();
                }
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }
        }

        public void OnChangedSceneLevel(Scene scene, LoadSceneMode sceneMode)
        {
            try
            {
                if (!config.pluginEnabled)
                {
                    return;
                }

                if (scene.name == "SceneTitle")
                {
                    this.isEnable = false;
                }

                BinaryLoader.ClearCache();
                ModMenuLoader.ClearCache();
                TextureLoader.ClearCache();
                PresetLoader.ClearCache();

                managerRegistry.OnChangedSceneLevel(scene, sceneMode);
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }
        }

        void OnApplicationQuit()
        {
            configManager.SaveConfigXml();
        }

        private void Initialize()
        {
            try
            {
                MTEUtils.Log("初期化中...");
                MTEUtils.LogDebug("Unity Version: " + Application.unityVersion);

                configManager.Init();

                GUIView.option = new GUIOption();

                if (!config.pluginEnabled)
                {
                    MTEUtils.Log("プラグインが無効になっています");
                    return;
                }

                SceneManager.sceneLoaded += OnChangedSceneLevel;

                managerRegistry.RegisterManager(TempPresetManager.instance);
                managerRegistry.RegisterManager(MaidPresetManager.instance);
                managerRegistry.RegisterManager(ModItemManager.instance);
                managerRegistry.RegisterManager(TextureManager.instance);
                managerRegistry.RegisterManager(WindowManager.instance);
                managerRegistry.RegisterManager(ConfigManager.instance);

                _ = ExPresetWrapper.instance;

                AddGearMenu();
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }
        }

        GameObject gearMenuIcon = null;

        public void AddGearMenu()
        {
            gearMenuIcon = GUIExtBase.GUIExt.Add(
                PluginInfo.PluginName,
                PluginInfo.PluginName,
                PluginInfo.Icon,
                (go) =>
                {
                    isEnable = !isEnable;
                });
        }

        public void RemoveGearMenu()
        {
            if (gearMenuIcon != null)
            {
                GUIExtBase.GUIExt.Destroy(gearMenuIcon);
                gearMenuIcon = null;
            }
        }

        private void UpdateGearMenu()
        {
            if (gearMenuIcon != null)
            {
                GUIExtBase.GUIExt.SetFrameColor(gearMenuIcon, isEnable ? Color.blue : Color.white);
            }
        }

        public void OnGUI()
        {
            try
            {
                if (isEnable)
                {
                    windowManager.OnGUI();
                }
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }
        }

        public void OnLoad()
        {
            MTEUtils.LogDebug("ModItemExplorer.OnLoad");
            managerRegistry.OnLoad();
        }

        private void OnPluginEnable()
        {
            MTEUtils.Log("プラグインが有効になりました");
            OnLoad();
        }

        private void OnPluginDisable()
        {
            MTEUtils.Log("プラグインが無効になりました");
            managerRegistry.OnPluginDisable();
        }
    }
}