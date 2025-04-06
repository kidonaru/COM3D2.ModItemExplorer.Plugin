using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace COM3D2.ModItemExplorer.Plugin
{
    public class ColorPaletteWindow : IWindow
    {
        public readonly static int WINDOW_ID = 4581852;
        public readonly static int WINDOW_WIDTH = 540;
        public readonly static int WINDOW_HEIGHT = 240;
        public readonly static int HEADER_HEIGHT = 20;
        public readonly static int COLOR_PICKER_SIZE = 150;

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
        private int _colorPickerWindowWidth = COLOR_PICKER_SIZE + 10;
        private bool _initializedGUI = false;

        private GUIView _rootView = new GUIView();
        private GUIView _headerView = new GUIView();
        private GUIView _contentView = new GUIView();
        private GUIView _colorPickerView = new GUIView();

        private Maid _maid;
        private ColorPaletteManager.ColorData _colorData;
        private Dictionary<MaidParts.PARTS_COLOR, ColorPaletteManager.ColorData> _initialColorData
            = new Dictionary<MaidParts.PARTS_COLOR, ColorPaletteManager.ColorData>();
        private MaidStatus.EyePartsTab _selectEyeType;
        private ColorPaletteManager.Category _category = ColorPaletteManager.Category.Main;

        private Texture2D _colorGradationTex = null;
        private Texture2D _colorPickerTex = null;

        public GUIStyle gsWin => GUIView.gsWin;

        public ColorPaletteWindow()
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

        public void Call(Maid maid, MaidParts.PARTS_COLOR colorType)
        {
            if (maid == null)
            {
                return;
            }

            isShowWnd = true;
            _maid = maid;
            _category = ColorPaletteManager.Category.Main;
            _colorData = ColorPaletteManager.ColorData.Create(maid, colorType);
            _initialColorData[_colorData.colorType] = _colorData;

            if (colorType == MaidParts.PARTS_COLOR.EYE_L || colorType == MaidParts.PARTS_COLOR.EYE_R)
            {
                var dataL = ColorPaletteManager.ColorData.Create(maid, MaidParts.PARTS_COLOR.EYE_L);
                var dataR = ColorPaletteManager.ColorData.Create(maid, MaidParts.PARTS_COLOR.EYE_R);
                _initialColorData[MaidParts.PARTS_COLOR.EYE_L] = dataL;
                _initialColorData[MaidParts.PARTS_COLOR.EYE_R] = dataR;

                if (_selectEyeType == MaidStatus.EyePartsTab.LR)
                {
                    if (!dataL.EqualsData(dataR))
                    {
                        _selectEyeType = MaidStatus.EyePartsTab.R;
                    }
                }

                OnSelectEyeTypeChanged();
            }
        }

        public void Close()
        {
            isShowWnd = false;
        }

        public void InitView()
        {
            _rootView.Init(0, 0, _windowWidth, _windowHeight);
            _headerView.Init(0, 0, _windowWidth, HEADER_HEIGHT);
            _contentView.Init(_colorPickerWindowWidth, HEADER_HEIGHT, _windowWidth - _colorPickerWindowWidth, _windowHeight - HEADER_HEIGHT);
            _colorPickerView.Init(0, HEADER_HEIGHT, _colorPickerWindowWidth, _windowHeight - HEADER_HEIGHT);

            _headerView.parent = _rootView;
            _contentView.parent = _rootView;
            _colorPickerView.parent = _rootView;
        }

        public void Init()
        {
        }

        public void Update()
        {
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

        private int _colorGradationHue = -1;

        private void UpdateColorGradation()
        {
            if (_colorGradationTex == null)
            {
                return;
            }

            var commonItem = _colorData.GetItem(_category);
            if (_colorGradationHue == commonItem.hue)
            {
                return;
            }
            _colorGradationHue = commonItem.hue;

            MTEUtils.LogDebug("UpdateColorGradation");

            TextureUtils.ClearTexture(_colorGradationTex, Color.clear);

            for (int y = 0; y < 50; y++)
            {
                for (int x = 0; x < 50; x++)
                {
                    var chroma = x / 50f;
                    var brightness = y / 50f * 510f / 255f;

                    var color = Color.HSVToRGB(
                        commonItem.hue / 255f,
                        chroma,
                        brightness);

                    _colorGradationTex.SetPixel(x, y, color);
                }
            }

            _colorGradationTex.Apply();
        }

        public void InitGUI()
        {
            if (_initializedGUI)
            {
                return;
            }
            _initializedGUI = true;

            InitView();

            if (_colorGradationTex == null)
            {
                _colorGradationTex = new Texture2D(50, 50);
            }

            if (_colorPickerTex == null)
            {
                _colorPickerTex = new Texture2D(13, 13);
                TextureUtils.ClearTexture(_colorPickerTex, new Color(0f, 0f, 0f, 0f));

                var center = new Vector2(13 * 0.5f, 13 * 0.5f);
                TextureUtils.DrawCircleFillTexture(_colorPickerTex, center, 6, Color.black);
                TextureUtils.DrawCircleFillTexture(_colorPickerTex, center, 5, Color.white);
                TextureUtils.DrawCircleFillTexture(_colorPickerTex, center, 3, new Color(1f, 1f, 1f, 0f));
            }

            if (config.colorPaletteWindowPosX != -1 && config.colorPaletteWindowPosY != -1)
            {
                _windowRect.x = config.colorPaletteWindowPosX;
                _windowRect.y = config.colorPaletteWindowPosY;
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

            windowRect = GUI.Window(WINDOW_ID, windowRect, DrawWindow, "カラーパレット", gsWin);
            MTEUtils.ResetInputOnScroll(windowRect);

            if (config.colorPaletteWindowPosX != (int)windowRect.x ||
                config.colorPaletteWindowPosY != (int)windowRect.y)
            {
                config.colorPaletteWindowPosX = (int)windowRect.x;
                config.colorPaletteWindowPosY = (int)windowRect.y;
            }
        }

        private void DrawWindow(int id)
        {
            _rootView.ResetLayout();

            DrawHeader();
            DrawColorPicker();
            DrawContent();

            _rootView.DrawComboBox();

            if (!_colorPickerInfo.isDragging)
            {
                GUI.DragWindow();
            }
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

        private static readonly Dictionary<ColorPaletteManager.Category, string> _categoryNameMap = new Dictionary<ColorPaletteManager.Category, string>
        {
            { ColorPaletteManager.Category.Main, "基本色" },
            { ColorPaletteManager.Category.Shadow, "影色" },
            { ColorPaletteManager.Category.OutLine, "輪郭色" },
        };

        private bool visibleShadowRate
        {
            get => _category == ColorPaletteManager.Category.Main
                || _category == ColorPaletteManager.Category.Shadow;
        }

        private GUIView.DragInfo _colorPickerInfo = new GUIView.DragInfo();

        private void DrawColorPicker()
        {
            var view = _colorPickerView;
            view.ResetLayout();
            view.SetEnabled(!view.IsComboBoxFocused());

            UpdateColorGradation();

            var drawRect = view.GetDrawRect(150, 150);

            var pos = Event.current.mousePosition;
            pos.x -= drawRect.x;
            pos.y -= drawRect.y;

            view.InvokeActionOnDragStart(
                drawRect,
                _colorPickerInfo,
                pos,
                null
            );

            view.InvokeActionOnDragging(
                _colorPickerInfo,
                newPos =>
                {
                    var horizon = newPos.x / 150f;
                    var vertical = 1f - newPos.y / 150f;

                    var commonItem = _colorData.GetItem(_category);
                    commonItem.chroma = (int) (horizon * 255);
                    commonItem.brightness = (int) (vertical * 510);

                    _colorData.SetItem(_category, commonItem);
                    ApplyColorData();
                }
            );

            view.DrawTexture(_colorGradationTex, 150, 150);

            {
                var commonItem = _colorData.GetItem(_category);
                var pickerPos = Vector2.zero;
                {
                    var horizon = commonItem.chroma / 255f;
                    var vertical = 1f - commonItem.brightness / 510f;

                    pickerPos.x = Mathf.Clamp(horizon * 150, 0, 150);
                    pickerPos.y = Mathf.Clamp(vertical * 150, 0, 150);
                }

                var halfPickerSize = _colorPickerTex.width / 2;
                view.currentPos = pickerPos - new Vector2(halfPickerSize, halfPickerSize);
                view.DrawTexture(_colorPickerTex);
            }
        }

        private void DrawContent()
        {
            var view = _contentView;
            view.ResetLayout();
            view.SetEnabled(!view.IsComboBoxFocused());

            if (_maid == null)
            {
                return;
            }

            if (_colorData.colorType == MaidParts.PARTS_COLOR.EYE_L || _colorData.colorType == MaidParts.PARTS_COLOR.EYE_R)
            {
                view.margin = 0;
                var newEyePartsTab = view.DrawTabs(_selectEyeType, 50, 20);
                if (newEyePartsTab != _selectEyeType)
                {
                    _selectEyeType = newEyePartsTab;
                    OnSelectEyeTypeChanged();
                }
                view.margin = GUIView.defaultMargin;
            }

            view.BeginHorizontal();
            {
                var tabTypes = MTEUtils.GetEnumValues<ColorPaletteManager.Category>();
                foreach (var tabType in tabTypes)
                {
                    if (tabType == ColorPaletteManager.Category.OutLine &&
                        !_colorData.enabledOutLine)
                    {
                        continue;
                    }
                    
                    var _item = _colorData.GetItem(tabType);

                    var color = Color.HSVToRGB(
                        _item.hue / 255f,
                        _item.chroma / 255f,
                        _item.brightness / 255f);

                    view.DrawTexture(GUIView.texWhite, 20, 20, color);

                    var buttonColor = _category == tabType ? Color.green : Color.white;
                    var name = _categoryNameMap[tabType];
                    if (view.DrawButton(name, 80, 20, true, buttonColor))
                    {
                        _category = tabType;
                    }
                }
            }
            view.EndLayout();

            var commonItem = _colorData.GetItem(_category);
            var initialColorData = _initialColorData[_colorData.colorType];
            var initialCommonItem = initialColorData.GetItem(_category);
            bool visibleShadowRate = this.visibleShadowRate;

            var updated = false;

            updated |= view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "色相",
                labelWidth = 50,
                width = -1,
                fieldType = FloatFieldType.Int,
                min = 0,
                max = 255,
                step = 1,
                defaultValue = initialCommonItem.hue,
                value = commonItem.hue,
                onChanged = value =>
                {
                    commonItem.hue = (int) value;
                },
            });

            updated |= view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "彩度",
                labelWidth = 50,
                width = -1,
                fieldType = FloatFieldType.Int,
                min = 0,
                max = 255,
                step = 1,
                defaultValue = initialCommonItem.chroma,
                value = commonItem.chroma,
                onChanged = value =>
                {
                    commonItem.chroma = (int) value;
                },
            });

            updated |= view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "明度",
                labelWidth = 50,
                width = -1,
                fieldType = FloatFieldType.Int,
                min = 0,
                max = 510,
                step = 1,
                defaultValue = initialCommonItem.brightness,
                value = commonItem.brightness,
                onChanged = value =>
                {
                    commonItem.brightness = (int) value;
                },
            });

            updated |= view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "ｺﾝﾄﾗｽﾄ",
                labelWidth = 50,
                width = -1,
                fieldType = FloatFieldType.Int,
                min = 0,
                max = 200,
                step = 1,
                defaultValue = initialCommonItem.contrast,
                value = commonItem.contrast,
                onChanged = value =>
                {
                    commonItem.contrast = (int) value;
                },
            });

            if (visibleShadowRate)
            {
                updated |= view.DrawSliderValue(new GUIView.SliderOption
                {
                    label = "影率",
                    labelWidth = 50,
                    width = -1,
                    fieldType = FloatFieldType.Int,
                    min = 0,
                    max = 255,
                    step = 1,
                    defaultValue = initialColorData.shadowRate,
                    value = _colorData.shadowRate,
                    onChanged = value =>
                    {
                        _colorData.shadowRate = (int) value;
                    },
                });
            }

            view.BeginHorizontal();
            {
                if (view.DrawButton("リセット", 80, 20))
                {
                    commonItem = initialCommonItem;
                    updated = true;
                }

                if (view.DrawButton("全リセット", 80, 20))
                {
                    commonItem = initialCommonItem;
                    _colorData = initialColorData;
                    updated = true;
                }
            }

            if (updated)
            {
                _colorData.SetItem(_category, commonItem);
                ApplyColorData();
            }
        }

        private void ApplyColorData()
        {
            ColorPaletteManager.ColorData.Apply(_maid, _colorData);

            if ((_colorData.colorType == MaidParts.PARTS_COLOR.EYE_L || _colorData.colorType == MaidParts.PARTS_COLOR.EYE_R)
                && _selectEyeType == MaidStatus.EyePartsTab.LR)
            {
                var data = ColorPaletteManager.ColorData.Create(_maid, MaidParts.PARTS_COLOR.EYE_L);
                data.colorType = MaidParts.PARTS_COLOR.EYE_R;
                ColorPaletteManager.ColorData.Apply(_maid, data);
            }
        }

        private void OnSelectEyeTypeChanged()
        {
            if (_selectEyeType == MaidStatus.EyePartsTab.LR)
            {
                var data = ColorPaletteManager.ColorData.Create(_maid, MaidParts.PARTS_COLOR.EYE_L);
                data.colorType = MaidParts.PARTS_COLOR.EYE_R;
                ColorPaletteManager.ColorData.Apply(_maid, data);
            }

            var colorType = MaidParts.PARTS_COLOR.EYE_L;
            if (_selectEyeType == MaidStatus.EyePartsTab.R)
            {
                colorType = MaidParts.PARTS_COLOR.EYE_R;
            }

            _colorData = ColorPaletteManager.ColorData.Create(_maid, colorType);
        }

        public void OnScreenSizeChanged()
        {
            MTEUtils.AdjustWindowPosition(ref _windowRect);
        }
    }
}