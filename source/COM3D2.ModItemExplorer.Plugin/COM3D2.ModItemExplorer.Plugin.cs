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
using System.Reflection;
// 参照 DLL (COM3D2.ExternalPreset.Managed 等) がグローバルな GearMenu 名前空間を
// 内包しており、素の GearMenu だとそちらへ解決されてしまうため別名で参照する
using MTEGearMenu = COM3D2.MotionTimelineEditor.GearMenu;

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
        // タブバー (TabBarDrawer.ACCENT_COLOR) やドッキング表示と揃えたアクセント色
        public override Color accentColor => Color.cyan;
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

                // ギアメニューアイコンが未追加または破棄済みなら再追加する
                // （Unity の == オーバーロードにより破棄済みオブジェクトも null 扱いになる）
                if (gearMenuIcon == null)
                {
                    AddGearMenu();
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

        void OnDestroy()
        {
            // ホストは常駐するため、解除を怠るとハンドラが掴んだ参照ごと残る
            EditorStateClient.Unsubscribe(OnEditorEnabledChanged);
            MaidSelectClient.Unsubscribe(OnSelectedMaidChanged);
        }

        // ロード順への追従と接続時の初期同期は EditorStateClient 側の責務のため、ここは反映するだけでよい
        private void OnEditorEnabledChanged(bool enabled)
        {
            isEnable = enabled;
        }

        // 選択解除（null）や一覧に居ないメイドの扱いは窓側に任せる
        private void OnSelectedMaidChanged(Maid maid)
        {
            windowManager.modItemWindow.SelectMaid(maid);
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
                managerRegistry.RegisterManager(ItemHistoryManager.instance);
                managerRegistry.RegisterManager(ModItemManager.instance);
                managerRegistry.RegisterManager(TextureManager.instance);
                managerRegistry.RegisterManager(WindowManager.instance);
                managerRegistry.RegisterManager(ConfigManager.instance);

                _ = ExPresetWrapper.instance;

                // SceneEditor の有効/無効へ追従する（SceneEditor 不在時は無視される）
                EditorStateClient.Subscribe(OnEditorEnabledChanged);

                // SceneEditor の選択中メイドへ追従する（SceneEditor 不在時は無視される）
                MaidSelectClient.Subscribe(OnSelectedMaidChanged);

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
            // SysShortcut 生成前に呼ばれた場合は何もしない（シーンロード時に再試行される）
            if (!MTEGearMenu.Buttons.IsReady)
            {
                return;
            }

            gearMenuIcon = MTEGearMenu.Buttons.Add(
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
                MTEGearMenu.Buttons.Remove(gearMenuIcon);
                gearMenuIcon = null;
            }
        }

        private void UpdateGearMenu()
        {
            if (gearMenuIcon != null)
            {
                MTEGearMenu.Buttons.SetFrameColor(gearMenuIcon, isEnable ? Color.blue : Color.white);
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