# mod の photo_bg_object_list.nei 対応 設計

**日付:** 2026-08-21
**対象リポジトリ:** COM3D2.ModItemExplorer.Plugin (MTE)

## 目的

`Mod` フォルダに置かれた `*_photo_bg_object_list.nei` を読み、そこで宣言された背景オブジェクト
(`.asset_bg` アセットバンドル) を ModItemExplorer のツリーに一覧表示し、自前配置
(`SelfModelPlacer`) でシーンへ配置できるようにする。

公式スタジオモードの「オブジェクト管理」UI には手を出さない (MaidLoader 互換のマージは行わない)。

## 背景・前提

### 対象 MOD の構成

実例: `Mod/ゆかぺろ/手術台のような何か/mod/手術台のような何か/`

```
PhotoBG_OBJ_NEI/ykpr_operating_table_00_photo_bg_object_list.nei
ykpr_operating_table_00.asset_bg
ykpr_operating_table_01.asset_bg
...
```

本来は MaidLoader が nei を公式の `phot_bg_object_list.nei` へマージする前提の MOD だが、
この環境に MaidLoader は導入されていない。`.menu` を一切持たないため現状の
ModItemExplorer からは完全に不可視。

### nei の中身 (実機で確認済み)

6 列 × 7 行 (1 行目はヘッダ)。

| ＩＤ | カテゴリー | 名前 | 内部名 | アセットバンドル | 必要パック |
|---|---|---|---|---|---|
| 1100 | mod | 手術台のような何か | | ykpr_operating_table_00 | |
| 1100 | mod | 手術台のような何か(ベルト付き) | | ykpr_operating_table_01 | |

**ＩＤ 列は全行 1100 で重複している。** 公式 `PhotoBGObjectData` は id をキーにするが、
本設計では id を識別子に使わず**アセットバンドル名をキーにする**。
`内部名` (公式では `create_prefab_name`、`Resources.Load("Prefab/...")` 用) は mod では常に空。

### 実機で検証済みの事実

以下はすべて稼働中の COM3D2.5 で確認した (`com3d25-devbridge`)。

1. `GameUty.FileSystemMod.IsExistentFile("ykpr_operating_table_00_photo_bg_object_list.nei")` = true。
   `FileSystemMod` は Mod 配下のサブフォルダも**ファイル名のみのフラットな索引**で引ける
2. ゲーム内蔵の `CsvParser` が mod の nei をそのまま開ける
   (`FileSystemMod.FileOpen()` → `CsvParser.Open()` が成功)。**自前の AES 復号実装は不要**
3. `CsvParser` は**ワーカースレッドからでも動く**。1 ファイル約 14ms。
   → 既存の `LoadModItems` と同じ `ThreadPool` ブロック内に置ける
4. `GameMain.Instance.BgMgr.CreateAssetBundle("ykpr_operating_table_00")` は **null を返す**。
   `BgMgr` は `GameUty.BgFiles` (システム側 1127 件) しか見ないため、Mod 配下の
   `.asset_bg` は解決できない。**自前でバンドルを読む必要がある**
5. `AssetBundle.LoadFromMemory(FileSystemMod.FileOpen("ykpr_operating_table_00.asset_bg").ReadAll())`
   → `LoadAllAssets<GameObject>()[0]` で prefab が取れる (`mainAsset` は null)。
   バンドルサイズ 24MB、`Instantiate` して Renderer 1 個 / マテリアル 4 枚、
   シェーダーは `Custom/ykprRimLightSpecular2Shader` (バンドル同梱) で `isSupported == true`。
   **URP 変換なしでそのまま描画できる**

### 既存コードの前提

- `ModItemManager.Load()` は `MTEUtils.ExecuteAfterMenuDataBaseReady` の中で `ThreadPool` に投げ、
  `LoadOfficialMenuItems` → `LoadModItems("*.menu")` → `LoadModItems("mod_*.mod")` →
  … → `SaveMenuCache()` の順に流す
- Mod アイテムのツリー位置は `GetRelativePath(MTEUtils.ModDirPath, menuFilePath)` 由来 (実フォルダ構造)
- `SelfModelPlacer.CreateModel(fileName, group, visible)` は `.menu` 前提:
  `ModelMenuScript.Load` → `ModelMeshLoader.LoadMesh(.model)` → ラッパー GameObject + ギズモ
- 生成した Mesh/Material は `disposables` に積み、モデル削除時に明示 `Destroy` する
- `ModItemManager.CreateModel(MenuItem item, string pluginName)` が UI からの唯一の配置入口

## Section 1: nei パース層 (`BgObjectNeiLoader` 新設)

`source/COM3D2.ModItemExplorer.Plugin/BgObject/BgObjectNeiLoader.cs`

### データ

```csharp
public class BgObjectInfo
{
    public string category;          // nei の「カテゴリー」列 (例: "mod")
    public string name;              // nei の「名前」列。ツリーの表示名
    public string assetBundleName;   // nei の「アセットバンドル」列。実質の一意キー
    public string neiFilePath;       // 由来 nei のフルパス。ツリー位置と fullPath に使う
}
```

### 読み込み

- `Directory.GetFiles(MTEUtils.ModDirPath, "*_photo_bg_object_list.nei", SearchOption.AllDirectories)`
- 各パスについて `GameUty.FileSystemMod.FileOpen(Path.GetFileName(path))` で `AFileBase` を取り、
  `CsvParser.Open()` に渡す。`using` で両方確実に破棄する
- `y = 1` から `max_cell_y` まで走査。列インデックスは公式 `PhotoBGObjectData.Create()` と同じ順
  (0:ID, 1:カテゴリー, 2:名前, 3:内部名, 4:アセットバンドル, 5:必要パック)
- スキップ条件:
  - `アセットバンドル` 列が空 (内部名 prefab 方式は非対応)
  - `名前` 列が空
  - `必要パック` 列が非空かつ `PluginData.IsEnabled(必要パック)` が false (公式と同じ判定)
- 同一 `assetBundleName` が複数 nei に出た場合は**先勝ちで 1 件だけ採用**し、警告ログを出す
  (AssetBundle はバンドル単位でしかロードできず、重複を両方載せると配置時に衝突するため)

### 非対応と理由

- `ＩＤ` 列: 実データで重複しており識別子として使えない。公式リストへのマージもしないので不要
- `内部名` (prefab) 列: `Resources` に入るのは公式アセットのみで、mod では使われない (YAGNI)
- `phot_bg_object_enabled_list` によるゲート: 公式リスト向けの仕組みで mod の nei には適用されない

## Section 2: アイテムツリー (`BgObjectItem` 新設)

`source/COM3D2.ModItemExplorer.Plugin/BgObject/BgObjectItem.cs`

- `ModItemType` に `BgObject` を追加
- `BgObjectItem : ModItemBase` を新設し `BgObjectInfo info` を保持
  - `name` → `info.name`
  - `setumei` → 空
  - `tag` → `info.category` (色は固定の 1 色)
  - `thum` → 固定アイコン (下記)
  - `fullPath` → **nei ファイルのフルパス** (`AnmItem` が `.anm` のパスを入れるのと同じ)。
    `ModItemManager.ValidateItemFile` が `fullPath` 非空のアイテムを `File.Exists` で
    生存確認して消すため、ここにフォルダを入れるとロードのたびに消える
  - `canFavorite` は既定の true のまま

### ツリー位置

nei の実フォルダにそのまま展開する。

```
Mod/ゆかぺろ/手術台のような何か/mod/手術台のような何か/PhotoBG_OBJ_NEI/手術台のような何か
```

`itemPath` は `GetRelativePath(MTEUtils.ModDirPath, neiFilePath)` のディレクトリ部 + `info.name`。
同一フォルダ内で `名前` が重複した場合は既存の `GetOrCreate` 系と同じく後勝ちになるため、
重複を検出したら警告ログを出したうえで**アセットバンドル名を接尾に付けて一意化**する。

### サムネイル

背景オブジェクト専用アイコンを**プラグインに同梱して新規に用意する** (Section 2.5)。

ゲーム側には流用できるアイコンが無い。公式の「オブジェクト管理」(`BGObjectWindow`) は
`UILabel` だけのテキスト一覧でアイコンを持たず、`noimage.tex` / `cm3d2_objecticon01.tex`
等の汎用アイコン候補も実機で `IsExistentFile` = false を確認済み。`AnmItem` が使う
`cm3d2_poseicon01.tex` はポーズ用の図案で意味が合わない。

`BgObjectItem.thum` は `PluginInfo.BgObjectIconTexture` を返す
(`TextureManager` は `.tex` 用なので経由しない)。

## Section 2.5: 専用アイコンの用意

### 図案

アイソメトリックな立方体 (「配置する 3D オブジェクト」をそのまま表す)。
上面・左面・右面で明度を変えた 3 面構成にする。単純な多角形の塗り分けなので
縮小しても形が崩れない。

### 生成フロー

COM3D2.SceneEditor.Plugin の `assets/icons/` と同じ方式を、このリポジトリにも自前で持つ。
SceneEditor の `node_modules` を参照すると別リポジトリに依存して壊れやすいため共用しない。

新設するファイル:

```
assets/icons/package.json    # @resvg/resvg-js のみ
assets/icons/generate.js     # SVG -> PNG(base64) 。SceneEditor 版の移植
assets/icons/BgObject.svg    # 図案の原本
assets/icons/BgObject.png    # 生成物 (確認用にコミットする)
```

- `generate.js` は SceneEditor 版とほぼ同一。**PNG のエンコードは Node 標準の `zlib` で自前で行う**
  (ライブラリが吐く PNG は `Texture2D.LoadImage` が読めないことがあるため。
  SceneEditor の `generate.js` と MTEUtils の `ResizeCursor.cs` に同じ注意書きがある)
- **出力サイズは 128x128。** SceneEditor のツールバーアイコンは 32x32 だが、
  こちらはタイルビューのサムネイル (タイル 120x110) に使うため大きくする
- `.gitignore` に `assets/icons/node_modules/` を追加する

### 埋め込み

`PluginInfo.cs` に既存の `SearchIcon` / `OpenIcon` / `UpdateIcon` と同じ形で追加する。

```csharp
public readonly static byte[] BgObjectIcon = Convert.FromBase64String("...");

private static Texture2D _bgObjectIconTexture = null;
public static Texture2D BgObjectIconTexture { get; }  // 遅延生成 + LoadImage
```

`Texture2D.LoadImage` は COM3D2.5 でも `ImageConversionModule` 参照済みで動作する
(既存アイコンが同じ経路で表示できている)。

### ロード順への組み込み

`ModItemManager.Load()` の `LoadModItems("mod_*.mod")` の直後に `LoadModBgObjectItems()` を挿入。
`LoadState` に `LoadModBgObjectItems` を追加して進捗表示に載せる。

`_menuMap` / menu キャッシュ (`SaveMenuCache`) は `MenuInfo` 前提なので nei は載せず、
`Load()` のたびに再パースする (対象ファイルが数個かつ 1 ファイル 14ms のため実測上問題にならない)。

### 消えた行の掃除

`Load()` のたびに「今回の nei に現れた `itemPath` の集合」を作り、そこに含まれない
既存の `BgObjectItem` を明示的に削除する。

`BgObjectItem.fullPath` は nei ファイル自体を指すため、MOD 側が nei の**行だけ**を
削除・リネームした場合、`ValidateItemFile` の `File.Exists` は通ってしまい
旧アイテムがゴーストとしてツリーに残る。行単位の生存確認はここでしかできない。

検索・お気に入りは `itemPath` / `name` ベースで動くため、追加対応なしで乗る。

## Section 3: 配置 (`SelfModelPlacer` に asset_bg 経路を追加)

### AssetBundle キャッシュ (`BgObjectAssetLoader` 新設)

`source/COM3D2.ModItemExplorer.Plugin/BgObject/BgObjectAssetLoader.cs`

```csharp
static Dictionary<string, GameObject> _prefabCache;  // assetBundleName -> prefab
static Dictionary<string, AssetBundle> _bundleCache;
public static GameObject LoadPrefab(string assetBundleName);
```

- `FileSystemMod.FileOpen(assetBundleName + ".asset_bg")` → `ReadAll()` →
  `AssetBundle.LoadFromMemory` → `LoadAllAssets<GameObject>()` の先頭を prefab とする。
  複数入っている場合の並び順は Unity が保証していないため、2 個以上あれば警告ログを出す
  (公式 `BgMgr` も `mainAsset` が無ければ先頭を使うが、`mainAsset` は obsolete で
  今回の対象では null なので使わない)
- **キャッシュは必須**。同一バンドルを二重に `LoadFromMemory` すると
  「同じファイルを含む AssetBundle が既にロード済み」で例外になる
- ロード済みバンドルは**アンロードしない**。公式 `BgMgr.asset_bundle_dic` と同じ方針
  (トレードオフは末尾に記載)
- 失敗時は警告ログを出して null を返す (ゲームを落とさない)

### `SelfModelPlacer.CreateBgObject(BgObjectInfo info, int group, bool visible)`

既存の `CreateModel` とラッパー生成以降を共通化する。

- `BgObjectAssetLoader.LoadPrefab` → `Instantiate` → レイヤ設定
- **`disposables` には何も積まない。** Mesh/Material はバンドル所有であり、
  破棄すると同じバンドルから作った他インスタンスと次回以降の `Instantiate` が壊れる
- 以降は既存経路と同じ: ラッパー GameObject を作り、配置親にぶら下げ、`AddGizmo`、
  `StudioModelStatWrapper` を組んで `_models` に登録、`selectedModel` に設定、
  `history.RegisterCreate`
- 識別のため `infoWrapper.fileName` / `label` には `info.assetBundleName + ".asset_bg"` を入れる
- `ResolveGroup` / `GetModelName` はこの `fileName` をそのまま使えるので変更不要

### 配置先プラグインの制限

MTE (MotionTimelineEditor) / StudioMode 側の配置経路は `.menu` 名を渡す前提なので
背景オブジェクトを扱えない。`BgObjectItem` 選択時は:

- 配置プラグインのコンボボックスの選択に関わらず、自前配置 (`SelfModelPlacer.PluginName`) を使う
- 他プラグインが選ばれていた場合は「背景オブジェクトは自前配置でのみ扱えます」と情報ログを出す

呼び出しは `ModelPlacerManager.CreateBgObject` を新設して**ファサード経由に揃える**。
`ModItemManager` は既存の配置経路をすべて `ModelPlacerManager` 越しに呼んでおり、
ここだけ `SelfModelPlacer.instance` を直接触ると、将来ファサードに共通処理が
足された時にすり抜ける。

## Section 4: プリセット・操作履歴

**プリセットのフォーマットは変更しない。** `ModelPlacementPresetItem.fileName` に入る
`<assetBundleName>.asset_bg` という**拡張子そのものを種別の判別に使う**。

- `SelfModelPlacer.RestoreModel` で `fileName` の拡張子を見て分岐する
  - `.asset_bg` → `CreateBgObject`
  - それ以外 (`.menu` / `.mod`) → 従来どおり `CreateModel`
- `BuildPresetItem` は `model.infoWrapper?.fileName` をそのまま保存しているので変更不要
- `ModelPlacementPreset.CurrentVersion` は **2 のまま**。旧プリセットに `.asset_bg` が
  現れることはなく、追加フィールドも無いので互換の心配がない

`RestoreModel` はプリセット復元と**操作履歴の undo/redo の両方**が通る唯一の経路なので、
ここで分岐すれば undo/redo も追加対応なしで動く。

拡張子を判別に使うのは既存コードの流儀と揃っている
(`ModItemManager.GetMenu` も `.menu` / `.mod` の拡張子で分岐している)。

復元時に該当バンドルが見つからない場合は `CreateBgObject` が警告ログを出して null を返し、
`RestoreModel` がその 1 件だけスキップする (他のモデルの復元は続行される)。

なお `BgObjectInfo` を引き当てる必要は無い。配置に要るのは `assetBundleName` だけで、
これは `fileName` から拡張子を外せば得られる。

## Section 5: UI

- `ModItemManager.CreateModel(MenuItem item, string pluginName)` を
  `CreateModel(ModItemBase item, string pluginName)` に一般化し、
  内部で `MenuItem` / `BgObjectItem` に分岐する
- `ModItemWindow.DrawModelPlacementRow` の「配置」ボタン活性条件を
  `selectedMenuItem != null` から「配置可能アイテムが選択中か」(`MenuItem` または `BgObjectItem`) に変更
- `ModItemWindow.CreateSelectedModel` が `selectedMenuItem` ではなく `selectedItem` を渡すようにする
- フッターのツールチップ (`_mouseOverItem is MenuItem` 分岐) に `BgObjectItem` の分岐を追加し、
  `名前 + カテゴリー` を表示する
- バリエーション欄は `BgObjectItem` では対象外 (既存の `selectedMenuItem == null` 早期 return でそのまま空になる)
- `DelItem` の分岐は `ModItemType.Model` 経由 (配置済みアイテム) で従来どおり動くため変更不要

## エラーハンドリング方針

既存コードと同じく「警告ログを出して当該 1 件をスキップし、全体は止めない」で統一する。

- nei が開けない / `CsvParser.Open` が false → その nei をスキップ
- `.asset_bg` が `FileSystemMod` に無い → 配置時に警告、null 返し
- `LoadAllAssets<GameObject>()` が空 → 配置時に警告、null 返し

## テスト・検証

自動テストの基盤が無いリポジトリのため、稼働中の実機 (`com3d25-devbridge`) で確認する。

1. ツリー: `Mod/ゆかぺろ/…/PhotoBG_OBJ_NEI/` に 6 件が並び、専用アイコンが
   タイル (120x110) でぼやけずに表示される
2. 検索: 「手術台」で 6 件がヒットする
3. 配置: 6 件それぞれを配置し、描画されること・ギズモで操作できることを確認
4. 同一オブジェクトの 2 個目を配置してもエラーにならない (バンドル二重ロード回避の確認)
5. 削除: 配置済みを削除しても、残った同バンドル由来のモデルが壊れない
6. プリセット: 保存 → 全削除 → 復元で位置・回転・スケールが再現される
7. 操作履歴: 配置の undo / redo が効く (`RestoreModel` 経由の確認)
8. 既存回帰: 従来の `.menu` アイテムの配置・削除・プリセット保存/復元・undo/redo が壊れていない
9. 既存プリセット (`.menu` のみのもの) がそのまま読めること

## 既知のトレードオフ

- **AssetBundle をアンロードしない。** 今回の MOD は 1 バンドル 24MB あり、6 種すべてを
  触ると常駐 ~140MB になる。参照カウントによるアンロードは、同一バンドルから作った
  複数インスタンスとプリセット復元をまたいだ寿命管理が必要で、複雑さに見合わないと判断した。
  公式 `BgMgr` も同じくアンロードしない
- **nei をキャッシュせず毎回パースする。** menu キャッシュは `MenuInfo` 前提で、
  nei 用に別スキーマを足すコストに見合わない。対象ファイル数が少ないため実測上の影響はない
- **公式スタジオモードの「オブジェクト管理」には出ない。** MaidLoader 相当の
  `PhotoBGObjectData` マージは今回のスコープ外
