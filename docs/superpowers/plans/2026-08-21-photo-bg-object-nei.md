# mod の photo_bg_object_list.nei 対応 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
> (このリポジトリのワークフロー規約により subagent-driven-development は使わない)

**Goal:** Mod フォルダの `*_photo_bg_object_list.nei` を読み、宣言された `.asset_bg` 背景オブジェクトを ModItemExplorer のツリーに一覧表示し、自前配置でシーンへ置けるようにする。

**Architecture:** ゲーム内蔵の `CsvParser` で nei をパースして `BgObjectInfo` にし、既存の Mod ツリーへ `BgObjectItem` として登録する。配置は `SelfModelPlacer` に `.asset_bg` を `AssetBundle.LoadFromMemory` で読む経路を追加し、ラッパー GameObject 以降は既存 `.menu` 経路と共通化する。プリセットと undo/redo は `RestoreModel` で `fileName` の拡張子を見て分岐するだけで通す。

**Tech Stack:** C# (.NET Framework 4.7.1) / Unity 2022.3 / UnityInjector プラグイン / MSBuild。アイコン生成のみ Node.js + `@resvg/resvg-js`。

**Spec:** `docs/superpowers/specs/2026-08-21-photo-bg-object-nei-design.md`

## Global Constraints

- **コードのコメントとログメッセージは日本語**で書く (リポジトリ規約)
- **`deploy.bat` / `deploy.ps1` は絶対に実行しない** (GitHub Releases への本番公開。取り消せない)
- **git worktree を使わない。** 作業は常にメインの作業ディレクトリで行う
- ビルド確認は**必ず MSBuild を直接叩く**。`debug.bat` はゲームフォルダへ DLL をコピーするため、ゲーム停止中に実行すると実機へ反映されてしまう
  ```bash
  cd source/COM3D2.ModItemExplorer.Plugin
  "C:/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe" \
      COM3D2.ModItemExplorer.Plugin.csproj -p:Configuration=Debug -p:GameVersion=COM3D25 -v:minimal -nologo
  ```
  Git Bash から実行する場合は **`/p:` ではなく `-p:`** を使うこと (`/p:` はパスに化けて MSB1008 になる)
- **COM3D2 (2.0) 版も壊さないこと。** 両版ビルドが通る必要がある。COM3D2 版のビルド確認は
  `-p:GameVersion=COM3D2` に変えて同じコマンドを実行する
- **このリポジトリに自動テストは存在しない。** 各タスクの検証は「MSBuild が警告なく通ること」で行い、
  実機での動作確認は最終タスク (Task 7) にまとめる
- 新規ファイルの名前空間は `COM3D2.ModItemExplorer.Plugin`
- エラー時は例外を投げず、`MTEUtils.LogWarning` / `MTEUtils.LogException` を出して当該 1 件だけスキップする (既存コードの流儀)

---

## File Structure

| ファイル | 役割 |
|---|---|
| `assets/icons/package.json` (新規) | アイコン生成の依存 (`@resvg/resvg-js`) |
| `assets/icons/generate.js` (新規) | SVG → 128x128 PNG(base64) 変換 |
| `assets/icons/BgObject.svg` (新規) | アイコン図案の原本 |
| `assets/icons/BgObject.png` (新規/生成物) | 確認用にコミットする |
| `source/COM3D2.ModItemExplorer.Plugin/PluginInfo.cs` (変更) | `BgObjectIcon` / `BgObjectIconTexture` を追加 |
| `source/COM3D2.ModItemExplorer.Plugin/BgObject/BgObjectInfo.cs` (新規) | nei 1 行分のデータ |
| `source/COM3D2.ModItemExplorer.Plugin/BgObject/BgObjectNeiLoader.cs` (新規) | nei の列挙とパース |
| `source/COM3D2.ModItemExplorer.Plugin/BgObject/BgObjectAssetLoader.cs` (新規) | `.asset_bg` の読み込みとキャッシュ |
| `source/COM3D2.ModItemExplorer.Plugin/BgObject/BgObjectItem.cs` (新規) | ツリー上のアイテム |
| `source/COM3D2.ModItemExplorer.Plugin/ModItemBase.cs` (変更) | `ModItemType.BgObject` を追加 |
| `source/COM3D2.ModItemExplorer.Plugin/Manager/ModItemManager.cs` (変更) | nei のロードとツリー登録、`CreateModel` の一般化 |
| `source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/SelfModelPlacer.cs` (変更) | `CreateBgObject` と `RestoreModel` の分岐 |
| `source/COM3D2.ModItemExplorer.Plugin/ModItemWindow.cs` (変更) | 配置ボタンの活性条件とツールチップ |
| `.gitignore` (変更) | `assets/icons/node_modules/` |
| `README.md` (変更) | 変更履歴 |

`BgObject/` に新規 4 ファイルを固めるのは、nei 由来の機能がひとまとまりで、
`ModelPlacement/` (配置の仕組み) や `Manager/` (全体の統括) とは責務が別だから。

---

## Task 1: 専用アイコンの生成と埋め込み

**Files:**
- Create: `assets/icons/package.json`
- Create: `assets/icons/generate.js`
- Create: `assets/icons/BgObject.svg`
- Create: `assets/icons/BgObject.png` (生成物)
- Modify: `source/COM3D2.ModItemExplorer.Plugin/PluginInfo.cs`
- Modify: `.gitignore`

**Interfaces:**
- Consumes: なし
- Produces: `PluginInfo.BgObjectIconTexture` (`static Texture2D`) — Task 3 の `BgObjectItem.thum` が使う

- [ ] **Step 1: `.gitignore` に node_modules を追加**

`.gitignore` の末尾に追記する。

```
# アイコン生成用。assets/icons/generate.js の依存
assets/icons/node_modules/
```

- [ ] **Step 2: `assets/icons/package.json` を作る**

```json
{
  "name": "moditemexplorer-icons",
  "private": true,
  "description": "assets/icons/*.svg を PNG(base64) へラスタライズする",
  "dependencies": {
    "@resvg/resvg-js": "^2.6.2"
  }
}
```

- [ ] **Step 3: `assets/icons/BgObject.svg` を作る**

アイソメトリックな立方体。上面・左面・右面を明度違いの 3 つの多角形で塗り分ける。
viewBox は 32 単位で書き、`generate.js` 側で 128px に引き伸ばす (SVG なので劣化しない)。

```xml
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 32 32" width="32" height="32">
  <!-- 背景オブジェクト。アイソメトリックな立方体を上面/左面/右面で塗り分ける -->
  <!-- 上面 (最も明るい) -->
  <polygon points="16,4 28,11 16,18 4,11" fill="#e8e8e8"/>
  <!-- 左面 (中間) -->
  <polygon points="4,11 16,18 16,28 4,21" fill="#9a9a9a"/>
  <!-- 右面 (最も暗い) -->
  <polygon points="28,11 28,21 16,28 16,18" fill="#6b6b6b"/>
  <!-- 稜線。タイル背景に溶けないよう輪郭を締める -->
  <path d="M16,4 L28,11 L28,21 L16,28 L4,21 L4,11 Z M16,18 L16,28 M4,11 L16,18 L28,11"
        fill="none" stroke="#2b2b2b" stroke-width="1.2" stroke-linejoin="round"/>
</svg>
```

- [ ] **Step 4: `assets/icons/generate.js` を作る**

COM3D2.SceneEditor.Plugin の `assets/icons/generate.js` の移植。
**PNG のエンコードは Node 標準の `zlib` で自前で行う** — ライブラリが吐く PNG は
`Texture2D.LoadImage` が読めないことがあるため (SceneEditor 側と MTEUtils の
`ResizeCursor.cs` に同じ注意書きがある)。

```js
// このフォルダの各 SVG を 128x128 の PNG へラスタライズし、
// PluginInfo.cs へ貼り付ける base64 文字列を出力する。
//
//   npm install
//   node generate.js
//
// PNG のエンコードは自前で行う。ライブラリが吐く PNG は
// Unity の Texture2D.LoadImage が読めないことがあるため、
// Node 標準の zlib で素直に組み立てている。

const fs = require('fs');
const path = require('path');
const zlib = require('zlib');
const { Resvg } = require('@resvg/resvg-js');

// タイルビューのタイルが 120x110 のため、ツールバー用の 32 ではなく 128 で出す
const SIZE = 128;

const ICONS = ['BgObject'];

const CRC_TABLE = (() => {
    const table = new Int32Array(256);
    for (let n = 0; n < 256; n++) {
        let c = n;
        for (let k = 0; k < 8; k++) {
            c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
        }
        table[n] = c;
    }
    return table;
})();

function crc32(buffer) {
    let c = -1;
    for (let i = 0; i < buffer.length; i++) {
        c = CRC_TABLE[(c ^ buffer[i]) & 0xff] ^ (c >>> 8);
    }
    return (c ^ -1) >>> 0;
}

function chunk(type, data) {
    const head = Buffer.alloc(8);
    head.writeUInt32BE(data.length, 0);
    head.write(type, 4, 'ascii');
    const crc = Buffer.alloc(4);
    crc.writeUInt32BE(crc32(Buffer.concat([head.subarray(4), data])), 0);
    return Buffer.concat([head, data, crc]);
}

function encodePng(rgba, width, height) {
    const ihdr = Buffer.alloc(13);
    ihdr.writeUInt32BE(width, 0);
    ihdr.writeUInt32BE(height, 4);
    ihdr[8] = 8;  // ビット深度
    ihdr[9] = 6;  // カラータイプ: RGBA
    // 圧縮方式・フィルタ方式・インタレースはすべて既定 (0)

    // 各走査線の先頭にフィルタタイプ 0 (フィルタなし) を付ける
    const stride = width * 4;
    const raw = Buffer.alloc((stride + 1) * height);
    for (let y = 0; y < height; y++) {
        raw[y * (stride + 1)] = 0;
        rgba.copy(raw, y * (stride + 1) + 1, y * stride, (y + 1) * stride);
    }

    return Buffer.concat([
        Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
        chunk('IHDR', ihdr),
        chunk('IDAT', zlib.deflateSync(raw, { level: 9 })),
        chunk('IEND', Buffer.alloc(0)),
    ]);
}

for (const name of ICONS) {
    const svg = fs.readFileSync(path.join(__dirname, `${name}.svg`), 'utf8');
    const rendered = new Resvg(svg, { fitTo: { mode: 'width', value: SIZE } }).render();
    const png = encodePng(rendered.pixels, rendered.width, rendered.height);
    fs.writeFileSync(path.join(__dirname, `${name}.png`), png);
    console.log(`// ${name} (${png.length} bytes)`);
    console.log(`"${png.toString('base64')}",`);
    console.log();
}
```

- [ ] **Step 5: 依存を入れてアイコンを生成する**

```bash
cd assets/icons
npm install
node generate.js
```

期待: `BgObject.png` が生成され、標準出力に `// BgObject (NNNN bytes)` と base64 文字列が出る。
生成された `BgObject.png` を目視で開き、立方体に見えることを確認する。

- [ ] **Step 6: `PluginInfo.cs` にアイコンを埋め込む**

`source/COM3D2.ModItemExplorer.Plugin/PluginInfo.cs` の既存 `UpdateIcon` の
プロパティ定義のあとに追記する。**base64 は Step 5 の出力を貼ること**
(下記の `<Step 5 の出力をここに貼る>` を実際の文字列に置き換える)。
既存アイコンに倣い、長い base64 は 76 文字前後で `+` 連結して折り返す。

```csharp
        /// <summary>
        /// 背景オブジェクト (nei 由来) のタイル用アイコン。128x128 PNG (base64)。
        /// 差し替えるときは assets/icons/generate.js を実行して出力を貼り替えること
        /// </summary>
        public readonly static byte[] BgObjectIcon = Convert.FromBase64String(
            "<Step 5 の出力をここに貼る>");

        private static Texture2D _bgObjectIconTexture = null;
        public static Texture2D BgObjectIconTexture
        {
            get
            {
                if (_bgObjectIconTexture == null)
                {
                    _bgObjectIconTexture = new Texture2D(1, 1);
                    _bgObjectIconTexture.LoadImage(BgObjectIcon);
                }
                return _bgObjectIconTexture;
            }
        }
```

- [ ] **Step 7: ビルド確認 (COM3D2.5 版)**

```bash
cd source/COM3D2.ModItemExplorer.Plugin
"C:/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe" \
    COM3D2.ModItemExplorer.Plugin.csproj -p:Configuration=Debug -p:GameVersion=COM3D25 -v:minimal -nologo
```

期待: 警告・エラーなしで `... -> ...\bin\Debug\COM3D25\COM3D2.ModItemExplorer.Plugin.dll` が出力される。

- [ ] **Step 8: ビルド確認 (COM3D2 2.0 版)**

```bash
"C:/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe" \
    COM3D2.ModItemExplorer.Plugin.csproj -p:Configuration=Debug -p:GameVersion=COM3D2 -v:minimal -nologo
```

期待: 警告・エラーなし。

- [ ] **Step 9: コミット**

```bash
git add .gitignore assets/icons source/COM3D2.ModItemExplorer.Plugin/PluginInfo.cs
git commit -m "feat(bg-object): 背景オブジェクト用アイコンを追加

SceneEditor と同じ SVG -> PNG(base64) 埋め込み方式。タイルビューが
120x110 のため 128x128 で生成する。"
```

---

## Task 2: nei パース層

**Files:**
- Create: `source/COM3D2.ModItemExplorer.Plugin/BgObject/BgObjectInfo.cs`
- Create: `source/COM3D2.ModItemExplorer.Plugin/BgObject/BgObjectNeiLoader.cs`

**Interfaces:**
- Consumes: なし
- Produces:
  - `class BgObjectInfo { string category; string name; string assetBundleName; string neiFilePath; }`
  - `static List<BgObjectInfo> BgObjectNeiLoader.LoadAll()` — Task 3 の `ModItemManager` が使う

- [ ] **Step 1: `BgObjectInfo.cs` を作る**

```csharp
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
```

- [ ] **Step 2: `BgObjectNeiLoader.cs` を作る**

`CsvParser` はゲーム内蔵のネイティブラッパー。`GameUty.FileSystemMod` は Mod 配下を
**ファイル名のみのフラットな索引**で引けるため、ディスク上のパスからファイル名だけ取り出して渡す。
ワーカースレッドから呼んでも動くことは実機で確認済み。

```csharp
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
```

**注意:** `ColumnPrefabName` は現状どこからも参照しないが、公式の列順との対応を
コード上に残すために定義している。未使用警告が出る場合は定数なので問題にならない
(C# の `const` は未使用でも警告にならない)。

- [ ] **Step 3: csproj にファイルが含まれることを確認する**

`COM3D2.ModItemExplorer.Plugin.csproj` の `<Compile Include=...>` が明示列挙形式なら
新規 2 ファイルを追加する。ワイルドカード形式なら何もしなくてよい。

```bash
grep -n "Compile Include" source/COM3D2.ModItemExplorer.Plugin/COM3D2.ModItemExplorer.Plugin.csproj | head -5
```

明示列挙の場合、`ModItemBase.cs` の行の近くに以下を追加する。

```xml
    <Compile Include="BgObject\BgObjectInfo.cs" />
    <Compile Include="BgObject\BgObjectNeiLoader.cs" />
```

- [ ] **Step 4: ビルド確認 (両版)**

```bash
cd source/COM3D2.ModItemExplorer.Plugin
"C:/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe" \
    COM3D2.ModItemExplorer.Plugin.csproj -p:Configuration=Debug -p:GameVersion=COM3D25 -v:minimal -nologo
"C:/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe" \
    COM3D2.ModItemExplorer.Plugin.csproj -p:Configuration=Debug -p:GameVersion=COM3D2 -v:minimal -nologo
```

期待: 両方とも警告・エラーなし。
`CsvParser` / `PluginData` / `GameUty.FileSystemMod` はどちらのゲーム版にも存在するため
`#if COM3D25` の分岐は不要。もしここで 2.0 版だけ型解決に失敗したら、
その事実を報告して指示を仰ぐこと (分岐を勝手に入れない)。

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.ModItemExplorer.Plugin/BgObject source/COM3D2.ModItemExplorer.Plugin/COM3D2.ModItemExplorer.Plugin.csproj
git commit -m "feat(bg-object): nei パース層を追加

Mod 配下の *_photo_bg_object_list.nei を CsvParser で読み、
アセットバンドル名をキーに BgObjectInfo として列挙する。"
```

---

## Task 3: ツリーへの登録

**Files:**
- Create: `source/COM3D2.ModItemExplorer.Plugin/BgObject/BgObjectItem.cs`
- Modify: `source/COM3D2.ModItemExplorer.Plugin/ModItemBase.cs:10-20` (`ModItemType`)
- Modify: `source/COM3D2.ModItemExplorer.Plugin/Manager/ModItemManager.cs`

**Interfaces:**
- Consumes: `BgObjectNeiLoader.LoadAll()`、`BgObjectInfo`、`PluginInfo.BgObjectIconTexture`
- Produces:
  - `class BgObjectItem : ModItemBase { BgObjectInfo info { get; set; } }`
  - `ModItemType.BgObject`
  - `ModItemManager.LoadModBgObjectItems()` (private)

- [ ] **Step 1: `ModItemType` に `BgObject` を追加**

`source/COM3D2.ModItemExplorer.Plugin/ModItemBase.cs` の `enum ModItemType` の
`Anm,` の後ろに追加する (既存の値の順序は変えない。設定ファイルに数値で保存されている可能性があるため)。

```csharp
    public enum ModItemType
    {
        Dir,
        Official,
        Mod,
        Equipped,
        Preset,
        TempPreset,
        Model,
        Anm,
        BgObject,
    }
```

- [ ] **Step 2: `BgObjectItem.cs` を作る**

`thum` の setter を `_thum = value;` だけにするのが重要。`TileViewContentBase` の既定 setter は
古いテクスチャを `Destroy` するため、そのままだと全アイテムで共有している
`PluginInfo.BgObjectIconTexture` を壊す (`AnmItem` が同じ理由で同じ書き方をしている)。

```csharp
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
```

- [ ] **Step 3: `ModItemManager` に `LoadState` を追加**

`Manager/ModItemManager.cs` の `enum LoadState` の `LoadModItems,` の直後に追加する。

```csharp
            LoadModItems,
            LoadModBgObjectItems,
            UpdateModPresetItems,
```

- [ ] **Step 4: `LoadModBgObjectItems` を実装**

`LoadModItems` メソッドの直後 (`ModItemManager.cs:1382` 付近) に追加する。
`UpdateModAnmItems` と同じ形でツリーに登録する。

```csharp
        private void LoadModBgObjectItems()
        {
            MTEUtils.LogDebug("[ModMenuItemManager] LoadModBgObjectItems");
            loadState = LoadState.LoadModBgObjectItems;

            // 今回のロードで生き残った itemPath。取りこぼしを後で掃除するために覚えておく
            var alivePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var infoList = BgObjectNeiLoader.LoadAll();
            foreach (var info in infoList)
            {
                try
                {
                    // nei と同じフォルダに展開する。既存の Mod ツリー(実フォルダ構造)と揃える
                    var relativeDir = Path.GetDirectoryName(
                        GetRelativePath(MTEUtils.ModDirPath, info.neiFilePath));
                    var itemPath = MTEUtils.CombinePaths(ModDirName, relativeDir, info.name);

                    // 同一フォルダで表示名が衝突すると後勝ちで消えてしまうため、
                    // 一意なアセットバンドル名を足して逃がす。
                    // 前回ロード分の自分自身は衝突扱いにしない
                    var existing = GetItemByPath<ModItemBase>(itemPath);
                    if (existing != null && !(existing is BgObjectItem))
                    {
                        MTEUtils.LogWarning(
                            "背景オブジェクトの名前が重複しています。{0} ({1})",
                            info.name, info.assetBundleName);
                        itemPath = MTEUtils.CombinePaths(
                            ModDirName, relativeDir, info.name + "_" + info.assetBundleName);
                    }

                    if (GetOrCreateBgObjectItem(itemPath, info) != null)
                    {
                        alivePaths.Add(itemPath);
                    }
                }
                catch (Exception e)
                {
                    MTEUtils.LogException(e);
                }
            }

            RemoveStaleBgObjectItems(alivePaths);
        }

        /// <summary>
        /// 今回の nei に現れなかった背景オブジェクトのアイテムをツリーから消す。
        ///
        /// BgObjectItem の fullPath は nei ファイル自体を指すため、MOD 側が nei の行だけを
        /// 削除・リネームしても ValidateItemFile の File.Exists は通ってしまい、
        /// 旧アイテムがゴーストとしてツリーに残る。ここで明示的に掃除する
        /// </summary>
        private void RemoveStaleBgObjectItems(HashSet<string> alivePaths)
        {
            var staleItems = new List<ModItemBase>();
            foreach (var pair in _itemPathMap)
            {
                if (pair.Value is BgObjectItem && !alivePaths.Contains(pair.Key))
                {
                    staleItems.Add(pair.Value);
                }
            }

            foreach (var item in staleItems)
            {
                MTEUtils.LogDebug("[ModMenuItemManager] 背景オブジェクトを削除: " + item.itemPath);
                RemoveItem(item);
            }
        }
```

**注意:** `_itemPathMap` を列挙しながら `RemoveItem` すると
`InvalidOperationException` になるため、必ず一度リストに集めてから消すこと。

- [ ] **Step 5: `GetOrCreateBgObjectItem` を実装**

`GetOrCreateAnmItem` (`ModItemManager.cs:2404` 付近) の直後に追加する。
`fullPath` に **nei ファイルのフルパス**を入れるのが重要。`ValidateItemFile` が
`fullPath` 非空のアイテムを `File.Exists` で生存確認して消すため、
フォルダを入れるとロードのたびに消える。

```csharp
        private BgObjectItem GetOrCreateBgObjectItem(string itemPath, BgObjectInfo info)
        {
            if (string.IsNullOrEmpty(itemPath) || info == null)
            {
                return null;
            }

            var item = GetItemByPath<BgObjectItem>(itemPath);
            if (item != null)
            {
                item.info = info;
                return item;
            }

            var parentPath = Path.GetDirectoryName(itemPath);
            var parentItem = GetOrCreateDirItem(parentPath);
            if (parentItem == null)
            {
                MTEUtils.LogWarning("親ディレクトリが見つかりません。" + parentPath);
                return null;
            }

            var itemName = Path.GetFileName(itemPath);

            item = new BgObjectItem
            {
                name = info.name,
                itemName = itemName,
                itemPath = itemPath,
                itemType = ModItemType.BgObject,
                // ValidateItemFile が File.Exists で生存確認するため、
                // フォルダではなく nei ファイル自体のパスを入れる
                fullPath = info.neiFilePath,
                info = info,
            };

            parentItem.AddChild(item);
            _itemPathMap[itemPath] = item;
            _itemNameMap[itemName] = item;

            return item;
        }
```

- [ ] **Step 6: `Load()` のロード順に組み込む**

`ModItemManager.Load()` (`ModItemManager.cs:315` 付近) の
`LoadModItems("mod_*.mod");` の直後に 1 行足す。

```csharp
                        LoadModItems("*.menu");
                        LoadModItems("mod_*.mod");
                        LoadModBgObjectItems();
                        UpdateModPresetItems();
```

- [ ] **Step 7: csproj に `BgObjectItem.cs` を追加**

Task 2 Step 3 で明示列挙形式だった場合のみ、同じ場所に追加する。

```xml
    <Compile Include="BgObject\BgObjectItem.cs" />
```

- [ ] **Step 8: ビルド確認 (両版)**

Task 2 Step 4 と同じ 2 コマンドを実行する。期待: 警告・エラーなし。

- [ ] **Step 9: コミット**

```bash
git add source/COM3D2.ModItemExplorer.Plugin
git commit -m "feat(bg-object): nei 由来の背景オブジェクトをツリーへ登録する

nei と同じフォルダに展開し、専用アイコン付きのタイルとして並べる。"
```

---

## Task 4: アセットバンドルの読み込みと配置

**Files:**
- Create: `source/COM3D2.ModItemExplorer.Plugin/BgObject/BgObjectAssetLoader.cs`
- Modify: `source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/SelfModelPlacer.cs`
- Modify: `source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/ModelPlacerManager.cs`

**Interfaces:**
- Consumes: `BgObjectInfo`
- Produces:
  - `static GameObject BgObjectAssetLoader.LoadPrefab(string assetBundleName)`
  - `const string BgObjectAssetLoader.AssetBgExtension = ".asset_bg"`
  - `StudioModelStatWrapper SelfModelPlacer.CreateBgObject(string assetBundleName, int group, bool visible)`
  - `void ModelPlacerManager.CreateBgObject(string assetBundleName, int group, bool visible)`

- [ ] **Step 1: `BgObjectAssetLoader.cs` を作る**

`GameMain.Instance.BgMgr.CreateAssetBundle` は使えない。あれは `GameUty.BgFiles`
(システム側のみ) しか見ず、Mod 配下の `.asset_bg` には届かない (実機で null 確認済み)。

```csharp
using System;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.ModItemExplorer.Plugin
{
    /// <summary>
    /// Mod 配下の .asset_bg アセットバンドルを読み、prefab を取り出す。
    ///
    /// BgMgr.CreateAssetBundle は GameUty.BgFiles (システム側のみ) しか見ないため
    /// Mod 配下のバンドルには届かない。そこで FileSystemMod から自前で読む。
    ///
    /// 同じバンドルを二重に LoadFromMemory すると
    /// 「同じファイルを含む AssetBundle が既にロード済み」で例外になるため、
    /// キャッシュは高速化ではなく正しさのために必須。
    /// ロードしたバンドルはアンロードしない (公式 BgMgr.asset_bundle_dic と同じ方針)
    /// </summary>
    public static class BgObjectAssetLoader
    {
        public const string AssetBgExtension = ".asset_bg";

        private static readonly Dictionary<string, GameObject> _prefabCache
            = new Dictionary<string, GameObject>(16, StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// アセットバンドル名 (拡張子なし) から prefab を返す。失敗時は null。
        /// 返る GameObject はバンドル所有の原本なので、呼び出し側は Instantiate して使い、
        /// これ自体を Destroy してはいけない
        /// </summary>
        public static GameObject LoadPrefab(string assetBundleName)
        {
            if (string.IsNullOrEmpty(assetBundleName))
            {
                return null;
            }

            GameObject cached;
            if (_prefabCache.TryGetValue(assetBundleName, out cached) && cached != null)
            {
                return cached;
            }

            try
            {
                var fileName = assetBundleName + AssetBgExtension;
                if (!GameUty.FileSystemMod.IsExistentFile(fileName))
                {
                    MTEUtils.LogWarning("アセットバンドルが見つかりません。{0}", fileName);
                    return null;
                }

                byte[] buffer;
                using (var file = GameUty.FileSystemMod.FileOpen(fileName))
                {
                    if (file == null || !file.IsValid())
                    {
                        MTEUtils.LogWarning("アセットバンドルが開けません。{0}", fileName);
                        return null;
                    }
                    buffer = file.ReadAll();
                }

                var assetBundle = AssetBundle.LoadFromMemory(buffer);
                if (assetBundle == null)
                {
                    MTEUtils.LogWarning("アセットバンドルの読み込みに失敗しました。{0}", fileName);
                    return null;
                }

                var assets = assetBundle.LoadAllAssets<GameObject>();
                if (assets == null || assets.Length == 0)
                {
                    MTEUtils.LogWarning("アセットバンドルにGameObjectがありません。{0}", fileName);
                    return null;
                }

                // 公式 BgMgr も mainAsset が無ければ先頭を使うが、複数入っている場合の
                // 並び順は Unity が保証していない。意図しないものを掴んだ疑いを追えるよう知らせる
                if (assets.Length > 1)
                {
                    MTEUtils.LogWarning(
                        "アセットバンドルに複数のGameObjectがあります。先頭を使います。{0} (count={1}, name={2})",
                        fileName, assets.Length, assets[0].name);
                }

                _prefabCache[assetBundleName] = assets[0];
                return assets[0];
            }
            catch (Exception e)
            {
                MTEUtils.LogWarning("アセットバンドルの読み込みに失敗しました。{0}", assetBundleName);
                MTEUtils.LogException(e);
                return null;
            }
        }
    }
}
```

- [ ] **Step 2: `SelfModelPlacer.CreateBgObject` を実装**

`CreateModel` メソッド (`SelfModelPlacer.cs:577-657`) の直後に追加する。

`disposables` を積まないのが最重要。Mesh/Material はバンドル所有なので、
削除時に `Destroy` すると同バンドルから作った他インスタンスと
次回以降の `Instantiate` が壊れる。`_disposables` には空リストを入れて
`DeleteModel` 側の `TryGetValue` の形を既存と揃える。

```csharp
        /// <summary>
        /// nei 由来の背景オブジェクト (.asset_bg) をシーンに配置し、生成したモデルを返す（失敗時は null）。
        /// ラッパー生成以降は CreateModel と同じ扱いにする
        /// </summary>
        public StudioModelStatWrapper CreateBgObject(string assetBundleName, int group, bool visible)
        {
            GameObject modelGo = null;
            GameObject wrapperGo = null;

            try
            {
                var prefab = BgObjectAssetLoader.LoadPrefab(assetBundleName);
                if (prefab == null)
                {
                    return null;
                }

                modelGo = UnityEngine.Object.Instantiate(prefab);
                SetLayerRecursively(modelGo, GetModelLayer());

                var fileName = assetBundleName + BgObjectAssetLoader.AssetBgExtension;
                var resolvedGroup = ResolveGroup(fileName, group);
                var modelName = GetModelName(fileName, resolvedGroup);
                wrapperGo = new GameObject(modelName);
                wrapperGo.transform.SetParent(GetOrCreateParent().transform, false);
                wrapperGo.transform.position = GetDefaultPosition();
                modelGo.transform.SetParent(wrapperGo.transform, false);
                modelGo.transform.localPosition = Vector3.zero;
                modelGo.transform.localRotation = Quaternion.identity;
                modelGo.transform.localScale = Vector3.one;

                AddGizmo(wrapperGo);
                wrapperGo.SetActive(visible);

                var wrapper = new StudioModelStatWrapper
                {
                    original = null,
                    group = resolvedGroup,
                    name = modelName,
                    displayName = modelName,
                    obj = wrapperGo,
                    pluginName = PluginName,
                    visible = visible,
                    infoWrapper = new OfficialObjectInfoWrapper
                    {
                        fileName = fileName,
                        label = fileName,
                    },
                };

                _models.Add(wrapper);
                // Mesh/Material はアセットバンドル所有のため破棄対象に積まない。
                // 破棄すると同じバンドルから作った他インスタンスまで壊れる
                _disposables[wrapper] = new List<UnityEngine.Object>();

                selectedModel = wrapper;

                history.RegisterCreate(wrapper, history.TryCaptureState(wrapper));

                return wrapper;
            }
            catch (Exception e)
            {
                MTEUtils.LogWarning("背景オブジェクトの配置に失敗しました。{0}", assetBundleName);
                MTEUtils.LogException(e);

                if (wrapperGo != null)
                {
                    UnityEngine.Object.Destroy(wrapperGo);
                }
                else if (modelGo != null)
                {
                    UnityEngine.Object.Destroy(modelGo);
                }

                return null;
            }
        }

        /// <summary>
        /// GameObject とその全子孫のレイヤーを設定する。
        /// menu 経路 (ModelMeshLoader) が生成したボーンごとにレイヤーを設定しているのに合わせる
        /// </summary>
        private static void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
            {
                SetLayerRecursively(child.gameObject, layer);
            }
        }
```

- [ ] **Step 3: `ModelPlacerManager` に窓口を足す**

`ModItemManager` は配置先を必ずファサード (`ModelPlacerManager`) 経由で呼んでおり、
`SelfModelPlacer.instance` を直接触っていない。背景オブジェクトだけ直接呼ぶと
今後ファサードに共通処理が足された時にすり抜けるため、ここにも窓口を用意する。

`source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/ModelPlacerManager.cs` の
`CreateModel` の直後に追加する。

```csharp
        /// <summary>
        /// 背景オブジェクト (.asset_bg) を配置する。
        /// MTE / StudioMode の配置経路は .menu 名を渡す前提でアセットバンドルを扱えないため、
        /// 配置先は常に自前配置になる
        /// </summary>
        public void CreateBgObject(string assetBundleName, int group, bool visible)
        {
            selfPlacer.CreateBgObject(assetBundleName, group, visible);
        }
```

- [ ] **Step 4: ビルド確認 (両版)**

Task 2 Step 4 と同じ 2 コマンドを実行する。期待: 警告・エラーなし。

- [ ] **Step 5: csproj に `BgObjectAssetLoader.cs` を追加してビルドし直す**

Task 2 Step 3 で明示列挙形式だった場合のみ追加する。

```xml
    <Compile Include="BgObject\BgObjectAssetLoader.cs" />
```

追加したら Step 4 のビルドを再実行する。

- [ ] **Step 6: コミット**

```bash
git add source/COM3D2.ModItemExplorer.Plugin
git commit -m "feat(bg-object): .asset_bg を読み込んで配置する経路を追加

BgMgr.CreateAssetBundle は Mod 配下を見ないため FileSystemMod から自前で読む。
二重ロードは例外になるためバンドルはキャッシュし、Mesh/Material は
バンドル所有なので破棄対象に積まない。"
```

---

## Task 5: プリセット・undo/redo での復元

**Files:**
- Modify: `source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/SelfModelPlacer.cs:1226-1240` (`RestoreModel`)

**Interfaces:**
- Consumes: `SelfModelPlacer.CreateBgObject`、`BgObjectAssetLoader.AssetBgExtension`
- Produces: なし (既存メソッドの挙動拡張)

プリセットのフォーマットは変えない。`ModelPlacementPresetItem.fileName` に入る
`<assetBundleName>.asset_bg` の**拡張子そのもの**を種別の判別に使う。
`RestoreModel` はプリセット復元と操作履歴の undo/redo が通る唯一の経路なので、
ここで分岐すれば履歴も追加対応なしで動く。

- [ ] **Step 1: `RestoreModel` に分岐を入れる**

現在の実装:

```csharp
        internal StudioModelStatWrapper RestoreModel(ModelPlacementPresetItem item)
        {
            // 保存時と同じ生成経路を再実行してから Transform を適用する
            var wrapper = CreateModel(item.fileName, item.group, item.visible);
            if (wrapper?.obj as GameObject == null)
            {
                return null;
            }
```

これを次に置き換える。

```csharp
        internal StudioModelStatWrapper RestoreModel(ModelPlacementPresetItem item)
        {
            // 保存時と同じ生成経路を再実行してから Transform を適用する。
            // 背景オブジェクトは fileName の拡張子で見分ける
            // (ModItemManager.GetMenu が .menu/.mod を拡張子で見分けているのと同じ流儀)
            var wrapper = IsBgObjectFileName(item.fileName)
                ? CreateBgObject(GetAssetBundleName(item.fileName), item.group, item.visible)
                : CreateModel(item.fileName, item.group, item.visible);
            if (wrapper?.obj as GameObject == null)
            {
                return null;
            }
```

- [ ] **Step 2: 判別用のヘルパーを追加**

`RestoreModel` の直前に追加する。

```csharp
        /// <summary>配置データのファイル名が背景オブジェクト (.asset_bg) を指すか</summary>
        internal static bool IsBgObjectFileName(string fileName)
        {
            return !string.IsNullOrEmpty(fileName)
                && fileName.EndsWith(
                    BgObjectAssetLoader.AssetBgExtension, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>"xxx.asset_bg" からアセットバンドル名 "xxx" を取り出す</summary>
        private static string GetAssetBundleName(string fileName)
        {
            return Path.GetFileNameWithoutExtension(fileName);
        }
```

- [ ] **Step 3: ビルド確認 (両版)**

Task 2 Step 4 と同じ 2 コマンドを実行する。期待: 警告・エラーなし。

- [ ] **Step 4: コミット**

```bash
git add source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/SelfModelPlacer.cs
git commit -m "feat(bg-object): プリセットと undo/redo で背景オブジェクトを復元する

RestoreModel で fileName の .asset_bg 拡張子を見て分岐する。
プリセットのフォーマットとバージョンは変えない。"
```

---

## Task 6: UI 配線

**Files:**
- Modify: `source/COM3D2.ModItemExplorer.Plugin/Manager/ModItemManager.cs:799-846` (`CreateModel`)
- Modify: `source/COM3D2.ModItemExplorer.Plugin/ModItemWindow.cs:968-973` (配置ボタン)
- Modify: `source/COM3D2.ModItemExplorer.Plugin/ModItemWindow.cs:1769-1786` (`CreateSelectedModel`)
- Modify: `source/COM3D2.ModItemExplorer.Plugin/ModItemWindow.cs:1920-1936` (フッターのツールチップ)

**Interfaces:**
- Consumes: `BgObjectItem`、`SelfModelPlacer.CreateBgObject`
- Produces: `ModItemManager.CreateModel(ModItemBase item, string pluginName)` (シグネチャ変更)

- [ ] **Step 1: `ModItemManager.CreateModel` を一般化**

現在の `public void CreateModel(MenuItem item, string pluginName)` (`ModItemManager.cs:799`) を
`ModItemBase` 受けに変え、先頭で分岐させる。既存の `MenuItem` の処理本体は
`CreateMenuModel` に切り出してそのまま残す。

```csharp
        public void CreateModel(ModItemBase item, string pluginName)
        {
            if (item == null || string.IsNullOrEmpty(pluginName))
            {
                return;
            }

            if (item is BgObjectItem bgObjectItem)
            {
                CreateBgObjectModel(bgObjectItem, pluginName);
                return;
            }

            if (item is MenuItem menuItem)
            {
                CreateMenuModel(menuItem, pluginName);
            }
        }

        /// <summary>
        /// 背景オブジェクトを配置する。
        /// MTE / StudioMode の配置経路は .menu 名を渡す前提でアセットバンドルを扱えないため、
        /// 配置プラグインの選択に関わらず自前配置を使う
        /// </summary>
        private void CreateBgObjectModel(BgObjectItem item, string pluginName)
        {
            if (item.info == null)
            {
                MTEUtils.LogWarning("背景オブジェクトの情報がありません。" + item.itemPath);
                return;
            }

            try
            {
                if (pluginName != SelfModelPlacer.PluginName)
                {
                    MTEUtils.Log(
                        "背景オブジェクトは{0}でのみ配置できます。{1}",
                        SelfModelPlacer.PluginName, pluginName);
                }

                modelPlacerManager.CreateBgObject(item.info.assetBundleName, 0, true);

                UpdateModelItems();
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }
        }

        private void CreateMenuModel(MenuItem item, string pluginName)
        {
            try
            {
                // ... 既存 CreateModel の try ブロックの中身をそのまま移す ...
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }
        }
```

**注意:** `CreateMenuModel` の中身は既存 `CreateModel` の `try { ... } catch { ... }` を
**そのまま**移すこと。`var group = 0;` から `UpdateModelItems();` までが対象で、
先頭の `if (item == null || string.IsNullOrEmpty(pluginName)) return;` は
新しい `CreateModel` 側へ移したので `CreateMenuModel` には残さない。

- [ ] **Step 2: 配置ボタンの活性条件を一般化**

`ModItemWindow.cs:188` の `selectedMenuItem` の近くに、配置可能判定を足す。

```csharp
        private MenuItem selectedMenuItem => selectedItem as MenuItem;

        /// <summary>配置できるアイテムが選択されているか。menu アイテムと背景オブジェクトが対象</summary>
        private bool canCreateSelectedModel
            => selectedItem is MenuItem || selectedItem is BgObjectItem;
```

`DrawModelPlacementRow` (`ModItemWindow.cs:970` 付近) の配置ボタンを差し替える。

```csharp
                // 配置対象が決まらないため、アイテム未選択時は押せないようにする
                else if (view.DrawButton("配置", 50, 20, canCreateSelectedModel))
                {
                    CreateSelectedModel();
                }
```

- [ ] **Step 3: `CreateSelectedModel` の受け渡しを変更**

`ModItemWindow.cs:1779` の 1 行を差し替える。

```csharp
            modItemManager.CreateModel(selectedItem, pluginName);
```

- [ ] **Step 4: フッターのツールチップに分岐を追加**

`ModItemWindow.cs:1922` 付近の `if (_mouseOverItem is MenuItem menuItem)` ブロックの
直後に `else if` を挿す。

```csharp
                if (_mouseOverItem is MenuItem menuItem)
                {
                    var text = $"{menuItem.name} {menuItem.setumei}".Replace("\n", " ");
                    view.DrawLabel(text, -1, 20);
                }
                else if (_mouseOverItem is BgObjectItem bgObjectItem)
                {
                    var text = $"{bgObjectItem.name} [{bgObjectItem.tag}]";
                    view.DrawLabel(text, -1, 20);
                }
                else if (_mouseOverItem.itemType == ModItemType.Dir ||
```

- [ ] **Step 5: ビルド確認 (両版)**

Task 2 Step 4 と同じ 2 コマンドを実行する。期待: 警告・エラーなし。

`CreateModel` のシグネチャを変えたため、他に呼び出し元が残っていないか確認する。

```bash
grep -rn "modItemManager.CreateModel\|\.CreateModel(" --include=*.cs source/COM3D2.ModItemExplorer.Plugin
```

期待: `ModItemWindow.cs:1779` と `ModItemManager.cs` 内、`ModelPlacerManager` /
`ModelHackManagerWrapper` / `SelfModelPlacer` の各 `CreateModel` (別クラスなので無関係) のみ。

- [ ] **Step 6: コミット**

```bash
git add source/COM3D2.ModItemExplorer.Plugin
git commit -m "feat(bg-object): 背景オブジェクトを配置 UI へ配線する

CreateModel を ModItemBase 受けに一般化し、配置ボタンの活性条件と
フッターのツールチップを背景オブジェクトに対応させる。"
```

---

## Task 7: 実機検証とドキュメント更新

**Files:**
- Modify: `README.md` (変更履歴)

**Interfaces:**
- Consumes: Task 1〜6 の全て
- Produces: なし

このタスクだけ**ゲームへのデプロイを伴う**。`debug.bat` はゲームフォルダへ DLL を
コピーするので、ここで初めて使う。

- [ ] **Step 1: ゲームを終了してもらう**

ユーザーに COM3D2.5 を終了してもらう。起動中だと DLL のコピーが失敗する。
**`deploy.bat` は絶対に実行しない** (GitHub Releases への本番公開)。

- [ ] **Step 2: ビルドしてデプロイ**

```bash
./debug.bat com3d25
```

期待: `COM3D2.5 へデプロイしました` と `ビルドに成功しました` が出る。

- [ ] **Step 3: ゲームを起動して確認してもらう**

ユーザーに COM3D2.5 を起動し、スタジオモードで ModItemExplorer を開いてもらう。
その後 `com3d25-devbridge` の `ping` で接続を確認してから、以下を順に検証する。

1. **ツリー表示** — `Mod/ゆかぺろ/手術台のような何か/mod/手術台のような何か/PhotoBG_OBJ_NEI/` に
   6 件が並び、専用アイコン (立方体) がタイル 120x110 でぼやけずに出る
2. **検索** — 検索バーに「手術台」を入れて 6 件がヒットする
3. **配置** — 6 件それぞれを選んで「配置」ボタンで置き、描画されること・
   ギズモで移動/回転/拡縮できることを確認する
4. **同一オブジェクトの 2 個目** — 同じアイテムをもう一度配置してもエラーにならない
   (アセットバンドル二重ロード回避の確認)。BepInEx ログに例外が出ていないことも見る
5. **削除** — 配置済みの 1 つを削除しても、残った同バンドル由来のモデルが
   白抜け・消失しないこと (Mesh/Material を破棄していないことの確認)
6. **プリセット** — 保存 → 全削除 → 復元で位置・回転・スケールが再現される
7. **undo/redo** — 配置直後に undo で消え、redo で元の位置に戻る
8. **既存回帰** — 従来の `.menu` アイテムの配置・削除・プリセット保存/復元・undo/redo が
   壊れていない
9. **既存プリセット** — 変更前に保存した `.menu` のみのプリセットがそのまま読める
10. **nei を消すとツリーからも消える** — 対象 nei ファイルを一時的に別名へ退避してから
    設定画面の「アイテム更新」を押し、6 件がツリーから消えることを確認する。
    その後 nei を元に戻して再度「アイテム更新」を押し、6 件が戻ることを確認する

**item 10 の限界:** これは `RemoveStaleBgObjectItems` と既存の `ValidateItemFile` の
どちらでも消えるため、`RemoveStaleBgObjectItems` 単体の証明にはならない。
本来狙っている「nei ファイルは残ったまま行だけ消える/リネームされる」ケースは
nei の再暗号化ができないため手動では作れない。
その分岐はコードレビューで担保し、実機検証では item 10 を回帰確認として使う。

ログ確認には `mcp__com3d25-devbridge__tail_log` を使う。

- [ ] **Step 4: 問題があれば該当タスクへ戻る**

検証で不具合が出たら、原因のタスクの実装を直してから Step 2 からやり直す。
勝手に仕様を変えず、設計と食い違う挙動が見つかった場合は報告して指示を仰ぐ。

- [ ] **Step 5: README の変更履歴を更新**

`README.md` の目次と変更履歴に新バージョンの節を足す。既存の書式に揃えること。
バージョン番号は `bump-version.bat` の運用に従い、ユーザーに確認してから決める。

追記する内容:

```markdown
### 2026/08/21 v<新バージョン>

- Modフォルダの `*_photo_bg_object_list.nei` に対応
  - nei で宣言された背景オブジェクト（`.asset_bg`）をツリーに表示し、配置できるようにしました
  - MaidLoader は不要です
```

- [ ] **Step 6: コミット**

```bash
git add README.md
git commit -m "docs: 背景オブジェクト対応を変更履歴へ追記"
```

---

## Self-Review

**1. Spec coverage**

| Spec セクション | 対応タスク |
|---|---|
| Section 1: nei パース層 | Task 2 |
| Section 2: アイテムツリー | Task 3 |
| Section 2 サムネイル | Task 1 (アイコン生成) + Task 3 Step 2 (`thum`) |
| Section 2.5: 専用アイコンの用意 | Task 1 |
| Section 2 ロード順への組み込み | Task 3 Step 3, 4, 6 |
| Section 3: AssetBundle キャッシュ | Task 4 Step 1 |
| Section 3: `CreateBgObject` | Task 4 Step 2 |
| Section 3: 配置先プラグインの制限 | Task 6 Step 1 (`CreateBgObjectModel`) |
| Section 4: プリセット・操作履歴 | Task 5 |
| Section 5: UI | Task 6 |
| エラーハンドリング方針 | 全タスク (Global Constraints に明記) |
| テスト・検証 | Task 7 Step 3 (9 項目すべて) |

ギャップなし。

**2. Placeholder scan**

`<Step 5 の出力をここに貼る>` (Task 1 Step 6) と
`... 既存 CreateModel の try ブロックの中身をそのまま移す ...` (Task 6 Step 1) の 2 箇所は、
前者が同タスク内の生成物、後者が既存コードの移設で、どちらも直前のステップで
実物が手に入るため意図的に残している。それ以外の TBD/TODO はなし。

**3. Type consistency**

- `BgObjectInfo` のフィールド名 `category` / `name` / `assetBundleName` / `neiFilePath` は
  Task 2 の定義と Task 3・Task 6 の参照で一致
- `BgObjectNeiLoader.LoadAll()` の戻り値 `List<BgObjectInfo>` は Task 3 Step 5 の使用と一致
- `BgObjectAssetLoader.LoadPrefab(string)` / `AssetBgExtension` は Task 4・Task 5 で一致
- `SelfModelPlacer.CreateBgObject(string, int, bool)` は Task 4 の定義、
  Task 5 Step 1、Task 6 Step 1 の呼び出しで一致
- `PluginInfo.BgObjectIconTexture` は Task 1 の定義と Task 3 Step 2 の参照で一致
- `ModItemManager.CreateModel(ModItemBase, string)` は Task 6 Step 1 の定義と
  Step 3 の呼び出しで一致

なお当初 spec にあった `ModItemManager.GetBgObjectInfo(string)` と
`_bgObjectMap` は、Task 5 が拡張子判別方式になって呼び出し元が無くなったため
計画から落とした (YAGNI)。配置に必要な `assetBundleName` は
`BgObjectItem.info` と `fileName` の両方から取れる。

---

## レビュー却下メモ

`plan-review` (2026-08-21) で挙がった指摘のうち、取り込まなかったもの。

- **同一アセットバンドルを複数の `BgObjectItem` から配置した場合のキャッシュ整合性を
  Task 7 の検証項目に追加すべき** — 誤検知として却下。
  `BgObjectNeiLoader.LoadAll()` が `assetBundleName` で先勝ち重複排除しているため、
  2 つの `BgObjectItem` が同じバンドルを指す状況は原理的に発生しない
  (同名重複時のサフィックス付与も、名前が同じでバンドルが違う行が対象)。
  なお「同じアイテムを 2 回配置する」ケースは Task 7 item 4 でカバー済み。
