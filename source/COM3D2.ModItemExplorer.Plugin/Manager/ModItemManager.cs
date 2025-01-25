using System;
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
            get
            {
                if (colorMenuList == null || colorMenuList.Count == 0)
                {
                    return null;
                }
                return colorMenuList[index % colorMenuList.Count];
            }
            set
            {
                if (colorMenuList == null || colorMenuList.Count == 0)
                {
                    return;
                }
                index = colorMenuList.IndexOf(value);
                index = Mathf.Clamp(index, 0, colorMenuList.Count - 1);
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

        public bool isLoading { get; private set; }
        public int officialMenuLoadedIndex { get; private set; }
        public int officialMenuTotalCount { get; private set; }
        public int modMenuLoadedIndex { get; private set; }
        public int modMenuTotalCount { get; private set; }
        public Maid currentMaid { get; private set; }

        public enum LoadState
        {
            None,
            LoadOfficialNameCsv,
            LoadOfficialItems,
            LoadModItems,
            UpdatePresetItems,
            CollectVariationMenu,
            CollectColorSetMenu,
        }

        public LoadState loadState { get; private set; }

        public static readonly int menuCapacity = 1024 * 16;

        private Dictionary<string, MenuInfo> _menuMap = new Dictionary<string, MenuInfo>(menuCapacity);
        private Dictionary<string, ModItemBase> _itemPathMap = new Dictionary<string, ModItemBase>(menuCapacity);
        private Dictionary<string, ModItemBase> _itemNameMap = new Dictionary<string, ModItemBase>(menuCapacity);
        private List<string> _officialMenuFileNameList = new List<string>(menuCapacity);

        private ModItemManager()
        {
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

            LoadOfficialMenuFileNameList();

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

                    LoadOfficialItems();
                    LoadModItems("*.menu");
                    LoadModItems("mod_*.mod");
                    UpdateModPresetItems();
                    ValidateItemChildren(modRootItem);
                    SortItemChildren(modRootItem);
                    SaveMenuCache();

                    UpdatePresetItems();
                    UpdateTempPresetItems();

                    CollectVariationMenu();
                    CollectColorSetMenu();
                    UpdateEquippedItems();
                    UpdateModelItems();
                    UpdateSearchItems();
                    UpdateFavoriteItems();
                    ResetFlatView();

                    MTEUtils.EnqueueAction(() =>
                    {
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
            return _itemNameMap.GetOrDefault(name.ToLower()) as T;
        }

        public void ApplyItem(ModItemBase item)
        {
            if (item == null)
            {
                return;
            }

            if (item is MenuItem menuItem)
            {
                ApplyMenuItem(menuItem);
            }
            else if (item is PresetItem presetItem)
            {
                ApplyPresetItem(presetItem);
            }
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

        public void ApplyMenuItem(MenuItem item)
        {
            if (currentMaid == null || item == null || item.menu == null)
            {
                return;
            }

            var menu = item.menu;
            if (item.menuList?.Count > 0 && item.variationNumber > 0)
            {
                menu = item.menuList[item.variationNumber % item.menuList.Count];
            }

            currentMaid.SetProp(menu.mpn, menu.fileName, menu.rid, false, false);

            if (item.colorSet != null)
            {
                ApplyColorSet(item.colorSet);
            }

            currentMaid.AllProcPropSeqStart();

            UpdateEquippedItem(menu.maidPartType);
        }

        public void ApplyColorSet(ColorSetInfo colorSet)
        {
            if (currentMaid == null || colorSet == null || colorSet.selectedMenu == null)
            {
                return;
            }

            var menu = colorSet.selectedMenu;
            MTEUtils.LogDebug("[ModMenuItemManager] ApplyColorSet {0} {1}", colorSet.colorSetMPN, menu.fileName);
            currentMaid.SetProp(colorSet.colorSetMPN, menu.fileName, menu.rid, false, false);
            currentMaid.AllProcPropSeqStart();
        }

        public void ApplyPresetItem(PresetItem item)
        {
            if (currentMaid == null || item == null || item.preset == null)
            {
                return;
            }

            if (item.itemPath.StartsWith(PresetDirName))
            {
                maidPresetManager.ApplyPreset(currentMaid, item.preset);
            }
            else
            {
                maidPresetManager.ApplyPreset(currentMaid, item.preset, item.fullPath, item.xmlMemory);
            }
        }

        public void CreateModel(MenuItem item, string pluginName)
        {
            if (item == null || item.menu == null || string.IsNullOrEmpty(pluginName))
            {
                return;
            }

            try
            {
                var group = 0;
                var modelList = modelHackManager.modelList;
                var menu = item.menuList[item.variationNumber % item.menuList.Count];

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

                modelHackManager.CreateModel(
                    menu.fileName,
                    menu.fileName,
                    group,
                    pluginName,
                    true);

                UpdateModelItems();
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }
        }

        public void DelItem(MenuItem item)
        {
            if (item == null || item.menu == null)
            {
                return;
            }

            if (item.itemType == ModItemType.Equipped)
            {
                if (currentMaid != null)
                {
                    currentMaid.DelProp(item.menu.mpn);
                    currentMaid.AllProcPropSeqStart();

                    RemoveItem(item);
                }
            }
            else if (item.itemType == ModItemType.Official
                || item.itemType == ModItemType.Mod)
            {
                if (currentMaid != null)
                {
                    currentMaid.DelProp(item.menu.mpn);
                    currentMaid.AllProcPropSeqStart();
                }
            }
            else if (item.itemType == ModItemType.Model)
            {
                try
                {
                    var modelItem = item as ModelMenuItem;
                    modelHackManager.DeleteModel(modelItem?.model);
                }
                catch (Exception e)
                {
                    MTEUtils.LogException(e);
                }
                RemoveItem(item);
            }
        }

        public void SetCurrentMaid(Maid maid)
        {
            if (maid == currentMaid)
            {
                return;
            }

            currentMaid = maid;
            UpdateEquippedItems();
        }

        public bool IsEquippedItem(MenuItem item)
        {
            if (currentMaid == null || item == null || item.menu == null)
            {
                return false;
            }

            if (!MaidPartUtils.IsEquippableType(item.maidPartType))
            {
                return false;
            }

            var strFileName = currentMaid.GetProp(item.menu.mpn)?.strFileName;
            if (string.IsNullOrEmpty(strFileName))
            {
                return false;
            }

            var equippedMenu = GetMenu(strFileName);
            if (equippedMenu == null)
            {
                return false;
            }

            if (item.menu == equippedMenu)
            {
                return true;
            }

            if (item.menu.fileName == equippedMenu.variationBaseFileName)
            {
                return true;
            }

            return false;
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
                return GetItemByName<MenuItem>(menu.variationBaseFileName);
            }

            return GetItemByName<MenuItem>(prop.strFileName);
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
            try
            {
                using (var binaryWriter = new BinaryWriter(File.OpenWrite(PluginUtils.MenuCachePath)))
                {
                    binaryWriter.Write(MenuInfo.CacheVersion);
                    foreach (var menu in _menuMap.Values)
                    {
                        menu?.Serialize(binaryWriter);
                    }
                }
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }
        }

        public void ResetItems()
        {
            _menuMap.Clear();
            _officialMenuFileNameList.Clear();

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

        private Dictionary<string, string> _officialNameMap = new Dictionary<string, string>(128);

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

        private void LoadOfficialItems()
        {
            MTEUtils.LogDebug("[ModMenuItemManager] LoadOfficialItems");
            loadState = LoadState.LoadOfficialItems;

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

            ValidateItemChildren(officialRootItem);
            SortItemChildren(officialRootItem);
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
                return MTEUtils.CombinePaths(OfficialDirName, menu.maidPartType.ToString(), menu.fileName);
            }
            return MTEUtils.CombinePaths(OfficialDirName, menu.path);
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

                    var itemPath = GetModItemPath(menu, GetRelativePath(MTEUtils.ModDirPath, menuFilePath));
                    GetOrCreateMenuItem(itemPath, menu, ModItemType.Mod, menuFilePath);
                }
                catch (Exception e)
                {
                    MTEUtils.LogException(e);
                }
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

            var duplicatedModMap = new Dictionary<string, List<string>>(menuFilePaths.Length);
            foreach (var menuFilePath in menuFilePaths)
            {
                duplicatedModMap.GetOrCreate(Path.GetFileName(menuFilePath).ToLower()).Add(menuFilePath);
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
                return MTEUtils.CombinePaths(ModDirName, menu.maidPartType.ToString(), menu.fileName);
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
                        var preset = tempPreset.preset;
                        var xmlMemory = tempPreset.xmlMemory;
                        var itemPath = MTEUtils.CombinePaths(TempPresetDirName, maidName, preset.strFileName);
                        GetOrCreatePresetItem(itemPath, preset, ModItemType.TempPreset, xmlMemory);
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

        private MenuInfo GetMenu(string menuFileName)
        {
            if (string.IsNullOrEmpty(menuFileName))
            {
                return null;
            }
            return _menuMap.GetOrDefault(menuFileName.ToLower());
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
                _menuMap[menuFileName.ToLower()] = menu;
            }
            return menu;
        }

        private MenuInfo GetOrLoadModMenu(string menuFilePath, long lastWriteAt)
        {
            var menuFileName = Path.GetFileName(menuFilePath).ToLower();
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

                if (item is ModelMenuItem modelItem)
                {
                    if (modelItem.model.obj == null)
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

            var menuList = _menuMap.Values.ToList();
            foreach (var menu in menuList)
            {
                try
                {
                    if (string.IsNullOrEmpty(menu.variationBaseFileName))
                    {
                        continue;
                    }

                    var baseItem = GetItemByName<MenuItem>(menu.variationBaseFileName);
                    if (baseItem != null)
                    {
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
            foreach (var colorSet in colorSetList)
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
            }
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

        private static int CompareItem(ITileViewContent a, ITileViewContent b)
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
            return "Equipped_" + maidPartType.ToString();
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
                if (item == null)
                {
                    continue;
                }

                if (pattern.IsMatch(item.name))
                {
                    searchRootItem.AddChild(item);
                    continue;
                }

                if (pattern.IsMatch(item.itemName))
                {
                    searchRootItem.AddChild(item);
                    continue;
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

        public void UpdateModelItems()
        {
            MTEUtils.LogDebug("[ModMenuItemManager] UpdateModelItems");

            if (!modelHackManager.IsValid())
            {
                return;
            }

            var modelList = modelHackManager.modelList;
            foreach (var model in modelList)
            {
                UpdateModelItem(model);
            }

            ValidateItemChildren(modelRootItem);
            SortItemChildren(modelRootItem);
        }

        private string GetModelItemName(StudioModelStatWrapper model)
        {
            return "Model_" + model.name;
        }

        public void UpdateModelItem(StudioModelStatWrapper model)
        {
            try
            {
                var itemName = GetModelItemName(model);
                var itemPath = MTEUtils.CombinePaths(ModelDirName, itemName);
                var fileName = model.infoWrapper?.fileName;

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
                        maidPartType = MaidPartUtils.GetMaidPartType(itemName),
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
                item.name = menu.name;
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

            var itemName = Path.GetFileName(itemPath).ToLower();

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

            var itemName = Path.GetFileName(itemPath).ToLower();

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

            var itemName = Path.GetFileName(itemPath).ToLower();

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

            var itemName = Path.GetFileName(itemPath).ToLower();
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
            CharacterMgr.Preset preset,
            ModItemType itemType,
            XmlDocument xmlMemory = null)
        {
            if (preset == null)
            {
                return null;
            }

            var item = GetItemByPath<PresetItem>(itemPath);
            if (item != null)
            {
                item.preset = preset;
                return item;
            }

            var parentPath = Path.GetDirectoryName(itemPath);
            var parentItem = GetOrCreateDirItem(parentPath);
            if (parentItem == null)
            {
                MTEUtils.LogWarning("親ディレクトリが見つかりません。" + parentPath);
                return null;
            }

            var itemName = Path.GetFileName(itemPath).ToLower();
            var maidName = GetPresetMaidName(Path.GetFileName(itemPath));

            item = new PresetItem
            {
                name = maidName,
                setumei = itemName,
                itemName = itemName,
                itemPath = itemPath,
                preset = preset,
                xmlMemory = xmlMemory,
                itemType = itemType,
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

        public override void OnChangedSceneLevel(Scene scene, LoadSceneMode sceneMode)
        {
            if (plugin.isEnable)
            {
                return;
            }

            if (_menuMap.Count > 0 && !isLoading)
            {
                ResetItems();
            }
        }
    }
}