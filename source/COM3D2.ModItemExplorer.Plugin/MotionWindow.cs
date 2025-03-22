using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace COM3D2.ModItemExplorer.Plugin
{
    public class MotionWindow : IWindow
    {
        public readonly static int WINDOW_ID = 971237;
        public readonly static int WINDOW_WIDTH = 480;
        public readonly static int WINDOW_HEIGHT = 80;
        public readonly static int HEADER_HEIGHT = 20;

        private static ModItemExplorer plugin => ModItemExplorer.instance;
        private static ModItemManager modItemManager => ModItemManager.instance;
        private static Config config => ConfigManager.instance.config;

        public int windowIndex { get; set; }
        public bool isShowWnd { get; set; }

        private Rect _windowRect;
        public Rect windowRect
        {
            get => _windowRect;
            set => _windowRect = value;
        }

        private int _windowWidth = WINDOW_WIDTH;
        private int _windowHeight = WINDOW_HEIGHT;
        private bool _initializedGUI = false;

        private GUIView _rootView = new GUIView();
        private GUIView _headerView = new GUIView();
        private GUIView _contentView = new GUIView();

        private Maid _maid;
        private Animation _animation;
        private string _anmName;
        private AnimationState _animationState;
        private float _animationTime;

        public GUIStyle gsWin => GUIView.gsWin;

        public float animationTime
        {
            get => _animationTime;
            set
            {
                _animationTime = value;

                if (_animationState != null)
                {
                    _animationState.time = value;
                    _animationState.enabled = true;
                    _animation.Sample();
                    _animationState.enabled = false;
                }
            }
        }

        public bool isMotionPlaying
        {
            get
            {
                if (_animationState != null)
                {
                    return _animationState.enabled;
                }
                return false;
            }
            set
            {
                if (_animationState != null)
                {
                    _animationState.enabled = value;
                }
            }
        }

        public MotionWindow()
        {
            this.windowIndex = 0;
            this.isShowWnd = false;
            this.windowRect = new Rect(
                Screen.width - _windowWidth - 30,
                100,
                _windowWidth,
                _windowHeight
            );
        }

        public void Call(Maid maid)
        {
            if (maid == null)
            {
                return;
            }

            isShowWnd = true;
            _maid = maid;
            _animation = maid.GetAnimation();
            _anmName = maid.body0.LastAnimeFN.ToLower();
            _animationState = _animation[_anmName];
        }

        public void Close()
        {
            isShowWnd = false;
            _maid = null;
            _animation = null;
            _anmName = "";
            _animationState = null;
        }

        public void InitView()
        {
            _rootView.Init(0, 0, _windowWidth, _windowHeight);
            _headerView.Init(0, 0, _windowWidth, HEADER_HEIGHT);
            _contentView.Init(0, HEADER_HEIGHT, _windowWidth, _windowHeight - HEADER_HEIGHT);

            _headerView.parent = _rootView;
            _contentView.parent = _rootView;
        }

        public void Init()
        {
        }

        public void Update()
        {
            if (!isShowWnd || _maid == null || _animation == null)
            {
                return;
            }

            var anmName = _maid.body0.LastAnimeFN;
            if (anmName != _anmName)
            {
                _anmName = anmName.ToLower();
            }

            _animationState = _animation[_anmName];

            if (_animationState != null && _animationState.enabled && _animationState.length > 0f)
            {
                float value = _animationState.time;
                if (_animationState.length < _animationState.time)
                {
                    if (_animationState.wrapMode == WrapMode.ClampForever)
                    {
                        value = _animationState.length;
                    }
                    else
                    {
                        value = _animationState.time - _animationState.length * (float)((int)(_animationState.time / _animationState.length));
                    }
                }
                _animationTime = value;
            }
        }

        public void OnLoad()
        {
            MTEUtils.AdjustWindowPosition(ref _windowRect);
        }

        public void OnChangedSceneLevel(Scene scene, LoadSceneMode sceneMode)
        {
            if (plugin.isEnable)
            {
                return;
            }
        }

        public void InitGUI()
        {
            if (_initializedGUI)
            {
                return;
            }
            _initializedGUI = true;

            InitView();

            if (config.motionWindowPosX != -1 && config.motionWindowPosY != -1)
            {
                _windowRect.x = config.motionWindowPosX;
                _windowRect.y = config.motionWindowPosY;
            }

            MTEUtils.AdjustWindowPosition(ref _windowRect);
        }

        public void OnGUI()
        {
            if (!isShowWnd)
            {
                return;
            }

            InitGUI();

            windowRect = GUI.Window(WINDOW_ID, windowRect, DrawWindow, "モーション", gsWin);
            MTEUtils.ResetInputOnScroll(windowRect);

            if (config.motionWindowPosX != (int)windowRect.x ||
                config.motionWindowPosY != (int)windowRect.y)
            {
                config.motionWindowPosX = (int)windowRect.x;
                config.motionWindowPosY = (int)windowRect.y;
            }
        }

        private void DrawWindow(int id)
        {
            DrawHeader();
            DrawContent();

            _rootView.DrawComboBox();

            GUI.DragWindow();
        }

        private void DrawHeader()
        {
            var view = _headerView;
            view.ResetLayout();

            view.padding = Vector2.zero;

            view.BeginLayout(GUIView.LayoutDirection.Free);

            view.currentPos.x = _windowWidth - 20;

            if (view.DrawButton("x", 20, 20))
            {
                isShowWnd = false;
            }
        }

        private void DrawContent()
        {
            var view = _contentView;
            view.ResetLayout();
            view.SetEnabled(!view.IsComboBoxFocused());

            if (_maid == null || _animation == null || _animationState == null)
            {
                return;
            }

            view.DrawLabel($"アニメ名: {_maid.body0.LastAnimeFN}", -1, 20);

            view.BeginHorizontal();
            {
                view.DrawSliderValue(new GUIView.SliderOption
                {
                    label = "再生時間",
                    labelWidth = 50,
                    width = _windowWidth - 50,
                    fieldType = FloatFieldType.Float,
                    min = 0f,
                    max = _animationState.length,
                    step = 0.01f,
                    defaultValue = 0f,
                    value = _animationTime,
                    hiddenResetButton = true,
                    onChanged = value => animationTime = value,
                });

                if (isMotionPlaying)
                {
                    if (view.DrawButton("■", 20, 20))
                    {
                        isMotionPlaying = false;
                    }
                }
                else
                {
                    if (view.DrawButton("▶", 20, 20))
                    {
                        isMotionPlaying = true;
                    }
                }
            }
            view.EndLayout();
        }

        public void OnScreenSizeChanged()
        {
            MTEUtils.AdjustWindowPosition(ref _windowRect);
        }
    }
}