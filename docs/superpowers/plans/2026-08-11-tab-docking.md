# タブドッキング連携 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** ModItemExplorer の 6 ウィンドウを MTEUtils の `DockableWindowBase` 継承へ全面移行し、EditorWindow プラグインのタブドッキングへ参加させ、リサイズを 4辺+4隅 + カーソル変化方式へ統一する。

**Architecture:** 3 リポジトリにまたがる。① MTEUtils（共有 submodule）に EditorWindow から `WindowResizeController` / `ResizeCursor` を移設し `DockableWindowBase` を改修 → ② EditorWindow は自前実装を削除して MTEUtils 版を参照 → ③ ModItemExplorer は 6 窓を `DockableWindowBase` 継承へ移行。spec: `docs/superpowers/specs/2026-08-11-tab-docking-design.md`

**Tech Stack:** C# (.NET 3.5 / 4.7.1 両対応・旧形式 csproj・MSBuild)、Unity IMGUI、BepInEx/UnityInjector プラグイン

## Global Constraints

- ヘッダー高さは 26px（`DockableWindowBase.HEADER_HEIGHT`）。ホスト `EditorSubWindow` と揃っている前提のため変更禁止
- windowId は現行値据え置き: ModItem 582870 / ColorPalette 4581852 / CustomParts 4269465 / HairLength 741329 / Motion 971237 / ModelOperation 582880（EditorWindow 予約帯 923471〜923488 と衝突しない）
- 既存 Config キー（`windowWidth` / `windowHeight` / `windowPosX` / `windowPosY`、`modelOperationWindow～` 等）は名前を変えない
- `StorePlacement` は config への書き込み + `config.dirty = true` のみ。ファイル I/O は `ConfigManager.Update`（dirty かつマウス左ボタンアップで保存）に委ねる
- コメント・ログは日本語。既存コードのコメント密度・命名に合わせる
- 自動テスト基盤は無い。各タスクの検証は **ビルド成功**（`build.bat debug` が exit 0、COM3D2/COM3D25 両版）と最終タスクの実機検証（devbridge）で行う
- MTEUtils 単体ではコンパイルできない（csproj を持たない）。MTEUtils 変更のビルド検証は Task 5（EditorWindow）/ Task 6（ModItemExplorer）で行う
- ビルドコマンド:
  - ModItemExplorer: `cmd /c "cd /d W:\COM3D2_5\work\COM3D2.ModItemExplorer.Plugin\source\COM3D2.ModItemExplorer.Plugin && build.bat debug"`（COM3D2/COM3D25 両版がビルドされる）
  - EditorWindow: `cmd /c "cd /d W:\COM3D2_5\work\COM3D2.EditorWindow.Plugin2\source\COM3D25.EditorWindow.Plugin && build.bat debug"`

**リポジトリのパス:**

| 略称 | パス |
|---|---|
| MIE | `W:\COM3D2_5\work\COM3D2.ModItemExplorer.Plugin` |
| MIE-src | `MIE\source\COM3D2.ModItemExplorer.Plugin` |
| MTEUtils | `MIE\source\COM3D2.ModItemExplorer.Plugin\MTEUtils`（submodule 作業ツリー。ここでコミット・push する） |
| EW | `W:\COM3D2_5\work\COM3D2.EditorWindow.Plugin2` |
| EW-src | `EW\source\COM3D25.EditorWindow.Plugin` |
| EW-MTEUtils | `EW\source\COM3D25.EditorWindow.Plugin\MTEUtils`（同じ GitHub リポジトリの別 checkout） |

---

### Task 1: MTEUtils ブランチ整理（03cd45b を main へ集約）

**Files:**
- Modify: `MTEUtils`（submodule の git 状態のみ。ファイル編集なし）
- Modify: `MIE\.gitmodules`

**Interfaces:**
- Produces: MTEUtils submodule が `main` ブランチ（origin/main + 03cd45b）を checkout した状態。以降の MTEUtils タスクはこの上にコミットを積む

**背景:** ドッキング関連（`DockableWindowBase.cs` / `DockingClient.cs` / `IGUIWindow.cs`）は origin/main にのみ存在する。現在の submodule は `master`（origin/master に対し ahead 1）で、未 push コミット `03cd45b`（GUIComboBox 範囲外例外修正）を持つ。

- [ ] **Step 1: main へ切替えて cherry-pick**

```bash
cd "W:/COM3D2_5/work/COM3D2.ModItemExplorer.Plugin/source/COM3D2.ModItemExplorer.Plugin/MTEUtils"
git fetch origin
git checkout -b main origin/main
git cherry-pick 03cd45b
```

コンフリクトした場合: `03cd45b` は `GUIComboBox.cs` のみの修正で、origin/main 側の `9fc4920`（GUIComboBox の API 変更）と競合しうる。競合時は「候補が空のとき矢印ボタンで範囲外例外になる」ガード（空リスト時に index 操作をスキップ）を main 側のコード構造に手で移植して `git cherry-pick --continue`。

- [ ] **Step 2: push**

```bash
git push origin main
```

- [ ] **Step 3: .gitmodules に branch を設定**

`MIE\.gitmodules` を以下に変更:

```
[submodule "source/COM3D2.ModItemExplorer.Plugin/MTEUtils"]
	path = source/COM3D2.ModItemExplorer.Plugin/MTEUtils
	url = https://github.com/kidonaru/COM3D2.MTEUtils.git
	branch = main
```

- [ ] **Step 4: 確認**

Run: `git -C <MTEUtils> log --oneline -3` — 先頭が cherry-pick された GUIComboBox 修正、その下が `6d49b0b` であること。

**注意: Task 2〜5 の間、親リポジトリで `git submodule update` を実行しないこと。** 親の submodule ポインタは Task 6 まで旧 master コミットを指したままなので、実行すると MTEUtils 作業ツリーが巻き戻る（コミットは都度 push するため復旧可能だが、気づかないとビルド失敗の原因調査で時間を失う）。
（親リポジトリ MIE の submodule ポインタ更新のコミットは Task 6 でまとめて行う。この時点では MIE 側はビルドしない — csproj 未更新のため新ファイルは未コンパイルで、既存参照の `GUIComboBox` 等が main の API 変更で壊れる可能性があるが、Task 6 で解消する）

---

### Task 2: MTEUtils — マウス座標フックの追加

**Files:**
- Modify: `MTEUtils\MTEUtils.cs`（`mousePositionGetter` / `mousePosition` の直後。main 切替後は `IsMouseOverWindowRect` の直前にある）

**Interfaces:**
- Produces: `MTEUtils.rawGuiPosition`（`Vector2`、GUI 座標系 = 左上原点）、`MTEUtils.isOverOtherWindowChecker`（`Func<int, Vector2, bool>`）。Task 3 の `WindowResizeController` が使用

- [ ] **Step 1: MTEUtils.cs へ追加**

既存の `mousePosition` プロパティの直後に追加:

```csharp
/// <summary>マウス位置を GUI 座標系 (左上原点) で返す</summary>
public static Vector2 rawGuiPosition => new Vector2(mousePosition.x, Screen.height - mousePosition.y);

/// <summary>
/// 指定ウィンドウ以外の IMGUI ウィンドウがその座標を覆っているかの判定フック。
/// 既定は常に false (トラッカーを持たない環境では従来どおり自窓だけで判定する)
/// </summary>
public static Func<int, Vector2, bool> isOverOtherWindowChecker = (windowId, guiPos) => false;
```

`using System;` が MTEUtils.cs に無ければ追加。

- [ ] **Step 2: コミット**

```bash
cd <MTEUtils>
git add MTEUtils.cs
git commit -m "feat(MTEUtils): GUI座標のマウス位置と他窓被り判定フックを追加"
```

---

### Task 3: MTEUtils — WindowResizeController / ResizeCursor の移設

**Files:**
- Create: `MTEUtils\WindowResizeController.cs`（元: `EW-src\WindowResizeController.cs`）
- Create: `MTEUtils\ResizeCursor.cs`（元: `EW-src\ResizeCursor.cs`。`IResizeCursorProvider` を含む）

**Interfaces:**
- Consumes: Task 2 の `MTEUtils.rawGuiPosition` / `MTEUtils.isOverOtherWindowChecker`
- Produces（namespace `COM3D2.MotionTimelineEditor`）:
  - `class WindowResizeController` — `bool isResizing`、`bool TryBegin(Rect windowRect, Vector2 localPos)`、`bool UpdateResize(ref Rect windowRect, int minWidth, int minHeight)`（戻り値 = このフレームで確定）、`void Cancel()`、`bool IsOverHandle(Rect, Vector2)`、`ResizeCursor.Kind GetCursorKind(Rect windowRect, bool hoverEnabled, int selfWindowId)`、`static readonly int RESIZE_BORDER = 6` / `RESIZE_CORNER = 20`
  - `interface IResizeCursorProvider { bool isResizing { get; } ResizeCursor.Kind desiredCursorKind { get; } }`
  - `static class ResizeCursor` — `enum Kind { None, Horizontal, Vertical, DiagonalDown, DiagonalUp }`、`static void Set(Kind kind)`

- [ ] **Step 1: 2 ファイルをコピーして書き換え**

`EW-src\WindowResizeController.cs` と `EW-src\ResizeCursor.cs` を `MTEUtils\` へコピーし、両ファイルとも:

1. `namespace COM3D25.EditorWindow.Plugin` → `namespace COM3D2.MotionTimelineEditor`
2. `using COM3D2.MotionTimelineEditor;` 行があれば削除（同一 namespace になるため）

`WindowResizeController.cs` のみ、EditorWindow 固有依存 2 箇所を差し替え:

```csharp
// TryBegin 内 (元 :50)
_startMousePos = MTEUtils.rawGuiPosition;          // 元: InputRemapper.rawGuiPosition

// UpdateResize 内 (元 :66)
var delta = MTEUtils.rawGuiPosition - _startMousePos;  // 元: InputRemapper.rawGuiPosition

// GetCursorKind 内 (元 :129-141)
var guiPos = MTEUtils.rawGuiPosition;              // 元: InputRemapper.rawGuiPosition
if (!MTEUtils.isOverOtherWindowChecker(selfWindowId, guiPos))  // 元: GuiWindowTracker.IsOverWindowExcept(...)
```

それ以外のロジック（GetEdge / ToCursorKind / クランプ / PNG_BASE64 等）は一切変更しない。

- [ ] **Step 2: grep で残存依存が無いことを確認**

Run: `grep -n "InputRemapper\|GuiWindowTracker\|COM3D25.EditorWindow" <MTEUtils>/WindowResizeController.cs <MTEUtils>/ResizeCursor.cs`
Expected: ヒット 0 件

- [ ] **Step 3: コミット**

```bash
cd <MTEUtils>
git add WindowResizeController.cs ResizeCursor.cs
git commit -m "feat: EditorWindow から WindowResizeController / ResizeCursor を移設"
```

---

### Task 4: MTEUtils — DockableWindowBase の改修

**Files:**
- Modify: `MTEUtils\DockableWindowBase.cs`

**Interfaces:**
- Consumes: Task 3 の `WindowResizeController` / `ResizeCursor` / `IResizeCursorProvider`
- Produces（Task 5〜15 が依存）:
  - `DockableWindowBase : IGUIWindow, IResizeCursorProvider`
  - 新設 `protected virtual void OnResizeEnd()`（既定は空）
  - 閉じるボタンは `isShowWnd = false` 直接代入ではなく `Close()`（virtual）を呼ぶ
  - `Update()` が移動・リサイズ確定の両方で `StorePlacement` を呼ぶ
  - `RESIZE_HANDLE_SIZE` 定数は削除される

- [ ] **Step 1: 右下コーナーリサイズの削除と WindowResizeController への置換**

`DockableWindowBase.cs` へ以下の変更を加える:

1. フィールド削除: `RESIZE_HANDLE_SIZE`、`_isResizing`、`_resizeStartMouse`、`_resizeStartRect`
2. フィールド追加:

```csharp
private readonly WindowResizeController _resize = new WindowResizeController();

/// <summary>移動の永続化検知用。前フレームの矩形</summary>
private Rect _lastStoredRect;
```

3. クラス宣言を `public abstract class DockableWindowBase : IGUIWindow, IResizeCursorProvider` にし、実装を追加:

```csharp
public bool isResizing => _resize.isResizing;

/// <summary>ホバー中の望ましいカーソル種別。ウィンドウ管理側が仲裁して適用する</summary>
public ResizeCursor.Kind desiredCursorKind =>
    _resize.GetCursorKind(_windowRect, _isShowWnd && !_dockTabHidden, windowId);
```

4. `DrawWindowInternal` を `EditorSubWindow.HandleDragInput` と同型に書き換え（閉じるボタン・NotifyHeaderMouseDown は既存を維持）:

```csharp
private void DrawWindowInternal(int id)
{
    DrawContent();

    // 閉じるボタン (ヘッダー右端)
    var closeRect = new Rect(
        _windowRect.width - CLOSE_BUTTON_WIDTH - CLOSE_BUTTON_MARGIN * 2,
        (HEADER_HEIGHT - CLOSE_BUTTON_HEIGHT) * 0.5f,
        CLOSE_BUTTON_WIDTH,
        CLOSE_BUTTON_HEIGHT);
    if (GUI.Button(closeRect, "x"))
    {
        Close();
        return;
    }

    var e = Event.current;

    // リサイズ開始判定 (4辺+4隅)。開始したらイベントを消費して移動と競合させない
    if (e.type == EventType.MouseDown && e.button == 0 &&
        _resize.TryBegin(_windowRect, e.mousePosition))
    {
        e.Use();
    }

    // ヘッダー左押下をドッキング判定の起点としてホストへ通知する。
    // イベントは消費せず、そのまま GUI.DragWindow の移動に使わせる
    if (e.type == EventType.MouseDown && e.button == 0 &&
        e.mousePosition.y <= HEADER_HEIGHT && !closeRect.Contains(e.mousePosition))
    {
        DockingClient.NotifyHeaderMouseDown(_dockHandle);
    }

    if (!_resize.isResizing)
    {
        GUI.DragWindow(new Rect(0, 0, _windowRect.width, HEADER_HEIGHT));
    }
}
```

5. `Update()` を書き換え:

```csharp
public virtual void Update()
{
    if (_resize.UpdateResize(ref _windowRect, minWidth, minHeight))
    {
        OnResizeEnd();
        StorePlacementInternal();
    }

    // 移動でも配置を永続化する。config への書き込みと dirty 設定だけで、
    // ファイル保存は ConfigManager 側 (マウスアップ時) に委ねられる
    if (_windowRect != _lastStoredRect)
    {
        StorePlacementInternal();
    }
}

private void StorePlacementInternal()
{
    _lastStoredRect = _windowRect;
    StorePlacement(
        (int)_windowRect.x, (int)_windowRect.y,
        (int)_windowRect.width, (int)_windowRect.height);
}
```

6. virtual 追加:

```csharp
/// <summary>リサイズ確定 (マウスアップ) 時に呼ばれる。ビュー再構築などに使う</summary>
protected virtual void OnResizeEnd()
{
}
```

7. `Close()` に `_resize.Cancel();` を追加:

```csharp
public virtual void Close()
{
    _resize.Cancel();
    isShowWnd = false;
}
```

8. `Init()` の末尾で `_lastStoredRect = _windowRect;` を設定（初期化直後の無駄な StorePlacement を防ぐ）

- [ ] **Step 2: grep で旧実装の残骸が無いことを確認**

Run: `grep -n "RESIZE_HANDLE_SIZE\|_isResizing\|_resizeStartMouse\|_resizeStartRect" <MTEUtils>/DockableWindowBase.cs`
Expected: ヒット 0 件

- [ ] **Step 3: コミットして push**

```bash
cd <MTEUtils>
git add DockableWindowBase.cs
git commit -m "feat(docking): DockableWindowBase のリサイズを WindowResizeController へ置換"
git push origin main
```

---

### Task 5: EditorWindow — MTEUtils 版リサイズ実装への切替

**Files:**
- Modify: `EW-MTEUtils`（submodule を Task 4 の main 先端コミットへ bump）
- Delete: `EW-src\WindowResizeController.cs`、`EW-src\ResizeCursor.cs`
- Modify: `EW-src\COM3D25.EditorWindow.Plugin.csproj`
- Modify: `EW-src\COM3D25.EditorWindow.Plugin.cs`
- Modify（using 追加のみ、必要なファイルだけ）: `EW-src\EditorSubWindow.cs`、`EW-src\GameViewWindow.cs`、`EW-src\Manager\WindowManager.cs` ほか `WindowResizeController` / `ResizeCursor` / `IResizeCursorProvider` を参照する全ファイル

**Interfaces:**
- Consumes: Task 2〜4 の MTEUtils main（`rawGuiPosition` / `isOverOtherWindowChecker` / 移設済み 2 ファイル）
- Produces: EditorWindow が MTEUtils 版を参照してビルドできる状態

- [ ] **Step 1: submodule を bump**

```bash
cd "W:/COM3D2_5/work/COM3D2.EditorWindow.Plugin2/source/COM3D25.EditorWindow.Plugin/MTEUtils"
git fetch origin
git checkout origin/main
```

- [ ] **Step 2: 自前実装を削除し csproj を書き換え**

1. `EW-src\WindowResizeController.cs` と `EW-src\ResizeCursor.cs` を削除
2. `COM3D25.EditorWindow.Plugin.csproj` から `<Compile Include="WindowResizeController.cs" />`（:187 付近）と `<Compile Include="ResizeCursor.cs" />`（:193 付近）を削除
3. MTEUtils の ItemGroup（:108-127 付近）へ追加:

```xml
<Compile Include="MTEUtils\WindowResizeController.cs" />
<Compile Include="MTEUtils\ResizeCursor.cs" />
```

- [ ] **Step 3: 参照側の namespace を追従**

`WindowResizeController` / `ResizeCursor` / `IResizeCursorProvider` は `COM3D2.MotionTimelineEditor` へ移ったため、参照する各ファイル（`EditorSubWindow.cs`、`GameViewWindow.cs`、`Manager\WindowManager.cs` を含む。`grep -rln "WindowResizeController\|ResizeCursor\|IResizeCursorProvider" EW-src --include=*.cs` で列挙）に `using COM3D2.MotionTimelineEditor;` が無ければ追加。コードの呼び出し自体は変更不要（型名は同じ）。

- [ ] **Step 4: isOverOtherWindowChecker フックを接続**

`EW-src\COM3D25.EditorWindow.Plugin.cs` の `MTEUtils.mousePositionGetter = () => InputRemapper.rawMousePosition;`（:177 付近）の直後に追加:

```csharp
// リサイズカーソルのホバー判定で、他プラグイン窓に覆われた座標を除外する
MTEUtils.isOverOtherWindowChecker = GuiWindowTracker.IsOverWindowExcept;
```

- [ ] **Step 5: ビルド**

Run: `cmd /c "cd /d W:\COM3D2_5\work\COM3D2.EditorWindow.Plugin2\source\COM3D25.EditorWindow.Plugin && build.bat debug"`
Expected: exit 0

- [ ] **Step 6: コミット**

```bash
cd "W:/COM3D2_5/work/COM3D2.EditorWindow.Plugin2"
git add -A
git commit -m "refactor: WindowResizeController / ResizeCursor を MTEUtils へ移設し参照を切替"
```

---

### Task 6: ModItemExplorer — submodule main 切替と csproj 更新

**Files:**
- Modify: `MIE\.gitmodules`（Task 1 で変更済みの内容をコミット）
- Modify: `MTEUtils`（Task 4 の main 先端を checkout 済みのはず。ずれていれば `git checkout main && git pull`）
- Modify: `MIE-src\COM3D2.ModItemExplorer.Plugin.csproj`

**Interfaces:**
- Produces: MIE が MTEUtils main（ドッキング一式 + リサイズ一式）をコンパイルに含む状態。以降の窓移行タスクは `DockableWindowBase` / `IGUIWindow` / `WindowResizeController` / `ResizeCursor` を参照できる

- [ ] **Step 1: csproj の MTEUtils ItemGroup（:129-152 付近）へ追加**

```xml
<Compile Include="MTEUtils\IGUIWindow.cs" />
<Compile Include="MTEUtils\DockingClient.cs" />
<Compile Include="MTEUtils\DockableWindowBase.cs" />
<Compile Include="MTEUtils\WindowResizeController.cs" />
<Compile Include="MTEUtils\ResizeCursor.cs" />
```

（origin/main 追加分の `ColorPickerWindow.cs` / `CurveData.cs` / `CurveEditorWindow.cs` / `TexturePickerWindow.cs` は本機能で不要のため追加しない）

- [ ] **Step 2: API 変更の影響範囲を事前に洗い出す**

ビルド前に変更規模を把握する（反応的な修正だけにしない）:

```bash
cd <MTEUtils>
# 既存共有ファイルの master → main 差分（追従が必要な API 変更の一覧）
git diff bc768e4 origin/main --stat
git diff bc768e4 origin/main -- GUIComboBox.cs GUIView.cs GearMenu.cs MTEUtils.cs
# csproj に含めるファイルが、含めない main 追加ファイル (ColorPickerWindow / CurveData /
# CurveEditorWindow / TexturePickerWindow) を参照していないか確認
grep -n "ColorPickerWindow\|CurveData\|CurveEditorWindow\|TexturePickerWindow" GUIComboBox.cs GUIView.cs GearMenu.cs MTEUtils.cs DockableWindowBase.cs DockingClient.cs IGUIWindow.cs WindowResizeController.cs ResizeCursor.cs
```

参照が見つかったファイルは csproj の追加対象に含める。次に MIE-src 側の呼び出し箇所を列挙:

```bash
grep -rn "GUIComboBox\|GearMenu" <MIE-src> --include=*.cs --exclude-dir=MTEUtils
```

- [ ] **Step 3: ビルドと API 追従**

Run: `cmd /c "cd /d W:\COM3D2_5\work\COM3D2.ModItemExplorer.Plugin\source\COM3D2.ModItemExplorer.Plugin && build.bat debug"`
Expected: exit 0（両版）。
失敗する場合は Step 2 で洗い出した API 変更（`9fc4920` GUIComboBox の外部ウィンドウ描画 API 化、`704e9c4` GearMenu の namespace 変更、`bf639f8` mousePositionGetter）へ呼び出し側を追従させる。追従変更もこのタスクに含める。追従規模が大きい（複数ファイル・挙動変更を伴う）場合は独立コミットに分ける。

- [ ] **Step 4: コミット**

```bash
cd "W:/COM3D2_5/work/COM3D2.ModItemExplorer.Plugin"
git add .gitmodules source/COM3D2.ModItemExplorer.Plugin/MTEUtils source/COM3D2.ModItemExplorer.Plugin/COM3D2.ModItemExplorer.Plugin.csproj
git add -A  # Step 3 で API 追従した場合の変更を含める
git commit -m "build: MTEUtils submodule を main へ切替えドッキング関連ソースを追加"
```

---

### Task 7: ModItemExplorer — IWindow を IGUIWindow へ置換

**Files:**
- Modify: `MIE-src\Manager\WindowManager.cs`（:8-21 の `IWindow` インターフェース定義を削除、`List<IWindow>` → `List<IGUIWindow>`）
- Modify: `MIE-src\ModItemWindow.cs` / `ColorPaletteWindow.cs` / `CustomPartsWindow.cs` / `HairLengthWindow.cs` / `MotionWindow.cs` / `ModelOperationWindow.cs`（`: IWindow` → `: IGUIWindow`）

**Interfaces:**
- Consumes: Task 6 の `COM3D2.MotionTimelineEditor.IGUIWindow`（メンバーは旧 `IWindow` と完全に同一: `windowIndex` / `isShowWnd` / `windowRect` + `Init/Update/Close/OnLoad/OnScreenSizeChanged/OnChangedSceneLevel/OnGUI`）
- Produces: 全 6 窓が `IGUIWindow` 実装。以降のタスクで 1 窓ずつ `DockableWindowBase` 継承に置き換えても他の窓に影響しない

- [ ] **Step 1: 機械的置換**

1. `WindowManager.cs` から `public interface IWindow { ... }`（:8-21）を削除
2. 同ファイルの `List<IWindow>` と `IWindow` 型の変数・引数をすべて `IGUIWindow` へ（`using COM3D2.MotionTimelineEditor;` は既にある）
3. 6 窓の class 宣言 `: IWindow` を `: IGUIWindow` へ
4. Run: `grep -rn "IWindow" <MIE-src> --include=*.cs` — `IGUIWindow` 以外のヒットが 0 件であること

- [ ] **Step 2: ビルド**

Run: ModItemExplorer ビルドコマンド。Expected: exit 0

- [ ] **Step 3: コミット**

```bash
git add -A && git commit -m "refactor(window): IWindow を MTEUtils の IGUIWindow へ置換"
```

---

### Task 8: Config — 4 窓のサイズキー追加

**Files:**
- Modify: `MIE-src\Config.cs`（:46-67 の表示設定ブロック）

**Interfaces:**
- Produces: 以下の public フィールド（Task 9〜12 の `LoadPlacement` / `StorePlacement` が読み書き）

- [ ] **Step 1: Config.cs へ追加**

既存の各 `～WindowPosX/Y` の直後にそれぞれ追加:

```csharp
public int colorPaletteWindowWidth = 540;
public int colorPaletteWindowHeight = 240;
public int customPartsWindowWidth = 480;
public int customPartsWindowHeight = 360;
public int hairLengthWindowWidth = 320;
public int hairLengthWindowHeight = 320;
public int motionWindowWidth = 520;
public int motionWindowHeight = 320;
```

既定値の根拠: 各窓の現行 `WINDOW_WIDTH` / `WINDOW_HEIGHT` 定数（Motion のみ高さは拡張時の `WINDOW_HEIGHT_EXTEND = 320` を採用。移行後は高さの強制切替が無くなるため、拡張内容が収まる値を初期値にする）。

- [ ] **Step 2: ビルド + コミット**

Run: ModItemExplorer ビルドコマンド。Expected: exit 0

```bash
git add source/COM3D2.ModItemExplorer.Plugin/Config.cs
git commit -m "feat(config): 4ウィンドウのサイズ保存キーを追加"
```

---

### 窓移行の共通パターン（Task 9〜14 で繰り返し適用）

各窓を `IGUIWindow` 実装から `DockableWindowBase` 継承へ変える。**このパターンを各タスクで一字一句適用し、窓ごとの固有差分は各タスクに記載する。**

1. **class 宣言**: `public class XxxWindow : IGUIWindow` → `public class XxxWindow : DockableWindowBase`
2. **削除するメンバー**（基底が提供するため）:
   - `windowIndex` / `isShowWnd` の自動プロパティ、`private Rect _windowRect` + `windowRect` プロパティ
   - `HEADER_HEIGHT` 定数（基底の 26px を使う。`DockableWindowBase.HEADER_HEIGHT` で参照）
   - `_headerView` フィールドと `DrawHeader()`（タイトル・閉じるボタンは基底へ移譲）
   - `InitGUI()` 内の config からの位置復元と `MTEUtils.AdjustWindowPosition(ref _windowRect)` 呼び出し（`LoadPlacement` へ移す）
   - `OnGUI()` 内の `GUI.Window(...)` 呼び出しと config への位置書き戻しブロック（基底 `OnGUI` + `StorePlacement` へ移譲）
   - `DrawWindow(int id)` 内の `GUI.DragWindow()`（基底が処理）
3. **abstract 実装**:

```csharp
protected override int windowId => WINDOW_ID;
protected override string windowTitle => /* 現行 OnGUI の GUI.Window 第4引数の文字列をそのまま */;
protected override int minWidth => /* 各タスク記載の値 */;
protected override int minHeight => /* 各タスク記載の値 */;
```

4. **配置の永続化**（キー名は各タスク記載）:

```csharp
protected override void LoadPlacement(out int x, out int y, out int width, out int height)
{
    x = config.xxxWindowPosX;
    y = config.xxxWindowPosY;
    width = config.xxxWindowWidth;
    height = config.xxxWindowHeight;
}

protected override void StorePlacement(int x, int y, int width, int height)
{
    config.xxxWindowPosX = x;
    config.xxxWindowPosY = y;
    config.xxxWindowWidth = width;
    config.xxxWindowHeight = height;
    config.dirty = true;
}
```

5. **DrawContent**: 旧 `DrawWindow(int id)` の中身から `DrawHeader()` と `GUI.DragWindow()` を除いたものを `protected override void DrawContent()` にする（`_rootView.ResetLayout(); Draw～(); _rootView.DrawComboBox();` は維持）。
6. **サイズ追従**: `_windowWidth` / `_windowHeight` フィールドは「現在ビューを構築済みのサイズ」の意味で残し、`Update()` override で実寸との差分を検知してビューを再構築する:

```csharp
public override void Update()
{
    base.Update();

    // リサイズや ドッキングのタブ同期で実寸が変わったらビューを作り直す
    if (_windowWidth != (int)windowRect.width || _windowHeight != (int)windowRect.height)
    {
        _windowWidth = (int)windowRect.width;
        _windowHeight = (int)windowRect.height;
        InitView();
    }

    /* 窓固有の既存 Update 処理があればここに残す */
}
```

7. **InitView**: `_headerView.Init(...)` 行と `_headerView.parent = _rootView;` 行を削除し、`_contentView.Init(0, DockableWindowBase.HEADER_HEIGHT, _windowWidth, _windowHeight - DockableWindowBase.HEADER_HEIGHT);` に変更（他ビューの HEADER_HEIGHT 参照も同様に基底定数へ）。
8. **Init override**: 旧 `Init()` / `InitGUI()` にビュー初期化・購読等があれば `public override void Init() { base.Init(); _windowWidth = (int)windowRect.width; _windowHeight = (int)windowRect.height; InitView(); /* 既存の初期化 */ }` の形に統合。
9. **OnGUI**: 旧 OnGUI に `MTEUtils.ResetInputOnScroll(...)` 呼び出しがある場合は残す:

```csharp
public override void OnGUI()
{
    base.OnGUI();
    if (isShowWnd)
    {
        /* 現行 OnGUI の MTEUtils.ResetInputOnScroll 呼び出しを引数そのままここへ */
    }
}
```

10. **OnScreenSizeChanged**: 現行実装があれば `public override void OnScreenSizeChanged()` として維持（`ref _windowRect` が必要な箇所は `var rect = windowRect; ...(ref rect); windowRect = rect;` に変える）。

**各タスク共通の検証手順:**
- Run: `grep -n "_headerView\|GUI.DragWindow\|GUI.Window(" <対象ファイル>` — ヒット 0 件（ModItemWindow のみ例外あり、Task 14 参照）
- Run: ModItemExplorer ビルドコマンド。Expected: exit 0（両版）
- コミット（メッセージは各タスク記載）

---

### Task 9: ColorPaletteWindow の移行

**Files:**
- Modify: `MIE-src\ColorPaletteWindow.cs`

**Interfaces:**
- Consumes: 共通パターン + `config.colorPaletteWindowPosX/Y`（既存）、`config.colorPaletteWindowWidth/Height`（Task 8）

- [ ] **Step 1: 共通パターンを適用**

固有差分:
- `WINDOW_ID = 4581852`、`minWidth => 540`、`minHeight => 240`（現行 `WINDOW_WIDTH` / `WINDOW_HEIGHT`）
- `InitView()`（:104-111）は `_colorPickerView`（左、幅 `COLOR_PICKER_SIZE + 10`）と `_contentView`（右）の 2 分割 — この構造は維持し、HEADER_HEIGHT 参照と `_headerView` 行のみ共通パターンどおり変更
- 配置キー: `colorPaletteWindowPosX/Y` + `colorPaletteWindowWidth/Height`

- [ ] **Step 2: 検証 + コミット**

共通検証手順を実行。

```bash
git add source/COM3D2.ModItemExplorer.Plugin/ColorPaletteWindow.cs
git commit -m "feat(docking): ColorPaletteWindow を DockableWindowBase 継承へ移行"
```

---

### Task 10: CustomPartsWindow の移行

**Files:**
- Modify: `MIE-src\CustomPartsWindow.cs`

**Interfaces:**
- Consumes: 共通パターン + `config.customPartsWindowPosX/Y`（既存）、`config.customPartsWindowWidth/Height`（Task 8）

- [ ] **Step 1: 共通パターンを適用**

固有差分:
- `WINDOW_ID = 4269465`、`minWidth => 480`、`minHeight => 360`
- 配置キー: `customPartsWindowPosX/Y` + `customPartsWindowWidth/Height`

- [ ] **Step 2: 検証 + コミット**

共通検証手順を実行。

```bash
git commit -am "feat(docking): CustomPartsWindow を DockableWindowBase 継承へ移行"
```

---

### Task 11: HairLengthWindow の移行（高さ自動計算の廃止）

**Files:**
- Modify: `MIE-src\HairLengthWindow.cs`

**Interfaces:**
- Consumes: 共通パターン + `config.hairLengthWindowPosX/Y`（既存）、`config.hairLengthWindowWidth/Height`（Task 8）

- [ ] **Step 1: 共通パターンを適用**

固有差分:
- `WINDOW_ID = 741329`、`minWidth => 320`、`minHeight => 320`
- **高さ自動計算を削除**: `DrawContent()` 末尾の `_windowHeight = (int)(view.currentPos.y + view.viewRect.y);`（:340-342）と、OnGUI 側の追従ブロック `if (_windowHeight != windowRect.height) { _windowRect.height = _windowHeight; InitView(); }`（:196-200）を削除
- **スクロールビューを追加**: 現行の HairLengthWindow はスクロール未対応（auto-fit 前提）のため、削除だけだと毛髪グループ数が多いモデルでスライダーが窓外に隠れて操作不能になる。コンテンツ描画（スライダーのループ部分）を `view.BeginScrollView();` 〜 `view.EndScrollView();` で括る（MotionWindow.cs:281-427 の `DrawContentExtend` と同じ GUIView API を使用）
- `_headerView` の変則実装（:225-238、`BeginLayout(Free)` 無し）は丸ごと削除対象なので特別対応不要
- 配置キー: `hairLengthWindowPosX/Y` + `hairLengthWindowWidth/Height`

- [ ] **Step 2: 検証 + コミット**

共通検証手順を実行。

```bash
git commit -am "feat(docking): HairLengthWindow を DockableWindowBase 継承へ移行"
```

---

### Task 12: MotionWindow の移行（拡張トグルの content 移設）

**Files:**
- Modify: `MIE-src\MotionWindow.cs`

**Interfaces:**
- Consumes: 共通パターン + `config.motionWindowPosX/Y`（既存）、`config.motionWindowWidth/Height`（Task 8）、`config.animationExtend`（既存）

- [ ] **Step 1: 共通パターンを適用**

固有差分:
- `WINDOW_ID = 971237`、`minWidth => 520`、`minHeight => 80`（現行の非拡張時高さ。既定サイズは config 初期値 520x320）
- **拡張トグルの移設**: ヘッダー内トグル（:189-197）を削除し、`DrawContent()` の先頭で描画する（ドッキング時はタブバーがヘッダーを覆って押せなくなるため）:

```csharp
view.DrawToggle("拡張", config.animationExtend, 60, 20, newValue =>
{
    config.animationExtend = newValue;
    config.dirty = true;
});
```

- **高さの強制変更を廃止**: `_windowHeight = config.animationExtend ? WINDOW_HEIGHT_EXTEND : WINDOW_HEIGHT; if (...) { _windowRect.height = ...; InitView(); }`（:144-150）を削除。`WINDOW_HEIGHT_EXTEND` 定数も未使用になるため削除。トグルは `DrawContentExtend()` / 1 行版 `DrawContent()`（:168-175 の分岐）の表示切替としてのみ機能させる（旧 `DrawContent()` は基底の abstract と名前が衝突するため、1 行版を `DrawContentCompact()` 等へリネームし、`protected override void DrawContent()` が分岐して呼ぶ形にする）
- 配置キー: `motionWindowPosX/Y` + `motionWindowWidth/Height`

- [ ] **Step 2: 検証 + コミット**

共通検証手順を実行。

```bash
git commit -am "feat(docking): MotionWindow を移行し拡張トグルを content へ移設"
```

---

### Task 13: ModelOperationWindow の移行（リサイズグリップの廃止）

**Files:**
- Modify: `MIE-src\ModelOperationWindow.cs`

**Interfaces:**
- Consumes: 共通パターン + `config.modelOperationWindowPosX/Y/Width/Height`（すべて既存キー）

- [ ] **Step 1: 共通パターンを適用**

固有差分:
- `WINDOW_ID = 582880`、`minWidth => 380`、`minHeight => 320`（現行 `MIN_WINDOW_WIDTH` / `MIN_WINDOW_HEIGHT`）
- **自前リサイズの削除**: `DrawResizeGrip()`（:280-309）、`_windowSizeDragInfo`（:126）、`ClampWindowSize()`（:311-317）と、`OnScreenSizeChanged` / `InitGUI` からの `ClampWindowSize()` 呼び出し、OnGUI の config サイズ差分検知ブロック（:242-250）を削除。`if (!_windowSizeDragInfo.isDragging) { GUI.DragWindow(); }`（:270-278）も共通パターンどおり削除
- サイズ変化時の `InitView()` 再構築は共通パターンの `Update()` override が担う
- 配置キー: `modelOperationWindowPosX/Y` + `modelOperationWindowWidth/Height`（既存キーをそのまま使用）

- [ ] **Step 2: 検証 + コミット**

共通検証手順を実行（grep に `DrawResizeGrip\|_windowSizeDragInfo\|ClampWindowSize` も追加し 0 件を確認）。

```bash
git commit -am "feat(docking): ModelOperationWindow を移行し自前リサイズグリップを廃止"
```

---

### Task 14: ModItemWindow の移行（従属ウィンドウ維持・サイズグリップ廃止）

**Files:**
- Modify: `MIE-src\ModItemWindow.cs`

**Interfaces:**
- Consumes: 共通パターン + `config.windowPosX/Y`、`config.windowWidth/Height`、`config.naviWidth`（すべて既存キー）

- [ ] **Step 1: 共通パターンを適用**

固有差分:
- `WINDOW_ID = 582870`、`minWidth => 640`、`minHeight => 480`（現行 `MIN_WINDOW_WIDTH` / `MIN_WINDOW_HEIGHT`）
- 配置キー: `windowPosX/Y` + `windowWidth/Height`（既存キーをそのまま使用）
- **閉じるボタンの挙動維持**: 現行のヘッダー x ボタンは `isShowWnd = false` に加えて `plugin.isEnable = false` を行う。基底の閉じるボタンは `Close()`（virtual）を呼ぶため、override で維持する:

```csharp
public override void Close()
{
    base.Close();
    plugin.isEnable = false;
}
```

- **ウィンドウサイズグリップの削除**: フッターの `□` グリップ（:1762-1777、`config.windowWidth/Height` を書くもの）と対応する `_windowSizeDragInfo` を削除。**ナビ幅グリップ（:1744-1760、`naviWidth`）は現状維持**
- **variation / colorSet 従属ウィンドウは現状維持**: `VARIATION_WINDOW_ID` / `COLOR_SET_WINDOW_ID` の `GUI.Window` 呼び出し（:357-390 の追従処理、`DrawVariationWindow` / `DrawColorSetWindow`）はドッキング対象外としてそのまま残す。**呼び出し位置は `OnGUI()` override 内で `base.OnGUI()` の後**（メイン窓と同階層の兄弟呼び出し）とすること。`DrawContent()` 内（= メイン窓の GUI.Window コールバック内部）へ移すと GUI.Window のネストになり入力・重なり順が壊れるため禁止。`_windowRect` フィールド参照は `windowRect` プロパティ経由に置換（ドラッグ差分を親へ加算する箇所は `var rect = windowRect; rect.position += diffPosition; windowRect = rect;` の形へ）
- `InitView()`（:221-242）の 6 ビュー構成（`_infoView / _naviView / _contentView / _contentSettingView / _footerView`）は維持し、`_headerView` 行の削除と HEADER_HEIGHT の基底定数化のみ行う

- [ ] **Step 2: 検証 + コミット**

共通検証の grep は本窓のみ例外: `GUI.Window(` は従属ウィンドウ 2 つ分（VARIATION / COLOR_SET）のヒットが残ることが正しい。本体窓の `GUI.Window(WINDOW_ID` と `_headerView` が 0 件であることを確認。ビルド exit 0。

```bash
git commit -am "feat(docking): ModItemWindow を移行しサイズグリップを廃止 (従属窓は維持)"
```

---

### Task 15: WindowManager — リサイズカーソル仲裁の追加

**Files:**
- Modify: `MIE-src\Manager\WindowManager.cs`

**Interfaces:**
- Consumes: Task 9〜14 の全窓が `IResizeCursorProvider`（`DockableWindowBase` 経由）を実装済みであること

- [ ] **Step 1: UpdateResizeCursor を追加**

`Update()`（:94-114）の `UpdateInputBlock();` の直前に `UpdateResizeCursor();` 呼び出しを追加し、メソッドを追加（EditorWindow の `Manager/WindowManager.cs:76-107` と同型）:

```csharp
/// <summary>
/// 全ウィンドウのリサイズカーソル要求を仲裁して適用する。
/// カーソルはアプリ全体で 1 つなので、リサイズ中のウィンドウを最優先し、
/// 次にカーソルがつかみ範囲に乗っているウィンドウを採用する
/// </summary>
private void UpdateResizeCursor()
{
    var kind = ResizeCursor.Kind.None;

    foreach (var window in windows)
    {
        var provider = window as IResizeCursorProvider;
        if (provider == null)
        {
            continue;
        }

        if (provider.isResizing)
        {
            kind = provider.desiredCursorKind;
            break;
        }

        if (kind == ResizeCursor.Kind.None)
        {
            kind = provider.desiredCursorKind;
        }
    }

    ResizeCursor.Set(kind);
}
```

既存の入力ブロック処理（`UpdateInputBlock` / カメラ・NGUI・ギズモ）は変更しない。

- [ ] **Step 2: ビルド（両版）+ コミット**

Run: ModItemExplorer ビルドコマンド。Expected: exit 0（COM3D2 版・COM3D25 版とも）

```bash
git add source/COM3D2.ModItemExplorer.Plugin/Manager/WindowManager.cs
git commit -m "feat(window): リサイズカーソルの仲裁処理を追加"
```

---

### Task 16: 実機検証

**Files:** なし（検証のみ。不具合が出たら該当タスクのファイルへ修正コミットを追加）

前提: COM3D2.5 が起動しており、Task 5 / Task 15 のビルドで両プラグインの DLL がデプロイ済み（`build.bat` が自動デプロイする。ゲーム起動中でロックされていた場合はゲーム再起動→再ビルド）。

- [ ] **Step 1: DockingHost 登録状況の確認**

MCP `com3d25-devbridge` の `eval_csharp` で:

```csharp
var t = System.Type.GetType("COM3D25.EditorWindow.Plugin.DockingHost, COM3D25.EditorWindow.Plugin");
var m = t.GetMethod("EnumerateDockables", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
var list = ((System.Collections.IEnumerable)m.Invoke(null, null)).Cast<object>().Select(o => o.ToString()).ToList();
string.Join("\n", list.ToArray())
```

Expected: ModItemExplorer の表示中ウィンドウが `ExternalWindowAdapter` として列挙される（ModItemExplorer の窓を表示した状態で実行）。

- [ ] **Step 2: spec の検証 4 項目を実機で確認**

1. **EditorWindow あり**: ModItemExplorer の窓ヘッダーを EditorWindow の窓ヘッダーへ重ねてタブ統合できる／タブ切替で非アクティブ側の描画・入力が止まる／つまみドラッグで分離できる
2. **EditorWindow なし**: EditorWindow の DLL を一時退避して起動し、6 窓が独立ウィンドウとして従来どおり動く（COM3D2 2.0 版も同様に確認）
3. **リサイズ**: 6 窓すべてで 4辺+4隅リサイズとカーソル変化が働き、両プラグイン併用時に競合しない
4. **非退化**: ウィンドウ上でのカメラ操作抑止・NGUI 入力抑止・ギズモロックが従来どおり効く。ウィンドウ位置・サイズがゲーム再起動後に復元される
5. **非アクティブタブ中の背景処理**: タブ非アクティブ化した窓（特にサムネ撮影を伴う ModelOperation/CustomParts、モーション再生の Motion）で無駄・副作用のある Update 処理が観測されないか確認する。問題があれば `OnTabVisibleChanged(bool)` override で抑止を追加（YAGNI: 観測されるまで実装しない）

- [ ] **Step 3: 不具合修正のコミット（あれば）と最終ビルド**

Run: 両リポジトリのビルドコマンド。Expected: exit 0

---

## レビュー却下メモ

plan-reviewer の指摘のうち以下は却下（理由付き）:

- 「`IGUIWindow` の実メンバー未検証のまま IWindow と同一と断定」 — 誤検知。計画作成時の調査で origin/main の `IGUIWindow.cs` 全文を取得済みで、メンバーが旧 `IWindow` と完全一致（`windowRect` は get/set 両方あり）することを確認している
- 「`OnTabVisibleChanged` フックの活用検討が欠落」 — 実装としては却下（YAGNI）。基底が非アクティブタブで OnGUI を止めるため描画コストは既に抑止される。Update 側の背景処理は Task 16 の検証項目 5 として観測し、問題があった場合のみ対処する
