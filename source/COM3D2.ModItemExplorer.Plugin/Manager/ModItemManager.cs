using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace COM3D2.ModItemExplorer.Plugin
{
    public class ColorSetInfo
    {
        public MPN colorSetMPN;
        public string colorSetMenuName;
        public int index;
        public List<MenuInfo> colorMenuList = null;
        public Vector2 scrollPosition;

        public MenuInfo selectedMenu
        {
            get => colorMenuList?.GetOrDefault(index);
            set
            {
                if (colorMenuList?.Count > 0)
                {
                    index = colorMenuList.IndexOf(value);
                    index = Mathf.Clamp(index, 0, colorMenuList.Count - 1);
                }
            }
        }

        public ColorSetInfo(MPN colorSetMPN, string colorSetMenuName)
        {
            this.colorSetMPN = colorSetMPN;
            this.colorSetMenuName = colorSetMenuName;
        }

        public void AddColorMenu(MenuInfo menu)
        {
            if (colorMenuList == null)
            {
                colorMenuList = new List<MenuInfo>(16);
            }

            if (!colorMenuList.Contains(menu))
            {
                colorMenuList.Add(menu);
            }
        }
    }

    public class ModItemManager : ManagerBase
    {
        public static readonly string OfficialDirName = "Official";
        public static readonly string ModDirName = "Mod";
        public static readonly string EquippedDirName = "Equipped";
        public static readonly string ModelDirName = "Model";
        public static readonly string PresetDirName = "Preset";
        public static readonly string TempPresetDirName = "TempPreset";
        public static readonly string SearchDirName = "Search";
        public static readonly string FavoriteDirName = "Favorite";
        public static readonly string HistoryDirName = "History";
        public static readonly int MinLayerIndex = 2;
        public static readonly int MaxLayerIndex = 8;

        private static ModItemManager _instance;
        public static ModItemManager instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new ModItemManager();
                }
                return _instance;
            }
        }

        public DirItem rootItem { get; private set; } = new DirItem
        {
            name = "Root",
            itemName = "",
            itemPath = "",
            canFavorite = false,
            children = new List<ITileViewContent>(16),
        };

        public DirItem officialRootItem { get; private set; } = new DirItem
        {
            name = "公式",
            itemName = OfficialDirName,
            itemPath = OfficialDirName,
            canFavorite = false,
            children = new List<ITileViewContent>(16),
        };

        public DirItem modRootItem { get; private set; } = new DirItem
        {
            name = "Mod",
            itemName = ModDirName,
            itemPath = ModDirName,
            canFavorite = false,
            fullPath = Path.Combine(UTY.gameProjectPath, ModDirName),
            children = new List<ITileViewContent>(16),
        };

        public DirItem equippedRootItem { get; private set; } = new DirItem
        {
            name = "着用中",
            itemName = EquippedDirName,
            itemPath = EquippedDirName,
            canFavorite = false,
            children = new List<ITileViewContent>(16),
        };

        public DirItem modelRootItem { get; private set; } = new DirItem
        {
            name = "配置中",
            itemName = ModelDirName,
            itemPath = ModelDirName,
            canFavorite = false,
            children = new List<ITileViewContent>(16),
        };

        public DirItem presetRootItem { get; private set; } = new DirItem
        {
            name = "Preset",
            itemName = PresetDirName,
            itemPath = PresetDirName,
            canFavorite = false,
            fullPath = Path.Combine(UTY.gameProjectPath, PresetDirName),
            children = new List<ITileViewContent>(16),
        };

        public DirItem tempPresetRootItem { get; private set; } = new DirItem
        {
            name = "一時記録",
            itemName = TempPresetDirName,
            itemPath = TempPresetDirName,
            canFavorite = false,
            children = new List<ITileViewContent>(16),
        };

        public TempDirItem searchRootItem { get; private set; } = new TempDirItem
        {
            name = "検索結果",
            itemName = SearchDirName,
            itemPath = SearchDirName,
            canFavorite = false,
            children = new List<ITileViewContent>(16),
        };

        public TempDirItem favoriteRootItem { get; private set; } = new TempDirItem
        {
            name = "お気に入り",
            itemName = FavoriteDirName,
            itemPath = FavoriteDirName,
            canFavorite = false,
            children = new List<ITileViewContent>(16),
        };

        public TempDirItem historyRootItem { get; private set; } = new TempDirItem
        {
            name = "履歴",
            itemName = HistoryDirName,
            itemPath = HistoryDirName,
            canFavorite = false,
            children = new List<ITileViewContent>(16),
        };

        public bool isLoading { get; private set; }
        public int officialMenuLoadedIndex { get; private set; }
        public int officialMenuTotalCount { get; private set; }
        public int modMenuLoadedIndex { get; private set; }
        public int modMenuTotalCount { get; private set; }
        public Maid currentMaid { get; private set; }
        public int animationLayer { get; set; }

        public enum LoadState
        {
            None,
            LoadOfficialNameCsv,
            LoadOfficialMenuItems,
            LoadOfficialAnmItems,
            LoadModItems,
            LoadModBgObjectItems,
            UpdateModPresetItems,
            UpdateModAnmItems,
            UpdatePresetItems,
            CollectVariationMenu,
            CollectColorSetMenu,
        }

        public LoadState loadState { get; private set; }

        public static readonly int menuCapacity = 1024 * 16;

        private Dictionary<string, MenuInfo> _menuMap = new Dictionary<string, MenuInfo>(menuCapacity, StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, ModItemBase> _itemPathMap = new Dictionary<string, ModItemBase>(menuCapacity, StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, ModItemBase> _itemNameMap = new Dictionary<string, ModItemBase>(menuCapacity, StringComparer.OrdinalIgnoreCase);
        private List<string> _officialMenuFileNameList = new List<string>(menuCapacity);
        private Dictionary<int, string> _variationMenuPathMap = new Dictionary<int, string>(menuCapacity); // rid -> path
        private Dictionary<string, List<MenuInfo>> _variationMenuMap = new Dictionary<string, List<MenuInfo>>(1024);

        private HashSet<string> _warnedNotFoundEquippedItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>アセットバンドル名 -> 背景オブジェクト情報。配置中アイテムの表示名解決に使う</summary>
        private Dictionary<string, BgObjectInfo> _bgObjectInfoMap = new Dictionary<string, BgObjectInfo>(64, StringComparer.OrdinalIgnoreCase);

        public List<AnimationLayerInfo> animationLayerInfos = new List<AnimationLayerInfo>();
        public List<AnimationState> animationStates = new List<AnimationState>();

        public static Config config => ConfigManager.instance.config;
        private static MaidManagerWrapper maidManagerWrapper => MaidManagerWrapper.instance;

        private ModItemManager()
        {
            for (int i = 0; i <= MaxLayerIndex; i++)
            {
                animationLayerInfos.Add(new AnimationLayerInfo(i));
                animationStates.Add(null);
            }
        }

        public override void Init()
        {
            rootItem.AddChild(officialRootItem);
            rootItem.AddChild(modRootItem);
            rootItem.AddChild(equippedRootItem);
            rootItem.AddChild(modelRootItem);
            rootItem.AddChild(presetRootItem);
            rootItem.AddChild(tempPresetRootItem);
            rootItem.AddChild(searchRootItem);
            rootItem.AddChild(favoriteRootItem);
            rootItem.AddChild(historyRootItem);

            InitItemCache();
        }

        public void InitItemCache()
        {
            _itemNameMap.Clear();
            _itemPathMap.Clear();

            searchTargetDirItem = null;
            searchPattern = string.Empty;
            _searchTempItems.Clear();

            foreach (ModItemBase item in rootItem.children)
            {
                RemoveItemChildren(item);
                _itemPathMap[item.itemPath] = item;
            }
        }

        public override void OnLoad()
        {
            if (_menuMap.Count == 0)
            {
                Load();
            }
        }

        public void Load(bool rebuild = false, bool reset = false)
        {
            if (isLoading)
            {
                return;
            }

            isLoading = true;
            loadState = LoadState.None;
            officialMenuLoadedIndex = 0;
            officialMenuTotalCount = 0;
            modMenuLoadedIndex = 0;
            modMenuTotalCount = 0;

            _officialMenuFileNameList.Clear();
            _variationMenuPathMap.Clear();
            _variationMenuMap.Clear();
            _warnedNotFoundEquippedItems.Clear();

            // MenuDataBaseは非同期構築のため、完了前に列挙すると公式アイテムを途中までしか登録できない
            MTEUtils.ExecuteAfterMenuDataBaseReady(() =>
            {
                try
                {
                    LoadOfficialMenuFileNameList();
                }
                catch (Exception e)
                {
                    // ここで失敗するとisLoadingがtrueのまま固着し、以後の読み込みが一切できなくなる
                    MTEUtils.LogException(e);
                    isLoading = false;
                    return;
                }

                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        if (reset)
                        {
                            _menuMap.Clear();
                            InitItemCache();
                        }
                        else if (rebuild)
                        {
                            InitItemCache();
                        }

                        LoadOfficialNameCsv();

                        if (_menuMap.Count == 0)
                        {
                            LoadMenuCache();
                        }

                        LoadOfficialMenuItems();
                        LoadOfficialAnmItems();
                        ValidateItemChildren(officialRootItem);
                        SortItemChildren(officialRootItem);

                        LoadModItems("*.menu");
                        LoadModItems("mod_*.mod");
                        LoadModBgObjectItems();
                        UpdateModPresetItems();
                        UpdateModAnmItems();
                        ValidateItemChildren(modRootItem);
                        SortItemChildren(modRootItem);

                        SaveMenuCache();

                        UpdatePresetItems();
                        UpdateTempPresetItems();

                        CollectVariationMenu();
                        CollectColorSetMenu();
                        UpdateEquippedItems();
                        UpdateSearchItems();
                        UpdateFavoriteItems();
                        UpdateHistoryItems();
                        ResetFlatView();

                        _officialMenuFileNameList.Clear();
                        _variationMenuPathMap.Clear();
                        _variationMenuMap.Clear();

                        MTEUtils.EnqueueAction(() =>
                        {
                            UpdateModelItems();
                            GC.Collect();
                            isLoading = false;
                        });
                    }
                    catch (Exception e)
                    {
                        MTEUtils.LogException(e);
                        isLoading = false;
                    }
                });
            });
        }

        public T GetItemByPath<T>(string path)
            where T : ModItemBase
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }
            return _itemPathMap.GetOrDefault(path) as T;
        }

        public T GetItemByName<T>(string name)
            where T : ModItemBase
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }
            return _itemNameMap.GetOrDefault(name) as T;
        }

        public void ApplyItem(ModItemBase item)
        {
            if (item == null)
            {
                return;
            }

            var applied = false;
            if (item is MenuItem menuItem)
            {
                applied = ApplyMenuItem(menuItem);
            }
            else if (item is PresetItem presetItem)
            {
                applied = ApplyPresetItem(presetItem);
            }
            else if (item is AnmItem anmItem)
            {
                applied = ApplyAnmItem(anmItem);
            }

            if (applied)
            {
                AddItemHistory(item);
            }
        }

        /// <summary>選択履歴へ 1 件積み、一覧へ反映する</summary>
        private void AddItemHistory(ModItemBase item)
        {
            itemHistoryManager.Add(item.itemPath);
            UpdateHistoryItems();
        }

        // colorSetMPN -> colorSetMenuName -> ColorSetInfo
        private Dictionary<MPN, Dictionary<string, ColorSetInfo>> _colorSetMap = new Dictionary<MPN, Dictionary<string, ColorSetInfo>>(16);

        public ColorSetInfo GetOrCreateColorSetInfo(MPN colorSetMPN, string colorSetMenuName)
        {
            var colorSetMap = _colorSetMap.GetOrCreate(colorSetMPN);
            return colorSetMap.GetOrCreate(colorSetMenuName, () => new ColorSetInfo(colorSetMPN, colorSetMenuName));
        }

        public ColorSetInfo GetOrNullColorSetInfo(MPN colorSetMPN, string colorSetMenuName)
        {
            if (colorSetMPN == MPN.null_mpn || string.IsNullOrEmpty(colorSetMenuName))
            {
                return null;
            }

            var colorSetMap = _colorSetMap.GetOrDefault(colorSetMPN);
            if (colorSetMap == null)
            {
                return null;
            }

            return colorSetMap.GetOrDefault(colorSetMenuName);
        }

        /// <summary>アイテムを適用する。適用まで到達した場合のみ true を返す</summary>
        public bool ApplyMenuItem(MenuItem item)
        {
            if (currentMaid == null || item == null)
            {
                return false;
            }

            var menu = item.variationMenu;
            if (menu == null)
            {
                MTEUtils.LogWarning("Menuが見つかりません。" + item.itemPath);
                return false;
            }

            // 参照先ファイルが欠けているとゲーム側で例外が出るため、事前に弾く
            var missingFileName = FindMissingFileName(menu);
            if (missingFileName != null)
            {
                MTEUtils.LogWarning("参照先ファイルが見つかりません。" + missingFileName + " " + item.itemPath);
                return false;
            }

            var beforeSnapshot = CapturePropsForHistory();

            currentMaid.SetProp(menu.mpn, menu.fileName, menu.rid, false, false);

            if (menu.mpn == MPN.eye_hi && currentMaid.IsNewFace())
			{
				currentMaid.SetProp(MPN.eye_hi_r, menu.fileName, menu.rid, false, false);
			}

            if (item.colorSet != null)
            {
                // 履歴はアイテム適用 1 件にまとめるため、色セット単体では登録しない
                ApplyColorSetInternal(item.colorSet);
            }

            currentMaid.AllProcPropSeqStart();

            if (menu.maidPartType.ToCategory() == MaidPartCategory.Set)
            {
                // セットは複数部位が書き換わるため、適用完了後に全体を更新する
                UpdateEquippedItemsAfterProcProp();
            }
            else
            {
                UpdateEquippedItem(menu.maidPartType);
            }

            // カスタムパーツの表示
            windowManager.customPartsWindow.Call(currentMaid, menu.maidPartType);

            // 髪の長さの表示
            windowManager.hairLengthWindow.Call(currentMaid, menu.mpn);

            RegisterPropsHistory("アイテム適用: " + item.name, beforeSnapshot);
            return true;
        }

        /// <summary>
        /// 履歴用に現在の装備を控える。履歴機能が無い環境では
        /// 全 MPN 走査のコストを払う意味がないため何もしない
        /// </summary>
        private MaidPropsSnapshot CapturePropsForHistory()
        {
            return HistoryClient.isAvailable ? MaidPropsSnapshot.Capture(currentMaid) : null;
        }

        /// <summary>
        /// 装備の変化を操作履歴へ 1 件登録する。変化が無ければ何もしない
        /// </summary>
        private void RegisterPropsHistory(string description, MaidPropsSnapshot before)
        {
            if (before == null)
            {
                return;
            }

            var after = MaidPropsSnapshot.Capture(currentMaid);
            if (after == null || !after.HasDiffFrom(before))
            {
                return;
            }

            HistoryClient.Register(
                description,
                () => RestoreProps(before),
                () => RestoreProps(after),
                () => before.isAlive);
        }

        private void RestoreProps(MaidPropsSnapshot snapshot)
        {
            snapshot.Restore();
            UpdateEquippedItemsAfterProcProp();
        }

        /// <summary>
        /// メニューが参照するモデル・子メニューのうち、存在しないファイル名を返す（すべて存在する場合はnull）
        /// </summary>
        private string FindMissingFileName(MenuInfo menu)
        {
            return FindMissingFileName(menu, new HashSet<string>());
        }

        private string FindMissingFileName(MenuInfo menu, HashSet<string> checkedMenuNames)
        {
            if (!string.IsNullOrEmpty(menu.modelFileName) &&
                !MTEUtils.IsExistentFile(menu.modelFileName))
            {
                return menu.modelFileName;
            }

            if (menu.setItemMenuNames == null)
            {
                return null;
            }

            // セット系は子メニューのモデルも読み込まれるため再帰的に確認する
            foreach (var menuName in menu.setItemMenuNames)
            {
                // 循環参照や重複参照で再帰が膨らまないように確認済みはスキップする
                if (!checkedMenuNames.Add(menuName))
                {
                    continue;
                }

                if (!MTEUtils.IsExistentFile(menuName))
                {
                    return menuName;
                }

                // 読み込みに失敗した場合は判定できないためスキップする
                var childMenu = ModMenuLoader.Load(menuName);
                if (childMenu == null)
                {
                    continue;
                }

                var missingFileName = FindMissingFileName(childMenu, checkedMenuNames);
                if (missingFileName != null)
                {
                    return missingFileName;
                }
            }

            return null;
        }

        public void ApplyColorSet(ColorSetInfo colorSet)
        {
            if (currentMaid == null || colorSet == null || colorSet.selectedMenu == null)
            {
                return;
            }

            var beforeSnapshot = CapturePropsForHistory();
            var menuName = colorSet.selectedMenu.name;

            ApplyColorSetInternal(colorSet);

            RegisterPropsHistory("色変更: " + menuName, beforeSnapshot);
        }

        private void ApplyColorSetInternal(ColorSetInfo colorSet)
        {
            // ApplyMenuItem からは未検証の item.colorSet が渡るため、ここでも確認する
            if (currentMaid == null || colorSet == null || colorSet.selectedMenu == null)
            {
                return;
            }

            var menu = colorSet.selectedMenu;
            MTEUtils.LogDebug("[ModMenuItemManager] ApplyColorSet {0} {1}", colorSet.colorSetMPN, menu.fileName);
            currentMaid.SetProp(colorSet.colorSetMPN, menu.fileName, menu.rid, false, false);
            currentMaid.AllProcPropSeqStart();

            // 色パレットの表示
            {
                var partsColorId = menu.partsColorId;
                if (partsColorId != MaidParts.PARTS_COLOR.NONE)
                {
                    windowManager.colorPaletteWindow.Call(currentMaid, menu.partsColorId);
                }
                else
                {
                    windowManager.colorPaletteWindow.Close();
                }
            }
        }

        /// <summary>プリセットを適用する。適用まで到達した場合のみ true を返す</summary>
        public bool ApplyPresetItem(PresetItem item)
        {
            if (currentMaid == null || item == null || item.preset == null)
            {
                return false;
            }

            var maid = currentMaid;
            var beforeSnapshot = CapturePresetForHistory(maid);

            Action apply;
            if (item.itemPath.StartsWith(PresetDirName))
            {
                apply = () => maidPresetManager.ApplyPreset(maid, item.preset);
            }
            else
            {
                apply = () => maidPresetManager.ApplyPreset(maid, item.preset, item.fullPath, item.xmlMemory);
            }
            apply();

            // プリセットは全部位が書き換わるため、適用完了後に全体を更新する
            UpdateEquippedItemsAfterProcProp();

            RegisterPresetHistory("プリセット適用: " + item.name, maid, beforeSnapshot, apply);
            return true;
        }

        public void ApplyTempPreset(TempPreset tempPreset)
        {
            if (currentMaid == null || tempPreset == null || tempPreset.preset == null)
            {
                return;
            }

            var maid = currentMaid;
            var beforeSnapshot = CapturePresetForHistory(maid);

            Action apply = () => maidPresetManager.ApplyPreset(
                maid, tempPreset.preset, xmlMemory: tempPreset.xmlMemory);
            apply();

            // プリセットは全部位が書き換わるため、適用完了後に全体を更新する
            UpdateEquippedItemsAfterProcProp();

            RegisterPresetHistory(
                "一時記録の復元: " + tempPreset.preset.strFileName, maid, beforeSnapshot, apply);
        }

        /// <summary>
        /// 履歴用にメイドの全状態を控える。プリセットの保存相当の重い処理なので、
        /// 履歴機能が無い環境では実行しない
        /// </summary>
        private TempPreset CapturePresetForHistory(Maid maid)
        {
            return HistoryClient.isAvailable ? tempPresetManager.CaptureHistorySnapshot(maid) : null;
        }

        /// <summary>
        /// プリセット適用を操作履歴へ 1 件登録する。
        /// 装備以外（体型・顔・ExPreset）も書き換わるため、undo にはプリセット全体の控えを使う。
        /// TempPreset 同士を比較する手段が無いため、同じプリセットの再適用でも 1 件積む
        /// </summary>
        private void RegisterPresetHistory(
            string description, Maid maid, TempPreset before, Action redo)
        {
            if (before == null)
            {
                return;
            }

            HistoryClient.Register(
                description,
                () =>
                {
                    maidPresetManager.ApplyPreset(maid, before.preset, xmlMemory: before.xmlMemory);
                    UpdateEquippedItemsAfterProcProp();
                },
                () =>
                {
                    redo();
                    UpdateEquippedItemsAfterProcProp();
                },
                () => maid != null);
        }

        /// <summary>アニメーションを適用する。適用まで到達した場合のみ true を返す</summary>
        public bool ApplyAnmItem(AnmItem item)
        {
            if (currentMaid == null || currentMaid.body0 == null || item == null)
            {
                return false;
            }

            var animation = currentMaid.GetAnimation();
            if (animation == null)
            {
                return false;
            }

            // 再呼出直後などモーション未再生のメイドは anist が null や破棄済みの
            // ことがあるため、その場合は「非再生」として扱い適用を続行する
            var animationState = currentMaid.body0.GetAnist();
            var anistName = "";
            var isPlaying = false;
            try
            {
                if (animationState != null)
                {
                    anistName = animationState.name;
                    isPlaying = animationState.speed > 0f;
                }
            }
            catch (Exception)
            {
                // 破棄済みの AnimationState はメンバーアクセスで例外になる
            }

            GameMain.Instance.ScriptMgr.StopMotionScript();

            var layer = 0;
            if (config.animationExtend)
            {
                layer = animationLayer;
            }

            var info = animationLayerInfos.GetOrDefault(layer);
            if (info == null)
            {
                MTEUtils.LogWarning("レイヤー情報が見つかりません。layer=" + layer);
                return false;
            }

            var anmTag = item.itemName.ToLower();
            if (anistName == anmTag && layer > 0)
            {
                MTEUtils.LogWarning("デフォルトレイヤーで再生中のアニメはレイヤー変更できません。" + item.itemName);
                return false;
            }

            currentMaid.body0.StopAndDestroy(item.itemName);

            if (string.IsNullOrEmpty(item.fullPath))
            {
                animationState = currentMaid.body0.CrossFadeLayer(
                    item.itemName,
                    GameUty.FileSystem,
                    layer,
                    false,
                    true,
                    false,
                    0f,
                    info.weight);
            }
            else
            {
                byte[] anmData = new byte[0];
                try
                {
                    using (FileStream fileStream = new FileStream(item.fullPath, FileMode.Open, FileAccess.Read))
                    {
                        anmData = new byte[fileStream.Length];
                        fileStream.Read(anmData, 0, anmData.Length);
                    }
                }
                catch
                {
                }

                if (anmData.Length > 0)
                {
                    animationState = currentMaid.body0.CrossFadeLayer(anmTag, anmData, layer, false, true, false, 0f, info.weight);
                    currentMaid.SetAutoTwistAll(true);
                }
            }

            if (animationState == null)
            {
                MTEUtils.LogWarning("アニメーションのロードに失敗しました。" + item.itemName);
                return false;
            }

            if (!isPlaying)
            {
                animationState.speed = 0f;
                currentMaid.GetAnimation().Sample();
            }

            // モーションウィンドウの表示
            windowManager.motionWindow.Call(currentMaid);
            return true;
        }

        public void CreateModel(ModItemBase item, string pluginName)
        {
            if (item == null || string.IsNullOrEmpty(pluginName))
            {
                return;
            }

            var created = false;
            if (item is BgObjectItem bgObjectItem)
            {
                created = CreateBgObjectModel(bgObjectItem, pluginName);
            }
            else if (item is MenuItem menuItem)
            {
                created = CreateMenuModel(menuItem, pluginName);
            }

            if (created)
            {
                AddItemHistory(item);
            }
        }

        /// <summary>
        /// 背景オブジェクトを配置する。配置まで到達した場合のみ true を返す。
        /// MTE / StudioMode の配置経路は .menu 名を渡す前提でアセットバンドルを扱えないため、
        /// 配置プラグインの選択に関わらず自前配置を使う
        /// </summary>
        private bool CreateBgObjectModel(BgObjectItem item, string pluginName)
        {
            if (item.info == null)
            {
                MTEUtils.LogWarning("背景オブジェクトの情報がありません。" + item.itemPath);
                return false;
            }

            try
            {
                if (pluginName != SelfModelPlacer.PluginName)
                {
                    MTEUtils.Log(
                        "背景オブジェクトは{0}に固定して配置します。(指定されたプラグイン: {1})",
                        SelfModelPlacer.PluginName, pluginName);
                }

                modelPlacerManager.CreateBgObject(item.info.assetBundleName, 0, true);

                UpdateModelItems();
                return true;
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
                return false;
            }
        }

        /// <summary>モデルを配置する。配置まで到達した場合のみ true を返す</summary>
        private bool CreateMenuModel(MenuItem item, string pluginName)
        {
            try
            {
                var group = 0;
                var modelList = modelPlacerManager.modelList;
                var menu = item.variationMenu;

                if (menu == null)
                {
                    MTEUtils.LogWarning("Menuが見つかりません。" + item.itemPath);
                    return false;
                }

                foreach (var model in modelList)
                {
                    if (model.infoWrapper?.fileName == menu.fileName)
                    {
                        var nextGroup = model.group + 1;
                        if (nextGroup == 1) nextGroup = 2;
                        group = Mathf.Max(group, nextGroup);
                        break;
                    }
                }

                var fileName = menu.fileName;
                if (pluginName == "StudioMode")
                {
                    fileName = Path.GetFileNameWithoutExtension(fileName);
                }

                modelPlacerManager.CreateModel(
                    fileName,
                    fileName,
                    group,
                    pluginName,
                    true);

                UpdateModelItems();
                return true;
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
                return false;
            }
        }

        public void DelItem(ModItemBase item)
        {
            if (item == null)
            {
                return;
            }

            if (item.itemType == ModItemType.Model)
            {
                try
                {
                    var modelItem = item as IModelItem;
                    modelPlacerManager.DeleteModel(modelItem?.model);
                }
                catch (Exception e)
                {
                    MTEUtils.LogException(e);
                }
                RemoveItem(item);
                return;
            }

            // ここから先は menu を前提にした削除経路
            var menuItem = item as MenuItem;
            if (menuItem?.menu == null)
            {
                return;
            }

            if (item.itemType == ModItemType.Equipped)
            {
                if (currentMaid != null)
                {
                    var beforeSnapshot = CapturePropsForHistory();

                    currentMaid.DelProp(menuItem.menu.mpn);
                    currentMaid.AllProcPropSeqStart();

                    RemoveItem(item);

                    RegisterPropsHistory("アイテム削除: " + item.name, beforeSnapshot);
                }
            }
            else if (item.itemType == ModItemType.Official
                || item.itemType == ModItemType.Mod)
            {
                if (currentMaid != null)
                {
                    var beforeSnapshot = CapturePropsForHistory();

                    currentMaid.DelProp(menuItem.menu.mpn);
                    currentMaid.AllProcPropSeqStart();

                    // 脱いだ状態を反映するため、適用完了後に全体を更新する
                    UpdateEquippedItemsAfterProcProp();

                    RegisterPropsHistory("アイテム削除: " + item.name, beforeSnapshot);
                }
            }
        }

        public void SetCurrentMaid(Maid maid)
        {
            if (maid == currentMaid)
            {
                return;
            }

            currentMaid = maid;

            foreach (var info in animationLayerInfos)
            {
                info.Reset();
            }

            for (int i = 0; i < animationStates.Count; i++)
            {
                animationStates[i] = null;
            }

            if (currentMaid != null && currentMaid.IsAllProcPropBusy)
            {
                // 適用途中の不完全な状態を読まないよう、完了を待ってから更新する
                UpdateEquippedItemsAfterProcProp();
            }
            else
            {
                UpdateEquippedItems();
            }
        }

        public bool IsEquippedItem(MenuItem item)
        {
            if (currentMaid == null || item == null || item.menu == null)
            {
                return false;
            }

            var itemName = GetEquippedItemName(item.maidPartType);
            var equippedItem = GetItemByName<MenuItem>(itemName);

            return item.menu == equippedItem?.menu;
        }

        public MenuItem GetEquippedItem(MaidPartType maidPartType)
        {
            if (currentMaid == null)
            {
                return null;
            }

            if (!MaidPartUtils.IsEquippableType(maidPartType))
            {
                return null;
            }

            var prop = currentMaid.GetProp(maidPartType.ToMPN());
            if (prop == null || string.IsNullOrEmpty(prop.strFileName))
            {
                return null;
            }

            var menu = GetMenu(prop.strFileName);
            if (menu == null)
            {
                MTEUtils.LogWarning("着用中のアイテムが見つかりません。" + prop.strFileName);
                return null;
            }

            if (!string.IsNullOrEmpty(menu.variationBaseFileName))
            {
                var baseItem = GetItemByName<MenuItem>(menu.variationBaseFileName);
                if (baseItem != null)
                {
                    baseItem.variationMenu = menu;
                    return baseItem;
                }
            }

            var equippedItem = GetItemByName<MenuItem>(prop.strFileName);
            if (equippedItem == null && _warnedNotFoundEquippedItems.Add(prop.strFileName))
            {
                // 非表示・男性用・未所持のアイテムを着用している場合もここに来る
                MTEUtils.LogWarning("着用中のアイテムがツリーに登録されていません。" + prop.strFileName);
            }

            return equippedItem;
        }

        private static string WildCardMatchEvaluator(Match match)
        {
            string value = match.Value;
            if (value.Equals("?"))
            {
                return ".";
            }
            if (value.Equals("*"))
            {
                return ".*";
            }
            return Regex.Escape(value);
        }

        private void SaveMenuCache()
        {
            MTEUtils.LogDebug("[ModMenuItemManager] SaveMenuCache");

            var tempPath = PluginUtils.MenuCachePath + ".tmp";
            try
            {
                // 一時ファイルに書き込み
                using (var binaryWriter = new BinaryWriter(File.Open(tempPath, FileMode.Create)))
                {
                    binaryWriter.Write(MenuInfo.CacheVersion);
                    foreach (var menu in _menuMap.Values)
                    {
                        menu?.Serialize(binaryWriter);
                    }
                }

                // 書き込みが成功したら、既存のファイルを置き換え
                if (File.Exists(PluginUtils.MenuCachePath))
                {
                    File.Delete(PluginUtils.MenuCachePath);
                }
                File.Move(tempPath, PluginUtils.MenuCachePath);
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);

                // エラー時は一時ファイルを削除
                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch (Exception e2)
                {
                    MTEUtils.LogException(e2);
                }
            }
        }

        public void ResetItems()
        {
            _menuMap.Clear();
            InitItemCache();
        }

        private bool LoadMenuCache()
        {
            MTEUtils.LogDebug("[ModMenuItemManager] LoadMenuCache");
            try
            {
                if (!File.Exists(PluginUtils.MenuCachePath))
                {
                    return false;
                }

                using (var binaryReader = new BinaryReader(File.OpenRead(PluginUtils.MenuCachePath)))
                {
                    var version = binaryReader.ReadInt32();
                    if (version != MenuInfo.CacheVersion)
                    {
                        MTEUtils.LogWarning("キャッシュのバージョンが古いため再更新します。version={0}", version);
                        return false;
                    }

                    while (binaryReader.BaseStream.Position < binaryReader.BaseStream.Length)
                    {
                        var menu = MenuInfo.Deserialize(binaryReader);
                        _menuMap[menu.fileName] = menu;
                    }
                }

                return true;
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
                return false;
            }
        }

        public void DeleteMenuCache()
        {
            try
            {
                if (File.Exists(PluginUtils.MenuCachePath))
                {
                    File.Delete(PluginUtils.MenuCachePath);
                }
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }
        }

        private void LoadOfficialMenuFileNameList()
        {
            if (_officialMenuFileNameList.Count > 0)
            {
                return;
            }

            MTEUtils.LogDebug("[ModMenuItemManager] LoadOfficialMenuFileNameList");

            var menuDataBase = GameMain.Instance.MenuDataBase;
            var menuCount = menuDataBase.GetDataSize();

            _officialMenuFileNameList.Capacity = menuCount;
            for (int i = 0; i < menuCount; i++)
            {
                menuDataBase.SetIndex(i);
                _officialMenuFileNameList.Add(menuDataBase.GetMenuFileName());
            }
        }

        private Dictionary<string, string> _officialNameMap = new Dictionary<string, string>(128, StringComparer.OrdinalIgnoreCase);

        private void LoadOfficialNameCsv()
        {
            MTEUtils.LogDebug("[ModMenuItemManager] LoadOfficialNameCsv");
            loadState = LoadState.LoadOfficialNameCsv;

            if (_officialNameMap.Count > 0)
            {
                return;
            }

            var path = PluginUtils.OfficialNameCsvPath;
            if (!File.Exists(path))
            {
                MTEUtils.LogWarning("CSVファイルが見つかりません path={0}", path);
                return;
            }

            try
            {
                _officialNameMap.Clear();

                using (FileStream fileStream = new FileStream(path, FileMode.Open, FileAccess.Read))
                using (StreamReader reader = new StreamReader(fileStream))
                {
                    bool isFirstLine = true;

                    while (!reader.EndOfStream)
                    {
                        string line = reader.ReadLine();
                        if (isFirstLine)
                        {
                            isFirstLine = false;
                            continue; // ヘッダー行をスキップ
                        }

                        if (string.IsNullOrEmpty(line))
                        {
                            continue; // 空行をスキップ
                        }

                        string[] values = line.Split(',');
                        if (values.Length < 2)
                        {
                            MTEUtils.LogWarning("CSVファイルの形式が不正です line={0}", line);
                            continue;
                        }

                        var key = values[0];
                        var name = values[1];

                        if (name == "")
                        {
                            continue; // 空行をスキップ
                        }

                        if (_officialNameMap.ContainsKey(key))
                        {
                            MTEUtils.LogWarning("keyが重複していたのでスキップ key={0}", key);
                            continue;
                        }

                        _officialNameMap[key] = name;
                    }
                }
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }
        }

        private void LoadOfficialMenuItems()
        {
            MTEUtils.LogDebug("[ModMenuItemManager] LoadOfficialMenuItems");
            loadState = LoadState.LoadOfficialMenuItems;

            officialMenuTotalCount = _officialMenuFileNameList.Count;

            for (officialMenuLoadedIndex = 0; officialMenuLoadedIndex < officialMenuTotalCount; officialMenuLoadedIndex++)
            {
                try
                {
                    var menuFileName = _officialMenuFileNameList[officialMenuLoadedIndex];

                    var menu = GetOrLoadOfficialMenu(menuFileName);
                    if (menu == null)
                    {
                        MTEUtils.LogWarning("Menuのロードに失敗しました。" + menuFileName);
                        continue;
                    }

                    if (!IsVisibleMenu(menu))
                    {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(menu.variationBaseFileName))
                    {
                        _variationMenuPathMap[menu.rid] = menuFileName;
                        _variationMenuMap.GetOrCreate(
                            menu.variationBaseFileName,
                            () => new List<MenuInfo>(8)).Add(menu);
                        continue;
                    }

                    var item = GetItemByName<MenuItem>(menuFileName);
                    if (item == null)
                    {
                        var itemPath = GetOfficialItemPath(menu);
                        item = GetOrCreateMenuItem(itemPath, menu, ModItemType.Official);
                    }
                }
                catch (Exception e)
                {
                    MTEUtils.LogException(e);
                }
            }
        }

        public void LoadOfficialAnmItems()
        {
            MTEUtils.LogDebug("[ModMenuItemManager] LoadOfficialAnmItems");
            loadState = LoadState.LoadOfficialAnmItems;

            if (PhotoMotionData.data == null)
            {
                PhotoMotionData.Create();
            }

            foreach (var motionData in PhotoMotionData.data)
            {
                try
                {
                    if (!motionData.direct_file.EndsWith(".anm", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    if (motionData.is_man_pose)
                    {
                        continue;
                    }

                    var itemPath = GetOfficialItemPath(motionData);
                    var fullPath = (motionData.is_mod || motionData.is_mypose) ? motionData.direct_file : null;
                    GetOrCreateAnmItem(itemPath, fullPath, motionData.name, ModItemType.Anm);
                }
                catch (Exception e)
                {
                    MTEUtils.LogException(e);
                }
            }
        }

        private bool IsVisibleMenu(MenuInfo menu)
        {
            if (menu.isHidden || menu.isMan)
            {
                return false;
            }

            if (menu.isOfficial)
            {
                if (string.IsNullOrEmpty(menu.iconName))
                {
                    return false;
                }

                if (!characterMgr.status.IsHavePartsItem(menu.fileName))
                {
                    return false;
                }
            }

            if (menu.fileName.EndsWith("_del.menu", StringComparison.Ordinal) ||
                menu.fileName.EndsWith("_del_folder.menu", StringComparison.Ordinal))
            {
                return false;
            }

            return true;
        }

        private string GetOfficialItemPath(MenuInfo menu)
        {
            if (config.groupOfficialItemsByMPN)
            {
                var partName = menu.maidPartType.ToName();
                return MTEUtils.CombinePaths(OfficialDirName, partName, menu.fileName);
            }
            return MTEUtils.CombinePaths(OfficialDirName, menu.path);
        }

        private string GetOfficialItemPath(PhotoMotionData motionData)
        {
            if (motionData.is_mod || motionData.is_mypose)
            {
                var fileName = Path.GetFileName(motionData.direct_file);
                return MTEUtils.CombinePaths(
                        OfficialDirName,
                        "motion",
                        "mypose",
                        fileName);
            }
            else
            {
                return MTEUtils.CombinePaths(
                        OfficialDirName,
                        "motion",
                        motionData.category,
                        motionData.direct_file);
            }
        }

        private void LoadModItems(string searchPattern)
        {
            MTEUtils.LogDebug("[ModMenuItemManager] LoadItems {0}", searchPattern);
            loadState = LoadState.LoadModItems;

            string[] menuFilePaths = Directory.GetFiles(MTEUtils.ModDirPath, searchPattern, SearchOption.AllDirectories);

            modMenuTotalCount = menuFilePaths.Length;
            for (modMenuLoadedIndex = 0; modMenuLoadedIndex < modMenuTotalCount; modMenuLoadedIndex++)
            {
                try
                {
                    var menuFilePath = menuFilePaths[modMenuLoadedIndex];
                    var lastWriteAt = GetLastWriteAt(menuFilePath);

                    var menu = GetOrLoadModMenu(menuFilePath, lastWriteAt);
                    if (menu == null)
                    {
                        MTEUtils.LogWarning("Menuのロードに失敗しました。" + menuFilePath);
                        continue;
                    }

                    if (!IsVisibleMenu(menu))
                    {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(menu.variationBaseFileName))
                    {
                        _variationMenuPathMap[menu.rid] = menuFilePath;
                        _variationMenuMap.GetOrCreate(
                            menu.variationBaseFileName,
                            () => new List<MenuInfo>(8)).Add(menu);
                        continue;
                    }

                    var itemPath = GetModItemPath(menu, GetRelativePath(MTEUtils.ModDirPath, menuFilePath));
                    GetOrCreateMenuItem(itemPath, menu, ModItemType.Mod, menuFilePath);
                }
                catch (Exception e)
                {
                    MTEUtils.LogException(e);
                }
            }
        }

        private void LoadModBgObjectItems()
        {
            MTEUtils.LogDebug("[ModMenuItemManager] LoadModBgObjectItems");
            loadState = LoadState.LoadModBgObjectItems;

            // 今回のロードで生き残った itemPath。取りこぼしを後で掃除するために覚えておく
            var alivePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var infoList = BgObjectNeiLoader.LoadAll();
            _bgObjectInfoMap.Clear();

            foreach (var info in infoList)
            {
                try
                {
                    _bgObjectInfoMap[info.assetBundleName] = info;

                    // nei と同じフォルダに展開する。既存の Mod ツリー(実フォルダ構造)と揃える
                    var relativeDir = Path.GetDirectoryName(
                        GetRelativePath(MTEUtils.ModDirPath, info.neiFilePath));
                    var itemPath = MTEUtils.CombinePaths(ModDirName, relativeDir, info.name);

                    // 同一フォルダで表示名が衝突すると後勝ちで消えてしまうため、
                    // 一意なアセットバンドル名を足して逃がす。
                    // 前回ロード分の BgObjectItem は自分自身なので衝突扱いにしないが、
                    // 今回のロードで既に使った itemPath なら別のバンドルとの衝突なので逃がす
                    var existing = GetItemByPath<ModItemBase>(itemPath);
                    if (existing != null &&
                        (!(existing is BgObjectItem) || alivePaths.Contains(itemPath)))
                    {
                        MTEUtils.LogWarning(
                            "背景オブジェクトの名前が重複しています。{0} ({1})",
                            info.name, info.assetBundleName);
                        itemPath = MTEUtils.CombinePaths(
                            ModDirName, relativeDir, info.name + "_" + info.assetBundleName);
                    }

                    if (GetOrCreateBgObjectItem(itemPath, info) != null)
                    {
                        alivePaths.Add(itemPath);
                    }
                }
                catch (Exception e)
                {
                    MTEUtils.LogException(e);
                }
            }

            RemoveStaleBgObjectItems(alivePaths);
        }

        /// <summary>
        /// 今回の nei に現れなかった背景オブジェクトのアイテムをツリーから消す。
        ///
        /// BgObjectItem の fullPath は nei ファイル自体を指すため、MOD 側が nei の行だけを
        /// 削除・リネームしても ValidateItemFile の File.Exists は通ってしまい、
        /// 旧アイテムがゴーストとしてツリーに残る。ここで明示的に掃除する
        /// </summary>
        private void RemoveStaleBgObjectItems(HashSet<string> alivePaths)
        {
            var staleItems = new List<ModItemBase>();
            foreach (var pair in _itemPathMap)
            {
                // 配置中の ModelBgObjectItem も BgObjectItem だが nei 由来ではないので対象外
                if (pair.Value.itemType == ModItemType.BgObject && !alivePaths.Contains(pair.Key))
                {
                    staleItems.Add(pair.Value);
                }
            }

            foreach (var item in staleItems)
            {
                MTEUtils.LogDebug("[ModMenuItemManager] 背景オブジェクトを削除: " + item.itemPath);
                RemoveItem(item);
            }
        }

        public void DumpDuplicatedMods(List<string> patterns)
        {
            MTEUtils.Log("[ModMenuItemManager] DumpDuplicatedMods");

            if (isLoading)
            {
                return;
            }

            isLoading = true;
            loadState = LoadState.None;
            modMenuLoadedIndex = 0;
            modMenuTotalCount = 0;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    foreach (var pattern in patterns)
                    {
                        DumpDuplicatedModsInternal(pattern);
                    }
                    isLoading = false;
                }
                catch (Exception e)
                {
                    MTEUtils.LogException(e);
                    isLoading = false;
                }
            });
        }

        private void DumpDuplicatedModsInternal(string searchPattern)
        {
            string[] menuFilePaths = Directory.GetFiles(MTEUtils.ModDirPath, searchPattern, SearchOption.AllDirectories);

            var duplicatedModMap = new Dictionary<string, List<string>>(menuFilePaths.Length, StringComparer.OrdinalIgnoreCase);
            foreach (var menuFilePath in menuFilePaths)
            {
                duplicatedModMap.GetOrCreate(Path.GetFileName(menuFilePath)).Add(menuFilePath);
            }

            foreach (var pair in duplicatedModMap)
            {
                if (pair.Value.Count > 1)
                {
                    MTEUtils.LogWarning("Duplicated Mod: {0}", pair.Key);
                    foreach (var path in pair.Value)
                    {
                        MTEUtils.Log("  {0}", path);
                    }
                }
            }
        }

        private string GetModItemPath(MenuInfo menu, string filePath)
        {
            if (config.groupModItemsByMPN)
            {
                var partName = menu.maidPartType.ToName();
                return MTEUtils.CombinePaths(ModDirName, partName, menu.fileName);
            }
            return MTEUtils.CombinePaths(ModDirName, filePath);
        }

        public void UpdatePresetItems(bool rebuild = false)
        {
            MTEUtils.LogDebug("[ModMenuItemManager] UpdatePresetItems");
            loadState = LoadState.UpdatePresetItems;

            if (rebuild)
            {
                RemoveItemChildren(presetRootItem);
            }

            string[] presetFilePaths = Directory.GetFiles(MTEUtils.PresetDirPath, "*.preset");

            foreach (var presetFilePath in presetFilePaths)
            {
                try
                {
                    var itemPath = GetPresetItemPath(GetRelativePath(MTEUtils.PresetDirPath, presetFilePath));
                    GetOrCreatePresetItem(itemPath, presetFilePath, ModItemType.Preset);
                }
                catch (Exception e)
                {
                    MTEUtils.LogException(e);
                }
            }

            ValidateItemChildren(presetRootItem);
            SortItemChildren(presetRootItem);
        }

        private static readonly Regex _presetDateRegex = new Regex(@"_(\d{14})", RegexOptions.Compiled);

        private string GetPresetMaidName(string fileName)
        {
            var maidName = _presetDateRegex.Replace(fileName, "")
                        .Replace("pre_", "")
                        .Replace(".preset", "")
                        .Trim();
            return maidName;
        }

        private string GetPresetItemPath(string filePath)
        {
            var fileName = Path.GetFileName(filePath);
            var maidName = GetPresetMaidName(fileName);
            return MTEUtils.CombinePaths(PresetDirName, maidName, fileName);
        }

        public void UpdateTempPresetItems()
        {
            MTEUtils.LogDebug("[ModMenuItemManager] UpdateTempPresetItems");

            var maidList = MTEUtils.GetReadyMaidList();

            foreach (var maid in maidList)
            {
                var tempPresets = tempPresetManager.GetTempPresets(maid);
                var maidName = maid.status.fullNameJpStyle;
                foreach (var tempPreset in tempPresets)
                {
                    try
                    {
                        if (tempPreset?.preset == null)
                        {
                            continue;
                        }

                        var itemPath = MTEUtils.CombinePaths(TempPresetDirName, maidName, tempPreset.preset.strFileName);
                        GetOrCreatePresetItem(itemPath, tempPreset, ModItemType.TempPreset);
                    }
                    catch (Exception e)
                    {
                        MTEUtils.LogException(e);
                    }
                }
            }

            ValidateItemChildren(tempPresetRootItem);
            SortItemChildren(tempPresetRootItem);
        }

        public void UpdateModPresetItems()
        {
            MTEUtils.LogDebug("[ModMenuItemManager] UpdateModPresetItems");
            loadState = LoadState.UpdateModPresetItems;

            string[] presetFilePaths = Directory.GetFiles(MTEUtils.ModDirPath, "*.preset", SearchOption.AllDirectories);

            foreach (var presetFilePath in presetFilePaths)
            {
                try
                {
                    var relativePath = GetRelativePath(MTEUtils.ModDirPath, presetFilePath);
                    var itemPath = MTEUtils.CombinePaths(ModDirName, relativePath);
                    GetOrCreatePresetItem(itemPath, presetFilePath, ModItemType.Preset);
                }
                catch (Exception e)
                {
                    MTEUtils.LogException(e);
                }
            }
        }

        public void UpdateModAnmItems()
        {
            MTEUtils.LogDebug("[ModMenuItemManager] UpdateModAnmItems");
            loadState = LoadState.UpdateModAnmItems;

            string[] anmFilePaths = Directory.GetFiles(MTEUtils.ModDirPath, "*.anm", SearchOption.AllDirectories);

            foreach (var anmFilePath in anmFilePaths)
            {
                try
                {
                    var relativePath = GetRelativePath(MTEUtils.ModDirPath, anmFilePath);
                    var itemPath = MTEUtils.CombinePaths(ModDirName, relativePath);
                    var name = Path.GetFileNameWithoutExtension(anmFilePath);
                    GetOrCreateAnmItem(itemPath, null, name, ModItemType.Anm);
                }
                catch (Exception e)
                {
                    MTEUtils.LogException(e);
                }
            }
        }

        public MenuInfo GetMenu(string menuFileName)
        {
            if (string.IsNullOrEmpty(menuFileName))
            {
                return null;
            }

            // 拡張子がない場合は.menuとして扱う
            if (!menuFileName.EndsWith(".menu", StringComparison.OrdinalIgnoreCase) &&
                !menuFileName.EndsWith(".mod", StringComparison.OrdinalIgnoreCase))
            {
                menuFileName += ".menu";
            }

            return _menuMap.GetOrDefault(menuFileName);
        }

        /// <summary>
        /// 配置データのファイル名から背景オブジェクトの情報を引く。
        /// 背景オブジェクト以外、または nei から消えている場合は null
        /// </summary>
        public BgObjectInfo GetBgObjectInfo(string fileName)
        {
            if (!SelfModelPlacer.IsBgObjectFileName(fileName))
            {
                return null;
            }

            return _bgObjectInfoMap.GetOrDefault(Path.GetFileNameWithoutExtension(fileName));
        }

        /// <summary>
        /// 配置中モデルの表示名。menu 由来なら menu 名、背景オブジェクトなら nei の名前。
        /// StudioModelStatWrapper.displayName は GameObject 名 (menu/アセットバンドルのファイル名) の
        /// 使い回しで人間向けではないため、解決規則をここに集約して呼び出し側で共有する。
        /// どちらも引けない場合は null を返し、フォールバックは呼び出し側に任せる
        /// </summary>
        public string GetModelBaseName(StudioModelStatWrapper model)
        {
            var fileName = model?.infoWrapper?.fileName;

            var menuName = GetMenu(fileName)?.name;
            if (!string.IsNullOrEmpty(menuName))
            {
                return menuName;
            }

            var bgObjectName = GetBgObjectInfo(fileName)?.name;
            if (!string.IsNullOrEmpty(bgObjectName))
            {
                return bgObjectName;
            }

            return null;
        }

        /// <summary>
        /// 配置中モデルのサムネ。背景オブジェクトは menu を持たないため、
        /// アイテム一覧 (BgObjectItem.thum) と同じ共通アイコンへフォールバックする
        /// </summary>
        public Texture2D GetModelThum(StudioModelStatWrapper model)
        {
            var fileName = model?.infoWrapper?.fileName;

            var menu = GetMenu(fileName);
            if (menu != null)
            {
                var thum = TextureManager.instance.GetTexture(menu.iconName, menu.iconData);
                if (thum != null)
                {
                    return thum;
                }
            }

            if (SelfModelPlacer.IsBgObjectFileName(fileName))
            {
                return PluginInfo.BgObjectIconTexture;
            }

            return null;
        }

        private MenuInfo GetOrLoadOfficialMenu(string menuFileName)
        {
            var menu = GetMenu(menuFileName);
            if (menu != null)
            {
                return menu;
            }

            menu = ModMenuLoader.LoadDirect(menuFileName, isOfficial: true);

            if (menu != null)
            {
                _menuMap[menuFileName] = menu;
            }
            return menu;
        }

        private MenuInfo GetOrLoadModMenu(string menuFilePath, long lastWriteAt)
        {
            var menuFileName = Path.GetFileName(menuFilePath);
            var menu = GetMenu(menuFileName);
            if (menu != null && menu.lastWriteAt == lastWriteAt)
            {
                return menu;
            }

            menu = ModMenuLoader.LoadDirect(menuFileName, menuFilePath, isOfficial: false);
            if (menu != null)
            {
                menu.lastWriteAt = lastWriteAt;
                _menuMap[menuFileName] = menu;
            }
            return menu;
        }

        private List<ITileViewContent> _validateItems = new List<ITileViewContent>(menuCapacity);
        public void ValidateItemChildren(ModItemBase parentItem)
        {
            _validateItems.Clear();
            parentItem.GetAllChildren(_validateItems);

            foreach (ModItemBase item in _validateItems)
            {
                ValidateItemFile(item);
            }
            foreach (ModItemBase item in _validateItems)
            {
                ValidateItemDir(item);
            }
        }

        private void ValidateItemFile(ModItemBase item)
        {
            if (item == null || item.isDir)
            {
                return;
            }

            try
            {
                if (!string.IsNullOrEmpty(item.fullPath))
                {
                    if (!File.Exists(item.fullPath))
                    {
                        RemoveItem(item);
                    }
                }
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }
        }

        private void ValidateItemDir(ModItemBase item)
        {
            if (item == null || !item.isDir)
            {
                return;
            }

            try
            {
                if (item.children == null || item.children.Count == 0)
                {
                    RemoveItem(item);
                }
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }
        }

        private void CollectVariationMenu()
        {
            MTEUtils.LogDebug("[ModMenuItemManager] CollectVariationMenu");
            loadState = LoadState.CollectVariationMenu;

            foreach (var kvp in _variationMenuMap)
            {
                try
                {
                    var baseFileName = kvp.Key;
                    var variationMenuList = kvp.Value;

                    if (variationMenuList.Count == 0)
                    {
                        continue;
                    }

                    variationMenuList.Sort(CompareMenu);

                    var baseItem = GetItemByName<MenuItem>(baseFileName);

                    // バリエーション元がない場合、最初のメニューをベースとして表示
                    if (baseItem == null)
                    {
                        var baseMenu = variationMenuList[0];
                        if (baseMenu.isOfficial)
                        {
                            MTEUtils.LogWarning("バリエーション元が見つかりません。" + baseFileName);
                            continue;
                        }

                        if (_variationMenuPathMap.TryGetValue(baseMenu.rid, out var menuFilePath))
                        {
                            MTEUtils.LogDebug("バリエーション元として表示: {0} {1}", baseMenu.name, baseMenu.fileName);
                            var itemPath = GetModItemPath(baseMenu, GetRelativePath(MTEUtils.ModDirPath, menuFilePath));
                            baseItem = GetOrCreateMenuItem(itemPath, baseMenu, ModItemType.Mod, menuFilePath);
                        }
                    }

                    if (baseItem == null)
                    {
                        continue;
                    }

                    foreach (var menu in variationMenuList)
                    {
                        if (menu == baseItem.menu)
                        {
                            continue;
                        }

                        baseItem.AddMenu(menu);
                    }
                }
                catch (Exception e)
                {
                    MTEUtils.LogException(e);
                }
            }
        }

        private void CollectColorSetMenu()
        {
            MTEUtils.LogDebug("[ModMenuItemManager] CollectColorSetMenu");
            loadState = LoadState.CollectColorSetMenu;

            // 使用しているColorSetを生成
            foreach (var item in _itemNameMap.Values)
            {
                var menuItem = item as MenuItem;
                if (menuItem == null ||
                    menuItem.colorSet != null ||
                    menuItem.menu == null ||
                    menuItem.menu.colorSetMPN == MPN.null_mpn ||
                    string.IsNullOrEmpty(menuItem.menu.colorSetMenuName))
                {
                    continue;
                }

                menuItem.colorSet = GetOrCreateColorSetInfo(menuItem.menu.colorSetMPN, menuItem.menu.colorSetMenuName);
            }

            // 未初期化のColorSetを取得
            var colorSetList = new List<ColorSetInfo>(128);
            foreach (var colorSetMap in _colorSetMap.Values)
            {
                foreach (var colorSet in colorSetMap.Values)
                {
                    if (colorSet.colorMenuList == null)
                    {
                        colorSetList.Add(colorSet);
                    }
                }
            }

            // ColorSetで使用しているMenuを収集
            var menuList = _menuMap.Values.ToList();
            ParallelHelper.ForEach(colorSetList, colorSet =>
            {
                colorSet.colorMenuList = new List<MenuInfo>(16);

                var patternStr = Regex.Replace(colorSet.colorSetMenuName, ".", new MatchEvaluator(WildCardMatchEvaluator));
                var pattern = new Regex(patternStr, RegexOptions.IgnoreCase | RegexOptions.Compiled);

                foreach (var menu in menuList)
                {
                    if (menu.mpn != colorSet.colorSetMPN)
                    {
                        continue;
                    }

                    if (pattern.IsMatch(menu.fileName))
                    {
                        colorSet.AddColorMenu(menu);
                    }
                }

                colorSet.colorMenuList.Sort(CompareMenu);
            });
        }

        public static void SortItemChildren(ITileViewContent item)
        {
            if (item.children == null)
            {
                return;
            }

            item.children.Sort(CompareItem);

            foreach (var child in item.children)
            {
                if (child.isDir)
                {
                    SortItemChildren(child);
                }
            }
        }

        public void SortAllItems()
        {
            foreach (var child in rootItem.children)
            {
                // 履歴は新しい順が意味を持つため、ソート順の変更を反映しない
                if (child == historyRootItem)
                {
                    continue;
                }

                SortItemChildren(child);
            }
        }

        public static int CompareItem(ITileViewContent a, ITileViewContent b)
        {
            if (config.itemSortType == ItemSortType.DefaultAsc ||
                config.itemSortType == ItemSortType.NameAsc ||
                config.itemSortType == ItemSortType.LastWriteAtAsc)
            {
                return _CompareItem(a, b);
            }
            return _CompareItem(b, a);
        }

        private static int _CompareItem(ITileViewContent a, ITileViewContent b)
        {
            if (a.isDir && !b.isDir)
            {
                return -1;
            }

            if (!a.isDir && b.isDir)
            {
                return 1;
            }

            var aItem = a as ModItemBase;
            var bItem = b as ModItemBase;

            if (config.itemSortType == ItemSortType.LastWriteAtAsc ||
                config.itemSortType == ItemSortType.LastWriteAtDesc)
            {
                if (aItem.lastWriteAt != bItem.lastWriteAt)
                {
                    return aItem.lastWriteAt.CompareTo(bItem.lastWriteAt);
                }
            }

            if (config.itemSortType == ItemSortType.NameAsc ||
                config.itemSortType == ItemSortType.NameDesc)
            {
                if (aItem.name != bItem.name)
                {
                    return string.Compare(aItem.name, bItem.name, StringComparison.Ordinal);
                }
            }

            if (aItem.maidPartType != bItem.maidPartType)
            {
                return aItem.maidPartType.CompareTo(bItem.maidPartType);
            }

            if (aItem.priority != bItem.priority)
            {
                return aItem.priority.CompareTo(bItem.priority);
            }

            if (aItem.name != bItem.name)
            {
                return string.Compare(a.name, b.name, StringComparison.Ordinal);
            }

            return string.Compare(aItem.itemName, bItem.itemName, StringComparison.Ordinal);
        }

        private static int CompareMenu(MenuInfo a, MenuInfo b)
        {
            if (a.maidPartType != b.maidPartType)
            {
                return a.maidPartType.CompareTo(b.maidPartType);
            }

            if (a.priority != b.priority)
            {
                return a.priority.CompareTo(b.priority);
            }

            return string.Compare(a.name, b.name, StringComparison.Ordinal);
        }

        private string GetEquippedItemName(MaidPartType maidPartType)
        {
            return "equipped_" + maidPartType.ToName();
        }

        public void UpdateEquippedItems()
        {
            MTEUtils.LogDebug("[ModMenuItemManager] UpdateEquippedItems");

            var mpnList = MaidPartUtils.GetAllMaidPartType();
            foreach (var mpn in mpnList)
            {
                UpdateEquippedItem(mpn);
            }

            SortItemChildren(equippedRootItem);
        }

        private bool _isEquippedItemsUpdateReserved = false;

        /// <summary>
        /// メイドへの適用完了を待ってから着用中アイテムを更新する
        /// </summary>
        public void UpdateEquippedItemsAfterProcProp()
        {
            // 連続適用でコルーチンが多重起動しないよう、待機中は予約済みの更新に任せる
            if (currentMaid == null || _isEquippedItemsUpdateReserved)
            {
                return;
            }
            _isEquippedItemsUpdateReserved = true;

            MTEUtils.ExecuteAfterProcProp(currentMaid, () =>
            {
                _isEquippedItemsUpdateReserved = false;
                UpdateEquippedItems();
            });
        }

        public void UpdateEquippedItem(MaidPartType maidPartType)
        {
            try
            {
                var itemName = GetEquippedItemName(maidPartType);
                var itemPath = MTEUtils.CombinePaths(EquippedDirName, itemName);

                var equippedItem = GetEquippedItem(maidPartType);
                if (equippedItem == null || equippedItem.menu == null)
                {
                    var item = GetItemByPath<MenuItem>(itemPath);
                    RemoveItem(item);
                    return;
                }

                // カラーセット更新
                if (equippedItem.colorSet != null)
                {
                    var colorSet = equippedItem.colorSet;
                    var prop = currentMaid.GetProp(colorSet.colorSetMPN);
                    var menu = GetMenu(prop?.strFileName);
                    if (menu != null)
                    {
                        MTEUtils.LogDebug("[ModMenuItemManager] Update colorSet {0} {1}", colorSet.colorSetMPN, prop?.strFileName);
                        colorSet.selectedMenu = menu;
                    }
                }

                GetOrCreateRefMenuItem(itemPath, equippedItem, ModItemType.Equipped);
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }
        }

        public DirItem searchTargetDirItem = null;
        public string searchPattern = string.Empty;
        private List<ITileViewContent> _searchTempItems = new List<ITileViewContent>(menuCapacity);

        public void UpdateSearchItems(DirItem targetDirItem = null)
        {
            MTEUtils.LogDebug("[ModMenuItemManager] UpdateSearchItems");

            if (targetDirItem != null && targetDirItem != searchRootItem)
            {
                searchTargetDirItem = targetDirItem;
            }

            if (searchTargetDirItem == null || string.IsNullOrEmpty(searchPattern))
            {
                return;
            }

            searchRootItem.RemoveAllChildren();
            _searchTempItems.Clear();

            searchTargetDirItem.GetAllChildren(_searchTempItems);

            var patternStr = Regex.Replace(searchPattern, ".", new MatchEvaluator(WildCardMatchEvaluator));
            var pattern = new Regex(patternStr, RegexOptions.IgnoreCase | RegexOptions.Compiled);

            foreach (ModItemBase item in _searchTempItems)
            {
                if (item != null && item.IsMatch(pattern))
                {
                    searchRootItem.AddChild(item);
                }
            }

            SortItemChildren(searchRootItem);
        }

        public void UpdateFavoriteItems()
        {
            MTEUtils.LogDebug("[ModMenuItemManager] UpdateFavoriteItems");

            favoriteRootItem.RemoveAllChildren();

            foreach (var itemPath in config.favoriteItemPathSet)
            {
                var item = GetItemByPath<ModItemBase>(itemPath);
                if (item == null)
                {
                    continue;
                }

                favoriteRootItem.AddChild(item);
            }

            SortItemChildren(favoriteRootItem);
        }

        /// <summary>
        /// 選択履歴の一覧を作り直す。新しい順を保つためソートはしない。
        /// MODの削除などで解決できないパスは表示から外すだけで、履歴自体は残す
        /// </summary>
        public void UpdateHistoryItems()
        {
            MTEUtils.LogDebug("[ModMenuItemManager] UpdateHistoryItems");

            historyRootItem.RemoveAllChildren();

            foreach (var itemPath in itemHistoryManager.itemPaths)
            {
                var item = GetItemByPath<ModItemBase>(itemPath);
                if (item == null)
                {
                    continue;
                }

                historyRootItem.AddChild(item);
            }
        }

        private List<ITileViewContent> _modelTempItems = new List<ITileViewContent>(16);

        public void UpdateModelItems()
        {
            MTEUtils.LogDebug("[ModMenuItemManager] UpdateModelItems");

            try
            {
                var modelList = modelPlacerManager.modelList;
                foreach (var model in modelList)
                {
                    UpdateModelItem(model);
                }

                _modelTempItems.Clear();
                _modelTempItems.AddRange(modelRootItem.children);

                foreach (var content in _modelTempItems)
                {
                    var item = content as ModItemBase;
                    var modelItem = item as IModelItem;
                    if (modelItem != null && !modelList.Contains(modelItem.model))
                    {
                        RemoveItem(item);
                    }
                }

                SortItemChildren(modelRootItem);
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }
        }

        private string GetModelItemName(StudioModelStatWrapper model)
        {
            return "model_" + model.name;
        }

        public void UpdateModelItem(StudioModelStatWrapper model)
        {
            try
            {
                var itemName = GetModelItemName(model);
                var itemPath = MTEUtils.CombinePaths(ModelDirName, itemName);
                var fileName = model.infoWrapper?.fileName;

                // 背景オブジェクトは menu を持たないため、拡張子で見分けて別経路で作る
                if (SelfModelPlacer.IsBgObjectFileName(fileName))
                {
                    GetOrCreateModelBgObjectItem(itemPath, fileName, model);
                    return;
                }

                var menu = GetMenu(fileName);
                if (menu == null)
                {
                    var item = GetItemByPath<MenuItem>(itemPath);
                    RemoveItem(item);
                    return;
                }

                GetOrCreateModelMenuItem(itemPath, menu, model, ModItemType.Model);
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }
        }

        private DirItem GetOrCreateDirItem(string dirPath)
        {
            if (string.IsNullOrEmpty(dirPath))
            {
                return rootItem;
            }

            var parentItem = GetItemByPath<DirItem>(dirPath);
            if (parentItem != null)
            {
                return parentItem;
            }

            parentItem = rootItem;

            var dirNames = dirPath.Split(Path.DirectorySeparatorChar);
            for (int i = 0; i < dirNames.Length; i++)
            {
                var itemName = dirNames[i];
                if (string.IsNullOrEmpty(itemName))
                {
                    continue;
                }
                if (itemName[0] == '.')
                {
                    return null; // .で始まるディレクトリは無視
                }

                var itemPath = itemName;
                if (!string.IsNullOrEmpty(parentItem.itemPath))
                {
                    itemPath = Path.Combine(parentItem.itemPath, itemName);
                }

                var item = GetItemByPath<DirItem>(itemPath);
                if (item == null)
                {
                    var name = itemName;
                    var searchName = name.Replace("menu_", "")
                                            .Replace("parts_", "")
                                            .Replace("_2", "");
                    if (!_officialNameMap.TryGetValue(searchName, out name))
                    {
                        searchName = searchName.Replace("dlc", "");
                        if (!_officialNameMap.TryGetValue(searchName, out name))
                        {
                            name = itemName;
                        }
                    }

                    var fullPath = Path.Combine(UTY.gameProjectPath, itemPath);
                    if (!Directory.Exists(fullPath))
                    {
                        fullPath = null;
                    }

                    item = new DirItem
                    {
                        name = name,
                        maidPartType = itemName.ToMaidPartType(),
                        itemType = ModItemType.Dir,
                        itemName = itemName,
                        itemPath = itemPath,
                        fullPath = fullPath,
                        children = new List<ITileViewContent>(16),
                    };
                    parentItem.AddChild(item);
                    _itemPathMap[itemPath] = item;
                }

                parentItem = item;
            }

            return parentItem;
        }
        
        private MenuItem GetOrCreateMenuItem(
            string itemPath,
            MenuInfo menu,
            ModItemType itemType,
            string menuFilePath = null)
        {
            if (menu == null)
            {
                return null;
            }

            var item = GetItemByPath<MenuItem>(itemPath);
            if (item != null)
            {
                item.menu = menu;
                return item;
            }

            var parentPath = Path.GetDirectoryName(itemPath);
            var parentItem = GetOrCreateDirItem(parentPath);
            if (parentItem == null)
            {
                MTEUtils.LogWarning("親ディレクトリが見つかりません。" + parentPath);
                return null;
            }

            var itemName = Path.GetFileName(itemPath);

            item = new MenuItem
            {
                itemType = itemType,
                itemName = itemName,
                itemPath = itemPath,
                menu = menu,
                fullPath = menuFilePath,
            };

            parentItem.AddChild(item);
            _itemPathMap[itemPath] = item;
            _itemNameMap[itemName] = item;

            return item;
        }

        private RefMenuItem GetOrCreateRefMenuItem(
            string itemPath,
            MenuItem sourceItem,
            ModItemType itemType)
        {
            if (sourceItem == null)
            {
                return null;
            }

            var item = GetItemByPath<RefMenuItem>(itemPath);
            if (item != null)
            {
                item.sourceItem = sourceItem;
                return item;
            }

            var parentPath = Path.GetDirectoryName(itemPath);
            var parentItem = GetOrCreateDirItem(parentPath);
            if (parentItem == null)
            {
                MTEUtils.LogWarning("親ディレクトリが見つかりません。" + parentPath);
                return null;
            }

            var itemName = Path.GetFileName(itemPath);

            item = new RefMenuItem
            {
                sourceItem = sourceItem,
                itemType = itemType,
                itemName = itemName,
                itemPath = itemPath,
            };

            parentItem.AddChild(item);
            _itemPathMap[itemPath] = item;
            _itemNameMap[itemName] = item;

            return item;
        }

        private ModelMenuItem GetOrCreateModelMenuItem(
            string itemPath,
            MenuInfo menu,
            StudioModelStatWrapper model,
            ModItemType itemType)
        {
            if (menu == null)
            {
                return null;
            }

            var item = GetItemByPath<ModelMenuItem>(itemPath);
            if (item != null)
            {
                item.menu = menu;
                item.model = model;
                return item;
            }

            var parentPath = Path.GetDirectoryName(itemPath);
            var parentItem = GetOrCreateDirItem(parentPath);
            if (parentItem == null)
            {
                MTEUtils.LogWarning("親ディレクトリが見つかりません。" + parentPath);
                return null;
            }

            var itemName = Path.GetFileName(itemPath);

            item = new ModelMenuItem
            {
                menu = menu,
                model = model,
                itemType = itemType,
                itemName = itemName,
                itemPath = itemPath,
            };

            parentItem.AddChild(item);
            _itemPathMap[itemPath] = item;
            _itemNameMap[itemName] = item;

            return item;
        }

        /// <summary>
        /// 配置中の背景オブジェクトのアイテムを取得、なければ作る。
        /// nei が更新されて元の情報が消えていてもアセットバンドル名で表示できるようにする
        /// </summary>
        private ModelBgObjectItem GetOrCreateModelBgObjectItem(
            string itemPath,
            string fileName,
            StudioModelStatWrapper model)
        {
            // nei から消えていても配置は生きているので、アセットバンドル名で表示だけは残す
            var info = GetBgObjectInfo(fileName);
            var name = info?.name ?? Path.GetFileNameWithoutExtension(fileName);

            var item = GetItemByPath<ModelBgObjectItem>(itemPath);
            if (item != null)
            {
                // nei の再読み込みで情報が付いたり消えたりするため、表示名も追従させる
                item.info = info;
                item.name = name;
                item.model = model;
                return item;
            }

            var parentPath = Path.GetDirectoryName(itemPath);
            var parentItem = GetOrCreateDirItem(parentPath);
            if (parentItem == null)
            {
                MTEUtils.LogWarning("親ディレクトリが見つかりません。" + parentPath);
                return null;
            }

            var itemName = Path.GetFileName(itemPath);

            item = new ModelBgObjectItem
            {
                name = name,
                itemName = itemName,
                itemPath = itemPath,
                itemType = ModItemType.Model,
                info = info,
                model = model,
            };

            parentItem.AddChild(item);
            _itemPathMap[itemPath] = item;
            _itemNameMap[itemName] = item;

            return item;
        }

        private PresetItem GetOrCreatePresetItem(
            string itemPath,
            string presetFilePath,
            ModItemType itemType)
        {
            if (string.IsNullOrEmpty(presetFilePath))
            {
                return null;
            }

            var item = GetItemByPath<PresetItem>(itemPath);
            if (item != null)
            {
                item.fullPath = presetFilePath;
                return item;
            }

            var parentPath = Path.GetDirectoryName(itemPath);
            var parentItem = GetOrCreateDirItem(parentPath);
            if (parentItem == null)
            {
                MTEUtils.LogWarning("親ディレクトリが見つかりません。" + parentPath);
                return null;
            }

            var itemName = Path.GetFileName(itemPath);
            var maidName = GetPresetMaidName(Path.GetFileName(presetFilePath));

            item = new PresetItem
            {
                name = maidName,
                setumei = itemName,
                itemName = itemName,
                itemPath = itemPath,
                fullPath = presetFilePath,
                itemType = itemType,
            };

            parentItem.AddChild(item);
            _itemPathMap[itemPath] = item;
            _itemNameMap[itemName] = item;

            return item;
        }

        private PresetItem GetOrCreatePresetItem(
            string itemPath,
            TempPreset tempPreset,
            ModItemType itemType)
        {
            if (tempPreset == null || tempPreset.preset == null)
            {
                return null;
            }

            var item = GetItemByPath<PresetItem>(itemPath);
            if (item != null)
            {
                item.preset = tempPreset.preset;
                item.xmlMemory = tempPreset.xmlMemory;
                item.lastWriteAt = tempPreset.lastWriteAt;
                return item;
            }

            var parentPath = Path.GetDirectoryName(itemPath);
            var parentItem = GetOrCreateDirItem(parentPath);
            if (parentItem == null)
            {
                MTEUtils.LogWarning("親ディレクトリが見つかりません。" + parentPath);
                return null;
            }

            var itemName = Path.GetFileName(itemPath);
            var maidName = GetPresetMaidName(Path.GetFileName(itemPath));

            item = new PresetItem
            {
                name = maidName,
                setumei = itemName,
                itemName = itemName,
                itemPath = itemPath,
                preset = tempPreset.preset,
                xmlMemory = tempPreset.xmlMemory,
                lastWriteAt = tempPreset.lastWriteAt,
                itemType = itemType,
            };

            parentItem.AddChild(item);
            _itemPathMap[itemPath] = item;
            _itemNameMap[itemName] = item;

            return item;
        }

        private AnmItem GetOrCreateAnmItem(
            string itemPath,
            string fullPath,
            string name,
            ModItemType itemType)
        {
            if (string.IsNullOrEmpty(itemPath))
            {
                return null;
            }

            var item = GetItemByPath<AnmItem>(itemPath);
            if (item != null)
            {
                return item;
            }

            var parentPath = Path.GetDirectoryName(itemPath);
            var parentItem = GetOrCreateDirItem(parentPath);
            if (parentItem == null)
            {
                MTEUtils.LogWarning("親ディレクトリが見つかりません。" + parentPath);
                return null;
            }

            var itemName = Path.GetFileName(itemPath);

            item = new AnmItem
            {
                name = name,
                itemName = itemName,
                itemPath = itemPath,
                itemType = itemType,
                fullPath = fullPath,
            };

            parentItem.AddChild(item);
            _itemPathMap[itemPath] = item;
            _itemNameMap[itemName] = item;

            return item;
        }

        private BgObjectItem GetOrCreateBgObjectItem(string itemPath, BgObjectInfo info)
        {
            if (string.IsNullOrEmpty(itemPath) || info == null)
            {
                return null;
            }

            var item = GetItemByPath<BgObjectItem>(itemPath);
            if (item != null)
            {
                item.info = info;
                return item;
            }

            var parentPath = Path.GetDirectoryName(itemPath);
            var parentItem = GetOrCreateDirItem(parentPath);
            if (parentItem == null)
            {
                MTEUtils.LogWarning("親ディレクトリが見つかりません。" + parentPath);
                return null;
            }

            var itemName = Path.GetFileName(itemPath);

            item = new BgObjectItem
            {
                name = info.name,
                itemName = itemName,
                itemPath = itemPath,
                itemType = ModItemType.BgObject,
                // ValidateItemFile が File.Exists で生存確認するため、
                // フォルダではなく nei ファイル自体のパスを入れる
                fullPath = info.neiFilePath,
                info = info,
            };

            parentItem.AddChild(item);
            _itemPathMap[itemPath] = item;
            _itemNameMap[itemName] = item;

            return item;
        }

        private long GetLastWriteAt(string path)
        {
            return File.GetLastWriteTime(path).Ticks;
        }

        private string GetRelativePath(string basePath, string fullPath)
        {
            if (basePath.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                basePath = basePath.Substring(0, basePath.Length - 1);
            }

            if (fullPath.StartsWith(basePath))
            {
                return fullPath.Substring(basePath.Length + 1);
            }

            return fullPath;
        }

        public void RemoveItem(ModItemBase item)
        {
            if (item == null)
            {
                return;
            }

            //MTEUtils.LogDebug("[ModMenuItemManager] RemoveItem itemPath={0} itemName={1}", item.itemPath, item.itemName);

            if (item.children != null)
            {
                var children = item.children.ToList();
                foreach (var child in children)
                {
                    RemoveItem(child as ModItemBase);
                }

                item.children.Clear();
            }

            item.RemoveFromParent();
            _itemNameMap.Remove(item.itemName);
            _itemPathMap.Remove(item.itemPath);
        }

        public void RemoveItemChildren(ModItemBase item)
        {
            if (item == null || item.children == null)
            {
                return;
            }

            var children = item.children.ToList();
            foreach (var child in children)
            {
                RemoveItem(child as ModItemBase);
            }
        }

        public void ResetFlatView()
        {
            foreach (var item in _itemPathMap.Values)
            {
                if (!item.isDir)
                {
                    continue;
                }

                var dirItem = item as DirItem;
                if (dirItem == null)
                {
                    continue;
                }

                dirItem.ResetFlatView();
            }
        }

        public void ThumShot(Maid maid)
        {
            GameMain.Instance.StartCoroutine(ThumShotInternal(maid));
        }

        public IEnumerator ThumShotInternal(Maid maid)
        {
            if (maid == null)
            {
                yield break;
            }

            if (isLoading)
            {
                yield break;
            }

            isLoading = true;

            maid.body0.SetMaskMode(TBody.MaskMode.None);
            maid.status.UpdateBodyParam();
            GameMain.Instance.SysDlg.Close();
            windowManager.isExternalUIInputBlocked = true;

            var savedLookTarget = maid.body0.trsLookTarget;
            // 表情編集中などでまばたきを止めているケースがあるため、決め打ちで true に戻さない
            var savedMabataki = maid.boMabataki;

            maid.ThumShotCamMove();
            maid.body0.trsLookTarget = GameMain.Instance.ThumCamera.transform;
            maid.boMabataki = false;

            for (int nF = 0; nF < 60; nF++)
            {
                yield return null;
            }

            GameMain.Instance.SoundMgr.PlaySe("SE022.ogg", false);
            maid.ThumShot();
            maid.boMabataki = savedMabataki;
            maid.body0.trsLookTarget = savedLookTarget;
            windowManager.isExternalUIInputBlocked = false;

            isLoading = false;
        }

        public override void OnChangedSceneLevel(Scene scene, LoadSceneMode sceneMode)
        {
            // シーンをまたぐと配置モデルは無効になるため、UIの表示状態に関わらず自前配置分を破棄する
            modelPlacerManager.DeleteAllSelfModels();
            UpdateModelItems();

            if (plugin.isEnable)
            {
                return;
            }

            if (_menuMap.Count > 0 && !isLoading)
            {
                ResetItems();
            }
        }

        public void UpdateAnimationLayerInfos()
        {
            if (currentMaid == null || currentMaid.body0 == null)
            {
                return;
            }

            if (maidManagerWrapper.IsValid())
            {
                var maidCaches = maidManagerWrapper.maidCaches;
                var maidCache = maidCaches.FirstOrDefault(x => x.maid == currentMaid);
                if (maidCache != null)
                {
                    animationLayerInfos = maidCache.animationLayerInfos;
                }
            }

            for (int i = 0; i <= MaxLayerIndex; i++)
            {
                animationStates[i] = null;
            }

            var animation = currentMaid.GetAnimation();

            foreach (AnimationState state in animation)
            {
                if (state == null)
                {
                    continue;
                }

                if (state.layer > 0 && state.enabled && state.layer < animationStates.Count)
                {
                    animationStates[state.layer] = state;
                }
            }

            // レイヤー0は直取得
            animationStates[0] = currentMaid.body0.GetAnist();

            for (int i = 0; i <= MaxLayerIndex; i++)
            {
                var info = animationLayerInfos.GetOrDefault(i);
                if (info == null)
                {
                    continue;
                }

                var state = animationStates.GetOrDefault(i);
                if (state != info.state)
                {
                    info.anmName = state != null ? state.name : "";
                    info.state = state;
                    info.ApplyToObject();
                }
            }
        }
    }
}