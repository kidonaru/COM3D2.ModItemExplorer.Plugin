using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace COM3D2.ModItemExplorer.Plugin
{
    public class HairLengthWindow : IWindow
    {
        public readonly static int WINDOW_ID = 741329;
        public readonly static int WINDOW_WIDTH = 320;
        public readonly static int WINDOW_HEIGHT = 320;
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
        private MPN _mpn;
        private List<HairLengthData> _dataList = new List<HairLengthData>();
        private int _dataCount = 0;
        private bool _setupRequested = false;
        private int _setupWaitFrame = 0;

        public HairLengthWindow()
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

        public void ResetData()
        {
            MTEUtils.LogDebug("HairLengthWindow.ResetData");

            foreach (var data in _dataList)
            {
                data.Init();
            }
            _dataCount = 0;

            _maid = null;
            _mpn = MPN.null_mpn;
        }

        public void Call(Maid maid, MPN mpn)
        {
            ResetData();

            if (maid == null || mpn == MPN.null_mpn)
            {
                isShowWnd = false;
                return;
            }

            _maid = maid;
            _mpn = mpn;
            _setupRequested = true;
            _setupWaitFrame = 10;
        }

        public void Update()
        {
            if (_setupRequested)
            {
                if (GameMain.Instance.CharacterMgr.IsBusy())
                {
                    return;
                }

                if (_setupWaitFrame > 0)
                {
                    _setupWaitFrame--;
                    return;
                }

                MTEUtils.LogDebug("[HairLengthWindow] Setup");

                _setupRequested = false;

                var hairLengthMap = _maid.body0.GetHairLengthListFromMPN(_mpn);
                if (hairLengthMap == null || hairLengthMap.Count == 0)
                {
                    isShowWnd = false;
                    return;
                }

                isShowWnd = true;

                while (_dataList.Count < hairLengthMap.Count)
                {
                    _dataList.Add(new HairLengthData());
                }

                _dataCount = hairLengthMap.Count;

                var i = 0;
                foreach (var kvp in hairLengthMap)
                {
                    var groupName = kvp.Key;
                    var hairLength = kvp.Value;
                    var data = _dataList[i++];
                    data.Init(_maid, groupName, hairLength);
                }
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
            _contentView.Init(0, HEADER_HEIGHT, _windowWidth, _windowHeight - HEADER_HEIGHT);

            _headerView.parent = _rootView;
            _contentView.parent = _rootView;
        }

        public void Init()
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

        public void InitGUI()
        {
            if (_initializedGUI)
            {
                return;
            }
            _initializedGUI = true;

            InitView();

            if (config.hairLengthWindowPosX != -1 && config.hairLengthWindowPosY != -1)
            {
                _windowRect.x = config.hairLengthWindowPosX;
                _windowRect.y = config.hairLengthWindowPosY;
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

            if (_windowHeight != windowRect.height)
            {
                _windowRect.height = _windowHeight;
                InitView();
            }

            windowRect = GUI.Window(WINDOW_ID, windowRect, DrawWindow, "髪の長さ", GUIView.gsWin);
            MTEUtils.ResetInputOnScroll(windowRect);

            if (config.hairLengthWindowPosX != (int)windowRect.x ||
                config.hairLengthWindowPosY != (int)windowRect.y)
            {
                config.hairLengthWindowPosX = (int)windowRect.x;
                config.hairLengthWindowPosY = (int)windowRect.y;
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

            if (_dataCount == 0 || _setupRequested)
            {
                view.DrawLabel("読み込み中...", -1, 20);
                return;
            }

            view.BeginHorizontal();
            {
                view.currentPos.x = 190;
                if (view.DrawButton("リセット", 60, 20))
                {
                    foreach (var data in _dataList)
                    {
                        data.Reset();
                    }
                }

                if (view.DrawButton("C", 20, 20))
                {
                    try
                    {
                        var listXml = new HairLengthListXml(_dataList);

                        var serializer = new XmlSerializer(typeof(HairLengthListXml));
                        using (var writer = new StringWriter())
                        {
                            serializer.Serialize(writer, listXml);
                            var framesXml = writer.ToString();
                            GUIUtility.systemCopyBuffer = framesXml;
                        }

                        MTEUtils.Log("クリップボードにコピーしました");
                    }
                    catch (Exception e)
                    {
                        MTEUtils.LogException(e);
                        MTEUtils.ShowDialog("コピーに失敗しました");
                    }
                }

                if (view.DrawButton("P", 20, 20))
                {
                    try
                    {
                        var serializer = new XmlSerializer(typeof(HairLengthListXml));
                        using (var reader = new StringReader(GUIUtility.systemCopyBuffer))
                        {
                            var listXml = (HairLengthListXml) serializer.Deserialize(reader);
                            foreach (var dataXml in listXml.list)
                            {
                                var data = _dataList.Find(d => d.groupName == dataXml.groupName);
                                if (data != null)
                                {
                                    data.ApplyXml(dataXml);
                                }
                            }
                        }

                        MTEUtils.Log("クリップボードからペーストしました");
                    }
                    catch (Exception e)
                    {
                        MTEUtils.LogException(e);
                        MTEUtils.ShowDialog("ペーストに失敗しました");
                    }
                }
            }
            view.EndLayout();

            view.currentPos.y -= 20;
            view.layoutMaxPos.y = view.currentPos.y;

            for (var i = 0; i < _dataCount; i++)
            {
                var data = _dataList[i];

                view.DrawLabel(data.groupName, 180, 20);

                view.DrawSliderValue(new GUIView.SliderOption
                {
                    width = -1,
                    min = 0,
                    max = 1,
                    step = 0.01f,
                    defaultValue = data.initialLenghtRate,
                    value = data.lenghtRate,
                    onChanged = value =>
                    {
                        data.lenghtRate = value;
                        data.Apply();
                    },
                });
            }

            view.AddSpace(10);

            _windowHeight = (int) (view.currentPos.y + view.viewRect.y);
        }

        public void OnScreenSizeChanged()
        {
            MTEUtils.AdjustWindowPosition(ref _windowRect);
        }
    }
}