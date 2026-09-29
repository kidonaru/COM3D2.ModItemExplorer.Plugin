using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.ModItemExplorer.Plugin
{
    public enum ModItemType
    {
        Dir,
        Official,
        Mod,
        Equipped,
        Preset,
        TempPreset,
        Model,
        Anm,
        BgObject,
    }

    public abstract class ModItemBase : TileViewContentBase
    {
        public virtual ModItemType itemType { get; set; }

        public override bool isDir
        {
            get => itemType == ModItemType.Dir;
        }

        public override bool canFavorite { get; set; } = true;

        public override bool isFavorite
        {
            get => config.IsFavoriteItemPath(itemPath);
            set
            {
                config.SetFavoriteItemPath(itemPath, value);
                MTEUtils.EnqueueAction(() => modItemManager.UpdateFavoriteItems());
            }
        }

        public string itemName { get; set; }
        public string itemPath { get; set; }
        public virtual string fullPath { get; set; }
        public virtual MaidPartType maidPartType { get; set; }
        public virtual float priority { get; set; }
        public virtual long lastWriteAt { get; set; }

        protected static ModItemManager modItemManager => ModItemManager.instance;
        protected static ModelPlacerManager modelPlacerManager => ModelPlacerManager.instance;
        protected static TextureManager textureManager => TextureManager.instance;
        protected static MaidPresetManager maidPresetManager => MaidPresetManager.instance;
        protected static Config config => ConfigManager.instance.config;

        public virtual bool IsMatch(Regex pattern)
        {
            if (!string.IsNullOrEmpty(this.name) && pattern.IsMatch(this.name))
            {
                return true;
            }

            if (!string.IsNullOrEmpty(this.itemName) && pattern.IsMatch(this.itemName))
            {
                return true;
            }

            return false;
        }
    }

    public class MenuItem : ModItemBase
    {
        public override string name
        {
            get => menu?.name ?? string.Empty;
        }

        public override string setumei
        {
            get => menu?.setumei ?? string.Empty;
        }

        public override string tag
        {
            get => menu != null ? menu.maidPartType.ToJpName() : string.Empty;
        }

        public override Color tagColor
        {
            get => menu != null ? MaidPartUtils.GetMaidPartColor(menu.maidPartType, config.tagBGAlpha) : Color.gray;
        }

        public override bool isSelected
        {
            get => modItemManager.IsEquippedItem(this);
        }

        public override bool canDelete
        {
            get => isSelected;
        }

        public override Texture2D thum
        {
            get
            {
                if (_thum != null)
                {
                    return _thum;
                }

                if (!string.IsNullOrEmpty(menu?.iconName))
                {
                    _thum = textureManager.GetTexture(menu.iconName, menu.iconData);
                    return _thum;
                }

                return null;
            }
            set => _thum = value;
        }

        public override MaidPartType maidPartType => menu?.maidPartType ?? MaidPartType.null_mpn;
        public override float priority => menu?.priority ?? 0f;
        public override long lastWriteAt => menu?.lastWriteAt ?? 0;

        public virtual List<MenuInfo> menuList { get; set; }
        public virtual int variationNumber { get; set; }
        public virtual ColorSetInfo colorSet { get; set; }
        public virtual Vector2 scrollPosition { get; set; }

        public MenuInfo menu
        {
            get
            {
                if (menuList != null && menuList.Count > 0)
                {
                    return menuList[0];
                }

                return null;
            }
            set
            {
                if (menuList == null)
                {
                    menuList = new List<MenuInfo>();
                }

                if (menuList.Count > 0)
                {
                    menuList[0] = value;
                }
                else
                {
                    menuList.Add(value);
                }

                nameHeight = -1f;
            }
        }

        public MenuInfo variationMenu
        {
            get => menuList?.GetOrDefault(variationNumber);
            set
            {
                if (menuList != null && menuList.Count > 0)
                {
                    variationNumber = menuList.IndexOf(value);
                    variationNumber = Mathf.Clamp(variationNumber, 0, menuList.Count - 1);
                }
            }
        }

        public void AddMenu(MenuInfo menu)
        {
            if (menuList == null)
            {
                menuList = new List<MenuInfo>();
            }

            if (!menuList.Contains(menu))
            {
                menuList.Add(menu);
            }
        }

        public override bool IsMatch(Regex pattern)
        {
            foreach (var menu in menuList)
            {
                if (menu == null)
                {
                    continue;
                }
                if (!string.IsNullOrEmpty(menu.name) && pattern.IsMatch(menu.name))
                {
                    return true;
                }
                if (!string.IsNullOrEmpty(menu.fileName) && pattern.IsMatch(menu.fileName))
                {
                    return true;
                }
                if (config.setumeiSerch && !string.IsNullOrEmpty(menu.setumei) && pattern.IsMatch(menu.setumei))
                {
                    return true;
                }
            }

            return false;
        }
    }

    public class RefMenuItem : MenuItem
    {
        public MenuItem sourceItem { get; set; }

        public override string name
        {
            get => sourceItem?.name;
            set => sourceItem.name = value;
        }

        public override float nameHeight
        {
            get => sourceItem?.nameHeight ?? -1f;
            set => sourceItem.nameHeight = value;
        }

        public override string fullPath
        {
            get => sourceItem?.fullPath;
        }

        public override Texture2D thum
        {
            get => sourceItem?.thum;
            set => sourceItem.thum = value;
        }

        public override List<MenuInfo> menuList
        {
            get => sourceItem?.menuList;
            set => sourceItem.menuList = value;
        }

        public override int variationNumber
        {
            get => sourceItem?.variationNumber ?? 0;
            set => sourceItem.variationNumber = value;
        }

        public override ColorSetInfo colorSet
        {
            get => sourceItem?.colorSet;
            set => sourceItem.colorSet = value;
        }

        public override bool canFavorite
        {
            get => sourceItem?.canFavorite ?? false;
        }

        public override bool isFavorite
        {
            get => sourceItem?.isFavorite ?? false;
            set => sourceItem.isFavorite = value;
        }
    }

    /// <summary>
    /// 「配置中」に並ぶアイテム。menu 由来 (ModelMenuItem) と
    /// 背景オブジェクト由来 (ModelBgObjectItem) で継承元が違うため、参照はこれで揃える
    /// </summary>
    public interface IModelItem
    {
        StudioModelStatWrapper model { get; set; }
    }

    public class ModelMenuItem : MenuItem, IModelItem
    {
        public StudioModelStatWrapper model { get; set; }

        /// <summary>
        /// 選択状態はモデル操作ウィンドウ・ギズモと共有する。
        /// 着用中かどうかを見る MenuItem の実装は「配置中」では意味を持たないため差し替える
        /// </summary>
        public override bool isSelected => modelPlacerManager.IsSelected(model);

        /// <summary>
        /// menu 名そのままではなく連番付きの表示名を持たせるため、
        /// MenuItem の menu 参照ではなく格納した値を返す
        /// </summary>
        public override string name { get; set; }

        public override bool canDelete => true;
        public override bool canFavorite => false;
    }

    /// <summary>
    /// フォトモードのハンドアイテム。menu は普通の MenuInfo だが、装着先 MPN と
    /// 一時装備で入る点がフォトモードに従うため、装着判定と削除可否を差し替える
    /// </summary>
    public class HandMenuItem : MenuItem
    {
        /// <summary>CRC ボディでは Maid.SetPropIn が対応版へ差し替えるときにこの接頭辞を付ける</summary>
        private const string CrcReplacementPrefix = "crx_";

        /// <summary>各カテゴリの「アイテムなし」を示す menu 名の目印。PhotoMaidItemData の init_item 判定と同じ</summary>
        private const string RemoveItemMarker = "_del";

        public HandItemInfo handItemInfo { get; set; }

        /// <summary>装着中のものを外すための「アイテムなし」行か。モデルを持たないため配置できない</summary>
        public bool isRemoveItem =>
            handItemInfo?.menuFileName?.IndexOf(RemoveItemMarker, StringComparison.OrdinalIgnoreCase) >= 0;

        public override string name => handItemInfo?.name ?? base.name;

        public override string tag => handItemInfo?.category ?? base.tag;

        /// <summary>一時装備は strTempFileName に入るため、そちらで着用中を判定する</summary>
        public override bool isSelected
        {
            get
            {
                var maid = modItemManager.currentMaid;
                if (maid == null || handItemInfo == null)
                {
                    return false;
                }

                var prop = maid.GetProp(handItemInfo.mpn);
                var tempFileName = prop?.strTempFileName;
                if (string.IsNullOrEmpty(tempFileName))
                {
                    return false;
                }

                if (tempFileName.StartsWith(CrcReplacementPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    tempFileName = tempFileName.Substring(CrcReplacementPrefix.Length);
                }

                return string.Equals(
                    tempFileName, handItemInfo.menuFileName, StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>外すのは同カテゴリの「アイテムなし」行で行う (既存の削除は通常装備の DelProp 前提)</summary>
        public override bool canDelete => false;

        /// <summary>画面に出す nei の名前でも検索に当たるようにする</summary>
        public override bool IsMatch(Regex pattern)
        {
            if (handItemInfo != null && !string.IsNullOrEmpty(handItemInfo.name)
                && pattern.IsMatch(handItemInfo.name))
            {
                return true;
            }
            return base.IsMatch(pattern);
        }
    }

    /// <summary>配置中の背景オブジェクト 1 体。表示は元の BgObjectItem と同じ扱いにする</summary>
    public class ModelBgObjectItem : BgObjectItem, IModelItem
    {
        public StudioModelStatWrapper model { get; set; }

        /// <summary>選択状態の扱いは ModelMenuItem と同じ</summary>
        public override bool isSelected => modelPlacerManager.IsSelected(model);

        public override bool canDelete => true;
        public override bool canFavorite => false;
    }

    public class DirItem : ModItemBase
    {
        public override Texture2D thum
        {
            get
            {
                if (children != null && children.Count > 0)
                {
                    return children[0].thum;
                }
                return null;
            }
            set => _thum = value;
        }

        public override MaidPartType maidPartType { get; set; }
        public bool isExpanded { get; set; }
        public Vector2 scrollPosition { get; set; }
        public Vector2 scrollContentSize { get; set; }

        public bool canFlatView
        {
            get => this != modItemManager.searchRootItem &&
                    this != modItemManager.rootItem &&
                    this != modItemManager.favoriteRootItem &&
                    GetDirCount(false) > 0;
        }

        private bool? _isFlatView = null;
        public bool isFlatView
        {
            get
            {
                if (_isFlatView == null)
                {
                    _isFlatView = canFlatView && GetFileCount(true) <= config.flatViewItemCount;
                }
                return _isFlatView.Value;
            }
            set
            {
                _isFlatView = value;
            }
        }

        public void ResetFlatView()
        {
            _isFlatView = null;
        }
    }

    public class TempDirItem : DirItem
    {
        public override void AddChild(ITileViewContent child)
        {
            if (children == null)
            {
                children = new List<ITileViewContent>(16);
            }

            children.Add(child);
        }

        public override void RemoveChild(ITileViewContent child)
        {
            if (children != null)
            {
                children.Remove(child);
            }
        }

        public override void RemoveAllChildren()
        {
            if (children != null)
            {
                children.Clear();
            }
        }
    }

    public class PresetItem : ModItemBase
    {
        private static readonly Dictionary<CharacterMgr.PresetType, Color> _presetTypeColor = new Dictionary<CharacterMgr.PresetType, Color>
        {
            { CharacterMgr.PresetType.Wear, new Color(0.2f, 0.4f, 0.7f) },
            { CharacterMgr.PresetType.Body, new Color(0.5f, 0.3f, 0.5f) },
            { CharacterMgr.PresetType.All, new Color(0.8f, 0.5f, 0.2f) },
        };

        private static Color GetPresetTypeColor(CharacterMgr.Preset preset)
        {
            var color = Color.gray;
            if (preset != null)
            {
                color = _presetTypeColor.GetOrDefault(preset.ePreType, Color.gray);
            }

            color.a = config.tagBGAlpha;
            return color;
        }

        public override string tag
        {
            get => preset != null ? MTEUtils.GetPresetTypeName(preset.ePreType) : "";
        }

        public override Color tagColor
        {
            get => GetPresetTypeColor(preset);
        }

        public override bool canFavorite
        {
            get => itemType == ModItemType.Preset;
        }

        private CharacterMgr.Preset _preset = null;
        public CharacterMgr.Preset preset
        {
            get
            {
                if (_preset == null)
                {
                    var presetData = maidPresetManager.GetOrLoadPreset(fullPath);
                    if (presetData != null)
                    {
                        _preset = presetData.preset;
                        lastWriteAt = presetData.lastWriteAt;
                    }
                }
                return _preset;
            }
            set => _preset = value;
        }

        public XmlDocument xmlMemory { get; set; }

        public override Texture2D thum
        {
            get
            {
                if (_thum != null)
                {
                    return _thum;
                }

                var preset = this.preset;
                if (preset != null)
                {
                    _thum = preset.texThum;
                    return _thum;
                }

                return null;
            }
            set => _thum = value;
        }
    }

    public class AnmItem : ModItemBase
    {
        public override string tag => "アニメ";

        public override Color tagColor =>
            new Color(0.4f, 0.4f, 0.4f, config.tagBGAlpha);

        public override bool canFavorite => true;

        public override Texture2D thum
        {
            get
            {
                if (_thum != null)
                {
                    return _thum;
                }

                _thum = textureManager.GetTexture("cm3d2_poseicon01.tex");
                return _thum;
            }
            set => _thum = value;
        }
    }
}