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
    public class MotionWindow : DockableWindowBase
    {
        public readonly static int WINDOW_ID = 971237;
        public readonly static int WINDOW_WIDTH = 520;
        public readonly static int WINDOW_HEIGHT = 80;

        private static ModItemExplorer plugin => ModItemExplorer.instance;
        private static ModItemManager modItemManager => ModItemManager.instance;
        private static Config config => ConfigManager.instance.config;

        protected override int windowId => WINDOW_ID;
        protected override string windowTitle => "モーション";
        protected override int minWidth => WINDOW_WIDTH;
        protected override int minHeight => WINDOW_HEIGHT;

        private int _windowWidth = WINDOW_WIDTH;
        private int _windowHeight = WINDOW_HEIGHT;

        private GUIView _rootView = new GUIView();
        private GUIView _contentView = new GUIView();

        private Maid _maid;
        private Animation _animation;

        public GUIStyle gsWin => GUIView.gsWin;

        public MotionWindow()
        {
            this.windowIndex = 0;
        }

        protected override void LoadPlacement(out int x, out int y, out int width, out int height)
        {
            x = config.motionWindowPosX;
            y = config.motionWindowPosY;
            width = config.motionWindowWidth;
            height = config.motionWindowHeight;
        }

        protected override void StorePlacement(int x, int y, int width, int height)
        {
            config.motionWindowPosX = x;
            config.motionWindowPosY = y;
            config.motionWindowWidth = width;
            config.motionWindowHeight = height;
            config.dirty = true;
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

        public override void Close()
        {
            base.Close();
            _maid = null;
            _animation = null;
        }

        protected override void OnSizeChanged(int width, int height)
        {
            _windowWidth = width;
            _windowHeight = height;
            InitView();
        }

        public void InitView()
        {
            var headerHeight = DockableWindowBase.HEADER_HEIGHT;

            _rootView.Init(0, 0, _windowWidth, _windowHeight);
            _contentView.Init(0, headerHeight, _windowWidth, _windowHeight - headerHeight);

            _contentView.parent = _rootView;
        }

        public override void Init()
        {
            base.Init();

            _windowWidth = (int)windowRect.width;
            _windowHeight = (int)windowRect.height;
            InitView();
        }

        public override void Update()
        {
            base.Update();

            if (!isShowWnd)
            {
                return;
            }

            modItemManager.UpdateAnimationLayerInfos();
        }

        protected override void DrawContent()
        {
            _rootView.ResetLayout();

            if (config.animationExtend)
            {
                DrawMainContentExtend();
            }
            else
            {
                DrawMainContentCompact();
            }

            ComboBoxPopupWindow.instance.ProcessFocus(_rootView, this);
        }

        /// <summary>
        /// 拡張表示の切り替えトグル。ドッキング時はタブバーがヘッダーを覆って
        /// 押せなくなるため、ヘッダーではなく内容の先頭に置く
        /// </summary>
        private void DrawExtendToggle(GUIView view)
        {
            view.BeginLayout(GUIView.LayoutDirection.Free);

            view.currentPos.x = _windowWidth - 80;

            view.DrawToggle("拡張", config.animationExtend, 60, 20, newValue =>
            {
                config.animationExtend = newValue;
                config.dirty = true;
            });

            view.EndLayout();
        }

        private void DrawMainContentCompact()
        {
            var view = _contentView;
            view.ResetLayout();
            view.SetEnabled(!ComboBoxPopupWindow.instance.IsOpenFor(this));

            DrawExtendToggle(view);

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

        private void DrawMainContentExtend()
        {
            var view = _contentView;
            view.ResetLayout();
            view.SetEnabled(!ComboBoxPopupWindow.instance.IsOpenFor(this));

            DrawExtendToggle(view);

            if (_maid == null || _animation == null)
            {
                return;
            }

            view.DrawHorizontalLine();
            view.AddSpace(5);

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

                view.SetEnabled(!ComboBoxPopupWindow.instance.IsOpenFor(this));

                view.BeginHorizontal();
                {
                    var layerActive = layer == modItemManager.animationLayer;
                    view.DrawToggle($"レイヤー{layer}: {info.anmName}", layerActive, 350, 20, newValue =>
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

                view.SetEnabled(!ComboBoxPopupWindow.instance.IsOpenFor(this) && state != null);

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
                if (layer == 0) 
                {
                    view.DrawHorizontalLine();
                    continue;
                }

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

                view.DrawHorizontalLine();
            }

            view.SetEnabled(!ComboBoxPopupWindow.instance.IsOpenFor(this));

            view.EndScrollView();
        }

    }
}