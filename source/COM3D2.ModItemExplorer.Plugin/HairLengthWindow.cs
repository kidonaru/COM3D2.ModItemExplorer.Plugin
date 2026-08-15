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
    public class HairLengthWindow : DockableWindowBase
    {
        public readonly static int WINDOW_ID = 741329;
        public readonly static int WINDOW_WIDTH = 320;
        public readonly static int WINDOW_HEIGHT = 320;

        private static ModItemExplorer plugin => ModItemExplorer.instance;
        private static ModItemManager modItemManager => ModItemManager.instance;
        private static Config config => ConfigManager.instance.config;

        protected override int windowId => WINDOW_ID;
        protected override string windowTitle => "髪の長さ";
        protected override int minWidth => WINDOW_WIDTH;
        protected override int minHeight => WINDOW_HEIGHT;

        private int _windowWidth = WINDOW_WIDTH;
        private int _windowHeight = WINDOW_HEIGHT;

        private GUIView _rootView = new GUIView();
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
        }

        protected override void LoadPlacement(out int x, out int y, out int width, out int height)
        {
            x = config.hairLengthWindowPosX;
            y = config.hairLengthWindowPosY;
            width = config.hairLengthWindowWidth;
            height = config.hairLengthWindowHeight;
        }

        protected override void StorePlacement(int x, int y, int width, int height)
        {
            config.hairLengthWindowPosX = x;
            config.hairLengthWindowPosY = y;
            config.hairLengthWindowWidth = width;
            config.hairLengthWindowHeight = height;
            config.dirty = true;
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

        private void UpdateSetup()
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

            UpdateSetup();
        }

        protected override void DrawContent()
        {
            _rootView.ResetLayout();

            DrawMainContent();

            ComboBoxPopupWindow.instance.ProcessFocus(_rootView, this);
        }

        private void DrawMainContent()
        {
            var view = _contentView;
            view.ResetLayout();
            view.SetEnabled(!ComboBoxPopupWindow.instance.IsOpenFor(this));

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

            // 毛髪グループ数はモデル次第で窓の高さを超えるため、スライダー一覧はスクロールさせる
            view.BeginScrollView();

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

            view.EndScrollView();
        }

    }
}