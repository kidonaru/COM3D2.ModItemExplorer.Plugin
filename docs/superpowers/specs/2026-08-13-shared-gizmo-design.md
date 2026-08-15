# 共通ギズモ設計: MTEUtils 共通化 + SceneView 対応

日付: 2026-08-13
対象リポジトリ: COM3D2.MTEUtils / COM3D2.EditorWindow.Plugin / COM3D2.ModItemExplorer.Plugin

## 背景と目的

ModItemExplorer (MIE) の配置モデルギズモはゲーム本体の `GizmoRender` 派生
(`ModelGizmoRender`) で実装されている。`GizmoRender` はヒット判定を
`Camera.main.ScreenPointToRay(Input.mousePosition)` にハードコードしているため、
EditorWindow (EW) プラグインの SceneView（別カメラ + 専用 RT）上では描画はされても
操作が幾何的に成立しない。

本設計では EW の `GizmoRenderer` が持つカメラ非依存のギズモロジックを MTEUtils へ
抽出して両プラグインで共有し、MIE のギズモを `GizmoRender` から全面移行する。
EW 併用時は SceneView / GameView の両方で操作でき、EW 不在でも standalone で
動作することを目的とする。

## 全体構成

```
MTEUtils (ソース共有サブモジュール)
├── TransformGizmo.cs      … ギズモ本体 (描画 + ヒット判定 + ドラッグ解決)
└── GizmoHostClient.cs     … EW の GizmoHost へのリフレクションブリッジ

COM3D2.EditorWindow.Plugin
├── GizmoHost.cs           … 外部ギズモ登録の公開 API (新規)
└── Manager/GizmoRenderer.cs … TransformGizmo を内包する薄いホストへリファクタ

COM3D2.ModItemExplorer.Plugin
├── ModelPlacement/ModelGizmoRender.cs  … 削除
├── ModelPlacement/GizmoRenderHack.cs   … 存置 (WindowManager のギズモ誤掴み抑止が使用)
└── ModelPlacement/ModelGizmoManager.cs … 配置モデルごとの TransformGizmo 管理 (新規)
```

MTEUtils は各プラグインへ個別にコンパイルされるため、共通化はソースレベル。
クロスアセンブリの連携は DockingHost / DockingClient と同じ
「プリミティブ + デリゲートのみの静的 API + リフレクション解決」方式を踏襲する。
UnityEngine 型 (`Camera` / `Vector2` 等) はアセンブリ間で共有されるため
デリゲート引数に使ってよい。

## 1. TransformGizmo (MTEUtils)

EW の `GizmoRenderer` から抽出するカメラ非依存コア。

- ツール: Move / Rotate / Scale (+ None)。Local / Global 軸切替
- 描画: GL + `Hidden/Internal-Colored`。軸線・矢じり・面ハンドル・回転円。
  任意カメラの `OnPostRender` コンテキストから `Draw(Camera)` で呼ばれる
- ヒット判定: RT ピクセル座標での距離計算 (現行 `HitThreshold = 8px` を踏襲)
- ドラッグ解決: `TryBeginDrag(Camera, Vector2 rtPoint)` / `UpdateDrag(Camera, Vector2 rtPoint)` /
  `EndDrag()` / `isDragging`。軸ドラッグ・面ドラッグ・回転の数式は現行
  `GizmoRenderer` (ToRtPoint / AxisParamAt / RotationAngleAt / PlanePointAt) を移植
- 対象: `Transform` 参照 + `onTransformChanged` コールバック
- ドラッグ開始時のカメラを保持し、ドラッグ継続中は同一カメラ基準で解決する
  (SceneView で掴んだドラッグが GameView の座標で解釈されない保証)

EW 固有の描画 (メインカメラ視錐台・ライトギズモ・選択バウンズ) は移さない。

## 2. GizmoHost (EW) / GizmoHostClient (MTEUtils)

### GizmoHost (公開 API、シグネチャ変更禁止の契約)

```csharp
public static object Register(
    string name,
    Func<Camera, Vector2, bool> tryBeginDrag,
    Action<Camera, Vector2> updateDrag,
    Action endDrag,
    Func<bool> isDragging,
    Action<Camera> draw);
public static void Unregister(object handle);
```

- SceneViewWindow / GameViewWindow の入力優先順を
  「①自前ギズモ → ②外部ギズモ (登録順) → ③クリック選択 → ④カメラ操作」へ拡張。
  外部ギズモのドラッグ中は選択・カメラ操作を抑止し、領域外へ出てもドラッグを維持する
- 各ビューカメラの `OnPostRender` (既存 `GizmoRenderer` の描画パス) から
  登録済み `draw(camera)` を呼び出す
- 渡す座標は各ビューの RT ピクセル座標 (`GuiToRtPoint` 済み)、カメラは
  そのビューの描画カメラ (SceneView は sceneCamera、GameView はメインカメラ)

### GizmoHostClient

DockingClient と同様に `FindHostType("GizmoHost")` で解決し、
`Delegate.CreateDelegate` でキャッシュする。ホスト不在・旧バージョンなら
`isAvailable == false` を返し、呼び出し側は standalone へフォールバックする。

## 3. MIE 側の移行 (ModelGizmoManager)

- `ModelGizmoRender` を削除する。`GizmoRenderHack` は自前ギズモ用途では
  不要になるが、`WindowManager.UpdateGizmoDragSuppress`（IMGUI ウィンドウ上の
  押下でゲーム側・他プラグインの GizmoRender を誤って掴まない抑止）が
  使い続けるため**存置**する
- `ModelGizmoManager` が配置モデルごとに `TransformGizmo` を保持。
  現行 UX を維持: 編集モード中は全モデルにギズモ表示、`GizmoDragType`
  (None/Move/Rotate/Scale) と Z/X/C キー切替、`GizmoScale` 相当の表示スケール
- `TryBeginDrag` は全モデルのギズモを順に試し、最初にヒットした 1 個だけが
  ドラッグを開始する (多重掴みは構造的に発生しない)

### 入力経路 (2 系統)

1. **GizmoHost 経由** (EW が対応バージョン): Register してホストの入力・描画
   ディスパッチに乗る。SceneView / GameView の両方で操作可能
2. **standalone** (EW 不在 or 旧 EW): `Camera.main` + マウス座標で自前駆動し、
   `Camera.main` に描画用 MonoBehaviour を付けて OnPostRender で `Draw` する。
   座標は用途で使い分ける: レイ計算には `Input.mousePosition` をそのまま使う
   (旧 EW 環境では InputRemapper が GameView 内で RT 座標へ変換済みのため、
   Camera.main とのペアで正しく成立する)。窓上判定・押下開始可否には
   `MTEUtils.mousePosition` (生座標) を使い、自ウィンドウ上を除外する。
   これにより GameView 上の操作は旧 EW でも成立する (SceneView 対応のみ新ホスト必須)

起動時は standalone で開始し、GizmoHost の解決に成功したら Register して
standalone の入力駆動を止める (InputRemapperClient と同じ遅延解決パターン)。

## 4. エラーハンドリング / 互換性

- GizmoHost のシグネチャ不一致はバージョン差として警告ログ + standalone 確定
  (DockingClient の一括検出パターンを踏襲)
- シェーダ `Hidden/Internal-Colored` 不在時は描画・操作を諦めてログのみ (現行踏襲)
- 旧 EW + 新 MIE: GameView のみ操作可 (従来同等)。新 EW + 旧 MIE: 影響なし
- ドラッグ中に対象モデルが破棄された場合は `UpdateDrag` 側で null を検出して
  `EndDrag` する

## 5. 検証計画

com3d25-devbridge の実機で以下を確認する:

1. EW 不在 (standalone): GameView でモデルギズモの移動・回転・拡縮
2. EW 併用 GameView: 同上 + EW 自前ギズモとの排他 (同時掴みが起きない)
3. EW 併用 SceneView: モデルギズモの操作 (本件の目的)、クリック選択・
   カメラオービットとの優先順位、領域外へのドラッグ継続
4. Z/X/C キーでの種別切替、編集モード外での非表示
5. EW の既存ギズモ (SelectionManager 対象) のリグレッションが無いこと

## 6. 作業順序

1. MTEUtils: `TransformGizmo` 抽出 + `GizmoHostClient` 追加
2. EW: `GizmoRenderer` を共通コア使用へリファクタ + `GizmoHost` 追加 +
   サブモジュール更新 (この時点で EW 単体のリグレッション確認)
3. MIE: `ModelGizmoManager` 実装、旧実装削除、サブモジュール更新、実機検証
