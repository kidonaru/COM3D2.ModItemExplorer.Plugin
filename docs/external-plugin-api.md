# 外部プラグイン連携 API

外部プラグインからリフレクション経由で利用できる公開 API のメモ。

## モデル配置の XML 取得・反映（SelfModelPlacer）

対象クラス: `COM3D2.ModItemExplorer.Plugin.SelfModelPlacer`
（`source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/SelfModelPlacer.cs`）

すべて public のため、リフレクションでそのままアクセスできる。

| メンバー | 内容 |
|---|---|
| `static SelfModelPlacer instance` | シングルトン取得 |
| `string GetPlacementXml()` | 現在表示中の自前配置モデル一式を XML 文字列で返す。失敗時は null |
| `bool ApplyPlacementXml(string xml)` | XML 文字列を現在のシーンに反映する。成功可否を返す |
| `void SavePreset(string name)` | 名前付きプリセットとして xml ファイルに保存 |
| `bool LoadPreset(string name)` | 名前付きプリセットから復元 |
| `List<string> GetPresetNames()` | 保存済みプリセット名一覧 |
| `void DeletePreset(string name)` | プリセット削除 |

XML フォーマットは `ModelPlacementPreset` / `ModelPlacementPresetItem` を
`XmlSerializer` で直列化したもの。名前付きプリセットの xml ファイル
（`PluginUtils.ModelPresetDirPath` 配下）と同一フォーマットのため相互流用できる。

### 呼び出し例

```csharp
var asm = AppDomain.CurrentDomain.GetAssemblies()
    .First(a => a.GetName().Name == "COM3D2.ModItemExplorer.Plugin");
var type = asm.GetType("COM3D2.ModItemExplorer.Plugin.SelfModelPlacer");
var placer = type.GetProperty("instance").GetValue(null, null);

// 現在の配置を XML で取得
var xml = (string)type.GetMethod("GetPlacementXml").Invoke(placer, null);

// XML を反映
var ok = (bool)type.GetMethod("ApplyPlacementXml").Invoke(placer, new object[] { xml });
```

### 注意点

- 対象は **ModItemExplorer 自前配置分のみ**。MotionTimelineEditor 経由で配置した
  モデルは含まれない。
- `ApplyPlacementXml` / `LoadPreset` は既存の自前配置分を **全削除してから** 復元する
  置き換え動作。
- アタッチ情報は **メイドの guid + ボーン名** で保持する（スロット番号はシーンをまたぐと
  変わるため使わない）。復元時に guid のメイドが居ない場合は現在の対象メイドへ割り当て、
  それも不在・ボーン不明ならワールド配置にフォールバックする。
- 旧形式（`version` 1、アタッチ先がスロット番号）の xml も読み込めるが、アタッチ情報は
  復元されずワールド配置になる（読み込み時に警告ログを出す）。

## シーンプリセットプロバイダ規約（SceneEditor プラグイン連携）

SceneEditor プラグインのシーンプリセットは、外部プラグインの状態も一緒に
保存/復元できる。連携したいプラグインは以下の規約に従う:

1. 自アセンブリ内に `ScenePresetProviderAttribute` という**短名一致**の属性を自前定義する
   （SceneEditor プラグインへのアセンブリ参照は不要）
2. その属性を付けたクラスに、以下の public static メンバを実装する

```csharp
[ScenePresetProvider]
public static class MyPresetProvider
{
    // 一意な ID。プリセット XML 内の external 要素とサイドカーのファイル名になる
    public static string PresetProviderId => "MyPlugin.Something";
    // 保存ポップアップのチェックボックスに表示される名前
    public static string PresetProviderDisplayName => "○○ (MyPlugin)";
    // 任意。サイドカーの拡張子（未定義なら "xml"）
    public static string PresetProviderFileExtension => "xml";
    // 現在状態を XML 文字列で返す。null/空なら保存スキップ
    public static string CapturePresetXml() { /* 実装は各自 */ }
    // XML を適用して成功可否を返す
    public static bool ApplyPresetXml(string xml) { /* 実装は各自 */ }
}
```

`CapturePresetXml` / `ApplyPresetXml` の**テキスト対**の代わりに、
`static byte[] CapturePresetBinary()` / `static bool ApplyPresetBinary(byte[] data)` の
**バイナリ対**でもよい（どちらか一方が揃っていればよく、両方あればバイナリが使われる）。
id / 拡張子はサイドカーのファイル名になるため、パスに使えない文字・`..`・
拡張子中のドットを含むものは登録時に弾かれる。

SceneEditor プラグインは初回参照時（以降は保存ポップアップを開くたび）に全アセンブリを
走査してプロバイダを発見し、プリセット保存時に選択されたプロバイダの `CapturePresetXml` を、
ロード時に `ApplyPresetXml` を呼ぶ。例外はプロバイダ単位で握られ、他の復元は続行される。

ペイロードはプリセット本体の XML には埋め込まれず、
`<プリセット名>.<プロバイダid>.<拡張子>` というサイドカーファイルへ**そのまま**書き出される
（本体 XML にはそのファイル名だけが `<external id="..." file="..." />` として残る。
プリセット形式 v10 以降）。再シリアライズされないため、拡張子とフォーマットを
プラグイン自前のプリセットファイルに合わせておくとファイル単位で相互流用できる。

本プラグインでは `ModelPlacementPresetProvider`
（`source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/ModelPlacementPresetProvider.cs`）が
`SelfModelPlacer` の配置 XML をこの規約で公開している。拡張子は既定の `xml` のままで、
名前付きプリセット（`PluginUtils.ModelPresetDirPath` 配下）と同一フォーマットのため、
サイドカーをそのまま名前付きプリセットとして流用できる。

詳細は SceneEditor 側のガイド
（`W:\COM3D2_5\work\COM3D2.SceneEditor.Plugin\docs\scene-preset-provider-guide.md`）を参照。
