using System;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using wf;

namespace COM3D2.ModItemExplorer.Plugin
{
    /// <summary>
    /// フォトモードのハンドアイテム一覧 (phot_maid_item_list.nei) を読む。
    /// 2.5 ではこの nei が旧データ側 (GameUty.FileSystemOld) にしか無く、
    /// ゲームの PhotoMaidItemData.Create() は通常側しか見ずに例外になるため自前で読む
    /// </summary>
    public static class HandItemNeiLoader
    {
        private const string NeiFileName = "phot_maid_item_list.nei";
        private const string EnabledListName = "phot_maid_item_enabled_list";
        private const string MenuExtension = ".menu";

        // 列インデックス。公式 PhotoMaidItemData.Create() の読み取り順に合わせている
        private const int ColumnId = 0;
        private const int ColumnCategory = 1;
        private const int ColumnName = 2;
        private const int ColumnCallName = 3;
        private const int ColumnRequiredPack = 4;

        /// <summary>1 行目はヘッダー行なのでデータはここから</summary>
        private const int FirstDataRow = 1;

        /// <summary>カテゴリ -> 装着先 MPN。PhotoMaidItemData.Create() の対応表と同じ</summary>
        private static readonly Dictionary<string, MPN> CategoryMpnMap = new Dictionary<string, MPN>
        {
            { "右手", MPN.handitem },
            { "左手", MPN.seieki_naka },
            { "上半身", MPN.kousoku_upper },
            { "下半身", MPN.kousoku_lower },
            { "前穴", MPN.accvag },
            { "後穴", MPN.accanl },
        };

        public static List<HandItemInfo> LoadAll()
        {
            var result = new List<HandItemInfo>(64);

            try
            {
                // 通常側を優先する。2.0 は通常側にあり、旧データ側は CM3D2 由来の別物なので混ぜない
                var fileSystem = GameUty.FileSystem;
                var enabledIds = new HashSet<int>();
                CsvCommonIdManager.ReadEnabledIdList(
                    CsvCommonIdManager.FileSystemType.Normal, true, EnabledListName, ref enabledIds);

                if (!fileSystem.IsExistentFile(NeiFileName))
                {
                    fileSystem = GameUty.FileSystemOld;
                    if (fileSystem == null || !fileSystem.IsExistentFile(NeiFileName))
                    {
                        // 2.5 で CM3D2 のデータを連携していない環境では無いのが正常なので警告しない
                        MTEUtils.LogDebug("ハンドアイテム一覧が見つかりません。{0}", NeiFileName);
                        return result;
                    }

                    // 2.5 は旧データ側の有効 ID が本体分、通常側が追加パック分なので両方を合わせる
                    CsvCommonIdManager.ReadEnabledIdList(
                        CsvCommonIdManager.FileSystemType.Old, true, EnabledListName, ref enabledIds);
                }

                using (var file = fileSystem.FileOpen(NeiFileName))
                using (var csvParser = new CsvParser())
                {
                    if (file == null || !csvParser.Open(file))
                    {
                        MTEUtils.LogWarning("ハンドアイテム一覧を開けませんでした。{0}", NeiFileName);
                        return result;
                    }

                    for (var y = FirstDataRow; y < csvParser.max_cell_y; y++)
                    {
                        try
                        {
                            ReadRow(csvParser, y, enabledIds, result);
                        }
                        catch (Exception e)
                        {
                            MTEUtils.LogException(e);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                MTEUtils.LogWarning("ハンドアイテム一覧の読み込みに失敗しました。");
                MTEUtils.LogException(e);
            }

            return result;
        }

        private static void ReadRow(
            CsvParser csvParser, int y, HashSet<int> enabledIds, List<HandItemInfo> result)
        {
            if (!csvParser.IsCellToExistData(ColumnId, y)
                || !enabledIds.Contains(csvParser.GetCellAsInteger(ColumnId, y)))
            {
                return;
            }

            var category = csvParser.GetCellAsString(ColumnCategory, y);
            MPN mpn;
            if (!CategoryMpnMap.TryGetValue(category, out mpn))
            {
                MTEUtils.LogWarning("未対応のハンドアイテムカテゴリです。{0}", category);
                return;
            }

            var callName = csvParser.GetCellAsString(ColumnCallName, y);
            if (string.IsNullOrEmpty(callName))
            {
                return;
            }

            var requiredPack = csvParser.GetCellAsString(ColumnRequiredPack, y);
            if (!string.IsNullOrEmpty(requiredPack) && !PluginData.IsEnabled(requiredPack))
            {
                return;
            }

            result.Add(new HandItemInfo
            {
                category = category,
                name = csvParser.GetCellAsString(ColumnName, y),
                menuFileName = callName + MenuExtension,
                mpn = mpn,
            });
        }
    }
}
