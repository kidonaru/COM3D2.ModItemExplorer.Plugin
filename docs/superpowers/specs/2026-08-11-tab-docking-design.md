# タブドッキング連携 設計書

ModItemExplorer のウィンドウを EditorWindow プラグイン（COM3D25.EditorWindow.Plugin）の
タブドッキングへ参加させる。連携仕様は EditorWindow 側の
[docking-guest-guide.md](../../../../COM3D2.EditorWindow.Plugin2/docs/docking-guest-guide.md) に従う。

## 目的

- ModItemExplorer の 6 ウィンドウを EditorWindow のタブグループへ統合できるようにする
- ウィンドウのリサイズ操作を EditorWindow 側と同一方式（4辺+4隅 + カーソル変化）へ統一する
- EditorWindow が無い環境（COM3D2 2.0 ビルドを含む）では独立ウィンドウとして従来どおり動く

## 方針

ガイドの「A. MTEUtils を使う」を採用し、6 ウィンドウすべてを
`DockableWindowBase`（`COM3D2.MotionTimelineEditor` 名前空間）継承へ全面移行する。
サイズ挙動もウィンドウごとの独自実装をやめ、基底の標準挙動へ揃える。

ただし MTEUtils の `DockableWindowBase`（`6d49b0b`）が内蔵するリサイズは
右下コーナー限定のドラッグで、EditorWindow 側では `WindowResizeController`
（4辺+4隅 + カーソル変化、`9db70fb`）に置き換えられて既に廃止されている。
そのため `WindowResizeController` を MTEUtils へ移設し、両プラグインで共有する。

## スコープ

変更は 3 リポジトリにまたがる。

### ① MTEUtils（submodule・共有）

**ブランチ整理（前提作業）**

ドッキング関連（`DockingClient` / `DockableWindowBase` / `IGUIWindow`）は `origin/main` にのみ存在し、
ModItemExplorer の submodule は `master` を追っていて未 push のローカルコミット `03cd45b`
（GUIComboBox の範囲外例外修正）を 1 つ持つ。

1. `03cd45b` を `origin/main` へ cherry-pick して push
2. ModItemExplorer の submodule を `main` へ切替、`.gitmodules` の `branch` も `main` にする

**`WindowResizeController.cs` / `ResizeCursor.cs` の移設**

EditorWindow から MTEUtils へ移す（`ResizeCursor.cs` には `IResizeCursorProvider` も含まれる）。
名前空間は `COM3D25.EditorWindow.Plugin` → `COM3D2.MotionTimelineEditor` へ変更する。

移設にあたり、EditorWindow 固有の依存 2 つを MTEUtils 側の差し替え点へ置き換える。

| 移設前の依存 | 移設後 |
|---|---|
| `InputRemapper.rawGuiPosition` | `MTEUtils.rawGuiPosition`（既存の `mousePositionGetter` を GUI 座標へ変換） |
| `GuiWindowTracker.IsOverWindowExcept` | `MTEUtils.isOverOtherWindowChecker` フック（既定は常に false） |

```csharp
// MTEUtils.cs へ追加
public static Vector2 rawGuiPosition => new Vector2(mousePosition.x, Screen.height - mousePosition.y);

/// 指定ウィンドウ以外の IMGUI ウィンドウがその座標を覆っているか。
/// 既定は常に false（トラッカーを持たない環境では従来どおり自窓だけで判定する）
public static Func<int, Vector2, bool> isOverOtherWindowChecker = (windowId, guiPos) => false;
```

`mousePositionGetter` は EditorWindow が起動時に `InputRemapper.rawMousePosition` を差しているため、
`rawGuiPosition` は現行の `InputRemapper.rawGuiPosition` と同値になる。

**`DockableWindowBase` の改修**

- `RESIZE_HANDLE_SIZE` と `_isResizing` / `_resizeStartMouse` / `_resizeStartRect` による
  右下コーナードラッグ実装を削除する
- `WindowResizeController` を委譲で保持し、`IResizeCursorProvider` を実装する
- ドラッグ処理を `EditorSubWindow.HandleDragInput` と同型にする
  （リサイズ開始 → `e.Use()`、ヘッダー左押下 → `NotifyHeaderMouseDown`、
  リサイズ中でなければ `GUI.DragWindow(ヘッダー矩形)`）
- `Update` で `_resize.UpdateResize` を回し、確定時に `OnResizeEnd()`（新設 virtual）と
  `StorePlacement` を呼ぶ
- `Close` で `_resize.Cancel()` を呼ぶ
- 移動でも配置が永続化されるよう、`Update` で `windowRect` が前フレームから変化していたら
  `StorePlacement` を呼ぶ。現行の ModItemExplorer は毎フレーム config（メモリ上のオブジェクト）へ
  位置を書き `dirty` を立てるだけで、ファイル I/O は `ConfigManager.Update` が
  「`dirty` かつマウス左ボタンアップ」時に 1 回だけ行う（`ConfigManager.cs:42-48`）。
  `StorePlacement` もこの規約に合わせ、config への書き込み + `dirty` 設定のみ行い、
  ファイル保存は ConfigManager の既存機構へ委ねる（ドラッグ中にディスク書き込みは発生しない）。
  リサイズ確定時のみの保存では移動位置が保存されなくなって退化するため、移動検知は必要。
  なおドッキング参加中はホストが `setRect` で矩形を同期するため、グループ側の矩形も
  そのまま保存される。ゲストからグループ参加状態は見えず、実害は次回起動時に独立窓が
  その位置へ出るだけなので許容する

### ② EditorWindow プラグイン

- 自前の `WindowResizeController.cs` / `ResizeCursor.cs` を削除し、MTEUtils 側を参照する
  （名前空間が変わるため `using COM3D2.MotionTimelineEditor;` を追加）
- 起動時に `MTEUtils.isOverOtherWindowChecker` へ `GuiWindowTracker.IsOverWindowExcept` を接続する
  （`MTEUtils.mousePositionGetter` の設定箇所と同じ `COM3D25.EditorWindow.Plugin.cs:177` 付近）

### ③ ModItemExplorer

**ウィンドウの移行**

| ウィンドウ | 変更点 |
|---|---|
| 共通 | `IWindow` 廃止 → MTEUtils の `IGUIWindow` へ。ヘッダー 20px → 26px、`_headerView` を廃止して基底のヘッダー（タイトル・閉じるボタン）へ移譲。`_contentView` は `windowRect` の実寸から算出し、サイズ変化時に `InitView()` を回す |
| ModItemWindow | 設定 UI の幅/高さスライダーを削除。variation / colorSet ポップアップはドッキング対象外の従属ウィンドウとして現状維持 |
| ModelOperationWindow | 自前の `DrawResizeGrip`（`□` + `_windowSizeDragInfo`）と `ClampWindowSize` を削除 |
| HairLengthWindow | 内容依存の高さ自動計算（`_windowHeight = view.currentPos.y + view.viewRect.y`）を削除 |
| MotionWindow | 「拡張」トグルをヘッダーから content 先頭へ移設する（ドッキング時はタブバーがヘッダーを覆って押せなくなるため）。トグルは表示内容の切替としてのみ残し、高さの強制変更は廃止 |
| WindowManager | `UpdateResizeCursor()` を追加（EditorWindow の `Manager/WindowManager.cs:72-97` と同型）。既存の入力ブロック処理（カメラ・NGUI・ギズモ）は変更しない |

**windowId**

現行値を据え置く（ModItem 582870 / ColorPalette 4581852 / CustomParts 4269465 /
HairLength 741329 / Motion 971237 / ModelOperation 582880）。
いずれも EditorWindow の予約帯 923471〜923488 と衝突せず、保存キーとしての互換も保てる。

**Config の変更**

`colorPalette` / `customParts` / `hairLength` / `motion` の 4 つに
`～WindowWidth` / `～WindowHeight` を追加する。既存キー（`windowWidth` / `windowHeight` /
`windowPosX` / `windowPosY`、`modelOperationWindowWidth` / `modelOperationWindowHeight` 等）は
据え置き、各ウィンドウの `LoadPlacement` / `StorePlacement` から読み書きする。

**COM3D2 2.0 ビルドとの両立**

ModItemExplorer は `#if COM3D25` 分岐で 2.0 版もビルドする。`DockingClient` は
EditorWindow 不在なら standalone フォールバックするため、2.0 版はドッキング無し・
リサイズのみ有効となり、この機能のための分岐は不要。

## データフロー

```
DockingHost (EditorWindow)
    ↑ Register / Unregister / NotifyHeaderMouseDown
DockingClient (MTEUtils, リフレクション)
    ↑
DockableWindowBase (MTEUtils)
    ├ getRect / setRect        … 矩形はホストがタブ同期で書き換える
    ├ isVisible                … isShowWnd
    ├ setTabVisible            … false で OnGUI を早期 return
    └ WindowResizeController   … 4辺+4隅リサイズ
    ↑
ModItemExplorer の 6 ウィンドウ
    ↑ OnGUI / Update / Close
WindowManager (ModItemExplorer)
    └ UpdateResizeCursor      … 全ウィンドウのカーソル要求を仲裁して ResizeCursor.Set
```

## エラーハンドリング

- `DockingClient` はホストの型が見つからなければ standalone として動作し、
  シグネチャ不一致時は警告ログを出して同様に standalone へフォールバックする（実装済み）
- ホストへ渡すデリゲートは例外を投げないようにする。ホスト側は try/catch 済みだが
  `MTEUtils.LogException` にログが出る
- `ResizeCursor` の画像展開に失敗してもリサイズ操作自体は動き、カーソルだけ既定のままになる（実装済み）

## 既知のリスク

**リサイズカーソルの奪い合い**：`ResizeCursor` は MTEUtils が各プラグインへ静的リンクされるため、
ModItemExplorer と EditorWindow がそれぞれ別の静的状態を持つ。`Set` は種別が変わらない限り
`Cursor.SetCursor` を呼ばないので、片側がカーソルを要求していない間は競合しない。
ただし片側がカーソル要求を解除した瞬間に、もう片側の要求中のカーソルを既定へ戻す可能性がある。
実機確認で問題が出た場合のみ対処する（YAGNI）。

## 検証方法

ビルド後、`com3d25-devbridge` の `eval_csharp` で `DockingHost` の登録状況を確認し、実機で確認する。

1. **EditorWindow あり**：ModItemExplorer の窓のヘッダーを EditorWindow の窓のヘッダーへ重ねて
   タブ統合できる／タブ切替で非アクティブ側の描画・入力が止まる／つまみドラッグで分離できる
2. **EditorWindow なし**（および COM3D2 2.0 ビルド）：6 窓が独立ウィンドウとして従来どおり動く
3. **リサイズ**：6 窓すべてで 4辺+4隅リサイズとカーソル変化が働き、両プラグイン併用時に競合しない
4. **既存機能の非退化**：ウィンドウ上でのカメラ操作抑止・NGUI 入力抑止・ギズモロックが従来どおり効く

## 対象外

- スナップ吸着・コネクト（連結移動）— ガイドで v1 の対象外
- グループ構成の次回起動時復元 — 外部窓を含むグループはホスト側で復元されない
- ModItemWindow の variation / colorSet ポップアップのドッキング参加
