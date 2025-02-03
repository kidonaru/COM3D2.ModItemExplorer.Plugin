using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace COM3D2.ModItemExplorer.Plugin
{
    public class ModItemWindow : IWindow
    {
        public readonly static int WINDOW_ID = 582870;
        public readonly static int VARIATION_WINDOW_ID = 4474065;
        public readonly static int COLOR_SET_WINDOW_ID = 6345715;
        public readonly static int MIN_WINDOW_WIDTH = 640;
        public readonly static int MIN_WINDOW_HEIGHT = 480;
        public readonly static int HEADER_HEIGHT = 20;
        public readonly static int INFO_HEIGHT = (20 + 5) * 3 + 10;
        public readonly static int MIN_NAVI_WIDTH = 100;
        public readonly static int MAX_NAVI_WIDTH = 400;
        public readonly static int VARIATION_WIDTH = 100;
        public readonly static int COLOR_SET_WIDTH = 100;
        public readonly static int FOOTER_HEIGHT = 20;

        private static ModItemExplorer plugin => ModItemExplorer.instance;
        private static TextureManager textureManager => TextureManager.instance;
        private static ModItemManager modItemManager => ModItemManager.instance;
        private static MaidPresetManager maidPresetManager => MaidPresetManager.instance;
        private static TempPresetManager maidTempPresetManager => TempPresetManager.instance;
        private static WindowManager windowManager => WindowManager.instance;
        private static ModelHackManagerWrapper modelHackManager => ModelHackManagerWrapper.instance;
        private static ConfigManager configManager => ConfigManager.instance;
        private static Config config => ConfigManager.instance.config;
        private static DirItem rootItem => modItemManager.rootItem;
        private static CharacterMgr characterMgr => GameMain.Instance.CharacterMgr;

        public int windowIndex { get; set; }
        public bool isShowWnd { get; set; }

        private Rect _windowRect;
        public Rect windowRect
        {
            get => _windowRect;
            set => _windowRect = value;
        }

        private Rect _variationWindowRect;
        private Rect _colorSetWindowRect;

        private int _windowWidth = 960;
        private int _windowHeight = 480;
        private int _naviWidth = 200;
        private bool _initializedGUI = false;

        private ModItemBase _selectedItem = null;
        private ModItemBase selectedItem
        {
            get => _selectedItem;
            set
            {
                if (_selectedItem == value)
                {
                    return;
                }

                if (selectedColorSet != null)
                {
                    selectedColorSet.scrollPosition = _colorSetView.scrollPosition;
                }

                if (selectedMenuItem != null)
                {
                    selectedMenuItem.scrollPosition = _variationView.scrollPosition;
                }

                _selectedItem = value;

                if (selectedColorSet != null)
                {
                    _colorSetView.scrollPosition = selectedColorSet.scrollPosition;
                }

                if (selectedMenuItem != null)
                {
                    _variationView.scrollPosition = selectedMenuItem.scrollPosition;
                }

                windowManager.colorPaletteWindow.Close();
                windowManager.customPartsWindow.Close();
            }
        }

        private MenuInfo _mouseOverMenu = null;
        private ModItemBase _mouseOverItem = null;
        private int _mouseOverFrameCount = 0;
        private ModItemBase _focusedItem = null;

        private GUIView _rootView = new GUIView();
        private GUIView _headerView = new GUIView();
        private GUIView _infoView = new GUIView();
        private GUIView _naviView = new GUIView();
        private GUIView _contentView = new GUIView();
        private GUIView _contentSettingView = new GUIView();
        private GUIView _footerView = new GUIView();

        private GUIView _variationView = new GUIView();
        private GUIView _colorSetView = new GUIView();

        public GUIStyle gsWin => GUIView.gsWin;

        private GUIStyle gsHiddenButton = new GUIStyle("button")
        {
            fontSize = 12,
            alignment = TextAnchor.MiddleCenter,
        };
        private GUIStyle gsNaviButton = new GUIStyle("button")
        {
            fontSize = 12,
            alignment = TextAnchor.MiddleLeft,
        };

        private MenuItem selectedMenuItem => selectedItem as MenuItem;
        private ColorSetInfo selectedColorSet => selectedMenuItem?.colorSet;

        private bool isVariationVisible
        {
            get
            {
                if (selectedMenuItem == null)
                {
                    return false;
                }

                if (selectedMenuItem.colorSet == null)
                {
                    return true;
                }

                if (selectedMenuItem.menuList != null && selectedMenuItem.menuList.Count > 1)
                {
                    return true;
                }

                return false;
            }
        }

        private bool isColorSetVisible
        {
            get
            {
                if (selectedMenuItem == null)
                {
                    return false;
                }

                if (selectedMenuItem.colorSet != null)
                {
                    return true;
                }

                return false;
            }
        }

        public ModItemWindow()
        {
            this.windowIndex = 0;
            this.isShowWnd = true;
            this.windowRect = new Rect(
                Screen.width - _windowWidth - 30,
                100,
                _windowWidth,
                _windowHeight
            );
        }

        public void InitView()
        {
            _rootView.Init(0, 0, _windowWidth, _windowHeight);
            _headerView.Init(0, 0, _windowWidth, HEADER_HEIGHT);
            _infoView.Init(0, HEADER_HEIGHT, _windowWidth, INFO_HEIGHT);

            var contentHeight = _windowHeight - HEADER_HEIGHT - INFO_HEIGHT - FOOTER_HEIGHT;
            _naviView.Init(0, HEADER_HEIGHT + INFO_HEIGHT, _naviWidth, contentHeight);
            _contentView.Init(_naviWidth, HEADER_HEIGHT + INFO_HEIGHT, _windowWidth - _naviWidth, contentHeight);
            _contentSettingView.Init(_naviWidth, HEADER_HEIGHT + INFO_HEIGHT, _windowWidth - _naviWidth, contentHeight);
            _footerView.Init(0, _windowHeight - FOOTER_HEIGHT, _windowWidth, FOOTER_HEIGHT);

            _variationView.Init(0, 0, VARIATION_WIDTH, _windowHeight);
            _colorSetView.Init(0, 0, COLOR_SET_WIDTH, _windowHeight);

            _headerView.parent = _rootView;
            _infoView.parent = _rootView;
            _naviView.parent = _rootView;
            _contentView.parent = _rootView;
            _contentSettingView.parent = _rootView;
            _footerView.parent = _rootView;
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

            selectedItem = null;
            _mouseOverMenu = null;
            _mouseOverItem = null;
            _mouseOverFrameCount = 0;
            _focusedItem = null;

            _flatViewItem.itemPath = "";
            _flatViewItem.RemoveAllChildren();

            ResetCurrentDirItem();
        }

        public void InitGUI()
        {
            if (_initializedGUI)
            {
                return;
            }
            _initializedGUI = true;

            ResetCurrentDirItem();

            gsHiddenButton.normal.background = GUIView.CreateColorTexture(new Color(0, 0, 0, 0));
            UpdateItemLabelBGAlpha();

            InitView();

            if (config.windowPosX != -1 && config.windowPosY != -1)
            {
                _windowRect.x = config.windowPosX;
                _windowRect.y = config.windowPosY;
            }

            MTEUtils.AdjustWindowPosition(ref _windowRect);
        }

        private void UpdateItemLabelBGAlpha()
        {
            GUIView.gsTileLabel.normal.background = GUIView.CreateColorTexture(new Color(0, 0, 0, config.itemNameBGAlpha));
        }

        public void OnGUI()
        {
            InitGUI();

            if (_windowHeight != config.windowHeight)
            {
                _windowHeight = config.windowHeight;
                _windowRect.height = _windowHeight;
                InitView();
            }

            if (_windowWidth != config.windowWidth)
            {
                _windowWidth = config.windowWidth;
                _windowRect.width = _windowWidth;
                InitView();
            }

            if (_naviWidth != config.naviWidth)
            {
                _naviWidth = config.naviWidth;
                InitView();
            }

            windowRect = GUI.Window(WINDOW_ID, windowRect, DrawWindow, PluginInfo.WindowName, gsWin);
            MTEUtils.ResetInputOnScroll(windowRect);

            if (config.windowPosX != (int)windowRect.x ||
                config.windowPosY != (int)windowRect.y)
            {
                config.windowPosX = (int)windowRect.x;
                config.windowPosY = (int)windowRect.y;
            }

            Vector2 offset;
            offset.x = windowRect.x + windowRect.width;
            offset.y = windowRect.y;

            if (isVariationVisible)
            {
                _variationWindowRect.width = VARIATION_WIDTH;
                _variationWindowRect.height = windowRect.height;
                _variationWindowRect.position = offset;

                _variationWindowRect = GUI.Window(VARIATION_WINDOW_ID, _variationWindowRect, DrawVariationWindow, "", gsWin);
                MTEUtils.ResetInputOnScroll(_variationWindowRect);

                var diffPosition = _variationWindowRect.position - offset;
                _windowRect.position += diffPosition;

                offset.x += VARIATION_WIDTH;
            }

            if (isColorSetVisible)
            {
                _colorSetWindowRect.width = COLOR_SET_WIDTH;
                _colorSetWindowRect.height = windowRect.height;
                _colorSetWindowRect.position = offset;

                _colorSetWindowRect = GUI.Window(COLOR_SET_WINDOW_ID, _colorSetWindowRect, DrawColorSetWindow, "", gsWin);
                MTEUtils.ResetInputOnScroll(_colorSetWindowRect);

                var diffPosition = _colorSetWindowRect.position - offset;
                _windowRect.position += diffPosition;
            }

            if (_mouseOverItem != null || _mouseOverMenu != null)
            {
                if (Time.frameCount > _mouseOverFrameCount + 5)
                {
                    _mouseOverItem = null;
                    _mouseOverMenu = null;
                }
            }
        }

        private void ExpandParentItem(DirItem item)
        {
            if (item == null)
            {
                return;
            }

            var parent = item.parent as DirItem;
            while (parent != null)
            {
                parent.isExpanded = true;
                parent = parent.parent as DirItem;
            }

            if (item == modItemManager.equippedRootItem)
            {
                modItemManager.UpdateEquippedItems();
            }
            if (item == modItemManager.modelRootItem)
            {
                modItemManager.UpdateModelItems();
            }
            if (item == modItemManager.tempPresetRootItem)
            {
                modItemManager.UpdateTempPresetItems();
            }
        }

        private List<DirItem> _currentDirHistory = new List<DirItem>(16);
        private int _currentDirIndex = -1;

        private DirItem currentDirItem
        {
            get => _currentDirHistory.GetOrDefault(_currentDirIndex);
            set
            {
                if (_currentDirIndex >= 0)
                {
                    _currentDirHistory.RemoveRange(_currentDirIndex + 1, _currentDirHistory.Count - _currentDirIndex - 1);
                }
                _currentDirHistory.Add(value);
                _currentDirIndex = _currentDirHistory.Count - 1;
            }
        }

        private bool canNextCurrentDirItem => _currentDirIndex < _currentDirHistory.Count - 1;
        private bool canPrevCurrentDirItem => _currentDirIndex > 0;

        private void SaveScrollPosition()
        {
            if (currentDirItem != null)
            {
                currentDirItem.scrollPosition = _contentView.scrollPosition;
                currentDirItem.scrollContentSize = _contentView.scrollViewContentRect.size;
            }
        }

        private void LoadScrollPosition()
        {
            if (currentDirItem != null)
            {
                _contentView.scrollPosition = currentDirItem.scrollPosition;
                _contentView.scrollViewContentRect.size = currentDirItem.scrollContentSize;
            }
        }

        private void SetCurrentDirItem(DirItem item)
        {
            if (item == null || !item.isDir)
            {
                return;
            }

            if (_contentMode == ContentMode.設定)
            {
                _contentMode = ContentMode.メイド;
            }

            ExpandParentItem(item);

            if (currentDirItem == item)
            {
                return;
            }

            SaveScrollPosition();
            currentDirItem = item;
            LoadScrollPosition();

            _focusedItem = item;
            selectedItem = null;
            _flatViewItem.itemPath = "";
        }

        private void ResetCurrentDirItem()
        {
            _currentDirHistory.Clear();
            _currentDirIndex = -1;
            SetCurrentDirItem(rootItem);
        }

        private void NextCurrentDirItem()
        {
            if (canNextCurrentDirItem)
            {
                SaveScrollPosition();
                _currentDirIndex++;
                LoadScrollPosition();
            }

            if (_contentMode == ContentMode.設定)
            {
                _contentMode = ContentMode.メイド;
            }

            var item = currentDirItem;
            ExpandParentItem(item);

            _focusedItem = item;
            selectedItem = null;
            _flatViewItem.itemPath = "";
        }

        private void PrevCurrentDirItem()
        {
            if (canPrevCurrentDirItem)
            {
                SaveScrollPosition();
                _currentDirIndex--;
                LoadScrollPosition();
            }

            if (_contentMode == ContentMode.設定)
            {
                _contentMode = ContentMode.メイド;
            }

            var item = currentDirItem;
            ExpandParentItem(item);

            _focusedItem = item;
            selectedItem = null;
            _flatViewItem.itemPath = "";
        }

        private GUIComboBox<Maid> _maidComboBox = new GUIComboBox<Maid>
        {
            getName = (maid, _) => maid == null ? "未選択" : maid.status.fullNameJpStyle,
            buttonSize = new Vector2(100, 20),
            contentSize = new Vector2(150, 300),
        };

        private GUIComboBox<TempPreset> _tempPresetComboBox = new GUIComboBox<TempPreset>
        {
            getName = (tempPreset, _) => tempPreset == null ? "未選択" : tempPreset.preset.strFileName,
            buttonSize = new Vector2(100, 20),
            contentSize = new Vector2(100, 300),
        };

        public enum MaskMode
        {
            None,
            Underwear,
            Swim,
            Nude
        }

        private readonly static List<string> _maskModeNames = new List<string>
        {
            "なし",
            "下着",
            "水着",
            "裸"
        };

        private GUIComboBox<TBody.MaskMode> _maskModeComboBox = new GUIComboBox<TBody.MaskMode>
        {
            items = MTEUtils.GetEnumValues<TBody.MaskMode>(),
            getName = (mask, index) => _maskModeNames.GetOrDefault(index, ""), 
            buttonSize = new Vector2(50, 20),
            contentSize = new Vector2(50, 300),
        };

        private GUIComboBox<CharacterMgr.PresetType> _presetTypeComboBox = new GUIComboBox<CharacterMgr.PresetType>
        {
            items = MTEUtils.GetEnumValues<CharacterMgr.PresetType>(),
            currentIndex = (int)CharacterMgr.PresetType.All,
            getName = (presetType, index) => MTEUtils.GetPresetTypeName(presetType), 
            buttonSize = new Vector2(50, 20),
            contentSize = new Vector2(50, 300),
        };

        private GUIComboBox<string> _pluginComboBox = new GUIComboBox<string>
        {
            getName = (name, index) => name, 
            buttonSize = new Vector2(120, 20),
            contentSize = new Vector2(120, 300),
        };

        private GUIView.DraggableInfo _windowSizeDraggableInfo = new GUIView.DraggableInfo();
        private GUIView.DraggableInfo _naviWidthDraggableInfo = new GUIView.DraggableInfo();

        private enum ContentMode
        {
            メイド,
            モデル,
            設定,
        }

        private ContentMode _contentMode = ContentMode.メイド;

        private void DrawWindow(int id)
        {
            DrawHeader();
            DrawInfo();
            DrawNavi();

            if (modItemManager.isLoading)
            {
                DrawContentLoading();
            }
            else
            {
                DrawContent();
            }

            DrawFooter();

            _rootView.DrawComboBox();

            if (!_windowSizeDraggableInfo.isDragging)
            {
                GUI.DragWindow();
            }
        }

        private void DrawVariationWindow(int id)
        {
            DrawVariation();
            _variationView.DrawComboBox();
            GUI.DragWindow();
        }

        private void DrawColorSetWindow(int id)
        {
            DrawColorSet();
            _colorSetView.DrawComboBox();
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
                plugin.isEnable = false;
            }
        }

        private void DrawInfo()
        {
            var view = _infoView;
            view.ResetLayout();

            view.padding = new Vector2(10, 5);

            view.SetEnabled(!modItemManager.isLoading && !view.IsComboBoxFocused());

            DrawMainInfo();
            if (_contentMode == ContentMode.メイド)
            {
                DrawMaidInfo();
            }
            else if (_contentMode == ContentMode.モデル)
            {
                DrawModelInfo();
            }
            else
            {
                DrawSettingInfo();
            }
            DrawPathInfo();

            view.DrawHorizontalLine(Color.gray);
        }

        private void DrawMainInfo()
        {
            var view = _infoView;

            view.BeginHorizontal();

            view.DrawLabel("編集モード", 80, 20, style: GUIView.gsLabelRight);

            _contentMode = view.DrawTabs(_contentMode, 60, 20);

            view.DrawLabel("表示", 40, 20, style: GUIView.gsLabelRight);

            if (!modItemManager.isLoading)
            {
                view.margin = 0;
                foreach (var item in rootItem.children)
                {
                    var color = item == currentDirItem ? Color.green : Color.white;
                    if (view.DrawButton(item.name, 70, 20, color: color, enabled: item.children?.Count > 0))
                    {
                        SetCurrentDirItem(item as DirItem);
                    }
                }
                view.margin = GUIView.defaultMargin;
            }

            view.EndLayout();
        }

        private void DrawMaidInfo()
        {
            var view = _infoView;

            view.BeginHorizontal();

            view.DrawLabel("メイド", 50, 20, style: GUIView.gsLabelRight);

            var maids = MTEUtils.GetReadyMaidList();

            if (maids.Count == 0)
            {
                view.DrawLabel("メイドを配置してください", -1, 20, textColor: Color.yellow);
                view.EndLayout();
                return;
            }

            _maidComboBox.items = MTEUtils.GetReadyMaidList();
            _maidComboBox.DrawButton(view);

            var maid = _maidComboBox.currentItem;
            modItemManager.SetCurrentMaid(maid);

            if (maid == null)
            {
                view.DrawLabel("メイドを選択してください", -1, 20, textColor: Color.yellow);
                view.EndLayout();
                return;
            }

            if (view.DrawButton("サムネ更新", 80, 20))
            {
                modItemManager.ThumShot();
            }

            view.DrawLabel("マスク", 50, 20, style: GUIView.gsLabelRight);

            _maskModeComboBox.DrawButton(view);
            _maskModeComboBox.onSelected = (mask, _) =>
            {
                if (maid.body0 != null)
                {
                    maid.body0.SetMaskMode(mask);
                }
            };

            view.DrawLabel("プリセット", 60, 20, style: GUIView.gsLabelRight);
            _presetTypeComboBox.DrawButton(view);

            var presetType = _presetTypeComboBox.currentItem;

            if (view.DrawButton("セーブ", 60, 20))
            {
                characterMgr.PresetSave(maid, presetType);
                modItemManager.UpdatePresetItems();
            }

            view.DrawLabel("一時記録", 60, 20, style: GUIView.gsLabelRight);

            var tempPresets = maidTempPresetManager.GetTempPresets(maid);
            if (tempPresets.Count == 0)
            {
                maidTempPresetManager.SavePresetCache(maid, CharacterMgr.PresetType.All);
                modItemManager.UpdateTempPresetItems();
                tempPresets = maidTempPresetManager.GetTempPresets(maid);
            }

            _tempPresetComboBox.items = tempPresets;
            _tempPresetComboBox.DrawButton(view);

            if (view.DrawButton("セーブ", 60, 20))
            {
                maidTempPresetManager.SavePresetCache(maid, presetType);
                _tempPresetComboBox.currentIndex = 0;
                modItemManager.UpdateTempPresetItems();
            }

            if (view.DrawButton("ロード", 60, 20))
            {
                var tempPreset = _tempPresetComboBox.currentItem;
                if (tempPreset != null)
                {
                    maidPresetManager.ApplyPreset(maid, tempPreset.preset, xmlMemory: tempPreset.xmlMemory);
                }
            }

            view.EndLayout();
        }

        private void DrawModelInfo()
        {
            var view = _infoView;

            view.BeginHorizontal();

            try
            {
                if (!modelHackManager.IsValid())
                {
                    view.DrawLabel("モデル機能はMotionTimelineEditorのインストールが必要です", -1, 20, textColor: Color.yellow);
                    view.EndLayout();
                    return;
                }

                view.DrawLabel("配置プラグイン", 100, 20, style: GUIView.gsLabelRight);

                var pluginNames = modelHackManager.pluginNames;
                if (pluginNames.Count == 0)
                {
                    view.DrawLabel("有効なプラグインがありません", -1, 20, textColor: Color.yellow);
                    view.EndLayout();
                    return;
                }

                _pluginComboBox.items = pluginNames;
                _pluginComboBox.DrawButton(view);

                var pluginName = _pluginComboBox.currentItem;

                if (pluginName == null)
                {
                    view.DrawLabel("プラグインを選択してください", -1, 20, textColor: Color.yellow);
                    view.EndLayout();
                    return;
                }

                if (view.DrawButton("配置", 50, 20))
                {
                    modItemManager.CreateModel(selectedMenuItem, pluginName);
                }
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }

            view.EndLayout();
        }

        private void DrawSettingInfo()
        {
            var view = _infoView;

            view.BeginHorizontal();

            view.DrawLabel("設定", 50, 20, style: GUIView.gsLabelRight);

            view.EndLayout();
        }

        private void DrawPathInfo()
        {
            var view = _infoView;

            if (currentDirItem == null || _contentMode == ContentMode.設定)
            {
                view.AddSpace(20);
                return;
            }

            view.margin = 0;

            view.BeginHorizontal();
            {
                if (view.DrawButton("<", 20, 20, canPrevCurrentDirItem))
                {
                    PrevCurrentDirItem();
                }

                if (view.DrawButton(">", 20, 20, canNextCurrentDirItem))
                {
                    NextCurrentDirItem();
                }

                var searchWidth = 170;
                var pathWidth = _windowWidth - view.padding.x * 2 - view.currentPos.x - searchWidth;
                var pathButtonWidth = 20 * 2 + 10;

                // パスバー
                {
                    view.DrawBox(pathWidth - pathButtonWidth, 20);

                    {
                        var name = modItemManager.rootItem.name;
                        var nameWidth = GUIView.gsButton.CalcSize(new GUIContent(name)).x;

                        if (view.DrawButton(name, nameWidth, 20, style: gsHiddenButton))
                        {
                            SetCurrentDirItem(rootItem);
                        }
                    }

                    var names = currentDirItem.itemPath.Split(Path.DirectorySeparatorChar);
                    var subPath = "";
                    for (int i = 0; i < names.Length; i++)
                    {
                        var name = names[i];
                        if (string.IsNullOrEmpty(name))
                        {
                            continue;
                        }

                        view.DrawLabel("\\", 5, 20);

                        subPath = subPath == "" ? name : MTEUtils.CombinePaths(subPath, name);

                        var nameWidth = GUIView.gsButton.CalcSize(new GUIContent(name)).x;

                        if (view.DrawButton(name, nameWidth, 20, style: gsHiddenButton))
                        {
                            SetCurrentDirItem(modItemManager.GetItemByPath<DirItem>(subPath));
                        }
                    }

                    var searchRootItem = modItemManager.searchRootItem;
                    if (!modItemManager.isLoading &&
                        currentDirItem == searchRootItem &&
                        modItemManager.searchTargetDirItem != null)
                    {
                        var item = modItemManager.searchTargetDirItem;
                        
                        var seachDescription = string.Format(" [{0}] ({1})", item.itemPath, searchRootItem.children?.Count);
                        view.DrawLabel(seachDescription, -1, 20, textColor: new Color(0.5f, 0.5f, 0.5f));
                    }
                }

                // パスボタン
                {
                    view.currentPos.x = _windowWidth - view.padding.x * 2 - searchWidth - pathButtonWidth;

                    var fullPath = currentDirItem.fullPath;
                    if (view.DrawTextureButton(PluginInfo.OpenIconTexture, 20, 20, enabled: !string.IsNullOrEmpty(fullPath)))
                    {
                        MTEUtils.OpenDirectory(fullPath);
                    }

                    if (view.DrawTextureButton(PluginInfo.ListIconTexture, 20, 20, enabled: currentDirItem.canFlatView))
                    {
                        currentDirItem.isFlatView = !currentDirItem.isFlatView;
                    }
                }

                // 検索バー
                {
                    view.currentPos.x = _windowWidth - view.padding.x * 2 - searchWidth;

                    view.DrawTextField(new GUIView.TextFieldOption
                    {
                        width = searchWidth - 20,
                        value = modItemManager.searchPattern,
                        onChanged = value => modItemManager.searchPattern = value,
                        hiddenButton = true,
                    });

                    if (view.DrawTextureButton(PluginInfo.SearchIconTexture, 20, 20))
                    {
                        modItemManager.UpdateSearchItems(currentDirItem);
                        SetCurrentDirItem(modItemManager.searchRootItem);
                    }
                }
            }
            view.EndLayout();

            view.AddSpace(5);

            view.margin = GUIView.defaultMargin;
        }

        private void DrawNavi()
        {
            var view = _naviView;
            view.ResetLayout();
            view.SetEnabled(!view.IsComboBoxFocused());

            view.padding = Vector2.zero;
            view.margin = 0;

            view.BeginScrollView();
            {
                if (!modItemManager.isLoading)
                {
                    DrawNaviItem(rootItem);
                }
            }
            view.EndScrollView();
        }

        private void DrawNaviItem(DirItem item, int depth = 0)
        {
            var view = _naviView;

            view.BeginHorizontal();
            {
                if (view.currentPos.y + 20 < view.scrollPosition.y &&
                    view.currentPos.y > view.scrollPosition.y + view.scrollViewRect.height)
                {
                    view.DrawEmpty(20, 20);
                }
                else
                {
                    view.currentPos.x += depth * 10;

                    if (item.GetDirCount(false) > 0)
                    {
                        var texture = item.isExpanded ? PluginInfo.ExpandIconTexture : PluginInfo.CollapseIconTexture;
                        if (view.DrawTextureButton(texture, 20, 20, offsetSize: 2f, style: gsHiddenButton))
                        {
                            item.isExpanded = !item.isExpanded;
                            _focusedItem = item;
                        }
                    }
                    else
                    {
                        view.currentPos.x += 20;
                    }

                    var buttonColor = item == currentDirItem ? Color.green : Color.white;
                    if (view.DrawButton(item.name, -1, 20, color: buttonColor, style: gsNaviButton))
                    {
                        SetCurrentDirItem(item);
                    }
                }
            }
            view.EndLayout();

            float focusedTopY = 0;
            if (item == _focusedItem)
            {
                focusedTopY = view.currentPos.y - 20;
            }

            if (item.isExpanded)
            {
                foreach (var child in item.children)
                {
                    if (child.isDir && child.children.Count > 0)
                    {
                        DrawNaviItem(child as DirItem, depth + 1);
                    }
                }
            }

            if (item == _focusedItem)
            {
                if (view.scrollPosition.y + view.scrollViewRect.height < view.currentPos.y)
                {
                    view.scrollPosition.y = view.currentPos.y + 20 - view.scrollViewRect.height;
                }
                if (view.scrollPosition.y > focusedTopY)
                {
                    view.scrollPosition.y = focusedTopY;
                }
                _focusedItem = null;
            }
        }

        private void DrawContentLoading()
        {
            var view = _contentView;
            view.ResetLayout();
            view.SetEnabled(!view.IsComboBoxFocused());

            var loadState = modItemManager.loadState;
            var officialMenuLoadedIndex = modItemManager.officialMenuLoadedIndex;
            var officialMenuTotalCount = modItemManager.officialMenuTotalCount;
            var modMenuLoadedIndex = modItemManager.modMenuLoadedIndex;
            var modMenuTotalCount = modItemManager.modMenuTotalCount;

            switch (loadState)
            {
                case ModItemManager.LoadState.LoadOfficialNameCsv:
                    view.DrawLabel("Loading Official Name CSV...", -1, 20);
                    break;
                case ModItemManager.LoadState.LoadOfficialItems:
                    view.DrawLabel("Loading Official Items... " + officialMenuLoadedIndex + "/" + officialMenuTotalCount, -1, 20);
                    break;
                case ModItemManager.LoadState.LoadModItems:
                    view.DrawLabel("Loading Mod Items... " + modMenuLoadedIndex + "/" + modMenuTotalCount, -1, 20);
                    break;
                case ModItemManager.LoadState.CollectVariationMenu:
                    view.DrawLabel("Collecting Variation Menu...", -1, 20);
                    break;
                case ModItemManager.LoadState.CollectColorSetMenu:
                    view.DrawLabel("Collecting Color Set Menu...", -1, 20);
                    break;
                default:
                    view.DrawLabel("Loading...", -1, 20);
                    break;
            }
        }

        private void DrawContent()
        {
            switch (_contentMode)
            {
                case ContentMode.メイド:
                case ContentMode.モデル:
                    DrawContentMain();
                    break;
                case ContentMode.設定:
                    DrawContentSetting();
                    break;
            }
        }

        private TempDirItem _flatViewItem = new TempDirItem
        {
            children = new List<ITileViewContent>(1024),
        };

        private void DrawContentMain()
        {
            var view = _contentView;
            view.ResetLayout();
            view.SetEnabled(!view.IsComboBoxFocused());

            if (currentDirItem?.children.Count == 0)
            {
                view.DrawLabel("アイテムがありません", -1, 20);
            }
            else
            {
                DirItem targetItem = currentDirItem;

                if (currentDirItem.isFlatView)
                {
                    if (_flatViewItem.itemPath != currentDirItem.itemPath)
                    {
                        MTEUtils.LogDebug("Update FlatView: " + currentDirItem.itemPath);
                        _flatViewItem.itemPath = currentDirItem.itemPath;
                        _flatViewItem.RemoveAllChildren();
                        currentDirItem.GetAllFiles(_flatViewItem.children);
                        ModItemManager.SortItemChildren(_flatViewItem);
                    }
                    targetItem = _flatViewItem;
                }

                view.DrawTileView(
                    targetItem,
                    -1,
                    view.viewRect.height,
                    120,
                    110,
                    item =>
                    {
                        selectedItem = item as ModItemBase;
                        OnItemSelected(selectedItem);
                    },
                    item =>
                    {
                        _mouseOverItem = item as ModItemBase;
                        _mouseOverFrameCount = Time.frameCount;
                    },
                    item =>
                    {
                        MTEUtils.ExecuteNextFrame(() =>
                        {
                            OnItemDeleted(item as MenuItem);
                        });
                    });
            }
        }

        private static List<string> _duplicatedModsSearchPatterns = new List<string> { "*.menu", "*.mod", "*.model" };

        private void DrawContentSetting()
        {
            var view = _contentSettingView;
            view.ResetLayout();
            view.SetEnabled(!view.IsComboBoxFocused());

            view.BeginScrollView();
            {
                view.DrawToggle("公式アイテムをMPN毎に表示する", config.groupOfficialItemsByMPN, 200, 20, newValue =>
                {
                    config.groupOfficialItemsByMPN = newValue;
                    config.dirty = true;
                    modItemManager.Load(rebuild: true);
                });

                view.DrawToggle("ModアイテムをMPN毎に表示する", config.groupModItemsByMPN, 200, 20, newValue =>
                {
                    config.groupModItemsByMPN = newValue;
                    config.dirty = true;
                    modItemManager.Load(rebuild: true);
                });

                view.DrawToggle("アイテム選択時に詳細ログ表示", config.dumpItemInfo, 200, 20, newValue =>
                {
                    config.dumpItemInfo = newValue;
                    config.dirty = true;
                });

                view.BeginHorizontal();
                {
                    view.DrawLabel("アイテム名 背景透過度", 200, 20);

                    view.DrawSliderValue(new GUIView.SliderOption
                    {
                        min = 0f,
                        max = 1f,
                        step = 0.01f,
                        defaultValue = 0.7f,
                        value = config.itemNameBGAlpha,
                        onChanged = newValue =>
                        {
                            config.itemNameBGAlpha = newValue;
                            config.dirty = true;
                            UpdateItemLabelBGAlpha();
                        },
                    });
                }
                view.EndLayout();

                view.BeginHorizontal();
                {
                    view.DrawLabel("タグ 背景透過度", 200, 20);

                    view.DrawSliderValue(new GUIView.SliderOption
                    {
                        min = 0f,
                        max = 1f,
                        step = 0.01f,
                        defaultValue = 0.9f,
                        value = config.tagBGAlpha,
                        onChanged = newValue =>
                        {
                            config.tagBGAlpha = newValue;
                            config.dirty = true;
                        },
                    });
                }
                view.EndLayout();

                view.BeginHorizontal();
                {
                    view.DrawLabel("フラットビューの基本アイテム数", 200, 20);

                    view.DrawSliderValue(new GUIView.SliderOption
                    {
                        fieldType = FloatFieldType.Int,
                        min = 0,
                        max = 128,
                        step = 1,
                        defaultValue = 32,
                        value = config.flatViewItemCount,
                        onChanged = newValue =>
                        {
                            config.flatViewItemCount = (int) newValue;
                            config.dirty = true;
                        },
                    });
                }
                view.EndLayout();

                view.DrawToggle("カスタムパーツ選択時の自動編集", config.customPartsAutoEditMode, 200, 20, newValue =>
                {
                    config.customPartsAutoEditMode = newValue;
                    config.dirty = true;
                });

                view.BeginHorizontal();
                {
                    view.DrawLabel("カスタムパーツの移動範囲", 200, 20);

                    view.DrawSliderValue(new GUIView.SliderOption
                    {
                        min = 0.1f,
                        max = 10f,
                        step = 0.1f,
                        defaultValue = 1f,
                        value = config.customPartsPositionRange,
                        onChanged = newValue =>
                        {
                            config.customPartsPositionRange = newValue;
                            config.dirty = true;
                        },
                    });
                }
                view.EndLayout();

                view.BeginHorizontal();
                {
                    view.DrawLabel("重複ファイルチェック", 150, 20);

                    var patterns = new List<string>
                    {
                        "*.menu",
                        "*.mod",
                        "*.model",
                        "*.tex",
                    };

                    foreach (var pattern in patterns)
                    {
                        var enabled = _duplicatedModsSearchPatterns.Contains(pattern);

                        view.DrawToggle(pattern, enabled, 60, 20, newValue =>
                        {
                            if (newValue)
                            {
                                _duplicatedModsSearchPatterns.Add(pattern);
                            }
                            else
                            {
                                _duplicatedModsSearchPatterns.Remove(pattern);
                            }
                        });
                    }

                    if (view.DrawButton("出力", 60, 20))
                    {
                        modItemManager.DumpDuplicatedMods(_duplicatedModsSearchPatterns);
                    }
                }
                view.EndLayout();

                view.AddSpace(20);

                view.DrawHorizontalLine(Color.gray);

                view.BeginHorizontal();
                {
                    if (view.DrawButton("アイテム更新", 150, 20))
                    {
                        modItemManager.Load();
                    }

                    if (view.DrawButton("設定をリセット", 150, 20))
                    {
                        MTEUtils.ShowConfirmDialog("設定を初期化しますか？", () =>
                        {
                            GameMain.Instance.SysDlg.Close();
                            configManager.ResetConfig();
                            modItemManager.Load(rebuild: true);
                        }, null);
                    }

                    if (view.DrawButton("キャッシュを再構築", 150, 20))
                    {
                        MTEUtils.ShowConfirmDialog("キャッシュを再構築しますか？", () =>
                        {
                            GameMain.Instance.SysDlg.Close();
                            modItemManager.DeleteMenuCache();
                            modItemManager.Load(reset: true);
                        }, null);
                    }
                }
                view.EndLayout();
            }
            view.EndScrollView();
        }

        private void OnItemSelected(ModItemBase item)
        {
            if (config.GetKey(KeyBindType.OpenExplorer))
            {
                if (!string.IsNullOrEmpty(item.fullPath))
                {
                    MTEUtils.OpenDirectory(item.fullPath);
                }
                return;
            }

            if (item.isDir)
            {
                SetCurrentDirItem(item as DirItem);
            }
            else
            {
                if (config.dumpItemInfo)
                {
                    MTEUtils.Log("name: {0}", item.name);
                    MTEUtils.Log("setumei: {0}", item.setumei);
                    MTEUtils.Log("itemType: {0}", item.itemType);
                    MTEUtils.Log("itemName: {0}", item.itemName);
                    MTEUtils.Log("itemPath: {0}", item.itemPath);

                    if (item is MenuItem menuItem)
                    {
                        menuItem.menu?.Dump();
                    }
                }

                if (_contentMode == ContentMode.メイド)
                {
                    modItemManager.ApplyItem(item);
                }
            }
        }

        private void OnItemDeleted(MenuItem item)
        {
            if (item != null)
            {
                MTEUtils.LogDebug("Item deleted: " + item.itemPath);
                modItemManager.DelItem(item);
            }
        }

        private void DrawVariation()
        {
            var view = _variationView;
            view.ResetLayout();
            view.SetEnabled(!view.IsComboBoxFocused());

            view.padding = Vector2.zero;
            view.margin = 0;

            view.BeginScrollView();
            {
                var selectedMenuItem = this.selectedMenuItem;
                if (modItemManager.isLoading || selectedMenuItem == null)
                {
                    view.EndScrollView();
                    return;
                }

                if (selectedMenuItem.menuList?.Count > 0)
                {
                    var selectedMenu = selectedMenuItem.variationMenu;
                    foreach (var menu in selectedMenuItem.menuList)
                    {
                        DrawVariationMenu(menu, selectedMenu);
                    }
                }
            }
            view.EndScrollView();
        }

        private void DrawVariationMenu(MenuInfo menu, MenuInfo selectedMenu)
        {
            var view = _variationView;

            var selectedMenuItem = this.selectedMenuItem;
            if (selectedMenuItem == null)
            {
                return;
            }

            var drawRect = view.GetDrawRect(VARIATION_WIDTH - 20, VARIATION_WIDTH - 20);

            if (drawRect.position.y + drawRect.height < view.scrollPosition.y ||
                drawRect.position.y > view.scrollPosition.y + view.scrollViewRect.height)
            {
                view.NextElement(drawRect);
                return;
            }

            var thum = textureManager.GetTexture(menu.iconName, menu.iconData);
            if (view.DrawTextureButton(thum, drawRect.width, drawRect.height, 10f, selectedMenu != menu))
            {
                selectedMenuItem.variationMenu = menu;
                OnItemSelected(selectedItem);
            }

            if (selectedMenu == menu)
            {
                view.DrawRectInternal(drawRect, Color.green, 2);
            }

            if (drawRect.Contains(Event.current.mousePosition))
            {
                _mouseOverMenu = menu;
                _mouseOverFrameCount = Time.frameCount;
            }
        }

        private void DrawColorSet()
        {
            var view = _colorSetView;
            view.ResetLayout();
            view.SetEnabled(!view.IsComboBoxFocused());

            view.padding = Vector2.zero;
            view.margin = 0;

            view.BeginScrollView();
            {
                var selectedMenuItem = this.selectedMenuItem;
                if (modItemManager.isLoading || selectedMenuItem == null)
                {
                    view.EndScrollView();
                    return;
                }

                if (selectedMenuItem.colorSet != null)
                {
                    var colorSet = selectedMenuItem.colorSet;
                    var selectedMenu = colorSet.selectedMenu;
                    foreach (var menu in colorSet.colorMenuList)
                    {
                        DrawColorSetMenu(colorSet, menu, selectedMenu);
                    }
                }
            }
            view.EndScrollView();
        }

        private void DrawColorSetMenu(ColorSetInfo colorSet, MenuInfo menu, MenuInfo selectedMenu)
        {
            var view = _colorSetView;

            var selectedMenuItem = this.selectedMenuItem;
            if (selectedMenuItem == null)
            {
                return;
            }

            var drawRect = view.GetDrawRect(COLOR_SET_WIDTH - 20, COLOR_SET_WIDTH - 20);

            if (drawRect.position.y + drawRect.height < view.scrollPosition.y ||
                drawRect.position.y > view.scrollPosition.y + view.scrollViewRect.height)
            {
                view.NextElement(drawRect);
                return;
            }

            var thum = textureManager.GetTexture(menu.iconName, menu.iconData);
            if (view.DrawTextureButton(thum, drawRect.width, drawRect.height, 10f, menu != selectedMenu))
            {
                colorSet.selectedMenu = menu;
                modItemManager.ApplyColorSet(colorSet);
            }

            if (menu == selectedMenu)
            {
                view.DrawRectInternal(drawRect, Color.green, 2);
            }

            if (drawRect.Contains(Event.current.mousePosition))
            {
                _mouseOverMenu = menu;
                _mouseOverFrameCount = Time.frameCount;
            }
        }

        public void DrawFooter()
        {
            var view = _footerView;
            view.ResetLayout();
            view.SetEnabled(!view.IsComboBoxFocused());

            view.padding = Vector2.zero;
            view.margin = 0;

            view.BeginLayout(GUIView.LayoutDirection.Free);

            view.DrawBox(-1, 20);

            if (_mouseOverMenu != null)
            {
                var text = $"{_mouseOverMenu.name} {_mouseOverMenu.setumei}".Replace("\n", " ");
                view.DrawLabel(text, -1, 20);
            }
            else if (_mouseOverItem != null)
            {
                if (_mouseOverItem is MenuItem menuItem)
                {
                    var text = $"{menuItem.name} {menuItem.setumei}".Replace("\n", " ");
                    view.DrawLabel(text, -1, 20);
                }
                else if (_mouseOverItem.itemType == ModItemType.Dir || _mouseOverItem.itemType == ModItemType.Preset)
                {
                    view.DrawLabel(_mouseOverItem.itemPath, -1, 20);
                }
                else
                {
                    view.DrawLabel(_mouseOverItem.name, -1, 20);
                }
            }

            var footerRect = view.GetDrawRect(-1, 20);
            if (footerRect.Contains(Event.current.mousePosition) ||
                _naviWidthDraggableInfo.isDragging)
            {
                view.currentPos.x = config.naviWidth - 20;

                view.DrawDraggableButton("□", 20, 20,
                    _naviWidthDraggableInfo,
                    new Vector2(_naviWidth, 0f),
                    (delta, value) =>
                {
                    config.naviWidth = (int)value.x;
                    config.naviWidth = Mathf.Clamp(config.naviWidth, MIN_NAVI_WIDTH, MAX_NAVI_WIDTH);
                    config.dirty = true;
                });
            }

            view.currentPos.x = _windowWidth - 20;

            view.DrawDraggableButton("□", 20, 20,
                _windowSizeDraggableInfo,
                new Vector2(_windowWidth, _windowHeight),
                (delta, value) =>
            {
                config.windowWidth = (int)value.x;
                config.windowHeight = (int)value.y;

                config.windowWidth = Mathf.Clamp(config.windowWidth, MIN_WINDOW_WIDTH, Screen.width);
                config.windowHeight = Mathf.Clamp(config.windowHeight, MIN_WINDOW_HEIGHT, Screen.height);

                config.dirty = true;
            });
        }

        public void OnScreenSizeChanged()
        {
            MTEUtils.AdjustWindowPosition(ref _windowRect);
        }
    }
}