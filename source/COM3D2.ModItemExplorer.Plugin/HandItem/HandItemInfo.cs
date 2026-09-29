namespace COM3D2.ModItemExplorer.Plugin
{
    /// <summary>phot_maid_item_list.nei の 1 行分 (フォトモードの「ハンドアイテム」)</summary>
    public class HandItemInfo
    {
        /// <summary>nei のカテゴリ列 (右手 / 左手 / 上半身 ...)。タグ表示にも使う</summary>
        public string category;

        /// <summary>nei の名前列。ツリー上の表示名</summary>
        public string name;

        /// <summary>拡張子付きの menu ファイル名</summary>
        public string menuFileName;

        /// <summary>
        /// 装着先 MPN。menu 自身のカテゴリではなくフォトモードの割り当てに従う
        /// (左手は menu 上 handitem だが、右手と同時に持てるよう seieki_naka へ入れる)
        /// </summary>
        public MPN mpn;
    }
}
