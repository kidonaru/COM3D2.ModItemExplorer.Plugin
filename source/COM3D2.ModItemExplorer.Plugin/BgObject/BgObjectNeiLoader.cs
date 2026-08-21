using System;
using System.Collections.Generic;
using System.IO;
using COM3D2.MotionTimelineEditor;

namespace COM3D2.ModItemExplorer.Plugin
{
    /// <summary>
    /// Mod フォルダの *_photo_bg_object_list.nei を読み、宣言された背景オブジェクトを列挙する。
    /// nei は暗号化されているが、ゲーム内蔵の CsvParser がそのまま復号して読めるため
    /// 自前の復号は持たない。CsvParser はワーカースレッドからでも動く
    /// </summary>
    public static class BgObjectNeiLoader
    {
        /// <summary>MaidLoader 系 MOD が使う nei の命名規約</summary>
        private const string NeiSearchPattern = "*_photo_bg_object_list.nei";

        // 列インデックス。公式 PhotoBGObjectData.Create() の読み取り順に合わせている
        private const int ColumnId = 0;
        private const int ColumnCategory = 1;
        private const int ColumnName = 2;
        private const int ColumnPrefabName = 3;
        private const int ColumnAssetBundleName = 4;
        private const int ColumnRequiredPack = 5;

        /// <summary>1 行目はヘッダー行なのでデータはここから</summary>
        private const int FirstDataRow = 1;

        /// <summary>
        /// Mod フォルダ配下の nei を全て読み、背景オブジェクトの一覧を返す。
        /// 同じアセットバンドル名が複数の nei に現れた場合は先勝ちで 1 件だけ採る
        /// (AssetBundle はバンドル単位でしかロードできず、重複を両方載せると配置時に衝突するため)
        /// </summary>
        public static List<BgObjectInfo> LoadAll()
        {
            var result = new List<BgObjectInfo>(64);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string[] neiFilePaths;
            try
            {
                neiFilePaths = Directory.GetFiles(
                    MTEUtils.ModDirPath, NeiSearchPattern, SearchOption.AllDirectories);
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
                return result;
            }

            foreach (var neiFilePath in neiFilePaths)
            {
                try
                {
                    LoadNei(neiFilePath, result, seen);
                }
                catch (Exception e)
                {
                    MTEUtils.LogWarning("neiの読み込みに失敗しました。{0}", neiFilePath);
                    MTEUtils.LogException(e);
                }
            }

            return result;
        }

        private static void LoadNei(
            string neiFilePath,
            List<BgObjectInfo> result,
            HashSet<string> seen)
        {
            // FileSystemMod はファイル名のみのフラットな索引なので、パスではなく名前で開く
            var neiFileName = Path.GetFileName(neiFilePath);

            using (var file = GameUty.FileSystemMod.FileOpen(neiFileName))
            using (var csvParser = new CsvParser())
            {
                if (file == null || !csvParser.Open(file))
                {
                    MTEUtils.LogWarning("neiを開けませんでした。{0}", neiFilePath);
                    return;
                }

                for (var y = FirstDataRow; y < csvParser.max_cell_y; y++)
                {
                    if (!csvParser.IsCellToExistData(ColumnId, y))
                    {
                        continue;
                    }

                    var name = csvParser.GetCellAsString(ColumnName, y);
                    var assetBundleName = csvParser.GetCellAsString(ColumnAssetBundleName, y);

                    // 内部名(prefab)方式は公式アセット専用で mod からは使えないため、
                    // アセットバンドル名が無い行は扱えない
                    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(assetBundleName))
                    {
                        continue;
                    }

                    var requiredPack = csvParser.GetCellAsString(ColumnRequiredPack, y);
                    if (!string.IsNullOrEmpty(requiredPack) && !PluginData.IsEnabled(requiredPack))
                    {
                        continue;
                    }

                    if (!seen.Add(assetBundleName))
                    {
                        MTEUtils.LogWarning(
                            "アセットバンドル名が重複しているため無視しました。{0} ({1})",
                            assetBundleName, neiFilePath);
                        continue;
                    }

                    result.Add(new BgObjectInfo
                    {
                        category = csvParser.GetCellAsString(ColumnCategory, y),
                        name = name,
                        assetBundleName = assetBundleName,
                        neiFilePath = neiFilePath,
                    });
                }
            }
        }
    }
}
