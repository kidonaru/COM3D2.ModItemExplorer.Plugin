# NEI / `.asset_bg` フォーマット

この文書は、COM3D2 / COM3D2.5 の Photo BG オブジェクト MOD で使われる
`*_photo_bg_object_list.nei` と `.asset_bg` の関係、および
`COM3D2.ModItemExplorer.Plugin` における読み込み仕様をまとめたものです。

## 概要

NEI は背景オブジェクトの一覧表です。Prefabやモデル本体を格納しているファイルではありません。

```text
NEI の列4: ykpr_operating_table_00
                         |
                         v
MOD 仮想ファイルシステム: ykpr_operating_table_00.asset_bg
                         |
                         v
              Unity AssetBundle内の GameObject
```

標準的な配置例は次のようになります。

```text
Mod/
└─ <MOD名>/
   └─ mod/
      └─ <MOD名>/
         ├─ PhotoBG_OBJ_NEI/
         │  └─ <prefix>_photo_bg_object_list.nei
         ├─ <assetBundleName>.asset_bg
         └─ ...
```

`PhotoBG_OBJ_NEI` は標準的な配置場所です。ただし、このプラグインはフォルダ名を固定せず、
`Mod` 以下を再帰検索してファイル名が `*_photo_bg_object_list.nei` に一致するものを読みます。

## NEIの論理フォーマット

ゲーム内でCSVとして解釈した場合、1行目がヘッダーで、2行目以降が背景オブジェクトです。
列の順序は次の通りです。

| 列 | 標準的な見出し | 意味 |
|---:|---|---|
| 0 | `ＩＤ` | 背景オブジェクトのID。公式側では整数として扱われる |
| 1 | `カテゴリー` | 一覧表示時のカテゴリー。例: `mod` |
| 2 | `名前` | ユーザーに表示する名前 |
| 3 | `内部名` | `Resources` 側の内部Prefab名。公式の内部Prefab経路用 |
| 4 | `アセットバンドル` | `.asset_bg` のベース名。通常は拡張子を付けない |
| 5 | `必要パック` | 必要なプラグイン・パック名。空欄なら制限なし |

列4には `.asset_bg` を付けません。

```text
正しい: ykpr_operating_table_00
ファイル: ykpr_operating_table_00.asset_bg
```

### 参考ファイルの実測値

対象ファイル:

```text
PhotoBG_OBJ_NEI/ykpr_operating_table_00_photo_bg_object_list.nei
```

復号してCSVセルを確認すると、6列7行（ヘッダー1行、データ6行）でした。
全データ行のIDは`1100`、カテゴリーは`mod`、内部名と必要パックは空欄です。

| 名前 | アセットバンドル |
|---|---|
| 手術台のような何か | `ykpr_operating_table_00` |
| 手術台のような何か(ベルト付き) | `ykpr_operating_table_01` |
| 手術台のような何か(マット無し) | `ykpr_operating_table_02` |
| 手術台のような何か(マット無し・ベルト付き) | `ykpr_operating_table_03` |
| 手術台のような何か用ベルト | `ykpr_operating_table_00_belt` |
| 手術台のような何か用ベルト(小) | `ykpr_operating_table_00_belt_half` |

## NEIの実ファイル構造

NEIファイルは平文CSVではなく、暗号化されたバイナリです。公開されている逆解析実装と参考ファイルで、
次の構造を確認しています。

### 復号後

整数はlittle-endianです。

```text
offset 0   4 bytes                  シグネチャ: 77 73 76 FF
offset 4   uint32                   列数
offset 8   uint32                   行数
offset 12  列数 × 行数 × 8 bytes    セル索引（offset, length）の配列
後半       可変長バイト列            Shift-JISの文字列データ
```

セル索引は行優先です。各セルの`offset`は文字列データ領域の先頭からの相対位置、`length`はセルの長さです。
文字列データには終端用の`NUL`が付く場合があります。

### 暗号化

確認できた形式では、AES-128-CBCで暗号化されています。パディングは通常のPKCS#7ではなく、
暗号化前に0埋めした長さを末尾情報から復元する方式です。

末尾5 bytesは次の構成です。

```text
1 byte   パディング長とIVシード先頭byteのXOR
4 bytes  IVシード
```

IVは4 bytesのシードから生成されます。固定キーは次の値です。

```text
AA C9 D2 35 22 87 20 F2 40 C5 61 7C 01 DF 66 54
```

ゲーム内では、プラグインがこの暗号化処理を実装する必要はありません。
`FileSystemMod.FileOpen()`で開いたファイルをゲームの`CsvParser`へ渡すと、ゲーム本体側が復号とセル取得を行います。
ゲーム外で解析する場合は、ゲームの`CsvParser`を使うか、AES・IV生成・バイナリ索引を実装する必要があります。

## このプラグインでのNEI処理

実装は [`BgObjectNeiLoader.cs`](../source/COM3D2.ModItemExplorer.Plugin/BgObject/BgObjectNeiLoader.cs) です。

1. `Mod`以下から `*_photo_bg_object_list.nei` を再帰検索する。
2. `FileSystemMod.FileOpen()`でNEIを開き、ゲームの`CsvParser`で読む。
3. 先頭行（ヘッダー）を飛ばし、データ行を走査する。
4. 列2の名前と列4のAssetBundle名が空欄の行を除外する。
5. 列5が空欄でない場合、指定されたパックが有効なときだけ登録する。
6. 同じAssetBundle名が複数回現れた場合は、大文字小文字を無視して最初の行を採用する。

現在のローダーは列0のID値自体を識別子として利用せず、セルが存在するかを確認するだけです。
また、列3の内部Prefab名はMOD用の読み込みには使用しません。列3が空欄でも、列4が設定されていれば登録できます。

## `.asset_bg` の実体と読み込み

`.asset_bg` は独自のPrefabテキスト形式ではなく、Unity AssetBundleに`.asset_bg`という拡張子を付けたものです。
AssetBundle内にはロード対象となる`GameObject`が必要です。

実装は [`BgObjectAssetLoader.cs`](../source/COM3D2.ModItemExplorer.Plugin/BgObject/BgObjectAssetLoader.cs) です。
選択したNEI行の列4を使い、次の順序でロードします。

```text
<列4> + ".asset_bg"
        ↓
GameUty.FileSystemMod.FileOpen()
        ↓
ReadAll()
        ↓
AssetBundle.LoadFromMemory()
        ↓
LoadAllAssets<GameObject>()[0]
        ↓
Object.Instantiate()
```

そのため、次の条件を満たす必要があります。

- 列4の値から解決できる`<名前>.asset_bg`がMOD仮想ファイルシステムに存在する。
- AssetBundle内に少なくとも1つの`GameObject`がある。
- 複数の`GameObject`を含める場合、現在のプラグインは先頭のアセットを使用する。
- 列3にPrefabのパスを書いても、現在のMOD経路では`Resources.Load`によるロードには切り替わらない。

## 作成・検証時のチェックリスト

- [ ] NEIファイル名が`*_photo_bg_object_list.nei`になっている。
- [ ] NEIの列数と列順が6列である。
- [ ] 1行目をヘッダーとして用意している。
- [ ] 列2に表示名がある。
- [ ] 列4に`.asset_bg`を除いたAssetBundle名がある。
- [ ] 列4に対応する`<名前>.asset_bg`が存在する。
- [ ] AssetBundle内にロード対象の`GameObject`がある。
- [ ] MOD由来の行では、列3（内部名）が空欄でも問題ない。
- [ ] 必要パックを使う場合は列5に正しい識別名を設定する。

## 参考資料

- [COM3D2.ModLoader wiki: `.asset_bg` とNEIの列定義](https://github-wiki-see.page/m/Neerhom/COM3D2.ModLoader/wiki/.asset_bg-files-and-NEI-append)
- [MeidoSerialization: NEIの復号・シリアライズ実装](https://github.com/MeidoPromotionAssociation/MeidoSerialization/blob/main/serialization/COM3D2/nei.go)
- [Photo BGオブジェクト対応の設計メモ](superpowers/specs/2026-08-21-photo-bg-object-nei-design.md)
