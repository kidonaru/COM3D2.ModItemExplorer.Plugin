using UnityEngine;

namespace COM3D2.ModItemExplorer.Plugin
{
    /// <summary>
    /// nei で宣言された背景オブジェクト 1 件。実体は Mod 配下の .asset_bg アセットバンドル。
    /// menu を持たないため MenuItem ではなく ModItemBase を直接継承する
    /// </summary>
    public class BgObjectItem : ModItemBase
    {
        public BgObjectInfo info { get; set; }

        public override string tag => info?.category ?? "オブジェクト";

        public override Color tagColor =>
            new Color(0.3f, 0.5f, 0.7f, config.tagBGAlpha);

        public override bool canFavorite => true;

        public override Texture2D thum
        {
            get
            {
                if (_thum != null)
                {
                    return _thum;
                }

                _thum = PluginInfo.BgObjectIconTexture;
                return _thum;
            }
            // 既定の setter は旧テクスチャを Destroy するが、
            // ここは全アイテム共有のアイコンなので破棄してはいけない
            set => _thum = value;
        }
    }
}
