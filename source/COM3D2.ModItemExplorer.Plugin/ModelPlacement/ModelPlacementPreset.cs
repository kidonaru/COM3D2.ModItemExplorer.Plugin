using System.Collections.Generic;

namespace COM3D2.ModItemExplorer.Plugin
{
    /// <summary>
    /// 自前配置モデル1体分の保存データ。Transform は復元時にラッパー GameObject へ適用する
    /// </summary>
    public class ModelPlacementPresetItem
    {
        public string fileName;
        public int group;
        public bool visible = true;

        public float posX, posY, posZ;
        public float rotX, rotY, rotZ;
        public float sclX = 1f, sclY = 1f, sclZ = 1f;
    }

    /// <summary>
    /// 自前配置モデルの配置内容一式。XmlSerializer でそのまま永続化する
    /// </summary>
    public class ModelPlacementPreset
    {
        /// <summary>将来のフォーマット変更時の互換判定用（現状は書き出すだけで参照しない）</summary>
        public int version = 1;
        public List<ModelPlacementPresetItem> items = new List<ModelPlacementPresetItem>();
    }
}
