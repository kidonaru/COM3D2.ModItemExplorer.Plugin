namespace COM3D2.ModItemExplorer.Plugin
{
    /// <summary>
    /// photo_bg_object_list.nei の 1 行分。
    /// nei の「ＩＤ」列は実データで重複していて識別子に使えないため持たせていない。
    /// 「内部名」(公式の create_prefab_name / Resources の prefab) 列は
    /// 公式アセット専用で mod では常に空なので同じく持たない
    /// </summary>
    public class BgObjectInfo
    {
        /// <summary>nei の「カテゴリー」列。タグ表示に使う (例: "mod")</summary>
        public string category;

        /// <summary>nei の「名前」列。ツリー上の表示名</summary>
        public string name;

        /// <summary>nei の「アセットバンドル」列。拡張子なし。実質の一意キー</summary>
        public string assetBundleName;

        /// <summary>由来した nei のフルパス。ツリー位置と生存確認に使う</summary>
        public string neiFilePath;
    }
}
