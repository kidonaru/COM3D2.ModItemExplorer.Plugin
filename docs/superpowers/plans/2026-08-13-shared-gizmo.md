# 共通ギズモ (MTEUtils 共通化 + SceneView 対応) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** EditorWindow の GizmoRenderer からカメラ非依存のギズモコアを MTEUtils へ抽出して両プラグインで共有し、ModItemExplorer の配置モデルギズモをゲーム本体 GizmoRender から全面移行して SceneView 上でも操作可能にする。

**Architecture:** MTEUtils に `TransformGizmo`（描画 + ヒット判定 + ドラッグ解決）と `GizmoHostClient`（リフレクションブリッジ）を追加。EditorWindow は `GizmoRenderer` を共通コアの薄いホストにリファクタし、`GizmoHost` 公開 API で外部ギズモを入力・描画ディスパッチに組み込む。ModItemExplorer は `ModelGizmoManager` で移行し、ホスト不在時は Camera.main + Input.mousePosition の standalone 経路で動く。

**Tech Stack:** C# (.NET 3.5 相当 / Unity 5.6 系), GL 即時描画, リフレクション + Delegate.CreateDelegate 連携

**Spec:** `docs/superpowers/specs/2026-08-13-shared-gizmo-design.md`

## Global Constraints

- MTEUtils はソース共有サブモジュール。プラグイン定義型はアセンブリ間で共有できない。クロスアセンブリ契約は「静的クラス + プリミティブ/UnityEngine 型/デリゲートのみ」（DockingHost 方式）
- `GizmoHost` の公開シグネチャは公開後変更禁止（変更時は別名メソッド追加）
- コメント・ログは日本語
- 自動テスト基盤なし。検証は各リポジトリの `build.bat` によるビルド成功 + com3d25-devbridge での実機確認
- リポジトリパス: MIE = `W:\COM3D2_5\work\COM3D2.ModItemExplorer.Plugin`, EW = `W:\COM3D2_5\work\COM3D2.EditorWindow.Plugin`。MTEUtils サブモジュールは各リポジトリの `source/<plugin>/MTEUtils`（remote は共通で `github.com/kidonaru/COM3D2.MTEUtils.git`）
- MTEUtils の変更は MIE 側チェックアウトで行い、サブモジュールにコミット後、EW 側サブモジュールへ同一コミットを fetch/checkout して同期する
- EW にはボーン編集機能（BoneEdit、bee92d6 で SceneViewWindow/GameViewWindow にボーンピック分岐、GizmoRenderer に `externalTargetProvider` を追加）が入っており、**未コミットの作業中ファイルが残っている可能性がある**。EW を変更する各タスクは着手前に `git status` を確認し、未コミット変更があればユーザーに確認してから進める。計画内の引用コードは 2026-08-13 時点のスナップショットなので、編集時は必ず現在のコードを読み直すこと

---

### Task 1: MTEUtils に TransformGizmo を抽出

**Files:**
- Create: `source/COM3D2.ModItemExplorer.Plugin/MTEUtils/TransformGizmo.cs`（MIE チェックアウト内のサブモジュール）
- 参照元: `W:\COM3D2_5\work\COM3D2.EditorWindow.Plugin\source\COM3D2.EditorWindow.Plugin\Manager\GizmoRenderer.cs`（読み取りのみ、変更は Task 2）

**Interfaces:**
- Produces: `COM3D2.MotionTimelineEditor.GizmoTool`（enum: None/Move/Rotate/Scale）、`COM3D2.MotionTimelineEditor.TransformGizmo`:
  - `Transform target` / `GizmoTool tool` / `bool useLocalSpace` / `float sizeScale`（既定 1f）/ `Action onTransformChanged`
  - `bool isDragging { get; }`
  - `void Draw(Camera camera)` — カメラの OnPostRender コンテキストから呼ぶ
  - `bool TryBeginDrag(Camera camera, Vector2 rtPoint)` — ヒットしたらドラッグ開始
  - `void UpdateDrag(Vector2 rtPoint)` — 開始時に保持したカメラ基準で解決
  - `void EndDrag()`
  - `static bool EnsureMaterial()` — GL 用マテリアルの遅延生成（false ならシェーダ不在）

- [ ] **Step 1: GizmoRenderer.cs 全体を読む**

`W:\COM3D2_5\work\COM3D2.EditorWindow.Plugin\source\COM3D2.EditorWindow.Plugin\Manager\GizmoRenderer.cs` を全読みし、以下の分類を確認する:
- **移す（ギズモ本体）**: `GizmoTool` enum、定数 `HitThreshold` / `GizmoScreenScale` / `CircleSegments` / `ConeSegments` / `TipRadiusRatio` / `TipBaseRatio` / `DegenerateEpsilon` / `ParallelEpsilon` / `AxisColors` / `SelectedAxisColor` / `SelectedPlaneColor` / `PlaneFillAlpha` / `PlaneSizeRatio`、描画 `DrawAxisLine` / `DrawPlaneHandle` / `DrawCircle` / `CalcCircleBasis` と本体ギズモ描画部（OnPostRender 内の `switch (currentTool)` ブロック）、ヒット判定/ドラッグ `ToRtPoint` / `DistanceToSegment` / `DistanceToCircle` / `IsInsidePlaneHandle` / `AxisParamAt` / `RotationAngleAt` / `PlanePointAt` / `TryBeginDrag` / `UpdateDrag` / `UpdatePlaneDrag` / `EndDrag` / `AxisDirection` / `GizmoSize` / `AxisColor` / `PlaneColor` とドラッグ状態フィールド一式（`_dragAxis` / `_dragPlane` / `_dragStartPosition` / `_dragStartRotation` / `_dragStartScale` / `_dragStartParam` / `_dragStartPlanePoint`）
- **移さない（EW 固有）**: `DrawMainCameraFrustum` / `CalcFrustumCorners` / `DrawBoundsWire` / `DrawStudioLights` / ライトギズモ一式 / `FrustumColor` / `BoundsColor` / ライト系定数 / `drawEnabled` / `isHostActive` / `showSelectionBounds` / `showLightGizmos` / `selectionManager` 参照 / MonoBehaviour ライフサイクル

- [ ] **Step 2: TransformGizmo.cs を新規作成**

`source/COM3D2.ModItemExplorer.Plugin/MTEUtils/TransformGizmo.cs` に以下の骨格で作成し、Step 1 で「移す」とした定数・メソッド本体を**そのまま移植**する（数式・分割数・色は変更しない）:

```csharp
using System;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor
{
    /// <summary>ギズモの操作種別</summary>
    public enum GizmoTool
    {
        /// <summary>ギズモ非表示</summary>
        None,
        Move,
        Rotate,
        Scale,
    }

    /// <summary>
    /// カメラ非依存の Transform 操作ギズモ。
    /// 任意カメラの OnPostRender から Draw し、そのカメラの RT ピクセル座標で
    /// TryBeginDrag / UpdateDrag を呼ぶ。ドラッグ解決は開始時のカメラ基準で行うため、
    /// SceneView で掴んだドラッグが他ビューの座標で解釈されることはない。
    /// EditorWindow の GizmoRenderer から抽出した実装 (数式は同一)
    /// </summary>
    public class TransformGizmo
    {
        public Transform target;
        public GizmoTool tool = GizmoTool.Move;
        public bool useLocalSpace = true;
        /// <summary>表示倍率。配置モデル用に小さくする場合などに使う</summary>
        public float sizeScale = 1f;
        /// <summary>ドラッグで target を書き換えた直後に呼ばれる</summary>
        public Action onTransformChanged;

        public bool isDragging { get; private set; }

        // GL 描画用マテリアルは全インスタンス共有
        private static Material _lineMaterial;
        private static bool _materialFailed;

        // ドラッグ状態 (GizmoRenderer から移植)
        private Camera _dragCamera;
        private int _dragAxis = -1;
        private int _dragPlane = -1;
        // ... _dragStartPosition / _dragStartRotation / _dragStartScale /
        //     _dragStartParam / _dragStartPlanePoint

        /// <summary>GL 用マテリアルを遅延生成する。シェーダ不在なら false</summary>
        public static bool EnsureMaterial()
        {
            if (_lineMaterial != null) return true;
            if (_materialFailed) return false;
            var shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null)
            {
                _materialFailed = true;
                MTEUtils.LogError("ギズモ描画用シェーダ (Hidden/Internal-Colored) が見つかりません。ギズモは表示されません");
                return false;
            }
            _lineMaterial = new Material(shader);
            _lineMaterial.hideFlags = HideFlags.HideAndDontSave;
            // ギズモは常に手前に見せたいので深度テストを無効化する
            _lineMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            return true;
        }

        public void Draw(Camera camera)
        {
            if (target == null || tool == GizmoTool.None || !EnsureMaterial())
            {
                return;
            }

            _lineMaterial.SetPass(0);
            GL.PushMatrix();
            GL.LoadProjectionMatrix(camera.projectionMatrix);
            GL.modelview = camera.worldToCameraMatrix;

            // ここに GizmoRenderer.OnPostRender の switch (currentTool) 描画ブロックを移植。
            // currentTool → tool、_camera → camera に置換

            GL.PopMatrix();
        }

        public bool TryBeginDrag(Camera camera, Vector2 rtPoint) { /* 移植 */ }
        public void UpdateDrag(Vector2 rtPoint) { /* 移植 */ }
        public void EndDrag()
        {
            isDragging = false;
            _dragCamera = null;
            _dragAxis = -1;
            _dragPlane = -1;
        }
    }
}
```

移植時の機械的置換ルール:
1. `_camera` → 描画系は引数 `camera`、ドラッグ解決系（`ToRtPoint` / `AxisParamAt` / `RotationAngleAt` / `PlanePointAt` / `GizmoSize` / `DistanceToCircle` 内の参照）は `TryBeginDrag` で保持した `_dragCamera`。`ToRtPoint` / `GizmoSize` / `DistanceToCircle` / `DrawCircle` は `Camera camera` を先頭引数に追加し呼び出し側から渡す（描画とヒット判定で別カメラを使えるようにするため）
2. `currentTool`（static）→ `tool`（インスタンス）、`useLocalSpace`（static）→ インスタンス
3. `target.transform` → `target`（`Transform` を直接保持）
4. `GizmoSize` の戻り値に `* sizeScale` を掛ける
5. `UpdateDrag` / `UpdatePlaneDrag` で `target` の position/rotation/localScale を書き換えた直後に `onTransformChanged?.Invoke()`（C# バージョンの都合で `if (onTransformChanged != null) onTransformChanged();`）
6. `TryBeginDrag` 先頭の `drawEnabled` チェックは削除（呼び出し側の責務）。`UpdateDrag` 冒頭の `target == null` チェックは残す（ドラッグ中破棄対策）
7. `TryBeginDrag` 成功時に `_dragCamera = camera` を保存

- [ ] **Step 3: MIE をビルドして通ることを確認**

Run: `cd W:\COM3D2_5\work\COM3D2.ModItemExplorer.Plugin\source\COM3D2.ModItemExplorer.Plugin && build.bat`（build.bat の場所はリポジトリ直下と source 下のどちらかを確認して使う）
Expected: ビルド成功（この時点で TransformGizmo は未使用だがコンパイルは通る）

- [ ] **Step 4: サブモジュールにコミット**

```bash
cd W:/COM3D2_5/work/COM3D2.ModItemExplorer.Plugin/source/COM3D2.ModItemExplorer.Plugin/MTEUtils
git add TransformGizmo.cs
git commit -m "feat(gizmo): カメラ非依存の TransformGizmo を追加 (EditorWindow GizmoRenderer から抽出)"
```

---

### Task 2: MTEUtils に GizmoHostClient を追加

**Files:**
- Create: `source/COM3D2.ModItemExplorer.Plugin/MTEUtils/GizmoHostClient.cs`
- 参照: `MTEUtils/DockingClient.cs`（パターン踏襲元）

**Interfaces:**
- Consumes: `DockingClient.FindHostType(string)`（internal、同一アセンブリ内なので呼べる）
- Produces: `COM3D2.MotionTimelineEditor.GizmoHostClient`:
  - `static bool isAvailable`
  - `static object Register(string name, Func<Camera, Vector2, bool> tryBeginDrag, Action<Camera, Vector2> updateDrag, Action endDrag, Func<bool> isDragging, Action<Camera> draw)`
  - `static void Unregister(object handle)`
- ホスト側契約（Task 4 の `GizmoHost` と一致必須）: 上記と同名・同シグネチャの public static メソッド

- [ ] **Step 1: GizmoHostClient.cs を作成**

DockingClient と同じ構造（遅延 Initialize、型未発見なら `_initialized` を立てず再試行、シグネチャ不一致は警告して standalone 確定）で作成する:

```csharp
using System;
using System.Reflection;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor
{
    /// <summary>
    /// EditorWindow プラグインの GizmoHost へのリフレクションブリッジ。
    /// 登録すると SceneView / GameView の入力・描画ディスパッチに乗り、
    /// 各ビューの RT 座標とカメラでギズモを操作できる。
    /// EditorWindow が不在・旧バージョンの場合は isAvailable が false になり、
    /// 呼び出し側は standalone (Camera.main + Input.mousePosition) で駆動する
    /// </summary>
    public static class GizmoHostClient
    {
        private delegate object RegisterDelegate(
            string name,
            Func<Camera, Vector2, bool> tryBeginDrag,
            Action<Camera, Vector2> updateDrag,
            Action endDrag,
            Func<bool> isDragging,
            Action<Camera> draw);

        private static RegisterDelegate _register;
        private static Action<object> _unregister;
        private static bool _initialized;

        public static bool isAvailable
        {
            get
            {
                Initialize();
                return _register != null;
            }
        }

        private static void Initialize()
        {
            if (_initialized)
            {
                return;
            }

            // ロード順によってはホストのアセンブリが未登場のことがあるため、
            // 型が見つかるまでは _initialized を立てずに再試行を続ける
            var type = DockingClient.FindHostType("GizmoHost");
            if (type == null)
            {
                return;
            }

            _initialized = true;

            try
            {
                var register = type.GetMethod("Register", BindingFlags.Public | BindingFlags.Static);
                var unregister = type.GetMethod("Unregister", BindingFlags.Public | BindingFlags.Static);
                if (register == null || unregister == null)
                {
                    MTEUtils.LogWarning("GizmoHostClient: GizmoHost にシグネチャの一致するメソッドが見つかりませんでした");
                    return;
                }

                _register = (RegisterDelegate)Delegate.CreateDelegate(typeof(RegisterDelegate), register);
                _unregister = (Action<object>)Delegate.CreateDelegate(typeof(Action<object>), unregister);
            }
            catch (Exception e)
            {
                // ホスト側のバージョン差でシグネチャが合わない場合は standalone へフォールバックする
                MTEUtils.LogWarning("GizmoHostClient: GizmoHost との接続に失敗しました: " + e.Message);
                _register = null;
                _unregister = null;
            }
        }

        public static object Register(
            string name,
            Func<Camera, Vector2, bool> tryBeginDrag,
            Action<Camera, Vector2> updateDrag,
            Action endDrag,
            Func<bool> isDragging,
            Action<Camera> draw)
        {
            return isAvailable
                ? _register(name, tryBeginDrag, updateDrag, endDrag, isDragging, draw)
                : null;
        }

        public static void Unregister(object handle)
        {
            if (handle != null && isAvailable)
            {
                _unregister(handle);
            }
        }
    }
}
```

- [ ] **Step 2: MIE をビルドして確認**

Run: MIE の `build.bat`
Expected: ビルド成功

- [ ] **Step 3: サブモジュールにコミット**

```bash
cd W:/COM3D2_5/work/COM3D2.ModItemExplorer.Plugin/source/COM3D2.ModItemExplorer.Plugin/MTEUtils
git add GizmoHostClient.cs
git commit -m "feat(gizmo): GizmoHost へのリフレクションブリッジ GizmoHostClient を追加"
```

---

### Task 3: EditorWindow の GizmoRenderer を TransformGizmo 使用へリファクタ

**Files:**
- Modify: `W:\COM3D2_5\work\COM3D2.EditorWindow.Plugin\source\COM3D2.EditorWindow.Plugin\Manager\GizmoRenderer.cs`
- Modify: EW 側サブモジュール `source/COM3D2.EditorWindow.Plugin/MTEUtils`（Task 1-2 のコミットへ更新）
- 呼び出し側（シグネチャ互換を保つため変更しない）: `SceneViewWindow.cs` / `GameViewWindow.cs` / `InspectorWindow.cs` / `COM3D2.EditorWindow.Plugin.cs`

**Interfaces:**
- Consumes: `TransformGizmo`（Task 1）
- Produces: `GizmoRenderer` の既存 public API を維持: `static GizmoTool currentTool` / `static bool useLocalSpace` / `bool drawEnabled` / `bool isDragging` / `Func<bool> isHostActive` / `bool showSelectionBounds` / `bool showLightGizmos` / `bool TryBeginDrag(Vector2)` / `void UpdateDrag(Vector2)` / `void EndDrag()`。`GizmoTool` enum は EW ローカル定義を削除し MTEUtils の定義を使う

- [ ] **Step 1: EW のサブモジュールを Task 1-2 のコミットへ同期**

```bash
cd W:/COM3D2_5/work/COM3D2.EditorWindow.Plugin/source/COM3D2.EditorWindow.Plugin/MTEUtils
git fetch W:/COM3D2_5/work/COM3D2.ModItemExplorer.Plugin/source/COM3D2.ModItemExplorer.Plugin/MTEUtils master
git checkout FETCH_HEAD
```

EW の csproj に `MTEUtils\TransformGizmo.cs` / `MTEUtils\GizmoHostClient.cs` の Compile Include が無ければ追加する（既存の MTEUtils ファイルの記載方式に合わせる。ワイルドカード方式ならば不要）。

- [ ] **Step 2: GizmoRenderer.cs から移植済みコードを削除し TransformGizmo へ委譲**

- ファイル先頭の `GizmoTool` enum 定義を削除（`using COM3D2.MotionTimelineEditor;` は既にある。MTEUtils 側の同名 enum に切り替わる）
- Task 1 で「移す」に分類した定数・メソッド・ドラッグ状態フィールドを削除
- フィールド `private readonly TransformGizmo _gizmo = new TransformGizmo();` を追加
- `Awake` の自前マテリアル生成は EW 固有描画（視錐台・ライト・バウンズ）用に**残す**（`TransformGizmo.EnsureMaterial()` とは独立でよい）
- 委譲の実装:

```csharp
public bool isDragging => _gizmo.isDragging;

public bool TryBeginDrag(Vector2 rtPoint)
{
    if (!drawEnabled)
    {
        return false;
    }
    SyncGizmo();
    return _gizmo.TryBeginDrag(_camera, rtPoint);
}

public void UpdateDrag(Vector2 rtPoint)
{
    _gizmo.UpdateDrag(rtPoint);
}

public void EndDrag()
{
    _gizmo.EndDrag();
}

/// <summary>static な UI 設定と選択対象をインスタンスへ反映する</summary>
private void SyncGizmo()
{
    _gizmo.target = target != null ? target.transform : null;
    _gizmo.tool = currentTool;
    _gizmo.useLocalSpace = useLocalSpace;
}
```

注意: `target` プロパティはボーン編集の `externalTargetProvider`（static `Func<GameObject>`、ボーン編集モード中に選択ボーンをギズモ対象として注入する）を参照する実装になっている（`GizmoRenderer.cs:153-159` 付近）。このプロパティは**削除せずそのまま残し**、`SyncGizmo()` から参照する。これによりボーン編集のギズモ操作もリファクタ後に共通コアで動く。

- `OnPostRender` は EW 固有描画（視錐台・ライトギズモ・選択バウンズ）を従来どおり自前 GL で描いたあと、`SyncGizmo(); _gizmo.Draw(_camera);` を呼ぶ形にする（本体ギズモの switch ブロックは削除済み）

- [ ] **Step 3: EW をビルド**

Run: EW の `build.bat`
Expected: ビルド成功。`GizmoTool` 参照箇所（InspectorWindow / 本体 cs）がそのままコンパイルできること

- [ ] **Step 4: 実機で EW 単体のリグレッション確認**

devbridge (`mcp__com3d25-devbridge__eval_csharp` / `screenshot`) で:
- SceneView で選択オブジェクトの Move/Rotate/Scale ギズモが従来どおり操作できる
- GameView 側ギズモも操作できる
- Inspector の Local/Global 切替が効く
- ボーン編集モードでボーンをクリック選択し、注入されたボーン対象のギズモ操作（`externalTargetProvider` 経由）が従来どおり動く
Expected: 従来と同じ挙動

- [ ] **Step 5: コミット**

```bash
cd W:/COM3D2_5/work/COM3D2.EditorWindow.Plugin
git add -A
git commit -m "refactor(gizmo): GizmoRenderer のギズモ本体を MTEUtils の TransformGizmo へ抽出"
```

---

### Task 4: EditorWindow に GizmoHost を追加し入力・描画へ組み込む

**Files:**
- Create: `W:\COM3D2_5\work\COM3D2.EditorWindow.Plugin\source\COM3D2.EditorWindow.Plugin\GizmoHost.cs`
- Modify: `SceneViewWindow.cs`（`UpdatePointerInput`）
- Modify: `GameViewWindow.cs`（ギズモ入力処理部、`GameViewWindow.cs:290-330` 付近）
- Modify: `Manager\GizmoRenderer.cs`（`OnPostRender` 末尾に外部描画ディスパッチ）

**Interfaces:**
- Consumes: なし（自己完結）
- Produces（公開契約、変更禁止）:

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

- ウィンドウ統合用の内部 API: `static bool TryBeginExternalDrag(Camera, Vector2)` / `static void UpdateExternalDrag(Camera, Vector2)` / `static void EndExternalDrag()` / `static bool IsExternalDragging(Camera)` / `static void DrawExternals(Camera)`

- [ ] **Step 1: GizmoHost.cs を作成**

```csharp
using System;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.EditorWindow.Plugin
{
    /// <summary>
    /// 外部プラグインのギズモを SceneView / GameView の入力・描画へ参加させる公開 API。
    /// MTEUtils の GizmoHostClient からリフレクションで発見・呼び出しされるため、
    /// クラス名・メソッドシグネチャは公開後変更禁止 (変更時は Register2 等の別名で追加する)。
    /// 契約はプリミティブ + UnityEngine 型 + デリゲートのみ (プラグイン定義型は DLL 間で共有できない)
    /// </summary>
    public static class GizmoHost
    {
        private class Entry
        {
            public string name;
            public Func<Camera, Vector2, bool> tryBeginDrag;
            public Action<Camera, Vector2> updateDrag;
            public Action endDrag;
            public Func<bool> isDragging;
            public Action<Camera> draw;
        }

        private static readonly List<Entry> _entries = new List<Entry>();

        // ドラッグ中の外部ギズモ。同時に掴めるのは 1 個だけ
        private static Entry _dragEntry;

        // ドラッグを開始したカメラ。別ビューからの UpdateExternalDrag を無視し、
        // カメラ操作の抑止判定をビュー単位に絞るために使う
        private static Camera _dragCamera;

        public static object Register(
            string name,
            Func<Camera, Vector2, bool> tryBeginDrag,
            Action<Camera, Vector2> updateDrag,
            Action endDrag,
            Func<bool> isDragging,
            Action<Camera> draw)
        {
            if (tryBeginDrag == null || updateDrag == null || endDrag == null ||
                isDragging == null || draw == null)
            {
                MTEUtils.LogError("GizmoHost.Register: デリゲートに null は指定できません");
                return null;
            }

            // 同名の再登録はプラグインのリロードとみなして置き換える
            for (var i = _entries.Count - 1; i >= 0; i--)
            {
                if (_entries[i].name == name)
                {
                    Unregister(_entries[i]);
                }
            }

            var entry = new Entry
            {
                name = name ?? "",
                tryBeginDrag = tryBeginDrag,
                updateDrag = updateDrag,
                endDrag = endDrag,
                isDragging = isDragging,
                draw = draw,
            };
            _entries.Add(entry);
            return entry;
        }

        public static void Unregister(object handle)
        {
            var entry = handle as Entry;
            if (entry == null)
            {
                return;
            }
            if (_dragEntry == entry)
            {
                _dragEntry = null;
                _dragCamera = null;
            }
            _entries.Remove(entry);
        }

        /// <summary>
        /// 指定カメラのビューで始まった外部ギズモのドラッグが継続中か。
        /// ウィンドウ側は自ビューのドラッグに対してのみ選択・カメラ操作を抑止する
        /// (グローバルに抑止すると、別ビューのドラッグ中にこちらのカメラ操作まで止まってしまう)
        /// </summary>
        public static bool IsExternalDragging(Camera camera)
        {
            // ドラッグ主が登録解除された場合に isDragging が残らないよう都度問い合わせる
            return _dragEntry != null && _dragCamera == camera && SafeIsDragging(_dragEntry);
        }

        /// <summary>登録順に掴みを試し、最初にヒットした 1 個だけがドラッグを開始する</summary>
        public static bool TryBeginExternalDrag(Camera camera, Vector2 rtPoint)
        {
            foreach (var entry in _entries)
            {
                try
                {
                    if (entry.tryBeginDrag(camera, rtPoint))
                    {
                        _dragEntry = entry;
                        _dragCamera = camera;
                        return true;
                    }
                }
                catch (Exception e)
                {
                    // 外部プラグインの例外でホストの入力処理を止めない
                    MTEUtils.LogException(e);
                }
            }
            return false;
        }

        public static void UpdateExternalDrag(Camera camera, Vector2 rtPoint)
        {
            // 開始ビュー以外からの更新は無視する (両ビューが同フレームに呼んでも二重更新にならない)
            if (_dragEntry == null || camera != _dragCamera)
            {
                return;
            }
            try
            {
                _dragEntry.updateDrag(camera, rtPoint);
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
                EndExternalDrag();
            }
        }

        public static void EndExternalDrag()
        {
            if (_dragEntry == null)
            {
                return;
            }
            try
            {
                _dragEntry.endDrag();
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }
            _dragEntry = null;
            _dragCamera = null;
        }

        /// <summary>各ビューカメラの OnPostRender から呼ばれ、登録済みギズモを描画する</summary>
        public static void DrawExternals(Camera camera)
        {
            foreach (var entry in _entries)
            {
                try
                {
                    entry.draw(camera);
                }
                catch (Exception e)
                {
                    MTEUtils.LogException(e);
                }
            }
        }

        private static bool SafeIsDragging(Entry entry)
        {
            try
            {
                return entry.isDragging();
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
                return false;
            }
        }
    }
}
```

- [ ] **Step 2: SceneViewWindow の入力優先順へ組み込む**

`SceneViewWindow.UpdatePointerInput` を「①自前ギズモドラッグ継続 → ②外部ギズモドラッグ継続 → ③押下開始: 自前ギズモ → 外部ギズモ → 選択 → ④カメラ操作」へ拡張:

```csharp
private void UpdatePointerInput(Vector2 guiPos)
{
    var gizmo = sceneViewManager.isActive ? sceneViewManager.gizmoRenderer : null;
    var camera = sceneViewManager.isActive ? sceneViewManager.sceneCamera : null;

    // 1. ギズモドラッグが最優先 (継続中は領域外へ出ても維持する)
    if (gizmo != null && gizmo.isDragging)
    {
        if (Input.GetMouseButton(0))
        {
            gizmo.UpdateDrag(GuiToRtPoint(guiPos));
        }
        else
        {
            gizmo.EndDrag();
        }
    }
    // 2. 外部ギズモのドラッグ継続 (この SceneView で始まったものだけ)
    else if (GizmoHost.IsExternalDragging(camera))
    {
        if (Input.GetMouseButton(0))
        {
            GizmoHost.UpdateExternalDrag(camera, GuiToRtPoint(guiPos));
        }
        else
        {
            GizmoHost.EndExternalDrag();
        }
    }
    // 3. 左クリック開始: 自前ギズモ → 外部ギズモ → ボーンピック → 選択の順に試す。
    //    ボーン編集のボーンピック分岐 (bee92d6 で追加) は必ず残すこと。
    //    ギズモ類はハンドルの明示 UI なのでシーン内容 (関節・オブジェクト) より優先する
    else if (Input.GetMouseButtonDown(0) &&
        !Input.GetKey(KeyCode.LeftAlt) && !Input.GetKey(KeyCode.RightAlt) &&
        sceneViewManager.isActive && IsSceneViewActiveAt(guiPos))
    {
        var rtPoint = GuiToRtPoint(guiPos);
        if ((gizmo == null || !gizmo.TryBeginDrag(rtPoint)) &&
            !GizmoHost.TryBeginExternalDrag(camera, rtPoint))
        {
            // ボーン編集モード中は関節クリックを通常のオブジェクト選択より優先する
            var boneLine = sceneViewManager.boneLineRenderer;
            if (boneLine == null || !boneLine.TryPickBone(rtPoint))
            {
                selectionManager.SelectAtRay(camera, rtPoint);
            }
        }
    }

    // 4. カメラ操作 (このビューの自前・外部ギズモドラッグ中は抑止。
    //    別ビューで始まった外部ドラッグはこちらのカメラ操作を止めない)
    if ((gizmo == null || !gizmo.isDragging) && !GizmoHost.IsExternalDragging(camera))
    {
        UpdateCameraInput(guiPos);
    }
}
```

補足: ドラッグ開始カメラの記録と別ビューからの `UpdateExternalDrag` 無視は Step 1 の `GizmoHost` 実装（`_dragCamera`）に含まれている。`EndExternalDrag` はどのウィンドウから呼ばれても解放してよい（マウスボタンが離れた事実はビュー共通のため）。分岐 2 は `IsExternalDragging(camera)` で自ビューのドラッグだけを扱うため、開始ビューのウィンドウだけが継続更新と解放を担う。

- [ ] **Step 3: GameViewWindow にも同じ分岐を入れる**

`GameViewWindow.cs` の既存ギズモ入力処理（`gameViewManager.gizmoRenderer` を使う `UpdateDrag` / `TryBeginDrag` 呼び出し部、`GameViewWindow.cs:290-330` 付近）に、SceneViewWindow と同じ形で外部ギズモの「ドラッグ継続」「押下開始（自前 → 外部 → 従来処理）」「カメラ操作抑止」を追加する。カメラは GameView の描画カメラ（メインカメラ）、座標は `GuiToRtPoint(guiPos)`。既存コードの構造に合わせて挿入し、優先順「自前 → 外部 → ボーンピック → 従来のクリック処理」を守る（GameView 側にも bee92d6 でボーンピック分岐が入っているため、必ず現在のコードを読んでから挿入位置を決めること）。

実装前の確認事項: 既存の GameView ギズモ入力は `!gameViewManager.isWindowMode` で早期 return するガードを持つ。`isWindowMode` / `isMaximized` の正確な意味を `GameViewManager.cs` で確認し、非ウィンドウモード（直接描画）時に外部ギズモが standalone 経路（Task 5）で成立するのか、それともこのガードの内側に入れるべきかを判断してから挿入位置を決めること（非ウィンドウモードでは InputRemapper の変換も無効なので standalone 経路が正しく機能する想定だが、実機で裏取りする）。

- [ ] **Step 4: GizmoRenderer.OnPostRender から外部描画を呼ぶ**

現状の `OnPostRender` 先頭は `if (_lineMaterial == null || !EditorWindowPlugin.instance.isEnable || !isHostActive() || !drawEnabled) return;` の 1 つの合成条件になっている。`drawEnabled == false`（ツールバーで EW 自前ギズモ非表示）でも外部ギズモは描きたい、かつ EW 自前マテリアルの生成失敗と外部ギズモは無関係のため、条件を分割して外部描画を間に挟む:

```csharp
private void OnPostRender()
{
    // プラグイン無効・ビュー非表示時は外部ギズモも含めて描かない
    if (!EditorWindowPlugin.instance.isEnable || !isHostActive())
    {
        return;
    }

    // 外部プラグインのギズモはツールバーの自前ギズモ表示 (drawEnabled) と
    // 自前マテリアルの成否に依存せず描く
    GizmoHost.DrawExternals(_camera);

    if (_lineMaterial == null || !drawEnabled)
    {
        return;
    }

    // 以下、既存の EW 固有描画 + _gizmo.Draw(_camera)
```

- [ ] **Step 5: EW をビルド**

Run: EW の `build.bat`
Expected: ビルド成功

- [ ] **Step 6: 実機リグレッション確認**

devbridge で EW 単体（外部ギズモ未登録の状態）の SceneView / GameView 操作が従来どおりであること。
Expected: 挙動変化なし

- [ ] **Step 7: コミット**

```bash
cd W:/COM3D2_5/work/COM3D2.EditorWindow.Plugin
git add -A
git commit -m "feat(gizmo): 外部プラグインのギズモを入力・描画へ参加させる GizmoHost を追加"
```

---

### Task 5: MIE に ModelGizmoManager を実装し旧実装を削除

**Files:**
- Create: `source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/ModelGizmoManager.cs`
- Modify: `source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/SelfModelPlacer.cs`（`AddGizmo` / `ApplyDragType` / モデル削除処理 / `Update()`）
- Modify: `source/COM3D2.ModItemExplorer.Plugin/Manager/WindowManager.cs`（`isMouseOverWindow` プロパティ公開のみ）
- Delete: `source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/ModelGizmoRender.cs`
- **存置**: `ModelPlacement/GizmoRenderHack.cs` — 自前ギズモ用途では不要になるが、`Manager/WindowManager.cs` の `UpdateGizmoDragSuppress()`（IMGUI ウィンドウ上の押下でゲーム側・他プラグインの GizmoRender を誤って掴まない汎用抑止）が `isAvailable` / `isDrag` を使い続けるため削除しない。クラスコメントの「ハンドルの掴み判定を外から制御する」用途説明は WindowManager 専用になった旨へ更新する
- Modify: `COM3D2.ModItemExplorer.Plugin.csproj`（削除 1 ファイルの Compile 除去、新規 1 ファイル追加。ワイルドカード方式なら不要）

**Interfaces:**
- Consumes: `TransformGizmo` / `GizmoHostClient`（Task 1-2）、`SelfModelPlacer.GizmoDragType`（既存）
- Produces: `ModelGizmoManager`:
  - `static ModelGizmoManager instance`
  - `void AddGizmo(GameObject target)` — 配置モデルのラッパー GO にギズモを割り当てる
  - `void RemoveGizmo(GameObject target)`
  - `void SetToolAndVisible(GizmoTool tool, bool visible)` — SelfModelPlacer.ApplyDragType から呼ぶ
  - `void Update()` — ホスト解決の再試行 + standalone 入力駆動（毎フレーム呼ぶ）
  - `void Dispose()` — 全解除（プラグイン無効化時）

- [ ] **Step 1: ModelGizmoManager.cs を作成**

```csharp
using System;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.ModItemExplorer.Plugin
{
    /// <summary>
    /// 配置モデルの TransformGizmo を一括管理する。
    /// EditorWindow の GizmoHost が使える環境では登録して SceneView / GameView の
    /// 入力・描画ディスパッチに乗り、不在なら standalone (Camera.main +
    /// Input.mousePosition) で自前駆動する。GizmoHost は後からロードされる可能性が
    /// あるため、解決できるまで毎フレーム再試行する (InputRemapperClient と同じパターン)
    /// </summary>
    public class ModelGizmoManager
    {
        private static ModelGizmoManager _instance;
        public static ModelGizmoManager instance
            => _instance ?? (_instance = new ModelGizmoManager());

        private readonly Dictionary<GameObject, TransformGizmo> _gizmos
            = new Dictionary<GameObject, TransformGizmo>();

        private object _hostHandle;
        private GizmoTool _tool = GizmoTool.Move;
        private bool _visible;

        // standalone 描画用にメインカメラへ付けるフック
        private StandaloneDrawHook _drawHook;

        private bool isHosted => _hostHandle != null;

        public bool isDragging { get; private set; }
        private TransformGizmo _dragGizmo;
        private Camera _dragCamera;

        public void AddGizmo(GameObject target)
        {
            if (target == null || _gizmos.ContainsKey(target))
            {
                return;
            }
            var gizmo = new TransformGizmo
            {
                target = target.transform,
                tool = _visible ? _tool : GizmoTool.None,
                sizeScale = SelfModelPlacer.GizmoScale,
            };
            _gizmos[target] = gizmo;
        }

        public void RemoveGizmo(GameObject target)
        {
            if (target == null)
            {
                return;
            }
            TransformGizmo gizmo;
            if (_gizmos.TryGetValue(target, out gizmo))
            {
                if (_dragGizmo == gizmo)
                {
                    EndDrag();
                }
                _gizmos.Remove(target);
            }
        }

        /// <summary>操作種別と表示状態をまとめて反映する。非表示は tool = None で表現する</summary>
        public void SetToolAndVisible(GizmoTool tool, bool visible)
        {
            _tool = tool;
            _visible = visible;
            var applied = visible ? tool : GizmoTool.None;
            foreach (var gizmo in _gizmos.Values)
            {
                gizmo.tool = applied;
            }
        }

        public void Update()
        {
            TryRegisterHost();

            if (!isHosted)
            {
                UpdateStandaloneInput();
            }

            // 破棄済みターゲットの掃除 (Unity の == は破棄済みも null 扱い)
            _removeBuffer.Clear();
            foreach (var pair in _gizmos)
            {
                if (pair.Key == null)
                {
                    _removeBuffer.Add(pair.Key);
                }
            }
            foreach (var key in _removeBuffer)
            {
                RemoveGizmo(key);
            }
        }
        private readonly List<GameObject> _removeBuffer = new List<GameObject>();

        public void Dispose()
        {
            EndDrag();
            _gizmos.Clear();
            if (_hostHandle != null)
            {
                GizmoHostClient.Unregister(_hostHandle);
                _hostHandle = null;
            }
            DetachDrawHook();
        }

        // ---- GizmoHost 連携 ----

        private const int RETRY_INTERVAL_FRAMES = 60;
        private int _lastAttemptFrame = -RETRY_INTERVAL_FRAMES;
        private bool _hostResolved;

        private void TryRegisterHost()
        {
            if (_hostResolved)
            {
                return;
            }

            var frame = Time.frameCount;
            if (frame - _lastAttemptFrame < RETRY_INTERVAL_FRAMES)
            {
                return;
            }
            _lastAttemptFrame = frame;

            if (!GizmoHostClient.isAvailable)
            {
                return;
            }

            _hostResolved = true;
            _hostHandle = GizmoHostClient.Register(
                "ModItemExplorer",
                TryBeginDrag,
                (camera, rtPoint) => UpdateDrag(rtPoint),
                EndDrag,
                () => isDragging,
                DrawAll);

            if (_hostHandle != null)
            {
                // ホスト側が両ビューで描画するため standalone のフックは外す
                DetachDrawHook();
                MTEUtils.LogDebug("ModelGizmoManager: GizmoHost へ登録しました");
            }
        }

        // ---- 入力 (ホスト経由・standalone 共通のコア) ----

        private bool TryBeginDrag(Camera camera, Vector2 rtPoint)
        {
            foreach (var gizmo in _gizmos.Values)
            {
                if (gizmo.tool == GizmoTool.None)
                {
                    continue;
                }
                if (gizmo.TryBeginDrag(camera, rtPoint))
                {
                    _dragGizmo = gizmo;
                    _dragCamera = camera;
                    isDragging = true;
                    return true;
                }
            }
            return false;
        }

        private void UpdateDrag(Vector2 rtPoint)
        {
            if (_dragGizmo == null)
            {
                return;
            }
            _dragGizmo.UpdateDrag(rtPoint);
        }

        private void EndDrag()
        {
            if (_dragGizmo != null)
            {
                _dragGizmo.EndDrag();
            }
            _dragGizmo = null;
            _dragCamera = null;
            isDragging = false;
        }

        private void DrawAll(Camera camera)
        {
            foreach (var gizmo in _gizmos.Values)
            {
                gizmo.Draw(camera);
            }
        }

        // ---- standalone 駆動 ----

        private void UpdateStandaloneInput()
        {
            if (_gizmos.Count == 0)
            {
                DetachDrawHook();
                return;
            }
            AttachDrawHook();

            var camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            if (isDragging)
            {
                if (Input.GetMouseButton(0))
                {
                    // 旧 EditorWindow 環境では InputRemapper が GameView 内で
                    // RT 座標へ変換済みのため、Camera.main とのペアで正しく成立する
                    UpdateDrag((Vector2)Input.mousePosition);
                }
                else
                {
                    EndDrag();
                }
                return;
            }

            if (!Input.GetMouseButtonDown(0))
            {
                return;
            }

            // 自プラグインのウィンドウ上からの押下では掴まない。
            // 判定は WindowManager が毎フレーム更新している窓上フラグを再利用する
            // (生座標ベースの判定。独自にウィンドウ列挙をやり直さない)
            if (WindowManager.instance.isMouseOverWindow)
            {
                return;
            }

            TryBeginDrag(camera, (Vector2)Input.mousePosition);
        }

        private void AttachDrawHook()
        {
            var camera = Camera.main;
            if (camera == null)
            {
                return;
            }
            if (_drawHook != null && _drawHook.gameObject == camera.gameObject)
            {
                return;
            }
            DetachDrawHook();
            _drawHook = camera.gameObject.AddComponent<StandaloneDrawHook>();
            _drawHook.onPostRender = DrawAll;
        }

        private void DetachDrawHook()
        {
            if (_drawHook != null)
            {
                UnityEngine.Object.Destroy(_drawHook);
                _drawHook = null;
            }
        }

        /// <summary>standalone 時にメインカメラへ付ける描画フック</summary>
        private class StandaloneDrawHook : MonoBehaviour
        {
            public Action<Camera> onPostRender;
            private Camera _camera;

            private void Awake()
            {
                _camera = GetComponent<Camera>();
            }

            private void OnPostRender()
            {
                if (onPostRender != null && _camera != null)
                {
                    onPostRender(_camera);
                }
            }
        }
    }
}
```

注意点:
- `SelfModelPlacer.GizmoScale`（現在 private const 0.5f）を `public const` に変更して参照する
- `WindowManager`（MIE の `Manager/WindowManager.cs`）の private フィールド `_isMouseOverWindow`（`UpdateInputBlock()` が毎フレーム更新）を `public bool isMouseOverWindow => _isMouseOverWindow;` として公開し、それを使う。`MTEUtils.IsMouseOverAnyWindow()` という API は存在しないので新設もしない（窓上判定の二重実装を避ける）。`WindowManager.instance` 相当のアクセサ名は実コードを確認して合わせること
- `useLocalSpace` は既定 `true` のまま固定でよい（旧 GizmoRender もローカル軸操作であり、MIE に Local/Global 切替 UI は存在しない。切替 UI の追加は本件のスコープ外）

- [ ] **Step 2: SelfModelPlacer を ModelGizmoManager 使用へ変更**

- `AddGizmo(GameObject target)`: `target.AddComponent<ModelGizmoRender>()` を `ModelGizmoManager.instance.AddGizmo(target)` へ置き換え。`ApplyDragType(gizmo)` 呼び出しは不要になる（Manager が状態を持つ）
- モデル削除処理（`_models` から除去して GameObject を Destroy している箇所を grep で特定）: `ModelGizmoManager.instance.RemoveGizmo(go)` を追加
- `ApplyDragType()`（全モデル反映版）: ループを削除し以下へ:

```csharp
private void ApplyDragType()
{
    var tool = ToGizmoTool(_dragType);
    ModelGizmoManager.instance.SetToolAndVisible(
        tool, _isModelEditMode && _dragType != GizmoDragType.None);
}

private static GizmoTool ToGizmoTool(GizmoDragType dragType)
{
    switch (dragType)
    {
        case GizmoDragType.Move: return GizmoTool.Move;
        case GizmoDragType.Rotate: return GizmoTool.Rotate;
        case GizmoDragType.Scale: return GizmoTool.Scale;
        default: return GizmoTool.None;
    }
}
```

- `ApplyDragType(GizmoRender gizmo)`（個別版）と `GizmoRender` 参照を削除
- `SelfModelPlacer.Update()` に `ModelGizmoManager.instance.Update();` を追加
- `_dragType` / `_isModelEditMode` のセッターが `ApplyDragType()` を呼ぶ既存フローは維持
- RotationCache（オイラー角の軸単位加算）のロジックは**変更しない**。新ギズモも旧 GizmoRender と同様に wrapper GameObject の transform を直接書き換えるため、既存の差分検出がそのまま機能する
- プラグイン無効化/破棄処理（SelfModelPlacer に全モデル削除の口があればそこ）で `ModelGizmoManager.instance.Dispose()` を呼ぶ

- [ ] **Step 3: 旧実装を削除**

```bash
cd W:/COM3D2_5/work/COM3D2.ModItemExplorer.Plugin
git rm source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/ModelGizmoRender.cs
```

残参照を確認:

Run: `grep -rn "ModelGizmoRender" source/COM3D2.ModItemExplorer.Plugin --include=*.cs`
Expected: ヒットなし

Run: `grep -rn "GizmoRenderHack" source/COM3D2.ModItemExplorer.Plugin --include=*.cs`
Expected: `ModelPlacement/GizmoRenderHack.cs` 本体と `Manager/WindowManager.cs`（`UpdateGizmoDragSuppress`）のみ。それ以外にヒットしたら消し忘れ

- [ ] **Step 4: MIE をビルド**

Run: MIE の `build.bat`
Expected: ビルド成功

- [ ] **Step 5: コミット**

```bash
cd W:/COM3D2_5/work/COM3D2.ModItemExplorer.Plugin
git add -A
git commit -m "feat(gizmo): 配置モデルのギズモを TransformGizmo ベースの ModelGizmoManager へ全面移行"
```

---

### Task 6: 実機検証とサブモジュール最終同期

**Files:**
- Modify: 両リポジトリのサブモジュールポインタ / 必要ならプッシュ

**Interfaces:**
- Consumes: Task 1-5 の全成果

- [ ] **Step 1: 新旧 DLL を配置してゲームを再起動してもらう**

ビルド済み DLL の配置先（`W:\COM3D2_5\BepInEx\plugins` 等、build.bat の出力処理を確認）へ EW / MIE の新 DLL を配置。ゲーム再起動はユーザーに依頼する。

- [ ] **Step 2: devbridge で spec の検証計画を実施**

1. **EW 併用 GameView**: モデルを配置し、GameView 上で Move/Rotate/Scale の各ギズモ操作。EW 自前ギズモ（選択オブジェクト）と MIE ギズモが同時に掴まれないこと
2. **EW 併用 SceneView（本件の目的)**: SceneView 上で MIE モデルのギズモを操作できること。ギズモを外したクリックで EW の選択が動くこと。Alt ドラッグのオービットが効くこと。ドラッグ中に SceneView 領域外へ出ても継続すること
3. **Z/X/C キー切替**と編集モード OFF での非表示
4. **EW 自前ギズモのリグレッション**が無いこと
5. **シーン遷移**: モデル配置中にシーンを跨ぎ、メインカメラの破棄・再生成後も standalone 描画フック（`StandaloneDrawHook`）が新カメラへ再アタッチされ、ギズモ描画・操作が復帰すること（`ModelGizmoManager.Update()` の毎フレーム `AttachDrawHook` で追従する設計の裏取り）
6. **standalone**: 可能なら EW を外した構成（または GizmoHost 未解決状態のログ確認）で GameView 操作が成立すること。実機構成の入れ替えが難しければ、`GizmoHostClient.isAvailable == false` パスのコードレビュー + eval_csharp での座標検算で代替する

各確認は `screenshot` / `eval_csharp`（例: ドラッグ前後の `transform.position` 比較）でエビデンスを取る。

- [ ] **Step 3: 問題があれば修正して該当タスクへ戻る**

- [ ] **Step 4: サブモジュールを同期して最終コミット**

```bash
# MTEUtils を リモートへ push (ユーザー確認後)
cd W:/COM3D2_5/work/COM3D2.ModItemExplorer.Plugin/source/COM3D2.ModItemExplorer.Plugin/MTEUtils
git push origin HEAD:master

# 両リポジトリでサブモジュールポインタをコミット
cd W:/COM3D2_5/work/COM3D2.ModItemExplorer.Plugin
git add source/COM3D2.ModItemExplorer.Plugin/MTEUtils
git commit -m "chore(mteutils): TransformGizmo / GizmoHostClient を取り込み"

cd W:/COM3D2_5/work/COM3D2.EditorWindow.Plugin
git add source/COM3D2.EditorWindow.Plugin/MTEUtils
git commit -m "chore(mteutils): TransformGizmo / GizmoHostClient を取り込み"
```

（push はユーザーへ確認してから行う）
