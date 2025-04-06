using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.ConstrainedExecution;
using System.Text;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace COM3D2.ModItemExplorer.Plugin
{
    public class MotionWindow : IWindow
    {
        public readonly static int WINDOW_ID = 971237;
        public readonly static int WINDOW_WIDTH = 520;
        public readonly static int WINDOW_HEIGHT = 80;
        public readonly static int WINDOW_HEIGHT_EXTEND = 320;
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

        public GUIStyle gsWin => GUIView.gsWin;

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
        }

        public void Close()
        {
            isShowWnd = false;
            _maid = null;
            _animation = null;
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
            if (!isShowWnd)
            {
                return;
            }

            modItemManager.UpdateAnimationLayerInfos();
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

            _windowHeight = config.animationExtend ? WINDOW_HEIGHT_EXTEND : WINDOW_HEIGHT;

            if (_windowHeight != windowRect.height)
            {
                _windowRect.height = _windowHeight;
                InitView();
            }

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
            _rootView.ResetLayout();
            DrawHeader();
            
            if (config.animationExtend)
            {
                DrawContentExtend();
            }
            else
            {
                DrawContent();
            }

            _rootView.DrawComboBox();

            GUI.DragWindow();
        }

        private void DrawHeader()
        {
            var view = _headerView;
            view.ResetLayout();

            view.padding = Vector2.zero;

            view.BeginLayout(GUIView.LayoutDirection.Free);

            view.currentPos.x = _windowWidth - 80;

            view.DrawToggle("拡張", config.animationExtend, 60, 20, newValue =>
            {
                config.animationExtend = newValue;
                config.dirty = true;
            });

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

            if (_maid == null || _animation == null)
            {
                return;
            }

            var state = _maid.body0.GetAnist();
            if (state == null)
            {
                view.DrawLabel("アニメーションがありません", -1, 20);
                return;
            }

            view.DrawLabel($"アニメ名: {state.name}", -1, 20);

            view.BeginHorizontal();
            {
                view.DrawSliderValue(new GUIView.SliderOption
                {
                    label = "再生時間",
                    labelWidth = 50,
                    width = _windowWidth - 70,
                    fieldType = FloatFieldType.Float,
                    min = 0f,
                    max = state.length,
                    step = 0.01f,
                    defaultValue = 0f,
                    value = state.GetPlayingTime(),
                    hiddenResetButton = true,
                    onChanged = value =>
                    {
                        _animation.SeekTime(state, value);
                        state.speed = 0f;
                    },
                });

                if (state.enabled && state.speed > 0f)
                {
                    if (view.DrawButton("■", 20, 20))
                    {
                        state.speed = 0f;
                    }
                }
                else
                {
                    if (view.DrawButton("▶", 20, 20))
                    {
                        state.enabled = true;
                        state.speed = 1f;
                    }
                }
            }
            view.EndLayout();
        }

        private void DrawContentExtend()
        {
            var view = _contentView;
            view.ResetLayout();
            view.SetEnabled(!view.IsComboBoxFocused());

            if (_maid == null || _animation == null)
            {
                return;
            }

            view.BeginScrollView();

            var layers = new int[] { 0, 2, 3, 4, 5, 6, 7, 8 };
            foreach (var layer in layers)
            {
                var info = modItemManager.animationLayerInfos[layer];
                if (info == null)
                {
                    continue;
                }

                var state = info.state;

                var length = 0f;
                var playingTime = 0f;
                var speed = 1f;
                var enabled = false;

                if (state != null)
                {
                    length = state.length;
                    playingTime = state.GetPlayingTime();
                    speed = state.speed;
                    enabled = state.enabled;
                }

                view.SetEnabled(!view.IsComboBoxFocused());

                view.BeginHorizontal();
                {
                    var layerActive = layer == modItemManager.animationLayer;
                    view.DrawToggle($"{layer}: {info.anmName}", layerActive, 300, 20, newValue =>
                    {
                        modItemManager.animationLayer = layer;
                    });

                    view.currentPos.x = view.viewRect.width - 60;

                    if (layer > 0 && view.DrawButton("削除", 50, 20, enabled: enabled))
                    {
                        _maid.body0.StopAndDestroy(state.name);
                        info.anmName = "";
                        info.state = null;
                        info.ApplyToObject();
                    }
                }
                view.EndLayout();

                view.SetEnabled(!view.IsComboBoxFocused() && state != null);

                view.BeginHorizontal();
                {
                    view.DrawSliderValue(new GUIView.SliderOption
                    {
                        label = "再生時間",
                        labelWidth = 50,
                        width = _windowWidth - 70,
                        fieldType = FloatFieldType.Float,
                        min = 0f,
                        max = length,
                        step = 0.01f,
                        defaultValue = 0f,
                        value = playingTime,
                        hiddenResetButton = true,
                        onChanged = value =>
                        {
                            info.startTime = value;
                            info.ApplyToObject();
                            _animation.SeekTime(state, value);
                            state.speed = 0f;
                        },
                    });

                    if (enabled && speed > 0f)
                    {
                        if (view.DrawButton("■", 20, 20))
                        {
                            state.speed = 0f;
                        }
                    }
                    else
                    {
                        if (view.DrawButton("▶", 20, 20))
                        {
                            state.enabled = true;
                            state.speed = info.speed;
                        }
                    }
                }
                view.EndLayout();

                // レイヤー0は重み/速度の設定ができない
                if (layer == 0) continue;

                view.BeginHorizontal();
                {
                    view.DrawSliderValue(new GUIView.SliderOption
                    {
                        label = "重み",
                        labelWidth = 30,
                        width = 230f,
                        fieldType = FloatFieldType.Float,
                        min = 0f,
                        max = 1f,
                        step = 0.01f,
                        defaultValue = 1f,
                        value = info.weight,
                        onChanged = value =>
                        {
                            info.weight = value;
                            info.ApplyToObject();
                            state.weight = value;
                            _animation.Sample();
                        },
                    });

                    view.DrawSliderValue(new GUIView.SliderOption
                    {
                        label = "速度",
                        labelWidth = 30,
                        width = 230f,
                        fieldType = FloatFieldType.Float,
                        min = 0f,
                        max = 2f,
                        step = 0.01f,
                        defaultValue = 1f,
                        value = info.speed,
                        onChanged = value =>
                        {
                            info.speed = value;
                            info.ApplyToObject();
                            state.speed = value;
                        },
                    });
                }
                view.EndLayout();
            }

            view.SetEnabled(!view.IsComboBoxFocused());

            view.EndScrollView();
        }

        public void OnScreenSizeChanged()
        {
            MTEUtils.AdjustWindowPosition(ref _windowRect);
        }
    }
}