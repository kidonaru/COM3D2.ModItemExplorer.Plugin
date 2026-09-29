using System;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;

namespace COM3D2.ModItemExplorer.Plugin
{
    /// <summary>
    /// ゲーム本体の背景オブジェクト (フォトモードの「背景オブジェクト」一覧) を列挙する。
    /// 読み込みはゲームの PhotoBGObjectData に任せ、有効 ID やパック判定もそちらと揃える
    /// </summary>
    public static class OfficialBgObjectLoader
    {
        /// <summary>ユーザー作成の画像オブジェクト。direct_file 方式で配置経路が無いため除外する</summary>
        private const string MyObjectCategory = "マイオブジェクト";

        /// <summary>
        /// PrepareOnMainThread で作った一覧。ゲーム側のリストはフォトモードのマイオブジェクト追加で
        /// メインスレッドから書き換わるため、ワーカーはこちらだけを読む
        /// </summary>
        private static List<BgObjectInfo> _snapshot = new List<BgObjectInfo>();

        /// <summary>
        /// ゲーム側の一覧を用意し、ワーカー用の控えを作る。メインスレッドから呼ぶこと。
        /// Create() は bg_data_ を先に代入してから残りを組み立てるため、ワーカーで走らせると
        /// フォトモードが同時に触ったときや途中で例外が出たときに半初期化のまま固定される
        /// </summary>
        public static void PrepareOnMainThread()
        {
            try
            {
                if (PhotoBGObjectData.data == null)
                {
                    PhotoBGObjectData.Create();
                }
                _snapshot = CreateInfoList(PhotoBGObjectData.data);
            }
            catch (Exception e)
            {
                MTEUtils.LogWarning("公式背景オブジェクト一覧の読み込みに失敗しました。");
                MTEUtils.LogException(e);
            }
        }

        /// <summary>PrepareOnMainThread で作った一覧を返す。ワーカーから呼んでよい</summary>
        public static List<BgObjectInfo> LoadAll()
        {
            return _snapshot;
        }

        private static List<BgObjectInfo> CreateInfoList(List<PhotoBGObjectData> dataList)
        {
            var result = new List<BgObjectInfo>(512);
            if (dataList == null)
            {
                return result;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var data in dataList)
            {
                if (data == null || data.category == MyObjectCategory)
                {
                    continue;
                }

                // ゲームの Instantiate と同じく prefab 名を優先する
                var assetName = !string.IsNullOrEmpty(data.create_prefab_name)
                    ? data.create_prefab_name
                    : data.create_asset_bundle_name;
                if (string.IsNullOrEmpty(assetName) || string.IsNullOrEmpty(data.name))
                {
                    continue;
                }

                // 同じアセットを複数カテゴリで宣言している行は先勝ちで 1 件にする
                // (配置データは assetName で引くため、重複させても区別できない)
                if (!seen.Add(assetName))
                {
                    continue;
                }

                result.Add(new BgObjectInfo
                {
                    category = data.category,
                    name = data.name,
                    isOfficial = true,
                    officialAssetName = assetName,
                });
            }

            return result;
        }
    }
}
