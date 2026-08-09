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
        private bool _isGizmoLocked = false;
        private bool _isMouseOverWindow = false;
        private bool _isMousePressInProgress = false;
        private bool _isCameraPressInProgress = false;
        private bool _isCameraDragFromOutside = false;

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

        public override void LateUpdate()
        {
            UpdateGizmoDragSuppress();
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

            _isMouseOverWindow = isMouseOverWindow;

            UpdateCameraDragFromOutside(isMouseOverWindow);

            // ウィンドウ外から始まったドラッグを逃がすのはカメラ操作だけ。
            // UI 入力とギズモは誤操作防止を優先し、カーソルがウィンドウに乗った時点で従来どおり塞ぐ
            UpdateCameraControl(isMouseOverWindow && !_isCameraDragFromOutside);
            UpdateUIInput(isMouseOverWindow || isExternalUIInputBlocked);
            // hotControl は OnGUI で初めて立つため、押下フレームだけロックが 1 フレーム遅れる
            // （OnRenderObject は同フレームの OnGUI より前に走るのでそこで奪われてしまう）。
            // ウィンドウ上での押下はその場でロックして穴を塞ぐ
            UpdateGizmoLock(GUIUtility.hotControl != 0
                || (isMouseOverWindow && Input.GetMouseButton(0)));
        }

        /// <summary>
        /// ウィンドウ外で押し始めたドラッグは、途中でカーソルがウィンドウ内へ入ってもカメラ操作を続けさせる。
        /// 判定は押下フレームのカーソル位置だけで行い、以降はボタンをすべて離すまで維持する
        /// </summary>
        private void UpdateCameraDragFromOutside(bool isMouseOverWindow)
        {
            if (!Input.GetMouseButton(0) && !Input.GetMouseButton(1) && !Input.GetMouseButton(2))
            {
                _isCameraPressInProgress = false;
                _isCameraDragFromOutside = false;
                return;
            }

            if (_isCameraPressInProgress)
            {
                return;
            }
            _isCameraPressInProgress = true;

            _isCameraDragFromOutside = !isMouseOverWindow;
        }

        private void UpdateCameraControl(bool shouldBlock)
        {
            var mainCamera = GameMain.Instance.MainCamera;
            if (mainCamera == null)
            {
                return;
            }

            if (shouldBlock)
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

        /// <summary>
        /// IMGUI が何らかのコントロールでマウスを掴んでいる間はギズモのハンドル選択を止める。
        /// ただしこのロックだけでは軸線（X/Y/Z）のハンドル選択を防げない（詳細は UpdateGizmoDragSuppress 参照）。
        /// カーソルがウィンドウ外へ出てもロックを維持する必要があるため、
        /// カメラ操作・UI 入力と違い isMouseOverWindow では絞らない。
        /// global_control_lock はゲーム本体も使う共有フラグなので、
        /// 他と同様に「自分が立てたときだけ倒す」ガードを入れている
        /// </summary>
        private void UpdateGizmoLock(bool shouldLock)
        {
            if (shouldLock)
            {
                if (_isGizmoLocked || !GizmoRender.global_control_lock)
                {
                    GizmoRender.global_control_lock = true;
                    _isGizmoLocked = true;
                }
            }
            else if (_isGizmoLocked)
            {
                GizmoRender.global_control_lock = false;
                _isGizmoLocked = false;
            }
        }

        /// <summary>
        /// ウィンドウ上から始まった押下は、ボタンを離すまでギズモに渡さない。
        /// GizmoRender は押下時に NGUI のヒット判定しか見ないので、IMGUI のウィンドウ上で押しても
        /// ドラッグ扱いになり、そのままカーソルがハンドルへ重なった瞬間に操作を奪われてしまう。
        /// 判定は押下フレームの _isMouseOverWindow だけで行い、以降はボタンを離すまで維持する。
        /// GizmoRender.Update より後・OnRenderObject より前に走る必要があるため LateUpdate で処理する。
        /// 自前のモデル用ギズモは ModelGizmoRender が自分で掴み判定を絞るので、ここが効くのは
        /// ゲーム側や他プラグインのギズモに対してのみ
        /// </summary>
        private void UpdateGizmoDragSuppress()
        {
            if (!GizmoRenderHack.isAvailable)
            {
                return;
            }

            if (!Input.GetMouseButton(0))
            {
                // GizmoRender.Update がボタン解放時に is_drag_ を戻すのでこちらは状態を捨てるだけでよい
                _isMousePressInProgress = false;
                return;
            }

            if (_isMousePressInProgress)
            {
                return;
            }
            _isMousePressInProgress = true;

            if (_isMouseOverWindow)
            {
                GizmoRenderHack.isDrag = false;
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

            if (_isGizmoLocked)
            {
                _isGizmoLocked = false;
                GizmoRender.global_control_lock = false;
            }

            _isMousePressInProgress = false;
            _isCameraPressInProgress = false;
            _isCameraDragFromOutside = false;
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