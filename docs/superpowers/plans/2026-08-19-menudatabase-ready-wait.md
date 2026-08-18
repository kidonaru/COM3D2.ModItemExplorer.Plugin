# MenuDataBase 構築完了待ち対応 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
> （本リポジトリの CLAUDE.md により subagent-driven-development は使わない）

**Goal:** 公式アイテムの列挙をゲーム側 `MenuDataBase` の非同期構築完了後に行うようにし、「着用中」を含む一部の公式アイテムがツリーに登録されない不具合を修正する。

**Architecture:** `ModItemManager.Load()` は `GameMain.Instance.MenuDataBase.GetDataSize()` を同期的に呼んで公式 menu 名を全件列挙している。MenuDataBase は非同期構築で、完了前は `GetDataSize()` が途中までの件数しか返さない。ゲーム本体は必ず `MenuDataBase.JobFinished()` で待ってから利用している（`SceneUserEditMain.cs:114` 等）。同じ待機を `MTEUtils` のコルーチンヘルパーとして追加し、`Load()` の列挙〜バックグラウンド処理をその完了後に開始する。あわせて、着用中アイテムがツリーに存在しなかった場合の無言 null 返却に警告ログを追加する。

**Tech Stack:** C# (.NET 3.5 / Unity 5.x), BepInEx/UnityInjector プラグイン、Unity コルーチン（`GameMain.Instance.StartCoroutine`）

**Spec:** 本計画にインラインで記載（別途 spec ファイルなし）。根本原因の調査結果は下記「調査結果」節を参照。

## 調査結果（根本原因の根拠）

実機（COM3D2.5 稼働中）で `MenuDataBase` のインデックス順に「本来ツリーに登録されるべき公式 menu 件数 / 実際に `_itemNameMap` に存在する件数」を集計した結果:

```
0-999       should=270  have=270
...
9000-9999   should=315  have=315
10000-10999 should=327  have=227   ← ここで途切れる
11000-11999 should=437  have=0
...
20000-20999 should=285  have=0
```

- 現在 `MenuDataBase.GetDataSize()` = 20994、`JobFinished()` = true
- index 約 10,900 を境に、それ以降の公式アイテム約 3,000 件がツリーに一切登録されていない
- 実際に着用中の `Dress590_accHat_I_.menu` / `Dress590_onep_I_.menu` / `Dress590_stkg_I_.menu` / `Dress590_shoe_I_.menu` はいずれも `_menuMap` には存在するが `_itemNameMap` に無く、「着用中」リストから欠落していた
- プラグイン全体に `JobFinished` の呼び出しは 1 箇所も無い（`grep -rn "JobFinished" source` → 0 件）

## Global Constraints

- コードのコメントとログメッセージは日本語で記述する
- ハードコーディングは避ける
- COM3D2 (2.0) / COM3D2.5 の両ターゲットを同一ソースからビルドする。`MenuDataBase.JobFinished()` は両バージョンに存在することを確認済み（2.0: `Assembly-CSharp-firstpass.dll` の `MenuDataBase.JobFinished()`）。したがって `#if COM3D25` 等の条件コンパイルは不要
- 本リポジトリに単体テストのフレームワークは無い。検証はビルド成功 + 実機（MCP `com3d25-devbridge`）での確認で行う
- git worktree は使わず、メイン作業ディレクトリで作業する

## File Structure

| ファイル | 責務 | 変更内容 |
|---|---|---|
| `source/COM3D2.ModItemExplorer.Plugin/MTEUtils/MTEUtils.cs` | ゲーム側 API のユーティリティ・コルーチンヘルパー | `ExecuteAfterMenuDataBaseReady` を追加（既存の `ExecuteAfterProcProp` の直後） |
| `source/COM3D2.ModItemExplorer.Plugin/Manager/ModItemManager.cs` | アイテムツリーの構築・管理 | `Load()` の列挙開始を MenuDataBase 完了後に遅延。`GetEquippedItem()` に警告ログ追加 |

---

### Task 1: MenuDataBase の構築完了を待つヘルパーを追加する

**Files:**
- Modify: `source/COM3D2.ModItemExplorer.Plugin/MTEUtils/MTEUtils.cs:281-294`（`ExecuteAfterProcProp` / `ExecuteAfterProcPropInternal` の直後に追記）

**Interfaces:**
- Consumes: なし
- Produces: `public static void MTEUtils.ExecuteAfterMenuDataBaseReady(Action action)` — MenuDataBase の構築が完了していれば即座に、未完了ならコルーチンで完了を待ってから `action` を実行する

- [ ] **Step 1: ヘルパーメソッドを追加する**

`MTEUtils.cs` の `ExecuteAfterProcPropInternal` メソッドの閉じ括弧の直後に以下を挿入する。

```csharp
        /// <summary>MenuDataBaseの構築完了を待つ上限時間（秒）</summary>
        private const float MenuDataBaseReadyTimeout = 60f;

        /// <summary>
        /// MenuDataBaseの非同期構築の完了を待ってからアクションを実行する
        /// </summary>
        public static void ExecuteAfterMenuDataBaseReady(Action action)
        {
            if (IsMenuDataBaseReady())
            {
                action?.Invoke();
                return;
            }

            GameMain.Instance.StartCoroutine(ExecuteAfterMenuDataBaseReadyInternal(action));
        }

        private static bool IsMenuDataBaseReady()
        {
            var menuDataBase = GameMain.Instance?.MenuDataBase;
            return menuDataBase != null && menuDataBase.JobFinished();
        }

        private static IEnumerator ExecuteAfterMenuDataBaseReadyInternal(Action action)
        {
            // 構築完了前はGetDataSize()が途中までの件数しか返さず、公式アイテムを取りこぼす
            LogDebug("MenuDataBaseの構築完了を待機します");

            var startTime = Time.realtimeSinceStartup;
            while (!IsMenuDataBaseReady())
            {
                // 待ち続けると呼び出し元のロード状態が戻らず、以後アイテム更新自体ができなくなる。
                // 修正前と同じ「不完全な一覧」に留めるため、上限時間で打ち切って先へ進める
                if (Time.realtimeSinceStartup - startTime > MenuDataBaseReadyTimeout)
                {
                    LogWarning("MenuDataBaseの構築完了を待機できませんでした。アイテムの一部が表示されない可能性があります");
                    break;
                }
                yield return null;
            }

            action?.Invoke();
        }
```

補足: タイムアウトは `plan-reviewer` の指摘（待機が永久に完了しない場合に `ModItemManager.isLoading` が `true` のまま固定され、「アイテム更新」「キャッシュ再構築」等が二度と実行できなくなる）への対処。打ち切り時の挙動は修正前と同じ「不完全な一覧で進行」であり、退行にはならない。

- [ ] **Step 2: ビルドして通ることを確認する**

Run: `.\debug.bat`
Expected: `ビルドに成功しました`

`System.Collections`（`IEnumerator`）と `System`（`Action`）の using は同ファイルの既存コルーチン（`ExecuteNextFrameInternal`）で既に使われているため追加不要。エラーが出た場合のみ using を追加する。

- [ ] **Step 3: コミット**

```bash
git add source/COM3D2.ModItemExplorer.Plugin/MTEUtils/MTEUtils.cs
git commit -m "feat(utils): MenuDataBase の構築完了を待つヘルパーを追加する"
```

---

### Task 2: 公式アイテムの列挙を MenuDataBase 構築完了後に行う

**Files:**
- Modify: `source/COM3D2.ModItemExplorer.Plugin/Manager/ModItemManager.cs:252-333`（`Load` メソッド）

**Interfaces:**
- Consumes: `MTEUtils.ExecuteAfterMenuDataBaseReady(Action)`（Task 1）
- Produces: なし（`Load` のシグネチャは変更しない）

- [ ] **Step 1: Load() の列挙〜バックグラウンド処理を遅延実行に包む**

現在の `Load` は以下の構造になっている（抜粋）:

```csharp
            _officialMenuFileNameList.Clear();
            _variationMenuPathMap.Clear();
            _variationMenuMap.Clear();

            LoadOfficialMenuFileNameList();

            ThreadPool.QueueUserWorkItem(_ =>
            {
                ...
            });
        }
```

`LoadOfficialMenuFileNameList();` から `});`（`ThreadPool.QueueUserWorkItem` の閉じ）までを `MTEUtils.ExecuteAfterMenuDataBaseReady` のラムダで包み、以下の形にする。

```csharp
            _officialMenuFileNameList.Clear();
            _variationMenuPathMap.Clear();
            _variationMenuMap.Clear();

            // MenuDataBaseは非同期構築のため、完了前に列挙すると公式アイテムを途中までしか登録できない
            MTEUtils.ExecuteAfterMenuDataBaseReady(() =>
            {
                LoadOfficialMenuFileNameList();

                ThreadPool.QueueUserWorkItem(_ =>
                {
                    // （既存の本体をそのまま1段インデントするだけ。処理内容は変更しない）
                });
            });
        }
```

注意点:
- `isLoading = true` と各カウンタのリセット、3 つの `Clear()` は**ラムダの外**（従来どおり同期実行）に残す。多重呼び出しの抑止と UI のローディング表示のため
- `ThreadPool.QueueUserWorkItem` に渡すラムダの中身は 1 行も変更しない。インデントのみ調整する
- `catch` 節の `isLoading = false;` もそのまま残す

- [ ] **Step 2: ビルドして通ることを確認する**

Run: `.\debug.bat`
Expected: `ビルドに成功しました`

- [ ] **Step 3: 実機で公式アイテムが全件登録されることを確認する**

ゲームを起動し（既に起動中ならプラグイン設定画面の再読み込みで `Load(rebuild: true)` を実行）、MCP `com3d25-devbridge` の `eval_csharp` で以下を評価する。

```csharp
var asm = System.AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name.Contains("ModItemExplorer"));
var t = asm.GetTypes().First(x => x.Name == "ModItemManager");
var bf = System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
var inst = t.GetProperty("instance", System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).GetValue(null, null);
var nameMap = (System.Collections.IDictionary)t.GetField("_itemNameMap", bf).GetValue(inst);
var getMenu = t.GetMethod("GetMenu", bf, null, new System.Type[]{typeof(string)}, null);
var mdb = GameMain.Instance.MenuDataBase;
int n = mdb.GetDataSize();
int bucket = 1000;
var should = new int[n/bucket+1]; var have = new int[n/bucket+1];
for (int i=0;i<n;i++){ mdb.SetIndex(i); var f = mdb.GetMenuFileName();
  var m = getMenu.Invoke(inst, new object[]{f}); if (m==null) continue;
  var mt = m.GetType();
  if ((bool)mt.GetField("isHidden").GetValue(m)) continue;
  if ((bool)mt.GetField("isMan").GetValue(m)) continue;
  if (string.IsNullOrEmpty((string)mt.GetField("iconName").GetValue(m))) continue;
  if (!string.IsNullOrEmpty((string)mt.GetField("variationBaseFileName").GetValue(m))) continue;
  if (f.EndsWith("_del.menu")||f.EndsWith("_del_folder.menu")) continue;
  if (!GameMain.Instance.CharacterMgr.status.IsHavePartsItem(f)) continue;
  should[i/bucket]++; if (nameMap.Contains(f)) have[i/bucket]++;
}
var sb = new System.Text.StringBuilder();
sb.AppendLine("JobFinished=" + mdb.JobFinished() + " size=" + n);
for (int b=0;b<should.Length;b++) sb.AppendLine((b*bucket) + "\tshould=" + should[b] + "\thave=" + have[b]);
sb.ToString()
```

Expected: 全バケットで `should == have`（修正前は index 約 10,900 以降が `have=0` だった）

- [ ] **Step 4: 実機で着用中リストに Dress590 の 4 点が出ることを確認する**

Run: `eval_csharp`

```csharp
var root = t.GetProperty("equippedRootItem").GetValue(inst, null);
var children = (System.Collections.IEnumerable)root.GetType().GetProperty("children").GetValue(root, null);
var sb2 = new System.Text.StringBuilder();
foreach (var c in children) { var ct=c.GetType(); sb2.AppendLine(ct.GetProperty("itemName").GetValue(c,null) + "\t" + ct.GetProperty("name").GetValue(c,null)); }
sb2.ToString()
```

Expected: `equipped_acchat` / `equipped_onepiece` / `equipped_stkg` / `equipped_shoes` が含まれる（修正前は 14 件で、この 4 件が欠落していた）

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.ModItemExplorer.Plugin/Manager/ModItemManager.cs
git commit -m "fix(item): 公式アイテムの列挙を MenuDataBase の構築完了後に行う"
```

---

### Task 3: 着用中アイテムがツリーに無い場合に警告を出す

**Files:**
- Modify: `source/COM3D2.ModItemExplorer.Plugin/Manager/ModItemManager.cs:927-964`（`GetEquippedItem` メソッド）

**Interfaces:**
- Consumes: なし
- Produces: なし（戻り値・シグネチャは変更しない）

`GetEquippedItem` は menu が見つからない場合（:949）には警告を出すが、menu はあるのにツリー上の `MenuItem` が見つからない場合は無言で null を返し、`UpdateEquippedItem` が該当アイテムを削除する。今回の不具合が発見しづらかった直接の原因なので、同種の問題を次回すぐ切り分けられるようにする。

ただし `GetEquippedItem` は `IsVisibleMenu`（:1254）を経由せず `_itemNameMap` を直接引くため、`isHidden` / `isMan` / 未所持（`!IsHavePartsItem`）で**意図的に**ツリーから除外されているアイテムを着用している場合も null になる。`UpdateEquippedItem` はアイテム変更のたびに全部位分呼ばれるので、無条件に警告するとログが溢れる。同一ファイル名につき 1 回だけ警告する。

- [ ] **Step 1: 警告済みファイル名の記録用フィールドを追加する**

`ModItemManager` のフィールド定義部（`_officialMenuFileNameList` の宣言がある :195 付近）に以下を追加する。

```csharp
        // 同じアイテムで毎回警告が出るのを防ぐための記録
        private HashSet<string> _warnedNotFoundEquippedItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
```

- [ ] **Step 2: Load() のクリア処理に追加する**

`Load()` 内の同期部分にある 3 つの `Clear()` の並びに 1 行追加する（再構築で状況が変わるため）。

```csharp
            _officialMenuFileNameList.Clear();
            _variationMenuPathMap.Clear();
            _variationMenuMap.Clear();
            _warnedNotFoundEquippedItems.Clear();
```

- [ ] **Step 3: 末尾の return に警告ログを追加する**

現在の末尾:

```csharp
            return GetItemByName<MenuItem>(prop.strFileName);
        }
```

を以下に置き換える。

```csharp
            var equippedItem = GetItemByName<MenuItem>(prop.strFileName);
            if (equippedItem == null && _warnedNotFoundEquippedItems.Add(prop.strFileName))
            {
                // 非表示・男性用・未所持のアイテムを着用している場合もここに来る
                MTEUtils.LogWarning("着用中のアイテムがツリーに登録されていません。" + prop.strFileName);
            }

            return equippedItem;
        }
```

- [ ] **Step 4: ビルドして通ることを確認する**

Run: `.\debug.bat`
Expected: `ビルドに成功しました`

- [ ] **Step 5: 実機でログに当該警告が出ないことを確認する**

Task 2 の修正後は該当ケースが解消しているはずなので、`tail_log` で `着用中のアイテムがツリーに登録されていません` が出力されないことを確認する。

Run: MCP `com3d25-devbridge` の `tail_log`（lines=200）
Expected: 当該警告なし

- [ ] **Step 6: コミット**

```bash
git add source/COM3D2.ModItemExplorer.Plugin/Manager/ModItemManager.cs
git commit -m "fix(item): 着用中アイテムがツリーに無い場合に警告を出す"
```

---

## 完了条件

- `.\debug.bat` が成功する
- 実機で MenuDataBase 全 index 域について `should == have` になる
- 着用中リストに Dress590 の 4 点（acchat / onepiece / stkg / shoes）が表示される
- 実装後に code-review スキルでレビューを通す（CLAUDE.md の標準フロー）

---

## レビュー却下メモ

plan-review（plan-reviewer サブエージェント）の指摘のうち、取り込まなかったもの:

- **`ModItemWindow.cs:422` の `UpdateEquippedItems()` 呼び出しに「isLoading 中は UI 操作不能」という前提をコメントで明記すべき（確信度: 低）** — 現状の UI 遷移では `isLoading` 中は `DrawContentLoading()` が表示され到達不能であることをレビュアー自身が確認しており、実害がない。将来の構造変更に備えた投機的なコメント追加は本修正のスコープ外。
- **`Load()` が 80 行超と長大なので `LoadInternal` への分離を検討（確信度: 低・必須ではない）** — バグ修正のスコープを超えるリファクタリング。今回の変更で増えるネストは 1 段のみ。
