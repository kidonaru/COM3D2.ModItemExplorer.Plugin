using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.ModItemExplorer.Plugin
{
    public enum KeyBindType
    {
        PluginToggle,
        OpenExplorer,
    }

    public enum ItemSortType
    {
        DefaultAsc,
        DefaultDesc,
        NameAsc,
        NameDesc,
        LastWriteAtAsc,
        LastWriteAtDesc,
    }

    public class Config
    {
        public static readonly int CurrentVersion = 1;

        [XmlAttribute]
        public int version = 0;

        // 動作設定
        public bool pluginEnabled = true;
        public float keyRepeatTimeFirst = 0.15f;
        public float keyRepeatTime = 1f / 30f;
        public bool useHSVColor = false;
        public bool groupOfficialItemsByMPN = true;
        public bool groupModItemsByMPN = false;
        public int flatViewItemCount = 32;
        public bool dumpItemInfo = false;
        public bool setumeiSerch = false;
        public ItemSortType itemSortType = ItemSortType.DefaultAsc;

        // 表示設定
        public int windowWidth = 960;
        public int windowHeight = 480;
        public int windowPosX = -1;
        public int windowPosY = -1;
        public int naviWidth = 200;
        public float itemNameBGAlpha = 0.7f;
        public float tagBGAlpha = 0.9f;
        public int colorPaletteWindowPosX = -1;
        public int colorPaletteWindowPosY = -1;
        public int customPartsWindowPosX = -1;
        public int customPartsWindowPosY = -1;
        public float customPartsPositionRange = 1f;
        public bool customPartsAutoEditMode = false;
        public int hairLengthWindowPosX = -1;
        public int hairLengthWindowPosY = -1;

        // 色設定
        public Color windowHoverColor = new Color(48 / 255f, 48 / 255f, 48 / 255f, 224 / 255f);

        // お気に入りアイテム
        [XmlIgnore]
        public HashSet<string> favoriteItemPathSet = new HashSet<string>();

        [XmlArray("favoriteItemPaths")]
        [XmlArrayItem("value")]
        public string[] favoriteItemPathsXml
        {
            get => favoriteItemPathSet.ToArray();
            set
            {
                if (value == null)
                {
                    return;
                }
                favoriteItemPathSet = new HashSet<string>(value);
            }
        }

        [XmlIgnore]
        public Dictionary<KeyBindType, KeyBind> keyBinds = new Dictionary<KeyBindType, KeyBind>
        {
            { KeyBindType.PluginToggle, new KeyBind("Alt+M") },
            { KeyBindType.OpenExplorer, new KeyBind("Shift") },
        };

        public struct KeyBindPair
        {
            public KeyBindType key;
            public string value;
        }

        [XmlElement("keyBind")]
        public KeyBindPair[] keyBindsXml
        {
            get
            {
                var result = new List<KeyBindPair>(keyBinds.Count);
                foreach (var pair in keyBinds)
                {
                    result.Add(new KeyBindPair { key = pair.Key, value = pair.Value.ToString() });
                }
                return result.ToArray();
            }
            set
            {
                if (value == null)
                {
                    return;
                }

                foreach (var pair in value)
                {
                    //PluginUtils.LogDebug("keyBind: " + pair.key + " = " + pair.value);
                    keyBinds[pair.key] = new KeyBind(pair.value);
                }
            }
        }

        [XmlIgnore]
        public bool dirty = false;

        public void ConvertVersion()
        {
            version = CurrentVersion;
        }

        public bool GetKey(KeyBindType keyBindType)
        {
            return keyBinds[keyBindType].GetKey();
        }

        public bool GetKeyDown(KeyBindType keyBindType)
        {
            return keyBinds[keyBindType].GetKeyDown();
        }

        public bool GetKeyDownRepeat(KeyBindType keyBindType)
        {
            return keyBinds[keyBindType].GetKeyDownRepeat(keyRepeatTimeFirst, keyRepeatTime);
        }

        public bool GetKeyUp(KeyBindType keyBindType)
        {
            return keyBinds[keyBindType].GetKeyUp();
        }

        public string GetKeyName(KeyBindType keyBindType)
        {
            return keyBinds[keyBindType].ToString();
        }

        public bool IsFavoriteItemPath(string itemPath)
        {
            if (string.IsNullOrEmpty(itemPath))
            {
                return false;
            }
            return favoriteItemPathSet.Contains(itemPath);
        }

        public void SetFavoriteItemPath(string itemPath, bool isFavorite)
        {
            if (string.IsNullOrEmpty(itemPath))
            {
                return;
            }
            if (isFavorite)
            {
                favoriteItemPathSet.Add(itemPath);
            }
            else
            {
                favoriteItemPathSet.Remove(itemPath);
            }
            dirty = true;
        }

        public void ResetFavoriteItemPath()
        {
            favoriteItemPathSet.Clear();
            dirty = true;
        }
    }
}

