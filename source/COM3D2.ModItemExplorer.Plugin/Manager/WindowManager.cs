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
    }

    public class WindowManager : ManagerBase
    {
        public ModItemWindow modItemWindow = null;
        public ColorPaletteWindow colorPaletteWindow = null;
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
            modItemWindow.Init();

            colorPaletteWindow = new ColorPaletteWindow();
            colorPaletteWindow.Init();
        }

        public override void Update()
        {
            bool isScreenSizeChanged = _screenWidth != Screen.width || _screenHeight != Screen.height;
            if (isScreenSizeChanged)
            {
                modItemWindow.OnScreenSizeChanged();
                colorPaletteWindow.OnScreenSizeChanged();

                _screenWidth = Screen.width;
                _screenHeight = Screen.height;
            }
        }

        public override void OnLoad()
        {
            modItemWindow.OnLoad();
            colorPaletteWindow.OnLoad();
        }

        public override void OnChangedSceneLevel(Scene scene, LoadSceneMode sceneMode)
        {
            modItemWindow.OnChangedSceneLevel(scene, sceneMode);
            colorPaletteWindow.OnChangedSceneLevel(scene, sceneMode);
        }

        public void OnGUI()
        {
            modItemWindow.OnGUI();
            colorPaletteWindow.OnGUI();
        }
    }
}