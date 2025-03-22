using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace COM3D2.ModItemExplorer.Plugin
{
    public interface IWindow
    {
        int windowIndex { get; set; }
        bool isShowWnd { get; set; }
        Rect windowRect { get; set; }

        void Init();
        void Update();
        void Close();
        void OnLoad();
        void OnScreenSizeChanged();
        void OnChangedSceneLevel(Scene scene, LoadSceneMode sceneMode);
        void OnGUI();
    }

    public class WindowManager : ManagerBase
    {
        public ModItemWindow modItemWindow = null;
        public ColorPaletteWindow colorPaletteWindow = null;
        public CustomPartsWindow customPartsWindow = null;
        public HairLengthWindow hairLengthWindow = null;
        public MotionWindow motionWindow = null;

        public List<IWindow> windows = new List<IWindow>();

        private int _screenWidth = 0;
        private int _screenHeight = 0;

        private static WindowManager _instance = null;
        public static WindowManager instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new WindowManager();
                }
                return _instance;
            }
        }

        private WindowManager()
        {
        }

        public override void Init()
        {
            modItemWindow = new ModItemWindow();
            AddWindow(modItemWindow);

            colorPaletteWindow = new ColorPaletteWindow();
            AddWindow(colorPaletteWindow);

            customPartsWindow = new CustomPartsWindow();
            AddWindow(customPartsWindow);

            hairLengthWindow = new HairLengthWindow();
            AddWindow(hairLengthWindow);

            motionWindow = new MotionWindow();
            AddWindow(motionWindow);
        }

        public void AddWindow(IWindow window)
        {
            windows.Add(window);
            window.Init();
        }

        public override void Update()
        {
            bool isScreenSizeChanged = _screenWidth != Screen.width || _screenHeight != Screen.height;
            if (isScreenSizeChanged)
            {
                foreach (var window in windows)
                {
                    window.OnScreenSizeChanged();
                }

                _screenWidth = Screen.width;
                _screenHeight = Screen.height;
            }

            foreach (var window in windows)
            {
                window.Update();
            }
        }

        public override void OnLoad()
        {
            foreach (var window in windows)
            {
                window.OnLoad();
            }
        }

        public override void OnPluginDisable()
        {
            foreach (var window in windows)
            {
                window.Close();
            }
        }

        public override void OnChangedSceneLevel(Scene scene, LoadSceneMode sceneMode)
        {
            foreach (var window in windows)
            {
                window.OnChangedSceneLevel(scene, sceneMode);
            }
        }

        public void OnGUI()
        {
            foreach (var window in windows)
            {
                window.OnGUI();
            }
        }
    }
}