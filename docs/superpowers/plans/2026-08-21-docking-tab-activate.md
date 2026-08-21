# ドッキングタブのアクティブ化 API 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:executing-plans でタスク単位に実行する（このワークスペースでは subagent-driven-development は使わない）。ステップはチェックボックス（`- [ ]`）で追跡する。

**Goal:** モデルを配置したときに、モデル操作ウィンドウを（SceneEditor のタブグループに畳まれていても）前面のアクティブタブとして表示できるようにする。

**Architecture:** SceneEditor の `DockingHost` に副作用のないタブアクティブ化 API `ActivateTab(handle)` を追加し、MTEUtils（submodule）の `DockingClient` に後発 API 検出付きのブリッジと `DockableWindowBase.Activate()` を実装する。ModItemExplorer は配置成功時に `ModelOperationWindow.Activate()` を呼ぶだけにする。旧ホスト（`ActivateTab` 未実装）では検出に失敗して前面化のみに劣化し、従来どおり動く。

**Tech Stack:** C# / .NET Framework 3.5、Unity 5.6 IMGUI、UnityInjector プラグイン、MSBuild（VS2022）、git submodule

**Spec:** 本計画がスペックを兼ねる（会話中に合意した内容を以下へ集約した）。

## Global Constraints

- **リポジトリは 3 つにまたがる**。作業ディレクトリは以下。git worktree は使わない
  - MTEUtils（submodule 実体）: `W:\COM3D2_5\work\COM3D2.ModItemExplorer.Plugin\source\COM3D2.ModItemExplorer.Plugin\MTEUtils`（remote: `COM3D2.MTEUtils`、branch: master、HEAD: `c465b0c`）
  - SceneEditor: `W:\COM3D2_5\work\COM3D2.SceneEditor.Plugin`
  - ModItemExplorer: `W:\COM3D2_5\work\COM3D2.ModItemExplorer.Plugin`（親リポジトリ。MTEUtils を submodule として参照）
- `DockingHost` は**公開後シグネチャ変更禁止**の契約（`DockingHost.cs` 冒頭コメント）。既存メソッドは触らず新規メソッドの追加のみ行う
- ゲスト側の後発 API は**存在しなければ機能ごと無効化**する（`DockingClient` の既存作法。`Delegate.CreateDelegate` でキャッシュし、見つからない場合も警告は出さない）
- **`deploy.bat` / `deploy.ps1` は実行しない**
- ビルド確認は MSBuild を直接叩く（`debug.bat` はゲームフォルダへコピーするため、実機反映したくない場合は使わない）:
  ```
  MSYS_NO_PATHCONV=1 "C:/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe" <Plugin>.csproj -p:Configuration=Debug -p:GameVersion=COM3D25 "-p:COM3D2_DIR=W:\COM3D2" "-p:COM3D25_DIR=W:\COM3D2_5" -nologo -v:m
  ```
- 自動テストの基盤は無い（Unity プラグインのため）。各タスクの検証は「ビルド成功」＋「手動確認手順の記載」で代替する
- コミットは Conventional Commits 形式・日本語。push は行わない（ユーザー操作）

## File Structure

| ファイル | 責務 | 変更種別 |
|---|---|---|
| `COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin/DockingHost.cs` | ゲスト向け公開 API。`ActivateTab` を追加 | 変更 |
| `COM3D2.SceneEditor.Plugin/docs-site/dev/docking-guest-guide.md` | ゲスト向け API ドキュメント。`ActivateTab` の追記 | 変更 |
| `MTEUtils/DockingClient.cs` | ホストへのリフレクションブリッジ。`ActivateTab` の検出・呼び出し | 変更 |
| `MTEUtils/DockableWindowBase.cs` | ゲスト窓の基底。`Activate()` と要求処理を追加 | 変更 |
| `ModItemExplorer/ModelOperationWindow.cs` | `_userVisible` を持つため `Activate()` を上書き | 変更 |
| `ModItemExplorer/ModItemWindow.cs` | 配置成功時に操作ウィンドウをアクティブ化 | 変更 |

---

### Task 1: SceneEditor に `DockingHost.ActivateTab` を追加

**Files:**
- Modify: `W:\COM3D2_5\work\COM3D2.SceneEditor.Plugin\source\COM3D2.SceneEditor.Plugin\DockingHost.cs`（`NotifyTabMouseDown` の直後、L136-144 の下）
- Modify: `W:\COM3D2_5\work\COM3D2.SceneEditor.Plugin\docs-site\dev\docking-guest-guide.md`

**Interfaces:**
- Produces: `public static void ActivateTab(object handle)` — ハンドルのウィンドウをタブグループのアクティブへ切り替える。グループ非加入・不正ハンドルは何もしない。ドラッグ候補は記録しない

**背景（実装者向け）:** 既存の `NotifyTabMouseDown` は `TabGroupManager.OnTabPressed` を呼び、`group.SetActive` に加えて `OnTabMouseDown`（つまみドラッグ候補の記録）まで行う。実クリックを伴わずに呼ぶと次フレームの `UpdateTabDrag` が「カーソルがヘッダー外」と判定してタブを分離・移動してしまうため、プログラムからの切替には使えない。そのため副作用のない専用 API を足す。

- [ ] **Step 1: `ActivateTab` を追加する**

`DockingHost.cs` の `NotifyTabMouseDown` メソッドの直後へ挿入する:

```csharp
        /// <summary>
        /// ゲストからのタブアクティブ化要求。押下由来ではないため、
        /// NotifyTabMouseDown と違いつまみドラッグ候補は記録しない
        /// (記録すると次フレームの UpdateTabDrag がタブを分離してしまう)
        /// </summary>
        public static void ActivateTab(object handle)
        {
            var adapter = handle as ExternalWindowAdapter;
            if (adapter == null)
            {
                return;
            }

            var group = adapter.group;
            if (group == null)
            {
                return;
            }
            group.SetActive(adapter);
        }
```

- [ ] **Step 2: ビルドして通ることを確認する**

Run:
```bash
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin/source/COM3D2.SceneEditor.Plugin
MSYS_NO_PATHCONV=1 "C:/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe" COM3D2.SceneEditor.Plugin.csproj -p:Configuration=Debug -p:GameVersion=COM3D25 "-p:COM3D2_DIR=W:\COM3D2" "-p:COM3D25_DIR=W:\COM3D2_5" -nologo -v:m
```
Expected: エラーなしで `... -> ...\bin\Debug\COM3D25\COM3D2.SceneEditor.Plugin.dll` が出力される。csproj 名・パスが違う場合は `ls source/*/` で実際のプロジェクト名を確認して合わせる

- [ ] **Step 3: ゲスト向けガイドへ追記する**

`docs-site/dev/docking-guest-guide.md` の API 一覧（タブバー描画系のブロック、`NotifyTabMouseDown` の説明の直後）へ以下を追加する:

```
void ActivateTab(object handle);          // 自窓のタブをアクティブへ切り替える
                                          // (押下由来でないためドラッグ候補は記録しない。
                                          //  グループ非加入なら何もしない)
```

さらに「タブバー描画の実装義務」節の末尾へ次の 1 項目を追加する:

```markdown
5. プログラムからタブを前面へ出したい場合は `ActivateTab(handle)` を使う。
   `NotifyTabMouseDown` はつまみドラッグ候補の記録を伴うため、実クリック以外から
   呼んではいけない（次フレームでタブが分離してカーソルへ吸い付く）。
   `ActivateTab` も後発 API なので、単独で存在検出して欠けるホストでは無効化すること
```

- [ ] **Step 4: コミット**

```bash
cd /w/COM3D2_5/work/COM3D2.SceneEditor.Plugin
git add source/COM3D2.SceneEditor.Plugin/DockingHost.cs docs-site/dev/docking-guest-guide.md
git commit -m "feat(docking): ゲストからタブをアクティブ化する ActivateTab を追加"
```

---

### Task 2: MTEUtils に `DockingClient.ActivateTab` ブリッジを追加

**Files:**
- Modify: `W:\COM3D2_5\work\COM3D2.ModItemExplorer.Plugin\source\COM3D2.ModItemExplorer.Plugin\MTEUtils\DockingClient.cs`

**Interfaces:**
- Consumes: `DockingHost.ActivateTab(object)`（Task 1）
- Produces:
  - `public static bool isActivateTabAvailable { get; }` — ホストが `ActivateTab` を持つか
  - `public static void ActivateTab(object handle)` — 未対応ホスト・ハンドル null なら何もしない

**注意:** ここは submodule（`COM3D2.MTEUtils` リポジトリ）の作業ツリー。コミットは submodule 側で行う。

- [ ] **Step 1: フィールドを追加する**

`_snapResize` の宣言ブロック（「リサイズ吸着」のコメント群）の直後へ追加する:

```csharp
        // タブのアクティブ化 (ホストが旧バージョンだと存在しない)。
        // 単独で完結する機能なのでスナップ/コネクト系とは別に検出する
        private static Action<object> _activateTab;
```

- [ ] **Step 2: 可用性プロパティを追加する**

`isTabBarAvailable` プロパティの直後へ追加する:

```csharp
        /// <summary>タブのアクティブ化が使えるか (ホストが対応バージョンか)</summary>
        public static bool isActivateTabAvailable
        {
            get
            {
                Initialize();
                return _activateTab != null;
            }
        }
```

- [ ] **Step 3: `Initialize` で検出する**

`Initialize()` 内、タブバー描画系（`enableTabBar` / `notifyTab`）の検出ブロックの直後へ追加する:

```csharp
                // タブのアクティブ化も後発 API のため任意
                var activateTab = type.GetMethod("ActivateTab", BindingFlags.Public | BindingFlags.Static);
                if (activateTab != null)
                {
                    _activateTab = (Action<object>)Delegate.CreateDelegate(
                        typeof(Action<object>), activateTab);
                }
```

同メソッドの `catch` ブロック末尾（`_notifyTabMouseDown = null;` の次行）へ後始末を追加する:

```csharp
                _activateTab = null;
```

- [ ] **Step 4: 公開メソッドを追加する**

`NotifyTabMouseDown` メソッドの直後（クラス末尾）へ追加する:

```csharp
        /// <summary>
        /// 自窓のタブをアクティブへ切り替える。未対応ホスト・未登録なら何もしない。
        /// 押下由来の NotifyTabMouseDown と違い、つまみドラッグ候補は記録されない
        /// </summary>
        public static void ActivateTab(object handle)
        {
            if (handle != null && isActivateTabAvailable)
            {
                _activateTab(handle);
            }
        }
```

- [ ] **Step 5: ビルドして通ることを確認する**

Run:
```bash
cd /w/COM3D2_5/work/COM3D2.ModItemExplorer.Plugin/source/COM3D2.ModItemExplorer.Plugin
MSYS_NO_PATHCONV=1 "C:/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe" COM3D2.ModItemExplorer.Plugin.csproj -p:Configuration=Debug -p:GameVersion=COM3D25 "-p:COM3D2_DIR=W:\COM3D2" "-p:COM3D25_DIR=W:\COM3D2_5" -nologo -v:m
```
Expected: エラーなしでビルドが完了する

- [ ] **Step 6: コミットは Task 3 とまとめる**

`DockableWindowBase.Activate()`（Task 3）と 1 コミットにまとめるため、ここではコミットしない。

---

### Task 3: `DockableWindowBase.Activate()` を追加

**Files:**
- Modify: `W:\COM3D2_5\work\COM3D2.ModItemExplorer.Plugin\source\COM3D2.ModItemExplorer.Plugin\MTEUtils\DockableWindowBase.cs`

**Interfaces:**
- Consumes: `DockingClient.ActivateTab(object)`（Task 2）
- Produces: `public virtual void Activate()` — 表示を ON にし、ドッキング中は自タブをアクティブへ、standalone では次の `OnGUI` で最前面へ持ち上げる

**設計メモ（実装者向け）:**
- `_dockHandle` は `isShowWnd = true` の副作用（`RegisterDocking`）で作られる。非表示から呼ばれた場合はその場ではまだハンドルが無いため、**要求をフレーム跨ぎで保持して `Update()` で処理する**
- 非アクティブタブのときは `OnGUI()` が早期 return するため、要求処理を `OnGUI` に置いてはいけない（アクティブ化したい当の状況で走らない）
- タブグループへの復帰はホスト側の自動再ドッキング（最大 60 フレーム）を待つ必要があるため、**グループ加入（`_tabTitles != null`）まで再試行**する
- `GUI.BringWindowToFront` は GUI コンテキストからしか呼べないためフラグ経由で `OnGUI` に渡す

- [ ] **Step 1: `Update()` の実装位置を確認する**

Run: `grep -n "public virtual void Update\|public void Update" MTEUtils/DockableWindowBase.cs`
Expected: 基底に `Update` があればそこへ処理を足す。**無ければ** `public virtual void Update()` を新規に定義する（`ModelOperationWindow.Update` は `base.Update()` を呼んでいるので、基底側の定義が必要）。実際の定義位置に合わせて以降のステップを適用すること

- [ ] **Step 2: フィールドと `Activate()` を追加する**

`isTabVisible` プロパティ（`_dockTabHidden` の直後あたり）の下へ追加する:

```csharp
        /// <summary>Activate() の要求が残っているフレーム数。0 は要求なし</summary>
        private int _activateRetryFrames;

        /// <summary>
        /// アクティブ化要求を諦めるまでの猶予 (フレーム)。
        /// 再表示直後はホストの自動再ドッキング待ちでグループへ入るまで時間がかかるため、
        /// その待ちを吸収できる長さ (60fps でおよそ 1 秒) を独自に取る。
        /// ホスト側の猶予と厳密に一致させる必要はない (長くても空振りが続くだけ)
        /// </summary>
        private const int ACTIVATE_RETRY_FRAMES = 60;

        /// <summary>次の OnGUI で最前面へ持ち上げるか</summary>
        private bool _bringToFront;

        /// <summary>
        /// ウィンドウを前面へ出す。ドッキング中は自分のタブをアクティブにし、
        /// standalone では最前面へ持ち上げる。
        /// ハンドル生成・グループ復帰を待つ必要があるため、要求だけ立てて Update で処理する
        /// </summary>
        public virtual void Activate()
        {
            isShowWnd = true;
            _activateRetryFrames = ACTIVATE_RETRY_FRAMES;
        }
```

- [ ] **Step 3: 要求処理を `Update()` へ組み込む**

`Update()` の中（基底に無ければ新設して）で `UpdateActivateRequest()` を呼び、次のメソッドを追加する:

```csharp
        /// <summary>
        /// アクティブ化要求の処理。非アクティブタブ中は OnGUI が走らないためここで行う。
        /// グループへ加入するまでは ActivateTab が空振りするので猶予いっぱい再試行する
        /// </summary>
        private void UpdateActivateRequest()
        {
            if (_activateRetryFrames <= 0)
            {
                return;
            }
            _activateRetryFrames--;

            // 表示条件を満たさない窓 (別モード中など) は諦める
            if (!_isShowWnd)
            {
                _activateRetryFrames = 0;
                return;
            }

            _bringToFront = true;

            if (_dockHandle == null)
            {
                // standalone は最前面化だけで足りる
                _activateRetryFrames = 0;
                return;
            }

            DockingClient.ActivateTab(_dockHandle);

            // グループ加入前は空振りするため、タブ状態が push されるまで粘る
            if (_tabTitles != null)
            {
                _activateRetryFrames = 0;
            }
        }
```

- [ ] **Step 4: 非表示化で要求を捨てる**

`isShowWnd` の setter の `else` 側（`UnregisterDocking();` の直後）へ追加する。処理されないまま残った要求が、次に表示されたとき無関係なタイミングで発火するのを防ぐ:

```csharp
                    _activateRetryFrames = 0;
                    _bringToFront = false;
```

- [ ] **Step 5: `OnGUI()` で最前面化する**

`OnGUI()` の `GUI.Window` 呼び出しの直前へ追加する:

```csharp
            if (_bringToFront)
            {
                _bringToFront = false;
                GUI.BringWindowToFront(windowId);
            }
```

- [ ] **Step 6: ビルドして通ることを確認する**

Run:
```bash
cd /w/COM3D2_5/work/COM3D2.ModItemExplorer.Plugin/source/COM3D2.ModItemExplorer.Plugin
MSYS_NO_PATHCONV=1 "C:/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe" COM3D2.ModItemExplorer.Plugin.csproj -p:Configuration=Debug -p:GameVersion=COM3D25 "-p:COM3D2_DIR=W:\COM3D2" "-p:COM3D25_DIR=W:\COM3D2_5" -nologo -v:m
```
Expected: エラーなしでビルドが完了する

- [ ] **Step 7: submodule をコミット**

```bash
cd /w/COM3D2_5/work/COM3D2.ModItemExplorer.Plugin/source/COM3D2.ModItemExplorer.Plugin/MTEUtils
git add DockingClient.cs DockableWindowBase.cs
git commit -m "feat(docking): ウィンドウを前面へ出す Activate を追加"
```

`git status` で master ブランチ上のコミットになっていることを確認する（detached HEAD なら `git checkout master` してからやり直す）。push はしない。

---

### Task 4: 配置時にモデル操作ウィンドウをアクティブにする

**Files:**
- Modify: `W:\COM3D2_5\work\COM3D2.ModItemExplorer.Plugin\source\COM3D2.ModItemExplorer.Plugin\ModelOperationWindow.cs`（`ToggleVisible` の付近と `Update`）
- Modify: `W:\COM3D2_5\work\COM3D2.ModItemExplorer.Plugin\source\COM3D2.ModItemExplorer.Plugin\ModItemWindow.cs`（`CreateSelectedModel`）

**Interfaces:**
- Consumes: `DockableWindowBase.Activate()`（Task 3）
- Produces: `ModelOperationWindow.Activate()` のオーバーライド（`_userVisible` も立てる）

**背景（実装者向け）:** `ModelOperationWindow.Update()` は毎フレーム `isShowWnd = isModelMode && _userVisible;` を計算し直す。基底の `Activate()` が `isShowWnd = true` を立てても `_userVisible` が false のままだと同フレームで消えるため、オーバーライドで `_userVisible` を立てる必要がある。

- [ ] **Step 1: `ModelOperationWindow.Activate()` を上書きする**

`ToggleVisible()` の直後へ追加する:

```csharp
        /// <summary>
        /// 配置直後など外部から前面に呼び出すとき用。
        /// isShowWnd は Update() が _userVisible から計算し直すため、こちらも立てる
        /// </summary>
        public override void Activate()
        {
            _userVisible = true;
            base.Activate();
        }
```

- [ ] **Step 2: `ModItemWindow.CreateSelectedModel` からアクティブ化する**

`CreateSelectedModel()` の `modItemManager.CreateModel(selectedMenuItem, pluginName);` の直後へ追加する:

```csharp
            // モデル操作ウィンドウが扱うのは自前配置分だけなので、MTE 経由の配置では前面へ出さない
            if (pluginName == SelfModelPlacer.PluginName)
            {
                // 配置したモデルをすぐ操作できるよう、操作ウィンドウを前面へ出す
                windowManager.modelOperationWindow.Activate();
            }
```

`SelfModelPlacer` は `COM3D2.ModItemExplorer.Plugin` 名前空間の `ModelPlacement` 配下にあり、`ModItemWindow.cs` から追加の using なしで参照できる（`ModelPlacerManager.cs` と同じ名前空間）。ビルドが通らない場合のみ using を足すこと。

- [ ] **Step 3: 両バージョンをビルドする**

Run:
```bash
cd /w/COM3D2_5/work/COM3D2.ModItemExplorer.Plugin/source/COM3D2.ModItemExplorer.Plugin
MSYS_NO_PATHCONV=1 "C:/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe" COM3D2.ModItemExplorer.Plugin.csproj -p:Configuration=Debug -p:GameVersion=COM3D25 "-p:COM3D2_DIR=W:\COM3D2" "-p:COM3D25_DIR=W:\COM3D2_5" -nologo -v:m
MSYS_NO_PATHCONV=1 "C:/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe" COM3D2.ModItemExplorer.Plugin.csproj -p:Configuration=Debug -p:GameVersion=COM3D2 "-p:COM3D2_DIR=W:\COM3D2" "-p:COM3D25_DIR=W:\COM3D2_5" -nologo -v:m
```
Expected: 両方ともエラーなし

- [ ] **Step 4: 親リポジトリをコミット（submodule ポインタ込み）**

```bash
cd /w/COM3D2_5/work/COM3D2.ModItemExplorer.Plugin
git add source/COM3D2.ModItemExplorer.Plugin/MTEUtils source/COM3D2.ModItemExplorer.Plugin/ModelOperationWindow.cs source/COM3D2.ModItemExplorer.Plugin/ModItemWindow.cs
git commit -m "feat(model-placement): 配置時にモデル操作ウィンドウを前面へ出す"
```

`git show --stat HEAD` で MTEUtils のポインタ更新が含まれていることを確認する。

- [ ] **Step 5: 手動確認手順をユーザーへ提示する**

自動テストが無いため、以下をユーザーに依頼する（DLL はゲーム起動中ロックされるため、ゲーム終了後に反映する）:

1. ゲームを終了し、`debug.bat com3d25`（ModItemExplorer）と SceneEditor 側の `debug.bat com3d25` を実行して両 DLL を反映する
2. ゲームを起動し、モデル操作ウィンドウを SceneEditor の他ウィンドウとタブドッキングさせ、**別のタブをアクティブ**にする
3. ModItemExplorer をモデルモードにしてアイテムを選び、「配置」ボタン または バリエーションのダブルクリックで配置する
4. 期待: モデル操作ウィンドウのタブがアクティブになり前面に出る。タブがグループから分離したりカーソルへ吸い付いたりしない
5. ドッキングしていない状態（standalone）でも配置でウィンドウが最前面に来ることを確認する
6. モデル操作ウィンドウを閉じた（×）状態から配置した場合も、再表示されてアクティブになることを確認する
7. 配置プラグインに MTE 系（自前配置以外）を選んだ場合は、操作ウィンドウが勝手に前面へ出ないことを確認する

---

## Self-Review

- **仕様網羅**: ホスト API 追加（Task 1）/ ゲストブリッジ（Task 2）/ 基底の Activate（Task 3）/ 呼び出しと窓固有の上書き（Task 4）で、合意した 4 点をすべて満たす。ドキュメント更新は Task 1 に同梱
- **プレースホルダ**: 各ステップに実際のコード片を記載済み。`Update()` の有無だけ実装時に確認が要るため Task 3 Step 1 で明示した
- **型整合**: `Activate()` は基底で `public virtual void`、`ModelOperationWindow` で `public override void`。`DockingClient.ActivateTab(object)` / `DockingHost.ActivateTab(object)` はシグネチャ一致。`_dockHandle` / `_tabTitles` / `_isShowWnd` は既存フィールド名と一致（`DockableWindowBase.cs:107,113,77`）

## 未対応（意図的なスコープ外）

- SceneEditor と PostEffects の MTEUtils submodule ポインタ更新: 今回の機能に不要なため据え置く（各リポジトリで次に MTEUtils を更新するときに追従する）
- SceneEditor 内部ウィンドウ（`EditorSubWindow`）への同等 API: ホスト内部からは `TabGroup.SetActive` を直接呼べるため不要

## レビュー却下メモ

- 配置直後に `_tabType = TabType.操作` へ戻す — 要件外。プリセットタブを開いたまま配置したいケースもあり、勝手に切り替えるのは過剰。
- ホスト側 `AUTO_DOCK_RETRY_FRAMES` とゲスト側 `ACTIVATE_RETRY_FRAMES` の双方向クロスリファレンスコメント — 別リポジトリ同士を相互参照コメントで縛るのは結合が強すぎる。ゲスト側の猶予は「ホストの再ドッキング待ちを吸収できれば十分」で厳密一致は不要、という趣旨をゲスト側コメントに明記する形で対応した。
