using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.ModItemExplorer.Plugin
{
    /// <summary>選択履歴の保存フォーマット。件数が多くなるため設定XMLとは別ファイルに置く</summary>
    [XmlRoot("ItemHistory")]
    public class ItemHistory
    {
        public static readonly int CurrentVersion = 1;

        [XmlAttribute]
        public int version = 0;

        /// <summary>適用したアイテムのパス。新しい順</summary>
        [XmlArray("itemPaths")]
        [XmlArrayItem("value")]
        public List<string> itemPaths = new List<string>();
    }

    /// <summary>
    /// 適用したアイテムの選択履歴を保持する。
    /// undo/redo の操作履歴（HistoryClient）とは無関係で、こちらは一覧表示用の記録
    /// </summary>
    public class ItemHistoryManager : ManagerBase
    {
        private ItemHistory _history = new ItemHistory();

        private static ItemHistoryManager _instance;
        public static ItemHistoryManager instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new ItemHistoryManager();
                }
                return _instance;
            }
        }

        /// <summary>新しい順のアイテムパス一覧</summary>
        public List<string> itemPaths => _history.itemPaths;

        public bool dirty { get; set; }

        private static Config config => ConfigManager.instance.config;

        private ItemHistoryManager()
        {
        }

        public override void Init()
        {
            Load();
        }

        public override void Update()
        {
            if (dirty && Input.GetMouseButtonUp(0))
            {
                Save();
            }
        }

        /// <summary>
        /// 履歴の先頭へ追加する。既に含まれるパスは重複させず先頭へ移動する。
        /// 追加によって上限を超えた分は古い方から捨てる
        /// </summary>
        public void Add(string itemPath)
        {
            if (string.IsNullOrEmpty(itemPath))
            {
                return;
            }

            _history.itemPaths.Remove(itemPath);
            _history.itemPaths.Insert(0, itemPath);

            Trim();
            dirty = true;
        }

        public void Clear()
        {
            if (_history.itemPaths.Count == 0)
            {
                return;
            }

            _history.itemPaths.Clear();
            dirty = true;
        }

        /// <summary>上限を超えた古い履歴を捨てる。上限設定を下げた直後にも呼ぶ</summary>
        public void Trim()
        {
            var maxCount = Mathf.Max(config.maxItemHistoryCount, 0);
            if (_history.itemPaths.Count <= maxCount)
            {
                return;
            }

            _history.itemPaths.RemoveRange(maxCount, _history.itemPaths.Count - maxCount);
            dirty = true;
        }

        public void Load()
        {
            try
            {
                var path = PluginUtils.ItemHistoryPath;
                if (!File.Exists(path))
                {
                    return;
                }

                var serializer = new XmlSerializer(typeof(ItemHistory));
                using (var stream = new FileStream(path, FileMode.Open))
                {
                    _history = (ItemHistory)serializer.Deserialize(stream);
                }

                if (_history.itemPaths == null)
                {
                    _history.itemPaths = new List<string>();
                }
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
                _history = new ItemHistory();
            }
        }

        public void Save()
        {
            MTEUtils.LogDebug("[ItemHistoryManager] 履歴保存中...");

            var path = PluginUtils.ItemHistoryPath;
            var tempPath = path + ".tmp";
            try
            {
                dirty = false;
                _history.version = ItemHistory.CurrentVersion;

                var serializer = new XmlSerializer(typeof(ItemHistory));
                using (var stream = new FileStream(tempPath, FileMode.Create))
                {
                    serializer.Serialize(stream, _history);
                }

                // 書き込み途中で落ちても既存の履歴を壊さないよう、書き上げてから差し替える
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
                File.Move(tempPath, path);
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);

                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch (Exception deleteError)
                {
                    MTEUtils.LogException(deleteError);
                }
            }
        }
    }
}
