using System.Collections.Generic;
using System.IO;
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

        protected static ModItemManager modItemManager => ModItemManager.instance;
        protected static TextureManager textureManager => TextureManager.instance;
        protected static MaidPresetManager maidPresetManager => MaidPresetManager.instance;
        protected static Config config => ConfigManager.instance.config;
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
            get => menu != null ? MaidPartUtils.GetMaidPartJpName(menu.maidPartType) : string.Empty;
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
    }

    public class RefMenuItem : MenuItem
    {
        public MenuItem sourceItem { get; set; }

        public override string name
        {
            get => sourceItem?.name;
            set => sourceItem.name = value;
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

    public class ModelMenuItem : MenuItem
    {
        public StudioModelStatWrapper model { get; set; }

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
                    _preset = maidPresetManager.GetPreset(fullPath);
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
}