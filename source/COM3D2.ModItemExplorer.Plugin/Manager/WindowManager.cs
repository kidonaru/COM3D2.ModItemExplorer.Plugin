using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
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
        public ModelOperationWindow modelOperationWindow = null;

        public List<IWindow> windows = new List<IWindow>();

        private int _screenWidth = 0;
        private int _screenHeight = 0;
        private bool _isCameraControlDisabled = false;
        private bool _isUIInputDisabled = false;

        /// <summary>
        /// カーソル位置以外の理由（サムネ撮影中など）でゲーム UI 入力を止めたいときに立てる。
        /// UICamera.InputEnable を直接書き換えると毎フレームの UpdateUIInput と取り合いになるため、必ずこのフラグ経由にすること
        /// </summary>
        public bool isExternalUIInputBlocked { get; set; }

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

            modelOperationWindow = new ModelOperationWindow();
            AddWindow(modelOperationWindow);
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

            UpdateInputBlock();
        }

        /// <summary>
        /// ウィンドウ上にカーソルがある間はゲーム側のマウス入力を止める。
        /// 止めないと右クリック（履歴を戻る）や左ドラッグでカメラが動き、
        /// またウィンドウ裏に隠れたゲーム UI のボタンまで押されてしまう
        /// </summary>
        private void UpdateInputBlock()
        {
            var isMouseOverWindow = false;
            foreach (var window in windows)
            {
                if (window.isShowWnd && MTEUtils.IsMouseOverWindowRect(window.windowRect))
                {
                    isMouseOverWindow = true;
                    break;
                }
            }

            UpdateCameraControl(isMouseOverWindow);
            UpdateUIInput(isMouseOverWindow || isExternalUIInputBlocked);
        }

        private void UpdateCameraControl(bool isMouseOverWindow)
        {
            var mainCamera = GameMain.Instance.MainCamera;
            if (mainCamera == null)
            {
                return;
            }

            if (isMouseOverWindow)
            {
                // 自分が無効化する前から無効なら他プラグイン等の管理下なので触らない（復帰時に誤って有効化しないため）。
                // 無効化後に外部から有効へ戻された場合は毎フレーム無効化し直す
                if (_isCameraControlDisabled || mainCamera.GetControl())
                {
                    mainCamera.SetControl(false);
                    _isCameraControlDisabled = true;
                }
            }
            else if (_isCameraControlDisabled)
            {
                mainCamera.SetControl(true);
                _isCameraControlDisabled = false;
            }
        }

        /// <summary>
        /// ゲーム UI（NGUI）のイベント処理を止める。
        /// UICamera.InputEnable はゲーム本体もフェード中の入力遮断に使う共有フラグなので、
        /// カメラ操作と同様に「自分が無効化したときだけ戻す」ガードを入れている
        /// </summary>
        private void UpdateUIInput(bool shouldBlock)
        {
            if (shouldBlock)
            {
                if (_isUIInputDisabled || UICamera.InputEnable)
                {
                    UICamera.InputEnable = false;
                    _isUIInputDisabled = true;
                }
            }
            else if (_isUIInputDisabled)
            {
                UICamera.InputEnable = true;
                _isUIInputDisabled = false;
            }
        }

        private void RestoreInputBlock()
        {
            isExternalUIInputBlocked = false;

            if (_isCameraControlDisabled)
            {
                _isCameraControlDisabled = false;

                var mainCamera = GameMain.Instance.MainCamera;
                if (mainCamera != null)
                {
                    mainCamera.SetControl(true);
                }
            }

            if (_isUIInputDisabled)
            {
                _isUIInputDisabled = false;
                UICamera.InputEnable = true;
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
            RestoreInputBlock();

            foreach (var window in windows)
            {
                window.Close();
            }
        }

        public override void OnChangedSceneLevel(Scene scene, LoadSceneMode sceneMode)
        {
            RestoreInputBlock();

            foreach (var window in windows)
            {
                window.OnChangedSceneLevel(scene, sceneMode);
            }
        }

        public void OnGUI()
        {
            // 組み込み GUIStyle の複製は OnGUI 内でしか行えないためここで初期化する
            GUIView.InitStyles();

            foreach (var window in windows)
            {
                window.OnGUI();
            }
        }
    }
}