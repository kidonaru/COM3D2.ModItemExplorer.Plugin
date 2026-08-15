# ギズモ設定同期 + Inspector 汎用化 設計

**日付:** 2026-08-15
**対象リポジトリ:** COM3D2.ModItemExplorer.Plugin (MTE)、COM3D2.EditorWindow.Plugin (EW)、MTEUtils (共有サブモジュール)

## 目的

1. MTE と EW のギズモ操作設定 (なし/移動/回転/拡縮、Local/Global) を双方向同期する
2. EW Inspector の基本描画 (ギズモツール行 + Transform 行) を MTEUtils の共通部品へ抽出し、MTE の ModelOperationWindow でも同じ部品を使う
3. EW Inspector に外部プラグインへの描画委譲点 (InspectorHost) を追加し、MTE 管理モデル選択時は MTE が Inspector 内容を丸ごと描画する

## 背景・前提

- EW は `GizmoRenderer.currentTool` (`GizmoTool.None/Move/Rotate/Scale`) と `GizmoRenderer.useLocalSpace` を public static プロパティで保持。切替 UI は InspectorWindow の `DrawGizmoToolRow`、キー切替は EW 本体にある
- MTE は `SelfModelPlacer.dragType` (`GizmoDragType.None/Move/Rotate/Scale`) を ModelOperationWindow のトグルと Z/X/C キーで切替。Local/Global の概念は UI に無く、`TransformGizmo.useLocalSpace` は常にデフォルト (true = Local)
- MTEUtils は両リポジトリ共有のサブモジュールだが、**各プラグインに個別コンパイルされるため static 状態は共有されない**。プラグイン間の状態同期は必ずリフレクションブリッジ (既存の `SelectionClient` / `GizmoHostClient` / `DockingClient` と同型) で行う
- 既存方針の踏襲: ギズモ本体は MTE 管理モデルでは常に MTE 側 (`ModelGizmoManager`)、EW 側ギズモは `gizmoSuppressed` で抑止 (2026-08-14 の選択同期プラン参照)

## Section 1: ギズモ設定の双方向同期 (GizmoToolClient)

### EW 側

変更なし。`GizmoRenderer.currentTool` / `useLocalSpace` は既に public static プロパティであり、そのまま同期対象になる。

### MTEUtils: GizmoToolClient.cs 新設

`SelectionClient` と同型のリフレクションブリッジ。

- `COM3D2.EditorWindow.Plugin.GizmoRenderer` 型を `DockingClient.FindHostType` で解決し、`currentTool` / `useLocalSpace` の get/set デリゲートを束ねる
- 公開 API: `bool isAvailable`、`GizmoTool tool { get; set; }`、`bool useLocalSpace { get; set; }`
- enum `GizmoTool` は両アセンブリで別型になるため **int 経由で授受**して変換する
- EW 不在・シグネチャ不一致時は `isAvailable = false` で確定フォールバック (呼び出し側は同期しない)

### MTE 側

- `SelfModelPlacer` に `useLocalSpace` プロパティを追加し、`ModelGizmoManager` 経由で全ギズモの `TransformGizmo.useLocalSpace` へ反映する
- 同期は `SelfModelPlacer.Update()` でポーリング:
  - MTE 側での変更 (UI トグル / Z/X/C キー) はその場で `GizmoToolClient` に書き込む
  - 毎フレーム `GizmoToolClient` を読み、**前回同期値**と異なれば MTE 側状態へ取り込む (EW 側 UI / キーの変更に追従)
  - 前回同期値を 1 つ持つことでどちらが動いたかを判別し、同値 no-op でループを防ぐ
- マッピングは 1:1 (`GizmoDragType.None` ⇔ `GizmoTool.None`、以下同様)
- EW 不在時 (`isAvailable == false`) は従来通りローカル状態のみで動作
- ModelOperationWindow のギズモ行に Local/Global トグルボタンを追加 (EW Inspector と同じ「押すたび反転」ボタン)

## Section 2: Transform Inspector 描画部品の MTEUtils 抽出

MTEUtils に `TransformInspectorDrawer.cs` を新設し、EW InspectorWindow と MTE ModelOperationWindow の重複実装を統合する。

- `DrawGizmoToolRow`: なし/移動/回転/拡縮トグル + Local/Global ボタン。現在値の取得と変更通知は `Func<GizmoTool>` / `Action<GizmoTool>` / `Func<bool>` / `Action<bool>` で注入し、**部品自体は状態を持たない**
- `DrawVector3Row`: ラベル + X/Y/Z ドラッグラベル (Shift で 0.1 倍) + 数値入力 + リセットボタンの 1 行。両者の実装差 (EW の操作履歴記録、MTE の ScaleLink 等) はコールバック側で吸収する
- 表示・編集用オイラー角キャッシュ (quaternion 変換による 180 度付近の値飛び対策) を部品側へ移す。**MTE の「ギズモ回転の軸単位加算」ロジック (`RotationCache`) は SelfModelPlacer に残す** (責務が異なるため)
- EW InspectorWindow / MTE ModelOperationWindow を部品利用へ書き換える。**見た目・挙動は現状維持が原則** (挙動不変のリファクタ)

## Section 3: EW Inspector の外部委譲 (InspectorHost)

### EW: InspectorHost 新設

GizmoHost と同型の static 登録式。

- `Register(name, canDraw: Func<GameObject, bool>, draw: ...)` で外部プラグインが登録
- `InspectorWindow.DrawContent` は選択オブジェクトに対し `canDraw` が true の登録者がいれば、ヘッダー以下の内容描画を丸ごとその登録者へ委譲し、従来描画 (ギズモ行 + Transform 行) をスキップする
- 登録者の例外は握って登録者単位で隔離する (GizmoHost と同じ流儀)

### 委譲インターフェースの制約

GUIView は MTEUtils 型だが両アセンブリで別型になるため、draw コールバックへ GUIView を渡すことはできない。委譲は **EW が描画領域 (Rect) と GameObject を渡し、外部側は自前の GUIView で描く**形にする (GizmoHost の Draw 委譲と同じ構図)。

### MTE 側

- MTEUtils に `InspectorHostClient` (リフレクションブリッジ) を新設
- MTE は SelfModelPlacer 管理モデル (`Owns` が true) に対して登録し、Section 2 の共通部品でギズモ行 + Transform 行を描いた上で、アタッチ先コンボ等の MTE 独自行を足す

## 段階分け

各段階でビルド (`debug.bat com3d25`) + 実機確認を行い、独立してコミットする。

1. Section 1: GizmoToolClient + 双方向同期 + MTE の Local/Global 対応
2. Section 2: TransformInspectorDrawer 抽出 (挙動不変)
3. Section 3: InspectorHost + InspectorHostClient + MTE 登録

## スコープ外

- ボーン選択・IK 選択・メイド選択時の EW Inspector 描画 (従来のまま)
- EW 側ギズモ抑止 (`gizmoSuppressed`) の仕組みの変更
- 旧バージョン EW との同期互換 (シグネチャ不一致時は同期無効化で対応)
