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

        /// <summary>
        /// アタッチ先メイドの guid。null/空 は未アタッチ。
        /// スロット番号はシーンをまたぐと変わるため、SceneEditor のシーンプリセット本体に合わせて guid で持つ
        /// </summary>
        public string attachMaidGuid = null;

        /// <summary>アタッチ先ボーン名。null/空 は未アタッチ</summary>
        public string attachBoneName = null;
    }

    /// <summary>
    /// 自前配置モデルの配置内容一式。XmlSerializer でそのまま永続化する
    /// </summary>
    public class ModelPlacementPreset
    {
        /// <summary>
        /// 現行フォーマットのバージョン。
        /// version 2: アタッチ先の識別子がスロット番号から guid になった
        /// </summary>
        public const int CurrentVersion = 2;

        /// <summary>フォーマットの互換判定用。旧形式の読み込み時に警告を出すために使う</summary>
        public int version = CurrentVersion;
        public List<ModelPlacementPresetItem> items = new List<ModelPlacementPresetItem>();
    }
}
