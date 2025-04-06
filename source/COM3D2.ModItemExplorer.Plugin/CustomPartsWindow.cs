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
    public class CustomPartsWindow : IWindow
    {
        public readonly static int WINDOW_ID = 4269465;
        public readonly static int WINDOW_WIDTH = 480;
        public readonly static int WINDOW_HEIGHT = 360;
        public readonly static int HEADER_HEIGHT = 20;

        private static ModItemExplorer plugin => ModItemExplorer.instance;
        private static ModItemManager modItemManager => ModItemManager.instance;
        private static Config config => ConfigManager.instance.config;

        public int windowIndex { get; set; }
        
        private bool _isShowWnd;
        public bool isShowWnd
        {
            get => _isShowWnd;
            set
            {
                if (_isShowWnd == value)
                {
                    return;
                }

                _isShowWnd = value;

                if (!value)
                {
                    editMode = false;
                }
            }
        }

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
        private Animation _animation;
        private bool _setupRequested = false;
        private bool _applyRequested = false;
        private List<CustomPartsData> _dataList = new List<CustomPartsData>();
        private int _apIndex = 0;
        private int _apCount = 0;

        public static readonly HashSet<MaidPartType> enabledMaidPartType = new HashSet<MaidPartType>
        {
            MaidPartType.acchat,
            MaidPartType.headset,
            MaidPartType.acckami,
            MaidPartType.acckamisub,
            MaidPartType.megane,
            MaidPartType.acchead,
            MaidPartType.hairt,
            MaidPartType.hairaho,
            MaidPartType.acckubi,
            MaidPartType.acckubiwa,
            MaidPartType.accmimi,
            MaidPartType.acchana,
        };

        public bool playMaidAnimation
        {
            get
            {
                if (_animation == null)
                {
                    return false;
                }

                return !_maid.GetLockHeadAndEye();
            }
            set
            {
                if (_animation == null)
                {
                    return;
                }

                var animationState = _maid.body0.GetAnist();
                if (animationState == null)
                {
                    return;
                }

                animationState.enabled = value;
                _maid.LockHeadAndEye(!value);
                if (!GameMain.Instance.VRMode)
                {
                    return;
                }

                if (GameMain.Instance.OvrMgr.ovr_obj.left_controller != null && GameMain.Instance.OvrMgr.ovr_obj.left_controller.grip_collider != null)
                {
                    GameMain.Instance.OvrMgr.ovr_obj.left_controller.grip_collider.ResetGrip();
                }

                if (GameMain.Instance.OvrMgr.ovr_obj.right_controller != null && GameMain.Instance.OvrMgr.ovr_obj.right_controller.grip_collider != null)
                {
                    GameMain.Instance.OvrMgr.ovr_obj.right_controller.grip_collider.ResetGrip();
                }

                var component = _maid.GetComponent<MaidColliderCollect>();
                if (!(component != null))
                {
                    return;
                }

                var collider = component.GetCollider(MaidColliderCollect.ColliderType.Grab);
                foreach (CapsuleCollider item in collider)
                {
                    item.gameObject.SetActive(value);
                }
            }
        }

        public bool editMode
        {
            get => !playMaidAnimation;
            set => playMaidAnimation = !value;
        }

        public CustomPartsWindow()
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
            MTEUtils.LogDebug("CustomPartsWindow.ResetData");

            foreach (var data in _dataList)
            {
                data.Init();
            }
            _apIndex = 0;
            _apCount = 0;

            isShowWnd = false;

            _maid = null;
            _mpn = MPN.null_mpn;
            _animation = null;
        }

        public void Call(Maid maid, MaidPartType maidPartType)
        {
            if (!enabledMaidPartType.Contains(maidPartType))
            {
                ResetData();
                return;
            }

            var mpn = maidPartType.ToMPN();
            if (maid == null || mpn == MPN.null_mpn)
            {
                ResetData();
                return;
            }

            if (maid == _maid && mpn == _mpn)
            {
                _applyRequested = true;
                return;
            }

            ResetData();

            _maid = maid;
            _mpn = mpn;
            _animation = maid.body0.m_Bones.GetComponent<Animation>();

            _setupRequested = true;
        }

        public void Update()
        {
            if (_setupRequested)
            {
                if (GameMain.Instance.CharacterMgr.IsBusy())
                {
                    return;
                }

                MTEUtils.LogDebug("[CustomPartsWindow] Setup");

                _setupRequested = false;

                var apList = _maid.body0.GetAttachPointListFromMPN(_mpn);
                if (apList == null || apList.Count == 0)
                {
                    return;
                }

                isShowWnd = true;

                while (_dataList.Count < apList.Count)
                {
                    _dataList.Add(new CustomPartsData());
                }

                _apIndex = 0;
                _apCount = apList.Count;

                for (int i = 0; i < apList.Count; i++)
                {
                    var ap = apList[i];
                    var data = _dataList[i];
                    data.Init(_maid, ap.Key, ap.Value);
                }

                var _data = _dataList[0];
                if (!_data.enabled)
                {
                    editMode = false;
                }
                else if (config.customPartsAutoEditMode)
                {
                    editMode = true;
                }
            }

            if (_applyRequested)
            {
                if (GameMain.Instance.CharacterMgr.IsBusy())
                {
                    return;
                }

                MTEUtils.LogDebug("[CustomPartsWindow] Apply");

                _applyRequested = false;

                var apList = _maid.body0.GetAttachPointListFromMPN(_mpn);
                if (apList == null || apList.Count == 0)
                {
                    return;
                }

                if (apList.Count != _apCount)
                {
                    _setupRequested = true;
                    return;
                }

                for (int i = 0; i < _apCount; i++)
                {
                    var ap = apList[i];
                    var data = _dataList[i];

                    if (data.slotId != ap.Key || data.apName != ap.Value)
                    {
                        _setupRequested = true;
                        return;
                    }
                }

                isShowWnd = true;

                for (int i = 0; i < _apCount; i++)
                {
                    var data = _dataList[i];
                    data.ApplyLocalTransform();
                }

                var _data = _dataList[0];
                if (!_data.enabled)
                {
                    editMode = false;
                }
                else if (config.customPartsAutoEditMode)
                {
                    editMode = true;
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

            if (config.customPartsWindowPosX != -1 && config.customPartsWindowPosY != -1)
            {
                _windowRect.x = config.customPartsWindowPosX;
                _windowRect.y = config.customPartsWindowPosY;
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

            windowRect = GUI.Window(WINDOW_ID, windowRect, DrawWindow, "カスタムパーツ", GUIView.gsWin);
            MTEUtils.ResetInputOnScroll(windowRect);

            if (config.customPartsWindowPosX != (int)windowRect.x ||
                config.customPartsWindowPosY != (int)windowRect.y)
            {
                config.customPartsWindowPosX = (int)windowRect.x;
                config.customPartsWindowPosY = (int)windowRect.y;
            }
        }

        private void DrawWindow(int id)
        {
            _rootView.ResetLayout();

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

            if (_apCount == 0 || _setupRequested)
            {
                return;
            }

            bool apIndexChanged = false;

            view.BeginHorizontal();
            {
                for (int i = 0; i < _apCount; ++i)
                {
                    var buttonColor = i == _apIndex ? Color.green : Color.white;
                    var name = $"{i + 1}";
                    if (view.DrawButton(name, 20, 20, true, buttonColor))
                    {
                        _apIndex = i;
                        apIndexChanged = true;
                    }
                }
            }
            view.EndLayout();

            var data = _dataList.GetOrDefault(_apIndex);
            if (data == null || data.morph == null)
            {
                return;
            }

            if (apIndexChanged)
            {
                if (!data.enabled)
                {
                    editMode = false;
                }
                else if (config.customPartsAutoEditMode)
                {
                    editMode = true;
                }
                data.UpdateWorldTransform();
            }

            view.BeginHorizontal();
            {
                view.DrawToggle("有効", data.enabled, 60, 20, newValue =>
                {
                    data.enabled = newValue;
                    editMode = data.enabled;
                    data.UpdateWorldTransform();
                });

                view.DrawToggle("編集", editMode, 60, 20, data.enabled, newValue =>
                {
                    editMode = newValue;
                    data.UpdateWorldTransform();
                });

                if (view.DrawButton("リセット", 80, 20))
                {
                    data.Reset();
                }

                if (view.DrawButton("コピー", 80, 20))
                {
                    try
                    {
                        var serializer = new XmlSerializer(typeof(CustomPartsXml));
                        using (var writer = new StringWriter())
                        {
                            serializer.Serialize(writer, data.ToXml());
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

                if (view.DrawButton("ペースト", 80, 20))
                {
                    try
                    {
                        var serializer = new XmlSerializer(typeof(CustomPartsXml));
                        using (var reader = new StringReader(GUIUtility.systemCopyBuffer))
                        {
                            var xml = (CustomPartsXml) serializer.Deserialize(reader);
                            data.ApplyXml(xml);
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

            view.DrawHorizontalLine(Color.gray);

            view.SetEnabled(!view.IsComboBoxFocused() && editMode);

            var updated = false;

            var basePosition = data.basePosition;
            var positionRange = config.customPartsPositionRange;
            var minPosition = basePosition - new Vector3(positionRange, positionRange, positionRange);
            var maxPosition = basePosition + new Vector3(positionRange, positionRange, positionRange);

            updated |= view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "X",
                labelWidth = 30,
                width = -1,
                fieldType = FloatFieldType.F3,
                min = minPosition.x,
                max = maxPosition.x,
                step = 0.001f,
                defaultValue = basePosition.x,
                value = data.position.x,
                onChanged = x => data.position.x = x,
            });

            updated |= view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "Y",
                labelWidth = 30,
                width = -1,
                fieldType = FloatFieldType.F3,
                min = minPosition.y,
                max = maxPosition.y,
                step = 0.001f,
                defaultValue = basePosition.y,
                value = data.position.y,
                onChanged = y => data.position.y = y,
            });

            updated |= view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "Z",
                labelWidth = 30,
                width = -1,
                fieldType = FloatFieldType.F3,
                min = minPosition.z,
                max = maxPosition.z,
                step = 0.001f,
                defaultValue = basePosition.z,
                value = data.position.z,
                onChanged = z => data.position.z = z,
            });

            updated |= view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "RX",
                labelWidth = 30,
                width = -1,
                min = -180,
                max = 180,
                step = 0.1f,
                defaultValue = 0f,
                value = data.eulerAngles.x,
                onChanged = x =>
                {
                    var eulerAngles = data.eulerAngles;
                    eulerAngles.x = x;
                    data.eulerAngles = eulerAngles;
                },
            });

            updated |= view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "RY",
                labelWidth = 30,
                width = -1,
                min = -180,
                max = 180,
                step = 0.1f,
                defaultValue = 0f,
                value = data.eulerAngles.y,
                onChanged = y =>
                {
                    var eulerAngles = data.eulerAngles;
                    eulerAngles.y = y;
                    data.eulerAngles = eulerAngles;
                },
            });

            updated |= view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "RZ",
                labelWidth = 30,
                width = -1,
                min = -180,
                max = 180,
                step = 0.1f,
                defaultValue = 0f,
                value = data.eulerAngles.z,
                onChanged = z =>
                {
                    var eulerAngles = data.eulerAngles;
                    eulerAngles.z = z;
                    data.eulerAngles = eulerAngles;
                },
            });

            updated |= view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "SX",
                labelWidth = 30,
                width = -1,
                min = 0,
                max = 5,
                step = 0.1f,
                defaultValue = 1f,
                value = data.scale.x,
                onChanged = x => data.scale.x = x,
            });

            updated |= view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "SY",
                labelWidth = 30,
                width = -1,
                min = 0,
                max = 5,
                step = 0.1f,
                defaultValue = 1f,
                value = data.scale.y,
                onChanged = y => data.scale.y = y,
            });

            updated |= view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "SZ",
                labelWidth = 30,
                width = -1,
                min = 0,
                max = 5,
                step = 0.1f,
                defaultValue = 1f,
                value = data.scale.z,
                onChanged = z => data.scale.z = z,
            });

            updated |= view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "拡縮",
                labelWidth = 30,
                width = -1,
                min = 0,
                max = 5,
                step = 0.1f,
                defaultValue = 1f,
                value = data.scale.x,
                onChanged = x => data.scale = new Vector3(x, x, x),
            });

            if (updated)
            {
                data.ApplyWorldTransform();
            }
        }

        public void OnScreenSizeChanged()
        {
            MTEUtils.AdjustWindowPosition(ref _windowRect);
        }
    }
}