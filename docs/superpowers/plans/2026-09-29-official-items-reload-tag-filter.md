# 公式背景オブジェクト・ハンドアイテム / メインウィンドウのモデル再読込 / タグ絞り込み Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
> (このリポジトリのワークフロー規約により subagent-driven-development は使わない)

**Goal:** 機能要望 3 件を実装する。(1) 公式の背景オブジェクト（文房具・グルメ・パーティクル・その他 等）とハンドアイテムを「公式」ツリーに出して配置・装着できるようにする、(2) モデル再読込ボタンをメインウィンドウの「操作」ボタンの右にも置く、(3) 表示中アイテムをタグで絞り込めるようにする。

**Architecture:**
- 公式背景オブジェクトはゲームの `PhotoBGObjectData` から列挙し、既存の `BgObjectItem` に `isOfficial` を足して `Official/背景オブジェクト/<カテゴリ>` へ並べる。配置は SceneEditor 連携用に既にある `SelfModelPlacer.CreateGameModel` を使い、配置中一覧の名前・サムネも既存の背景オブジェクト経路に相乗りさせる。
- ハンドアイテムは `phot_maid_item_list.nei` を自前で読む（2.5 では旧データ側 `GameUty.FileSystemOld` にしか無く、ゲームの `PhotoMaidItemData.Create()` は 2.5 で例外になるため）。menu 本体は通常どおり `MenuInfo` として読み、`MenuItem` 派生の `HandMenuItem` にフォトモードと同じ装着先 MPN を持たせて一時装備（`f_bTemp: true`）で適用する。
- 絞り込みは `ModItemWindow` の描画側だけで完結させる。表示対象（フォルダ or フラットビュー）の子から非フォルダ項目をタグで間引いた `TempDirItem` をキャッシュして `DrawTileView` へ渡す。

**Tech Stack:** C# (.NET Framework 4.7.1) / Unity 2022.3 (2.5) / UnityInjector プラグイン / MSBuild

**Spec:** 本計画の「要件」節（ユーザーとの会話で合意した内容。独立した spec ファイルは無い）

## 要件

1. 公式の背景オブジェクトを全カテゴリ一覧に出す（実機 2.5 で 家具11・道具85・文房具18・グルメ52・ドリンク13・その他114・カジノアイテム58・プレイアイテム25・パーティクル13）。「マイオブジェクト」(ユーザー画像, `direct_file`) は対象外
2. 公式背景オブジェクトはモデルモードで配置できる。配置中一覧では nei の名前と背景オブジェクト共通アイコンで表示する
3. 2.5 で `Prefab/<名前>_for25` が存在するものはそちらを優先して配置する（ゲームの `PhotoBGObjectData.Instantiate` と同じ順）
4. ハンドアイテム（右手・左手・上半身・下半身・前穴・後穴、2.5 実機で計 58 行）を一覧に出す。メイドモードではフォトモードと同じ MPN・一時装備で装着する。各カテゴリの「アイテムなし」(`_del`) 行も出し、外す手段とする
5. ハンドアイテムはモデルモードでは通常の menu モデルとして配置できる
6. メインウィンドウのモデル配置行で「操作」ボタンの右に「モデル再読込」ボタンを置く。モデル操作ウィンドウ側のボタンは残す
7. タグ（アイテム右上に出る文字列。衣装なら部位名、背景オブジェクトならカテゴリ）で表示中のアイテムを絞り込める。単一選択。「すべて」で解除。フォルダは絞り込まず常に出す

## Global Constraints

- **コードのコメントとログメッセージは日本語**で書く（リポジトリ規約）
- **`deploy.bat` / `deploy.ps1` は絶対に実行しない**（GitHub Releases への本番公開。取り消せない）
- **git worktree を使わない。** 作業は常にメインの作業ディレクトリで行う
- ビルド確認は**必ず MSBuild を直接叩く**。`debug.bat` はゲームフォルダへ DLL をコピーするため使わない
  ```bash
  cd /w/COM3D2_5/work/COM3D2.ModItemExplorer.Plugin/source/COM3D2.ModItemExplorer.Plugin
  "C:/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe" \
      COM3D2.ModItemExplorer.Plugin.csproj -p:Configuration=Debug -p:GameVersion=COM3D25 -v:minimal -nologo
  ```
  Git Bash からは **`/p:` ではなく `-p:`** を使う。COM3D2 (2.0) 版は `-p:GameVersion=COM3D2` で同じコマンドを実行する。**両版とも新規警告なしで通ること**
- **自動テストは無い**（`tests/` は RuntimeAssetReload 専用）。各タスクの検証は両版ビルド、実機確認は最終タスクにまとめる
- 例外は投げず `MTEUtils.LogWarning` / `MTEUtils.LogException` を出して当該 1 件をスキップする（既存の流儀）
- アイテム読み込みはワーカースレッドで走る（`ModItemManager.Load` の `ThreadPool.QueueUserWorkItem`）。ここで Unity API（`Resources.Load` 等）を呼ばない。`CsvParser` / `GameUty.FileSystem*` はワーカーから呼んでよい。`PhotoBGObjectData.Create()` はゲーム側の静的状態を半初期化で固定しうるのでメインスレッドで呼ぶ（Task 1）
- ハードコーディングを避ける。nei のファイル名・列番号・カテゴリ→MPN 対応は名前付き定数/表にまとめる

## Review Focus

1. **公式背景オブジェクトが nei 由来掃除で消える**: `RemoveStaleBgObjectItems` は `ModItemType.BgObject` を全部掃除対象にしている。公式分を除外しないとロードのたびに公式が消える → Task 1 Step 4 で除外し、Task 6 で「アイテム更新」後も残ることを確認
2. **左手アイテムを menu の MPN で適用してしまう**: 左手 menu のカテゴリは `handitem` だが、フォトモードは `seieki_naka` に入れて右手と同時に持たせる。`menu.mpn` を使うと右手が外れる → Task 3 で `HandMenuItem.photoMpn` を使い、Task 6 で右手・左手の同時保持を確認
3. **ハンドアイテムの装着表示・削除**: 一時装備は `strFileName` ではなく `strTempFileName` に入るため、既存の `IsEquippedItem` / 削除（`DelProp(menu.mpn)`）はそのまま使えない → Task 3 で `isSelected` を一時装備で判定し `canDelete` を false に（外すのは「アイテムなし」行）
4. **絞り込み結果が古いまま / ロード中に壊れる**: 検索結果・履歴・お気に入り・配置中はマネージャーが同じフォルダの中身を入れ替える（件数が同じこともある）。ロード中はワーカーが children を書き換える → Task 5 で絞り込みを Layout イベントごとに作り直し、ロード中は走査しない。Task 6 の #11・#14 で確認
5. **2.0 版で壊れる**: 2.0 にも `GameUty.FileSystemOld` / `wf.CsvCommonIdManager.FileSystemType.Old` はあるが中身は旧 CM3D2 データ。ハンドアイテム nei は通常側を優先し、通常側で見つかった場合は Old の有効 ID を混ぜない → Task 3 の loader で分岐。`_for25` は `#if COM3D25` で囲む

---

## File Structure

| ファイル | 役割 |
|---|---|
| `BgObject/BgObjectInfo.cs` (変更) | `isOfficial` / `officialAssetName` を追加 |
| `BgObject/OfficialBgObjectLoader.cs` (新規) | `PhotoBGObjectData` から公式背景オブジェクトを列挙 |
| `HandItem/HandItemInfo.cs` (新規) | `phot_maid_item_list.nei` 1 行分 |
| `HandItem/HandItemNeiLoader.cs` (新規) | ハンドアイテム nei の読み込み（通常 → 旧データの順） |
| `ModItemBase.cs` (変更) | `HandMenuItem` を追加 |
| `Manager/ModItemManager.cs` (変更) | 公式背景・ハンドアイテムのロードとツリー登録、配置・適用の分岐、配置中表示 |
| `ModelPlacement/ModelPlacerManager.cs` (変更) | `CreateOfficialBgObject` |
| `ModelPlacement/SelfModelPlacer.cs` (変更) | `CreateGameModel` の `_for25` 優先 |
| `ModItemWindow.cs` (変更) | 再読込ボタン、タグ絞り込み UI とビューキャッシュ |
| `COM3D2.ModItemExplorer.Plugin.csproj` (変更) | 新規ファイルの `<Compile Include>`（csproj が明示列挙なら） |
| `README.md` (変更) | 機能説明（リリースノートは release-prep で書くので変更履歴には触れない） |

パスはすべて `source/COM3D2.ModItemExplorer.Plugin/` からの相対。`HandItem/` を新設するのは、nei 読み込みとデータがひとまとまりで `BgObject/` とは別機能だから。

---

## Task 1: 公式背景オブジェクトの一覧表示

**Files:**
- Modify: `BgObject/BgObjectInfo.cs`
- Create: `BgObject/OfficialBgObjectLoader.cs`
- Modify: `Manager/ModItemManager.cs`（`LoadState`、`Load()` の公式ロード列、`RemoveStaleBgObjectItems`、新メソッド）
- Modify: `COM3D2.ModItemExplorer.Plugin.csproj`（必要なら）

**Interfaces:**
- Produces: `BgObjectInfo.isOfficial` (`bool`)、`BgObjectInfo.officialAssetName` (`string`、配置時のファイル名。prefab 名があればそれ、無ければアセットバンドル名)
- Produces: `OfficialBgObjectLoader.LoadAll()` → `List<BgObjectInfo>`
- Produces: `ModItemManager._officialBgObjectInfoMap` (`Dictionary<string, BgObjectInfo>`、キーは `officialAssetName`、OrdinalIgnoreCase)。Task 2 が参照

- [ ] **Step 1: csproj の Compile 列挙方式を確認**

Run: `grep -n "Compile Include" COM3D2.ModItemExplorer.Plugin.csproj | head`
明示列挙なら以降の新規ファイルを毎回追記する。ワイルドカードなら何もしない。

- [ ] **Step 2: `BgObjectInfo` にフィールドを足す**

`neiFilePath` の後ろへ追加し、クラスコメントの「mod では常に空」の記述は Mod 由来の話として残す。

```csharp
        /// <summary>
        /// ゲーム本体の phot_bg_object_list.nei 由来なら true。
        /// 公式は nei ファイルを持たないため neiFilePath は null になる
        /// </summary>
        public bool isOfficial;

        /// <summary>
        /// 公式のみ。配置時に渡すファイル名 (prefab 名、無ければアセットバンドル名)。
        /// SelfModelPlacer.CreateGameModel と配置プリセットの fileName はこの値で一致させる
        /// </summary>
        public string officialAssetName;
```

- [ ] **Step 3: `OfficialBgObjectLoader` を作る**

```csharp
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
        /// ゲーム側の一覧を用意する。メインスレッドから呼ぶこと。
        /// Create() は bg_data_ を先に代入してから残りを組み立てるため、ワーカーで走らせると
        /// フォトモードが同時に触ったときや途中で例外が出たときに半初期化のまま固定される
        /// </summary>
        public static void EnsureGameDataCreated()
        {
            try
            {
                if (PhotoBGObjectData.data == null)
                {
                    PhotoBGObjectData.Create();
                }
            }
            catch (Exception e)
            {
                MTEUtils.LogWarning("公式背景オブジェクト一覧の読み込みに失敗しました。");
                MTEUtils.LogException(e);
            }
        }

        /// <summary>EnsureGameDataCreated 済みのゲーム側一覧から列挙する。ワーカーから呼んでよい</summary>
        public static List<BgObjectInfo> LoadAll()
        {
            var result = new List<BgObjectInfo>(512);

            var dataList = PhotoBGObjectData.data;
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
```

- [ ] **Step 4: `ModItemManager` にロードを足す**

(a) `LoadState` に `LoadOfficialBgObjectItems` を `LoadOfficialAnmItems` の直後へ追加。

(b) `_bgObjectInfoMap` の宣言の下に追加:

```csharp
        /// <summary>公式背景オブジェクトの配置名 (officialAssetName) -> 情報。配置中アイテムの表示名解決に使う</summary>
        private Dictionary<string, BgObjectInfo> _officialBgObjectInfoMap = new Dictionary<string, BgObjectInfo>(512, StringComparer.OrdinalIgnoreCase);

        /// <summary>公式背景オブジェクトを置く Official 直下のフォルダ名</summary>
        public static readonly string OfficialBgObjectDirName = "背景オブジェクト";
```

(c) `Load()` の `MTEUtils.ExecuteAfterMenuDataBaseReady` コールバック（メインスレッド）で、`LoadOfficialMenuFileNameList();` の直後（同じ try 内）に `OfficialBgObjectLoader.EnsureGameDataCreated();` を追加する（内部で例外を握るので try の外へ漏れない）。ワーカー内では `LoadOfficialAnmItems();` の直後に `LoadOfficialBgObjectItems();` を追加。

(d) `LoadModBgObjectItems` の上に追加:

```csharp
        private void LoadOfficialBgObjectItems()
        {
            MTEUtils.LogDebug("[ModMenuItemManager] LoadOfficialBgObjectItems");
            loadState = LoadState.LoadOfficialBgObjectItems;

            var infoList = OfficialBgObjectLoader.LoadAll();

            // 配置中一覧の描画 (メインスレッド) が GetBgObjectInfo で毎フレーム引くため、
            // 別の辞書に組み上げてから参照ごと差し替える
            var infoMap = new Dictionary<string, BgObjectInfo>(infoList.Count, StringComparer.OrdinalIgnoreCase);

            // 同一カテゴリ内で表示名が重複したときの逃がし先を決めるため、今回使った itemPath を覚えておく
            var usedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var info in infoList)
            {
                try
                {
                    infoMap[info.officialAssetName] = info;

                    var itemPath = MTEUtils.CombinePaths(
                        OfficialDirName, OfficialBgObjectDirName, info.category, info.name);
                    if (!usedPaths.Add(itemPath))
                    {
                        itemPath = MTEUtils.CombinePaths(
                            OfficialDirName, OfficialBgObjectDirName, info.category,
                            info.name + "_" + info.officialAssetName);
                        usedPaths.Add(itemPath);
                    }

                    GetOrCreateBgObjectItem(itemPath, info);
                }
                catch (Exception e)
                {
                    MTEUtils.LogException(e);
                }
            }

            _officialBgObjectInfoMap = infoMap;
        }
```

`_officialBgObjectInfoMap` の宣言から `readonly` を付けないこと（差し替えるため）。

`GetOrCreateBgObjectItem` は `fullPath = info.neiFilePath`（公式は null → `ValidateItemFile` がスキップ）なのでそのまま使える。

(e) `RemoveStaleBgObjectItems` の条件に公式除外を足し、コメントも更新:

```csharp
                // 配置中の ModelBgObjectItem も BgObjectItem だが nei 由来ではないので対象外。
                // 公式は Mod の nei と無関係にロードされるため、ここで消してはいけない
                if (pair.Value.itemType == ModItemType.BgObject
                    && !(pair.Value is BgObjectItem bgItem && bgItem.info != null && bgItem.info.isOfficial)
                    && !alivePaths.Contains(pair.Key))
```

- [ ] **Step 5: 両版ビルド**

Global Constraints のコマンドを COM3D25 / COM3D2 で実行。Expected: 成功、新規警告なし。
（2.0 の `PhotoBGObjectData` にも `create_prefab_name` / `create_asset_bundle_name` / `category_list` があることは ilspycmd で確認済み）

- [ ] **Step 6: コミット**

```bash
git add -A source/COM3D2.ModItemExplorer.Plugin
git commit -m "feat(item): 公式の背景オブジェクトを公式ツリーに一覧表示する"
```

---

## Task 2: 公式背景オブジェクトの配置と配置中表示

**Files:**
- Modify: `ModelPlacement/SelfModelPlacer.cs`（`CreateGameModel`）
- Modify: `ModelPlacement/ModelPlacerManager.cs`
- Modify: `Manager/ModItemManager.cs`（`CreateBgObjectModel`、`GetBgObjectInfo`、`GetModelThum`、`UpdateModelItem`）

**Interfaces:**
- Consumes: `BgObjectInfo.isOfficial` / `officialAssetName`、`_officialBgObjectInfoMap`（Task 1）
- Produces: `ModelPlacerManager.CreateOfficialBgObject(string assetName, int group, bool visible)`

- [ ] **Step 1: `CreateGameModel` に `_for25` を足す**

`GameMain.Instance.BgMgr.CreateAssetBundle(assetName)` の直後、`Resources.Load<GameObject>("Prefab/" + assetName)` の前へ挿入:

```csharp
#if COM3D25
                // 2.5 は新ボディ向けの差し替え prefab を持つものがあり、ゲームの
                // PhotoBGObjectData.Instantiate もこちらを先に探す
                if (!sourceObj)
                {
                    sourceObj = Resources.Load<GameObject>("Prefab/" + assetName + "_for25");
                }
#endif
```

`modelGo.name = assetName;` は元名のままにする（配置プリセット・SceneEditor はこの名前で引く）。

- [ ] **Step 2: `ModelPlacerManager.CreateOfficialBgObject` を足す**

`CreateBgObject` の下に:

```csharp
        /// <summary>
        /// 公式背景オブジェクト (prefab / アセットバンドル) を配置する。
        /// MTE 側の配置経路は .menu 名前提なので、背景オブジェクトと同じく常に自前配置にする
        /// </summary>
        public void CreateOfficialBgObject(string assetName, int group, bool visible)
        {
            selfPlacer.CreateGameModel(assetName, group, visible);
        }
```

- [ ] **Step 3: `CreateBgObjectModel` を公式対応にする**

`modelPlacerManager.CreateBgObject(item.info.assetBundleName, 0, true);` を置き換える:

```csharp
                if (item.info.isOfficial)
                {
                    modelPlacerManager.CreateOfficialBgObject(item.info.officialAssetName, 0, true);
                }
                else
                {
                    modelPlacerManager.CreateBgObject(item.info.assetBundleName, 0, true);
                }
```

- [ ] **Step 4: 配置中モデルから公式情報を引けるようにする**

`GetBgObjectInfo` を置き換える（呼び出し元 `GetModelBaseName` / `GetOrCreateModelBgObjectItem` はそのまま恩恵を受ける）:

```csharp
        /// <summary>
        /// 配置データのファイル名から背景オブジェクトの情報を引く。
        /// Mod は .asset_bg のファイル名、公式は拡張子なしの prefab / アセットバンドル名で引く。
        /// 該当しない、または一覧から消えている場合は null
        /// </summary>
        public BgObjectInfo GetBgObjectInfo(string fileName)
        {
            if (SelfModelPlacer.IsBgObjectFileName(fileName))
            {
                return _bgObjectInfoMap.GetOrDefault(Path.GetFileNameWithoutExtension(fileName));
            }

            if (SelfModelPlacer.ResolvePlacementType(fileName) == ModelPlacementType.Prefab)
            {
                return _officialBgObjectInfoMap.GetOrDefault(fileName);
            }

            return null;
        }

        /// <summary>背景オブジェクト (Mod / 公式) として配置中一覧に出すファイル名か</summary>
        private bool IsBgObjectModelFileName(string fileName)
        {
            return SelfModelPlacer.IsBgObjectFileName(fileName) || GetBgObjectInfo(fileName) != null;
        }
```

`GetModelThum` の `if (SelfModelPlacer.IsBgObjectFileName(fileName))` と `UpdateModelItem` の同条件を `IsBgObjectModelFileName(fileName)` に置き換える。`UpdateModelItem` のコメントも「背景オブジェクト（Mod / 公式）は menu を持たないため」に直す。

（`ResolvePlacementType` は `internal static`、同アセンブリなので呼べる。`ModelPlacementType.Prefab` の定義場所は `grep -rn "class ModelPlacementType" .` で確認して using を合わせる）

- [ ] **Step 5: 両版ビルド** — Expected: 成功、新規警告なし

- [ ] **Step 6: コミット**

```bash
git add -A source/COM3D2.ModItemExplorer.Plugin
git commit -m "feat(model): 公式の背景オブジェクトを配置し、配置中一覧に名前とアイコンで出す" \
  -m "CreateGameModel は SceneEditor の公式 prefab 配置でも使うため、2.5 用 prefab (_for25) がある 8 件は SceneEditor 経由でも 2.5 用に切り替わる (ゲーム本体と同じ順序)"
```

---

## Task 3: ハンドアイテム

**Files:**
- Create: `HandItem/HandItemInfo.cs`
- Create: `HandItem/HandItemNeiLoader.cs`
- Modify: `ModItemBase.cs`（`HandMenuItem`）
- Modify: `Manager/ModItemManager.cs`（ロード、`ApplyMenuItem` 冒頭の分岐、`ApplyHandItem`）

**Interfaces:**
- Produces: `HandItemInfo { string category; string name; string menuFileName; MPN mpn; }`
- Produces: `HandItemNeiLoader.LoadAll()` → `List<HandItemInfo>`
- Produces: `HandMenuItem : MenuItem { HandItemInfo handItemInfo }`

- [ ] **Step 1: `HandItemInfo`**

```csharp
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
```

- [ ] **Step 2: `HandItemNeiLoader`**

```csharp
using System;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using wf;

namespace COM3D2.ModItemExplorer.Plugin
{
    /// <summary>
    /// フォトモードのハンドアイテム一覧 (phot_maid_item_list.nei) を読む。
    /// 2.5 ではこの nei が旧データ側 (GameUty.FileSystemOld) にしか無く、
    /// ゲームの PhotoMaidItemData.Create() は通常側しか見ずに例外になるため自前で読む
    /// </summary>
    public static class HandItemNeiLoader
    {
        private const string NeiFileName = "phot_maid_item_list.nei";
        private const string EnabledListName = "phot_maid_item_enabled_list";
        private const string MenuExtension = ".menu";

        // 列インデックス。公式 PhotoMaidItemData.Create() の読み取り順に合わせている
        private const int ColumnId = 0;
        private const int ColumnCategory = 1;
        private const int ColumnName = 2;
        private const int ColumnCallName = 3;
        private const int ColumnRequiredPack = 4;
        private const int FirstDataRow = 1;

        /// <summary>カテゴリ -> 装着先 MPN。PhotoMaidItemData.Create() の対応表と同じ</summary>
        private static readonly Dictionary<string, MPN> CategoryMpnMap = new Dictionary<string, MPN>
        {
            { "右手", MPN.handitem },
            { "左手", MPN.seieki_naka },
            { "上半身", MPN.kousoku_upper },
            { "下半身", MPN.kousoku_lower },
            { "前穴", MPN.accvag },
            { "後穴", MPN.accanl },
        };

        public static List<HandItemInfo> LoadAll()
        {
            var result = new List<HandItemInfo>(64);

            try
            {
                // 通常側を優先する。2.0 は通常側にあり、旧データ側は CM3D2 由来の別物なので混ぜない
                var fileSystem = GameUty.FileSystem;
                var enabledIds = new HashSet<int>();
                CsvCommonIdManager.ReadEnabledIdList(
                    CsvCommonIdManager.FileSystemType.Normal, true, EnabledListName, ref enabledIds);

                if (!fileSystem.IsExistentFile(NeiFileName))
                {
                    fileSystem = GameUty.FileSystemOld;
                    if (fileSystem == null || !fileSystem.IsExistentFile(NeiFileName))
                    {
                        MTEUtils.LogWarning("ハンドアイテム一覧が見つかりません。{0}", NeiFileName);
                        return result;
                    }

                    // 2.5 は旧データ側の有効 ID が本体分、通常側が追加パック分なので両方を合わせる
                    CsvCommonIdManager.ReadEnabledIdList(
                        CsvCommonIdManager.FileSystemType.Old, true, EnabledListName, ref enabledIds);
                }

                using (var file = fileSystem.FileOpen(NeiFileName))
                using (var csvParser = new CsvParser())
                {
                    if (file == null || !csvParser.Open(file))
                    {
                        MTEUtils.LogWarning("ハンドアイテム一覧を開けませんでした。{0}", NeiFileName);
                        return result;
                    }

                    for (var y = FirstDataRow; y < csvParser.max_cell_y; y++)
                    {
                        try
                        {
                            ReadRow(csvParser, y, enabledIds, result);
                        }
                        catch (Exception e)
                        {
                            MTEUtils.LogException(e);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                MTEUtils.LogWarning("ハンドアイテム一覧の読み込みに失敗しました。");
                MTEUtils.LogException(e);
            }

            return result;
        }

        private static void ReadRow(
            CsvParser csvParser, int y, HashSet<int> enabledIds, List<HandItemInfo> result)
        {
            if (!csvParser.IsCellToExistData(ColumnId, y)
                || !enabledIds.Contains(csvParser.GetCellAsInteger(ColumnId, y)))
            {
                return;
            }

            var category = csvParser.GetCellAsString(ColumnCategory, y);
            MPN mpn;
            if (!CategoryMpnMap.TryGetValue(category, out mpn))
            {
                MTEUtils.LogWarning("未対応のハンドアイテムカテゴリです。{0}", category);
                return;
            }

            var callName = csvParser.GetCellAsString(ColumnCallName, y);
            if (string.IsNullOrEmpty(callName))
            {
                return;
            }

            var requiredPack = csvParser.GetCellAsString(ColumnRequiredPack, y);
            if (!string.IsNullOrEmpty(requiredPack) && !PluginData.IsEnabled(requiredPack))
            {
                return;
            }

            result.Add(new HandItemInfo
            {
                category = category,
                name = csvParser.GetCellAsString(ColumnName, y),
                menuFileName = callName + MenuExtension,
                mpn = mpn,
            });
        }
    }
}
```

（`wf.CsvCommonIdManager` の名前空間は `Assembly-CSharp/wf/CsvCommonIdManager.cs` で確認済み。ビルドで `wf` が他の識別子と衝突したら `using` をやめて完全修飾にする）

- [ ] **Step 3: `HandMenuItem`**

`ModItemBase.cs` の `MenuItem` クラスの後ろ（`ModelBgObjectItem` より前）へ:

```csharp
    /// <summary>
    /// フォトモードのハンドアイテム。menu は普通の MenuInfo だが、装着先 MPN と
    /// 一時装備で入る点がフォトモードに従うため、装着判定と削除可否を差し替える
    /// </summary>
    public class HandMenuItem : MenuItem
    {
        public HandItemInfo handItemInfo { get; set; }

        public override string name => handItemInfo?.name ?? base.name;

        public override string tag => handItemInfo?.category ?? base.tag;

        /// <summary>CRC ボディでは Maid.SetPropIn が対応版へ差し替えるときにこの接頭辞を付ける</summary>
        private const string CrcReplacementPrefix = "crx_";

        /// <summary>一時装備は strTempFileName に入るため、そちらで着用中を判定する</summary>
        public override bool isSelected
        {
            get
            {
                var maid = modItemManager.currentMaid;
                if (maid == null || handItemInfo == null)
                {
                    return false;
                }

                var prop = maid.GetProp(handItemInfo.mpn);
                var tempFileName = prop?.strTempFileName;
                if (string.IsNullOrEmpty(tempFileName))
                {
                    return false;
                }

                if (tempFileName.StartsWith(CrcReplacementPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    tempFileName = tempFileName.Substring(CrcReplacementPrefix.Length);
                }

                return string.Equals(
                    tempFileName, handItemInfo.menuFileName, StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>外すのは同カテゴリの「アイテムなし」行で行う (既存の削除は通常装備の DelProp 前提)</summary>
        public override bool canDelete => false;

        /// <summary>画面に出す nei の名前でも検索に当たるようにする</summary>
        public override bool IsMatch(Regex pattern)
        {
            if (handItemInfo != null && !string.IsNullOrEmpty(handItemInfo.name)
                && pattern.IsMatch(handItemInfo.name))
            {
                return true;
            }
            return base.IsMatch(pattern);
        }
    }
```

（`MenuItem.name` / `tag` / `isSelected` / `canDelete` / `IsMatch` が `override` 可能な宣言であることは確認済み。`modItemManager` は `ModItemBase` から参照できる既存プロパティ。`using System;` / `using System.Text.RegularExpressions;` が無ければ追加）

- [ ] **Step 4: ツリー登録**

(a) `LoadState` に `LoadOfficialHandItems` を `LoadOfficialBgObjectItems` の後に追加。`Load()` で `LoadOfficialBgObjectItems();` の直後に `LoadOfficialHandItems();`。

(b) 定数:

```csharp
        /// <summary>ハンドアイテムを置く Official 直下のフォルダ名</summary>
        public static readonly string OfficialHandItemDirName = "ハンドアイテム";
```

(c) メソッド:

```csharp
        private void LoadOfficialHandItems()
        {
            MTEUtils.LogDebug("[ModMenuItemManager] LoadOfficialHandItems");
            loadState = LoadState.LoadOfficialHandItems;

            foreach (var info in HandItemNeiLoader.LoadAll())
            {
                try
                {
                    // 公式 menu 一覧 (MenuDataBase) には載らないため個別に読む。
                    // 未所持パックなどで本体が無い行はここで落ちる
                    var menu = GetOrLoadOfficialMenu(info.menuFileName);
                    if (menu == null)
                    {
                        continue;
                    }

                    var itemPath = MTEUtils.CombinePaths(
                        OfficialDirName, OfficialHandItemDirName, info.category, info.menuFileName);
                    GetOrCreateHandMenuItem(itemPath, menu, info);
                }
                catch (Exception e)
                {
                    MTEUtils.LogException(e);
                }
            }
        }

        private HandMenuItem GetOrCreateHandMenuItem(string itemPath, MenuInfo menu, HandItemInfo info)
        {
            var item = GetItemByPath<HandMenuItem>(itemPath);
            if (item != null)
            {
                item.menu = menu;
                item.handItemInfo = info;
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

            item = new HandMenuItem
            {
                itemType = ModItemType.Official,
                itemName = itemName,
                itemPath = itemPath,
                menu = menu,
                handItemInfo = info,
            };

            parentItem.AddChild(item);
            _itemPathMap[itemPath] = item;
            // _itemNameMap には載せない。名前引きは着用中の通常装備 (strFileName) の解決に使われ、
            // 一時装備で持たせるハンドアイテムが引っかかると着用判定の意味が食い違う

            return item;
        }
```

`_itemNameMap` に載せないため、`RemoveItem` が `_itemNameMap.Remove(item.itemName)` で同名の別アイテムを消さないかを実装時に確認し、消すなら「マップの値が自分自身のときだけ消す」形にする。

`GetOrLoadOfficialMenu` が失敗時に警告を出すか確認し、出さないならここで `MTEUtils.LogDebug` だけにする（未所持パックは正常系なので警告にしない）。

- [ ] **Step 5: 適用の分岐**

`ApplyMenuItem` の `if (currentMaid == null || item == null) return false;` の直後へ:

```csharp
            if (item is HandMenuItem handItem)
            {
                return ApplyHandItem(handItem);
            }
```

`ApplyMenuItem` の下に:

```csharp
        /// <summary>
        /// ハンドアイテムをフォトモードと同じく一時装備で持たせる。
        /// 一時装備は操作履歴 (MaidPropsSnapshot は通常装備のみ控える) の対象外
        /// </summary>
        private bool ApplyHandItem(HandMenuItem item)
        {
            var info = item.handItemInfo;
            var menu = item.menu;
            if (info == null || menu == null)
            {
                MTEUtils.LogWarning("ハンドアイテムの情報がありません。" + item.itemPath);
                return false;
            }

            var missingFileName = FindMissingFileName(menu);
            if (missingFileName != null)
            {
                MTEUtils.LogWarning("参照先ファイルが見つかりません。" + missingFileName + " " + item.itemPath);
                return false;
            }

            currentMaid.SetProp(info.mpn, info.menuFileName, 0, f_bTemp: true);
            currentMaid.AllProcPropSeqStart();
            return true;
        }
```

（`SetProp(MPN, string, int, bool f_bTemp, ...)` の引数名は 2.0/2.5 両方で `f_bTemp`。ビルドが通らなければ位置引数 `true` に）

- [ ] **Step 6: 両版ビルド** — Expected: 成功、新規警告なし

- [ ] **Step 7: コミット**

```bash
git add -A source/COM3D2.ModItemExplorer.Plugin
git commit -m "feat(item): 公式のハンドアイテムを一覧に出し、フォトモードと同じ部位へ一時装備で持たせる"
```

---

## Task 4: メインウィンドウのモデル再読込ボタン

**Files:**
- Modify: `ModItemWindow.cs`（`DrawModelPlacementRow`）

**Interfaces:**
- Consumes: `SelfModelPlacer.instance.selectedModel` / `CanReloadModel(StudioModelStatWrapper)` / `ReloadModel(StudioModelStatWrapper)`（既存）

- [ ] **Step 1: ボタンを足す**

`DrawModelPlacementRow` の「操作」ボタンの if ブロックの直後へ:

```csharp
                // モデル操作ウィンドウを開かずに読み直せるよう、同じ処理をここにも置く。
                // 対象は自前配置の選択中モデルなので、配置プラグインの選択とは無関係に判定する
                var placer = SelfModelPlacer.instance;
                if (view.DrawButton("モデル再読込", 100, 20, placer.CanReloadModel(placer.selectedModel)))
                {
                    placer.ReloadModel(placer.selectedModel);
                }
```

幅 100 は `ModelOperationWindow.RELOAD_BUTTON_WIDTH` と同値。`private` なので、`ModelOperationWindow` 側を `public readonly static` にして参照する（値の二重定義を避ける）。

- [ ] **Step 2: 両版ビルド** — Expected: 成功

- [ ] **Step 3: コミット**

```bash
git add -A source/COM3D2.ModItemExplorer.Plugin
git commit -m "feat(ui): メインウィンドウの操作ボタンの右にモデル再読込ボタンを置く"
```

---

## Task 5: タグ絞り込み

**Files:**
- Modify: `ModItemWindow.cs`

**Interfaces:**
- Produces（ウィンドウ内部のみ）: `InvalidateViewCache()`、`GetViewSourceItem()` → `DirItem`、`RefreshTagFilter()`、`_tagFilterViewItem`

**キャッシュ方針:** 検索結果・履歴・お気に入り・配置中は、マネージャー側が同じ `DirItem` の中身を入れ替える（件数が同じこともある）ため、参照＋件数のキーでは古くなる。そこで **絞り込み結果とタグ一覧は IMGUI の Layout イベントごとに 1 回だけ作り直し**、Repaint や入力イベントでは Layout で作った結果を使う。走査は 1 フレーム 1 回・割り当てなし（リストは使い回し）。また **ロード中はワーカーが children を書き換えるため走査しない**（`DrawCategoryComboBox` を `!isLoading` で守っているのと同じ理由）。

- [ ] **Step 1: 状態を足す**

`_flatViewItem` の宣言の下へ:

```csharp
        /// <summary>タグ絞り込みの「すべて」を表す値</summary>
        private const string AllTagsFilter = "";

        private static readonly string AllTagsLabel = "すべて";
        private static readonly int TAG_FILTER_WIDTH = 90;

        /// <summary>選択中のタグ。フォルダを移動しても維持する</summary>
        private string _tagFilter = AllTagsFilter;

        /// <summary>絞り込み結果。Layout イベントで RefreshTagFilter が作り直す</summary>
        private TempDirItem _tagFilterViewItem = new TempDirItem
        {
            children = new List<ITileViewContent>(1024),
        };

        /// <summary>直前に絞り込んだ元の一覧。変わったらスクロール位置を先頭へ戻す</summary>
        private DirItem _tagFilterSource = null;
        private string _tagFilterBuiltTag = null;

        private GUIComboBox<string> _tagFilterComboBox = new GUIComboBox<string>
        {
            getName = (tag, _) => string.IsNullOrEmpty(tag) ? AllTagsLabel : tag,
            buttonSize = new Vector2(TAG_FILTER_WIDTH, 20),
            contentSize = new Vector2(TAG_FILTER_WIDTH + 30, 300),
            // パスバーの右端に固定幅で並べるため、ソートと同じく矢印を出さない
            showArrow = false,
        };
```

（`TAG_FILTER_WIDTH` は初期化子より前に宣言する。`_tagFilterComboBox.onSelected` は Step 5 でコンストラクタ相当の初期化箇所（`_categoryComboBox.onSelected` を設定している 310 行付近）に `_tagFilter = tag ?? AllTagsFilter;` で一度だけ設定する）

- [ ] **Step 2: `InvalidateViewCache` を作り、既存の無効化を置き換える**

```csharp
        /// <summary>フラットビューの展開結果を捨て、次の描画で作り直させる</summary>
        private void InvalidateViewCache()
        {
            _flatViewItem.itemPath = "";
        }
```

`grep -n '_flatViewItem.itemPath = ""' ModItemWindow.cs` の全箇所（現状 5 か所）を `InvalidateViewCache();` に置き換える。絞り込み側は毎 Layout で作り直すので、ここで捨てるものは無い。

- [ ] **Step 3: 表示対象の決定を関数に切り出す**

`DrawContentMain` のフラットビュー分岐を関数にする:

```csharp
        /// <summary>今の一覧の元になるアイテム (フラットビュー時は展開済みの一時フォルダ)。ロード中は呼ばない</summary>
        private DirItem GetViewSourceItem()
        {
            if (currentDirItem == null || !currentDirItem.isFlatView)
            {
                return currentDirItem;
            }

            if (_flatViewItem.itemPath != currentDirItem.itemPath)
            {
                MTEUtils.LogDebug("Update FlatView: " + currentDirItem.itemPath);
                _flatViewItem.itemPath = currentDirItem.itemPath;
                _flatViewItem.RemoveAllChildren();
                currentDirItem.GetAllFiles(_flatViewItem.children);
                ModItemManager.SortItemChildren(_flatViewItem);
            }
            return _flatViewItem;
        }
```

- [ ] **Step 4: 絞り込み結果とタグ一覧を作る**

```csharp
        /// <summary>
        /// 絞り込み結果とタグの選択肢を作り直す。マネージャー側が同じフォルダの中身を入れ替えても
        /// 追従できるよう、Layout イベントのたびに呼ぶ。フォルダは移動に要るので絞り込まない
        /// </summary>
        private void RefreshTagFilter(DirItem source)
        {
            var tags = _tagFilterComboBox.items;
            tags.Clear();
            tags.Add(AllTagsFilter);

            if (_tagFilterSource != source || _tagFilterBuiltTag != _tagFilter)
            {
                // 別の一覧・別のタグに切り替わったら前のスクロール位置を持ち越さない
                _tagFilterViewItem.scrollPosition = Vector2.zero;
                _tagFilterSource = source;
                _tagFilterBuiltTag = _tagFilter;
            }

            _tagFilterViewItem.itemPath = source?.itemPath ?? "";
            _tagFilterViewItem.children.Clear();

            if (source?.children != null)
            {
                foreach (var child in source.children)
                {
                    var tag = child.isDir ? null : child.tag;
                    if (!string.IsNullOrEmpty(tag) && !tags.Contains(tag))
                    {
                        tags.Add(tag);
                    }

                    // TempDirItem.AddChild と同じく親を書き換えずに children へ直接積む
                    if (child.isDir || tag == _tagFilter)
                    {
                        _tagFilterViewItem.children.Add(child);
                    }
                }
            }

            // 移動先に無いタグでも、選択中であることが見えるよう選択肢に残す
            if (!tags.Contains(_tagFilter))
            {
                tags.Add(_tagFilter);
            }

            _tagFilterComboBox.currentIndex = tags.IndexOf(_tagFilter);
        }
```

`tags.Contains` は選択肢数（数十）に比例するだけなので、1 万件でも許容範囲。`DirItem.scrollPosition` が `DrawTileView` のスクロールに使われていることを実装時に `grep -n "scrollPosition" MTEUtils/GUIView.cs ModItemBase.cs` で確認し、別名なら合わせる。`TempDirItem.RemoveAllChildren` が子の親を触るなら使わず `children.Clear()` のままにする。

- [ ] **Step 5: パスバーと一覧に配線する**

(a) `DrawPathInfo` の `var pathButtonWidth = 20 * 3 + 10;` を `var pathButtonWidth = 20 * 3 + 10 + TAG_FILTER_WIDTH;` にする（`showArrow = false` なので幅はボタン幅そのもの）。

(b) 「パスボタン」ブロックのソート `_itemSortTypeComboBox.DrawTextureButton(view);` の後に:

```csharp
                    // ロード中はワーカーがツリーを書き換えるため走査しない (カテゴリのドロップダウンと同じ)
                    if (!modItemManager.isLoading)
                    {
                        if (Event.current.type == EventType.Layout)
                        {
                            RefreshTagFilter(GetViewSourceItem());
                        }
                        _tagFilterComboBox.DrawButton(view);
                    }
                    else
                    {
                        view.AddSpace(TAG_FILTER_WIDTH);
                    }
```

`view.AddSpace` が横方向に効かない場合（`DrawTabRow` のコメント参照）は `view.currentPos.x += TAG_FILTER_WIDTH;` にする。`DrawPathInfo` が `DrawContentMain` より先に同じ OnGUI で呼ばれることを実装時に確認する（先でなければ `RefreshTagFilter` の呼び出しを `DrawContentMain` 側の先頭へ移す）。

(c) `DrawContentMain` の else 節を次に変える:

```csharp
            else if (modItemManager.isLoading)
            {
                // ロード中は従来どおりの一覧を出し、絞り込みは掛けない
                DrawItemTileView(view, GetViewSourceItem());
            }
            else
            {
                var targetItem = _tagFilter == AllTagsFilter
                    ? GetViewSourceItem()
                    : _tagFilterViewItem;

                if (targetItem.children == null || targetItem.children.Count == 0)
                {
                    view.DrawLabel("タグ「" + _tagFilter + "」に一致するアイテムがありません", -1, 20);
                }
                else
                {
                    DrawItemTileView(view, targetItem);
                }
            }
```

既存の `view.DrawTileView(targetItem, ...)` 呼び出し（引数・コールバック一式）はそのまま `private void DrawItemTileView(GUIView view, DirItem targetItem)` へ移す。ロード中の分岐は従来と同じ一覧（フラットビュー込み）を出すだけで、挙動は変えない。

- [ ] **Step 6: 両版ビルド** — Expected: 成功、新規警告なし

- [ ] **Step 7: コミット**

```bash
git add -A source/COM3D2.ModItemExplorer.Plugin
git commit -m "feat(ui): 表示中のアイテムをタグで絞り込めるようにする"
```

---

## Task 6: 実機検証と README

**Files:**
- Modify: `README.md`（機能一覧・ヘッダー・アドレスバーの説明）

- [ ] **Step 1: 実機へ反映**

ゲーム起動中は DLL を差し替えられないため、`com3d25-devbridge:restart-verify` スキルの手順でユーザーの了承を得てから再起動・反映する（勝手にゲームを終了しない）。

- [ ] **Step 2: 実機確認（devbridge の `screenshot` / `eval_csharp` で裏取り）**

| # | 操作 | 期待 |
|---|---|---|
| 1 | 公式 → 背景オブジェクト を開く | 9 カテゴリが出て、マイオブジェクトは無い。合計 389 件（`eval_csharp` で公式ツリーの件数を数える） |
| 2 | 文房具 / グルメ / パーティクル / その他 から 1 つずつダブルクリック配置 | カメラ注視点に置かれ、配置中一覧に nei の名前・共通アイコンで出る |
| 3 | `_for25` 版がある prefab（`eval_csharp` で `Resources.Load("Prefab/"+name+"_for25")` が非 null の 1 件）を配置 | 2.5 用 prefab が出る（`GetComponentsInChildren<Renderer>` の mesh 名などで確認） |
| 4 | 配置プリセットを保存 → 全削除 → 読込 | 公式背景オブジェクトが戻る |
| 5 | 設定 → アイテム更新 | 公式背景オブジェクトが消えない |
| 6 | 公式 → ハンドアイテム → 右手の 1 つ、左手の 1 つを適用 | 両手に同時に持ち、両方が緑枠 |
| 7 | 右手「アイテムなし」を適用 | 右手だけ外れる |
| 8 | モデルモードでハンドアイテムを配置 | menu モデルとして置ける |
| 9 | モデルを選択しメインウィンドウの「モデル再読込」 | 読み直し成功。未選択・MTE 管理モデルでは押せない。ウィンドウを最小幅にしてもボタンが行からはみ出さない（はみ出すならラベル・ボタン幅を詰める） |
| 10 | Mod ルートでフラットビュー → タグで「帽子」等を選択 | そのタグだけ残る。フォルダは残る |
| 11 | 絞り込み中にソート変更・フォルダ移動・検索 | 結果が追従し、古い並びが残らない |
| 12 | 絞り込みを「すべて」に戻す | 元の一覧に戻る |
| 13 | Mod ルートのフラットビュー表示中の FPS | 絞り込み UI 追加前と体感差が無い（落ちるなら Layout ごとの再構築を数フレームおきに間引く） |
| 14 | 絞り込み中に「アイテム更新」でロードを走らせる | 例外が出ず、ロード完了後に絞り込みが戻る |
| 15 | 検索語を変えて検索し直す（件数が同じになる語を選ぶ）／絞り込み中にお気に入り・履歴を開いて中身を変える | 絞り込み結果が新しい中身に追従する |
| 16 | CRC ボディのメイドにハンドアイテムを持たせる | 緑枠が付く（`crx_` 差し替え後も着用判定が合う） |
| 17 | ハンドアイテムを画面上の名前（例: 刺繍針）で検索 | 当たる |

2.0 版は実機が無ければビルド成功までとし、その旨を報告する。

- [ ] **Step 3: README を更新**

- 機能一覧: 「公式の背景オブジェクト・ハンドアイテムの表示」「タグでの絞り込み」を追加
- モデル配置ヘッダー: 「モデル再読込」ボタンの説明（対象は本プラグインで配置したモデル）
- アドレスバー: タグ絞り込みドロップダウンの説明
- ライブラリビュー付近: ハンドアイテムは一時装備で持たせること、外すには「アイテムなし」を選ぶこと、操作履歴（Undo）の対象外であること

- [ ] **Step 4: コミット**

```bash
git add README.md
git commit -m "docs(readme): 公式背景オブジェクト・ハンドアイテム・再読込ボタン・タグ絞り込みを追記する"
```

---

## Self-Review

- 要件 1 → Task 1 / 要件 2 → Task 2 / 要件 3 → Task 2 Step 1 / 要件 4 → Task 3 / 要件 5 → Task 3（`HandMenuItem` は `MenuItem` なので既存 `CreateMenuModel` がそのまま通る。Task 6 #8 で確認）/ 要件 6 → Task 4 / 要件 7 → Task 5
- 型・名前: `officialAssetName` / `isOfficial` / `_officialBgObjectInfoMap` / `CreateOfficialBgObject` / `HandItemInfo.mpn` / `HandMenuItem.handItemInfo` / `InvalidateViewCache` / `GetViewSourceItem` / `RefreshTagFilter` / `DrawItemTileView` をタスク間で統一済み
- Review Focus の 5 項目は Task 1 Step 4 / Task 3 Step 3・5 / Task 5 Step 2・3 / Task 3 Step 2 にそれぞれ対策を入れ、Task 6 の #5・#6・#7・#11 で確認する

## 設計判断メモ

- **ハンドアイテムを通常装備にしない理由**: フォトモードと同じ一時装備にすると、セーブやプリセットへハンドアイテムが紛れ込まない。代わりに操作履歴（MaidPropsSnapshot は通常装備だけ控える）の対象外になり、着用中一覧にも出ない。README に明記する
- **公式背景オブジェクトの配置先**: MTE 経路は .menu 名前提なので、Mod 背景オブジェクトと同様に常に自前配置
- **絞り込みは単一選択**: 要望は「カテゴリごとのフィルタ」で、複数選択の必要性は出ていない（YAGNI）

## レビュー却下メモ

- 公式 BG の重複排除をカテゴリ単位にする — 実機で assetName の重複は 0 件（計 389 件、要件の件数と一致）。Task 6 #1 で件数照合する
- 公式 BG の表示名を itemPath 用に無害化する — 実機で区切り文字・不正パス文字を含む名前は 0 件。Mod 背景オブジェクトも同じく名前をそのまま使っており、万一の不正名はアイテム単位の try/catch で 1 件スキップされるだけ
- `create_asset_bundle_name` に拡張子が含まれる場合の誤判定 — 実機で `.` を含む assetName は 0 件（誤検知）
- `GetOrCreateModelBgObjectItem` が `assetBundleName` に依存しないかの確認 — レビュアー自身が問題なしと確認済み。実装時の grep 確認で足りる

