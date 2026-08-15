# ギズモ設定同期 + Inspector 汎用化 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** MTE と EW のギズモ操作設定 (なし/移動/回転/拡縮、Local/Global) を双方向同期し、EW Inspector の基本描画を MTEUtils の共通部品へ抽出、EW Inspector に外部プラグインへの描画委譲点 (InspectorHost) を追加する。

**Architecture:** 状態同期は MTEUtils の新設リフレクションブリッジ `GizmoToolClient` (EW の `GizmoRenderer.currentTool` / `useLocalSpace` static プロパティを読み書き) + `SelfModelPlacer.Update` のポーリングで行う。ギズモツール行の描画は MTEUtils の `GizmoToolRowDrawer` に抽出し EW/MTE 両方が使う。Inspector 委譲は EW の `InspectorHost` (GizmoHost と同型の static 登録式) + MTEUtils の `InspectorHostClient` で行い、MTE は管理モデル選択時に自前の `ModelTransformPanel` で描く。

**Tech Stack:** C# (.NET 3.5 相当 / Unity プラグイン)、リフレクションブリッジ、MSBuild (`debug.bat`)

**Spec:** `docs/superpowers/specs/2026-08-15-gizmo-sync-inspector-host-design.md`

## Global Constraints

- コードコメント・ログメッセージは日本語で記述する
- MTE ビルド: `cd /w/COM3D2_5/work/COM3D2.ModItemExplorer.Plugin && cmd //c debug.bat com3d25` → `ビルドに成功しました`
- EW ビルド: `cd /w/COM3D2_5/work/COM3D2.EditorWindow.Plugin && cmd //c debug.bat com3d25` → `ビルドに成功しました`
- git worktree は使わない。メイン作業ディレクトリで作業する
- MTEUtils は両リポジトリ共有のサブモジュール。**MTE 側チェックアウト (`source/COM3D2.ModItemExplorer.Plugin/MTEUtils`) で編集・コミットし、EW 側 (`W:\COM3D2_5\work\COM3D2.EditorWindow.Plugin\source\COM3D2.EditorWindow.Plugin\MTEUtils`) はローカル fetch で同じコミットへ更新**する
- プラグイン間の契約はプリミティブ + UnityEngine 型 + デリゲートのみ (プラグイン定義型・MTEUtils 型は DLL 間で共有できない。enum も int で授受する)
- 自動テストは無い。検証はビルド成功 + 実機確認 (MCP `com3d25-devbridge`)

---

### Task 1: MTEUtils — GizmoToolClient 新設

**Files:**
- Create: `source/COM3D2.ModItemExplorer.Plugin/MTEUtils/GizmoToolClient.cs`
- Modify: `source/COM3D2.ModItemExplorer.Plugin/COM3D2.ModItemExplorer.Plugin.csproj` (`<Compile Include="MTEUtils\GizmoHostClient.cs" />` 行の直後に `<Compile Include="MTEUtils\GizmoToolClient.cs" />` を追加)

**Interfaces:**
- Consumes: `DockingClient.FindHostType(string)` (既存)、EW の `GizmoRenderer.currentTool` / `useLocalSpace` (既存 public static プロパティ、EW 側変更なし)
- Produces (Task 2 が使用): `public static bool isAvailable`、`public static GizmoTool tool { get; set; }`、`public static bool useLocalSpace { get; set; }`

**設計メモ:** `currentTool` の型は EW アセンブリ側の `GizmoTool` であり MTE 側の `GizmoTool` とは別型。`Delegate.CreateDelegate` で型付きデリゲートを作れないため、この 2 プロパティは `PropertyInfo.GetValue/SetValue` + int 変換で読み書きする (毎フレーム 2 回程度の reflection 呼び出しで、コストは無視できる)。

- [ ] **Step 1: GizmoToolClient.cs を作成**

```csharp
using System;
using System.Reflection;

namespace COM3D2.MotionTimelineEditor
{
    /// <summary>
    /// EditorWindow プラグインの GizmoRenderer が持つギズモ操作設定
    /// (操作種別 / Local・Global) へのリフレクションブリッジ。
    /// enum はアセンブリ間で別型になるため int 経由で授受する。
    /// EditorWindow が不在・シグネチャ不一致の場合は isAvailable が false になり、
    /// 呼び出し側は同期しない
    /// </summary>
    public static class GizmoToolClient
    {
        private static PropertyInfo _toolProp;
        private static PropertyInfo _useLocalSpaceProp;
        private static Type _hostToolType;
        private static bool _initialized;
        private static bool _failed;

        public static bool isAvailable
        {
            get
            {
                Initialize();
                return _toolProp != null && !_failed;
            }
        }

        /// <summary>EW 側のギズモ操作種別。取得失敗時は None</summary>
        public static GizmoTool tool
        {
            get
            {
                if (!isAvailable)
                {
                    return GizmoTool.None;
                }

                try
                {
                    return (GizmoTool)Convert.ToInt32(_toolProp.GetValue(null, null));
                }
                catch (Exception e)
                {
                    Fail("操作種別の取得に失敗しました", e);
                    return GizmoTool.None;
                }
            }
            set
            {
                if (!isAvailable)
                {
                    return;
                }

                try
                {
                    _toolProp.SetValue(null, Enum.ToObject(_hostToolType, (int)value), null);
                }
                catch (Exception e)
                {
                    Fail("操作種別の設定に失敗しました", e);
                }
            }
        }

        /// <summary>EW 側のギズモ軸空間 (true = Local)。取得失敗時は true</summary>
        public static bool useLocalSpace
        {
            get
            {
                if (!isAvailable)
                {
                    return true;
                }

                try
                {
                    return (bool)_useLocalSpaceProp.GetValue(null, null);
                }
                catch (Exception e)
                {
                    Fail("軸空間の取得に失敗しました", e);
                    return true;
                }
            }
            set
            {
                if (!isAvailable)
                {
                    return;
                }

                try
                {
                    _useLocalSpaceProp.SetValue(null, value, null);
                }
                catch (Exception e)
                {
                    Fail("軸空間の設定に失敗しました", e);
                }
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
            var type = DockingClient.FindHostType("GizmoRenderer");
            if (type == null)
            {
                return;
            }

            // ここから先はホストの型は見つかっている。シグネチャ不一致は
            // バージョン差による恒久的な問題なので、この場合のみ無効へ確定する
            _initialized = true;

            var toolProp = type.GetProperty("currentTool", BindingFlags.Public | BindingFlags.Static);
            var spaceProp = type.GetProperty("useLocalSpace", BindingFlags.Public | BindingFlags.Static);
            if (toolProp == null || spaceProp == null ||
                !toolProp.PropertyType.IsEnum || spaceProp.PropertyType != typeof(bool) ||
                !toolProp.CanWrite || !spaceProp.CanWrite)
            {
                MTEUtils.LogWarning("GizmoToolClient: GizmoRenderer にシグネチャの一致するプロパティが見つかりませんでした");
                return;
            }

            _toolProp = toolProp;
            _useLocalSpaceProp = spaceProp;
            _hostToolType = toolProp.PropertyType;
        }

        /// <summary>
        /// 毎フレーム呼ばれる経路なので、一度失敗したら以後は同期を止めてログを溢れさせない
        /// </summary>
        private static void Fail(string message, Exception e)
        {
            MTEUtils.LogWarning("GizmoToolClient: " + message + ": " + e.Message);
            _failed = true;
        }
    }
}
```

- [ ] **Step 2: csproj に追加**

`COM3D2.ModItemExplorer.Plugin.csproj` の `<Compile Include="MTEUtils\GizmoHostClient.cs" />` 行の直後に追加:

```xml
    <Compile Include="MTEUtils\GizmoToolClient.cs" />
```

- [ ] **Step 3: ビルド確認**

Run: `cd /w/COM3D2_5/work/COM3D2.ModItemExplorer.Plugin && cmd //c debug.bat com3d25`
Expected: `ビルドに成功しました`

- [ ] **Step 4: Commit (MTEUtils サブモジュール)**

```bash
cd /w/COM3D2_5/work/COM3D2.ModItemExplorer.Plugin/source/COM3D2.ModItemExplorer.Plugin/MTEUtils
git add GizmoToolClient.cs
git commit -m "feat(gizmo): EW のギズモ操作設定へのブリッジ GizmoToolClient を追加"
```

---

### Task 2: MTE — ギズモ設定の双方向同期 + Local/Global 対応

**Files:**
- Modify: `source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/SelfModelPlacer.cs`
- Modify: `source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/ModelGizmoManager.cs`
- Modify: `source/COM3D2.ModItemExplorer.Plugin/ModelOperationWindow.cs`

**Interfaces:**
- Consumes: `GizmoToolClient.isAvailable` / `tool` / `useLocalSpace` (Task 1)、既存の `SelfModelPlacer.dragType` (:133) / `ToGizmoTool` (:1339) / `Update` (:490)、`ModelGizmoManager.SetToolAndVisible` (:90) / `AddGizmo` (:57)
- Produces (Task 5, 8 が使用): `public bool useLocalSpace { get; set; }` (SelfModelPlacer)、`ModelGizmoManager.SetUseLocalSpace(bool)`

- [ ] **Step 1: ModelGizmoManager に useLocalSpace を追加**

`_tool` フィールド (ModelGizmoManager.cs:25) の直後に追加:

```csharp
        private bool _useLocalSpace = true;
```

`AddGizmo` (:57) の `new TransformGizmo` 初期化子に 1 行追加:

```csharp
            var gizmo = new TransformGizmo
            {
                target = target.transform,
                tool = _visible ? _tool : GizmoTool.None,
                sizeScale = SelfModelPlacer.GizmoScale,
                useLocalSpace = _useLocalSpace,
            };
```

`SetToolAndVisible` (:90) の直後にメソッドを追加:

```csharp
        /// <summary>軸空間 (Local/Global) を全ギズモへ反映する</summary>
        public void SetUseLocalSpace(bool useLocalSpace)
        {
            _useLocalSpace = useLocalSpace;
            foreach (var gizmo in _gizmos.Values)
            {
                gizmo.useLocalSpace = useLocalSpace;
            }
        }
```

- [ ] **Step 2: SelfModelPlacer に useLocalSpace プロパティを追加**

`dragType` プロパティ (SelfModelPlacer.cs:133-146) の直後に追加:

```csharp
        private bool _useLocalSpace = true;

        /// <summary>ギズモの軸空間 (true = Local)。EW 在席時は GizmoToolClient と双方向同期する</summary>
        public bool useLocalSpace
        {
            get => _useLocalSpace;
            set
            {
                if (_useLocalSpace == value)
                {
                    return;
                }

                _useLocalSpace = value;
                ModelGizmoManager.instance.SetUseLocalSpace(value);
            }
        }
```

- [ ] **Step 3: 双方向同期メソッドを追加**

`UpdateGizmoKeyInput` (:1351) の直後に追加:

```csharp
        // 前回同期時点の値。EW とどちら側が変更したかの判別に使う
        private GizmoDragType _lastSyncedDragType;
        private bool _lastSyncedUseLocalSpace;
        private bool _gizmoToolSyncStarted;

        /// <summary>
        /// ギズモ操作設定を EW (GizmoRenderer) と双方向同期する。
        /// EW 側にイベントが無いため毎フレームのポーリングで追従し、
        /// 前回同期値との差分でどちらが動いたかを判別する (同値なら no-op でループしない)。
        /// 両側が同フレームに変わった場合は MTE 側を優先する
        /// </summary>
        // ホスト型が未解決の間の再試行間隔 (フレーム)。
        // ホスト型の解決は毎フレーム行うほど安くはない (TryRegisterSelectionHandler と同じパターン)
        private const int GizmoToolSyncRetryIntervalFrames = 60;
        private int _lastGizmoToolSyncAttemptFrame = -GizmoToolSyncRetryIntervalFrames;

        private void UpdateGizmoToolSync()
        {
            if (!_gizmoToolSyncStarted)
            {
                var frame = Time.frameCount;
                if (frame - _lastGizmoToolSyncAttemptFrame < GizmoToolSyncRetryIntervalFrames)
                {
                    return;
                }
                _lastGizmoToolSyncAttemptFrame = frame;
            }

            if (!GizmoToolClient.isAvailable)
            {
                return;
            }

            if (!_gizmoToolSyncStarted)
            {
                // 初回は EW 側の現在値へ合わせる (EW を正とする)
                _gizmoToolSyncStarted = true;
                dragType = FromGizmoTool(GizmoToolClient.tool);
                useLocalSpace = GizmoToolClient.useLocalSpace;
                _lastSyncedDragType = dragType;
                _lastSyncedUseLocalSpace = useLocalSpace;
                return;
            }

            if (dragType != _lastSyncedDragType)
            {
                GizmoToolClient.tool = ToGizmoTool(dragType);
            }
            else
            {
                dragType = FromGizmoTool(GizmoToolClient.tool);
            }
            _lastSyncedDragType = dragType;

            if (useLocalSpace != _lastSyncedUseLocalSpace)
            {
                GizmoToolClient.useLocalSpace = useLocalSpace;
            }
            else
            {
                useLocalSpace = GizmoToolClient.useLocalSpace;
            }
            _lastSyncedUseLocalSpace = useLocalSpace;
        }

        private static GizmoDragType FromGizmoTool(GizmoTool tool)
        {
            switch (tool)
            {
                case GizmoTool.Move: return GizmoDragType.Move;
                case GizmoTool.Rotate: return GizmoDragType.Rotate;
                case GizmoTool.Scale: return GizmoDragType.Scale;
                default: return GizmoDragType.None;
            }
        }
```

- [ ] **Step 4: Update から呼び出す**

`Update` (:490) の `UpdateGizmoKeyInput();` の直後に 1 行追加:

```csharp
            UpdateGizmoToolSync();
```

- [ ] **Step 5: ModelOperationWindow のギズモ行に Local/Global ボタンを追加**

`DrawGizmoRow` (ModelOperationWindow.cs:431-451) の「拡縮」トグルの直後 (`view.EndLayout();` の直前) に追加:

```csharp
                if (view.DrawButton(placer.useLocalSpace ? "Local" : "Global",
                    54, ROW_HEIGHT))
                {
                    placer.useLocalSpace = !placer.useLocalSpace;
                }
```

併せてトグル 4 個の幅を `60` → `44` に変更する (EW Inspector の ToolButtonWidth と同じ。MIN_WINDOW_WIDTH = 380 に収めるため)。

- [ ] **Step 6: ビルド確認**

Run: `cd /w/COM3D2_5/work/COM3D2.ModItemExplorer.Plugin && cmd //c debug.bat com3d25`
Expected: `ビルドに成功しました`

- [ ] **Step 7: Commit (MTE リポ、submodule bump 込み)**

```bash
cd /w/COM3D2_5/work/COM3D2.ModItemExplorer.Plugin
git add source/COM3D2.ModItemExplorer.Plugin/MTEUtils \
    source/COM3D2.ModItemExplorer.Plugin/COM3D2.ModItemExplorer.Plugin.csproj \
    source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/SelfModelPlacer.cs \
    source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/ModelGizmoManager.cs \
    source/COM3D2.ModItemExplorer.Plugin/ModelOperationWindow.cs
git commit -m "feat(gizmo): ギズモ操作設定を EW と双方向同期し Local/Global 切替を追加"
```

---

### Task 3: MTEUtils — GizmoToolRowDrawer 抽出 + Vector3RowOption の軸コールバック追加

**Files:**
- Create: `source/COM3D2.ModItemExplorer.Plugin/MTEUtils/GizmoToolRowDrawer.cs`
- Modify: `source/COM3D2.ModItemExplorer.Plugin/MTEUtils/GUIView.cs` (`Vector3RowOption` :1325-1337 と `DrawVector3Row` :1348)
- Modify: `source/COM3D2.ModItemExplorer.Plugin/COM3D2.ModItemExplorer.Plugin.csproj` (`<Compile Include="MTEUtils\GizmoToolClient.cs" />` 行の直後に `<Compile Include="MTEUtils\GizmoToolRowDrawer.cs" />` を追加)

**Interfaces:**
- Produces (Task 4, 5, 8 が使用):
  - `GizmoToolRowDrawer.Draw(GUIView view, GizmoToolRowOption option)`
  - `GizmoToolRowOption { float labelWidth; float height; GUIStyle labelStyle; Func<GizmoTool> getTool; Action<GizmoTool> setTool; Func<bool> getUseLocalSpace; Action<bool> setUseLocalSpace; }`
  - `GUIView.Vector3RowOption` に `public Action<Vector3, int> onChangedAxis;` を追加 (変更後の値と変更軸 index を渡す。`onChanged` と併存し、どちらか設定された方が呼ばれる)

- [ ] **Step 1: GizmoToolRowDrawer.cs を作成**

```csharp
using System;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor
{
    /// <summary>ギズモツール行の設定。状態は持たず、取得・変更はデリゲートで注入する</summary>
    public struct GizmoToolRowOption
    {
        public float labelWidth;
        /// <summary>行の高さ。0 なら 20</summary>
        public float height;
        /// <summary>ラベルのスタイル。null なら既定</summary>
        public GUIStyle labelStyle;
        public Func<GizmoTool> getTool;
        public Action<GizmoTool> setTool;
        public Func<bool> getUseLocalSpace;
        public Action<bool> setUseLocalSpace;
    }

    /// <summary>
    /// ギズモの操作種別 (なし/移動/回転/拡縮) と軸空間 (Local/Global) の切替行。
    /// EW の Inspector と MTE のモデル操作ウィンドウで共通に使う
    /// </summary>
    public static class GizmoToolRowDrawer
    {
        private static readonly GizmoTool[] Tools =
            { GizmoTool.None, GizmoTool.Move, GizmoTool.Rotate, GizmoTool.Scale };
        private static readonly string[] ToolNames = { "なし", "移動", "回転", "拡縮" };

        public static readonly float ToolButtonWidth = 44f;
        public static readonly float SpaceButtonWidth = 54f;

        public static void Draw(GUIView view, GizmoToolRowOption option)
        {
            var height = option.height > 0f ? option.height : 20f;

            view.BeginHorizontal();
            {
                view.DrawLabel("ギズモ", option.labelWidth, height, style: option.labelStyle);

                var current = option.getTool();
                for (var i = 0; i < Tools.Length; i++)
                {
                    var tool = Tools[i];
                    view.DrawToggle(ToolNames[i], current == tool,
                        ToolButtonWidth, height,
                        // 選択中の項目を再度押しても解除しない (解除は「なし」で行う)
                        on => { if (on) option.setTool(tool); });
                }

                if (view.DrawButton(option.getUseLocalSpace() ? "Local" : "Global",
                    SpaceButtonWidth, height))
                {
                    option.setUseLocalSpace(!option.getUseLocalSpace());
                }
            }
            view.EndLayout();
        }
    }
}
```

- [ ] **Step 2: Vector3RowOption に軸コールバックを追加**

`GUIView.cs` の `Vector3RowOption` (:1325-1337) に追加:

```csharp
            /// <summary>変更後の値と変更軸の index を受け取る。onChanged とどちらかを設定する</summary>
            public Action<Vector3, int> onChangedAxis;
```

`DrawVector3Row` (:1348) 内の `option.onChanged(value);` (2 箇所: DrawDragLabel の delta ハンドラと DrawFloatField の onChanged) を以下に置き換え:

```csharp
                            NotifyChanged(option, value, index);
```

同メソッドの直後にヘルパを追加:

```csharp
        private static void NotifyChanged(Vector3RowOption option, Vector3 value, int index)
        {
            if (option.onChangedAxis != null)
            {
                option.onChangedAxis(value, index);
            }
            else if (option.onChanged != null)
            {
                option.onChanged(value);
            }
        }
```

- [ ] **Step 3: csproj に追加**

`COM3D2.ModItemExplorer.Plugin.csproj` の `<Compile Include="MTEUtils\GizmoToolClient.cs" />` 行の直後に追加:

```xml
    <Compile Include="MTEUtils\GizmoToolRowDrawer.cs" />
```

- [ ] **Step 4: ビルド確認 (MTE)**

Run: `cd /w/COM3D2_5/work/COM3D2.ModItemExplorer.Plugin && cmd //c debug.bat com3d25`
Expected: `ビルドに成功しました`

- [ ] **Step 5: Commit (MTEUtils サブモジュール)**

```bash
cd /w/COM3D2_5/work/COM3D2.ModItemExplorer.Plugin/source/COM3D2.ModItemExplorer.Plugin/MTEUtils
git add GizmoToolRowDrawer.cs GUIView.cs
git commit -m "feat(gui): ギズモツール行の共通描画部品と Vector3 行の軸コールバックを追加"
```

---

### Task 4: EW — InspectorWindow を共通部品利用へ書き換え

**Files:**
- Modify: `W:\COM3D2_5\work\COM3D2.EditorWindow.Plugin\source\COM3D2.EditorWindow.Plugin\MTEUtils` (サブモジュールを Task 3 のコミットへ更新)
- Modify: `W:\COM3D2_5\work\COM3D2.EditorWindow.Plugin\source\COM3D2.EditorWindow.Plugin\InspectorWindow.cs`
- Modify: `W:\COM3D2_5\work\COM3D2.EditorWindow.Plugin\source\COM3D2.EditorWindow.Plugin\COM3D2.EditorWindow.Plugin.csproj` (`<Compile Include="MTEUtils\GizmoHostClient.cs" />` 相当の行の並びに `GizmoToolClient.cs` / `GizmoToolRowDrawer.cs` を追加。EW 側では GizmoToolClient は未使用だがサブモジュール全ファイルを含める流儀に合わせる。既存 csproj が MTEUtils を個別 Include している場合のみ。ワイルドカード Include なら変更不要 — 実ファイルを確認して判断する)
- 挙動は現状維持 (見た目・操作は変えない)

**Interfaces:**
- Consumes: `GizmoToolRowDrawer.Draw` / `GizmoToolRowOption` (Task 3)

**計画時判断 (spec からの縮小):** spec ではオイラー角キャッシュ (`SyncEulerCache`) も MTEUtils へ移すとしたが、Vector3 行が既に共有部品 (`GUIView.DrawVector3Row`) だったため抽出対象はギズモツール行だけで十分になった。オイラー角キャッシュは利用者が EW InspectorWindow 1 箇所のみ (MTE は別方式の `RotationCache` を維持) なので YAGNI で EW 内に残す。

- [ ] **Step 1: EW 側サブモジュールを更新**

```bash
cd /w/COM3D2_5/work/COM3D2.EditorWindow.Plugin/source/COM3D2.EditorWindow.Plugin/MTEUtils
git fetch /w/COM3D2_5/work/COM3D2.ModItemExplorer.Plugin/source/COM3D2.ModItemExplorer.Plugin/MTEUtils master
git merge --ff-only FETCH_HEAD
```

Expected: Task 3 のコミットまで fast-forward される (`git log --oneline -2` で確認)

- [ ] **Step 2: DrawGizmoToolRow を共通部品の呼び出しへ置換**

`InspectorWindow.cs` の `DrawGizmoToolRow` (:459-482) の本体を以下へ置き換え (メソッド名・呼び出し 3 箇所は維持):

```csharp
        /// <summary>
        /// ギズモの操作種別と軸空間の切り替え。
        /// SceneView / GameView 双方のギズモがこの設定を共有する
        /// </summary>
        private void DrawGizmoToolRow()
        {
            GizmoToolRowDrawer.Draw(_view, new GizmoToolRowOption
            {
                labelWidth = LabelWidth,
                height = RowHeight,
                getTool = () => GizmoRenderer.currentTool,
                setTool = tool => GizmoRenderer.currentTool = tool,
                getUseLocalSpace = () => GizmoRenderer.useLocalSpace,
                setUseLocalSpace = value => GizmoRenderer.useLocalSpace = value,
            });
        }
```

併せて不要になった `GizmoTools` / `GizmoToolNames` / `ToolButtonWidth` / `SpaceButtonWidth` (:19-24) を削除する。

- [ ] **Step 3: ビルド確認 (EW)**

Run: `cd /w/COM3D2_5/work/COM3D2.EditorWindow.Plugin && cmd //c debug.bat com3d25`
Expected: `ビルドに成功しました`

- [ ] **Step 4: Commit (EW リポ)**

```bash
cd /w/COM3D2_5/work/COM3D2.EditorWindow.Plugin
git add source/COM3D2.EditorWindow.Plugin/MTEUtils \
    source/COM3D2.EditorWindow.Plugin/InspectorWindow.cs \
    source/COM3D2.EditorWindow.Plugin/COM3D2.EditorWindow.Plugin.csproj
git commit -m "refactor(inspector): ギズモツール行を MTEUtils の共通部品へ置換"
```

---

### Task 5: MTE — ModelOperationWindow を共通部品利用へ書き換え

**Files:**
- Modify: `source/COM3D2.ModItemExplorer.Plugin/ModelOperationWindow.cs`
- 挙動は現状維持 (Task 2 で追加した Local/Global ボタン含め見た目は変えない)

**Interfaces:**
- Consumes: `GizmoToolRowDrawer.Draw` / `GizmoToolRowOption` (Task 3)、`GUIView.DrawVector3Row` + `onChangedAxis` (Task 3)、`SelfModelPlacer.useLocalSpace` (Task 2)

- [ ] **Step 1: DrawGizmoRow を共通部品の呼び出しへ置換**

`DrawGizmoRow` (:431 付近、Task 2 適用後) の本体を以下へ置き換え:

```csharp
        /// <summary>
        /// ギズモの操作種別と軸空間。dragType は配置モデル全体で共有される
        /// </summary>
        private void DrawGizmoRow(GUIView view)
        {
            GizmoToolRowDrawer.Draw(view, new GizmoToolRowOption
            {
                labelWidth = LABEL_WIDTH,
                height = ROW_HEIGHT,
                labelStyle = GUIView.gsLabelRight,
                getTool = () => SelfModelPlacer.ToGizmoTool(placer.dragType),
                setTool = tool => placer.dragType = SelfModelPlacer.FromGizmoTool(tool),
                getUseLocalSpace = () => placer.useLocalSpace,
                setUseLocalSpace = value => placer.useLocalSpace = value,
            });
        }
```

`SelfModelPlacer.ToGizmoTool` (:1339) と `FromGizmoTool` (Task 2 で追加) の可視性を `private` → `public` に変更する。

- [ ] **Step 2: DrawVector3Row を GUIView 版へ置換**

`ModelOperationWindow.cs` の自前 `DrawVector3Row` (:553-600) を削除し、呼び出し 3 箇所 (:468, :474, :478) を以下へ置き換え:

```csharp
            view.DrawVector3Row(new GUIView.Vector3RowOption
            {
                label = "位置",
                labelWidth = LABEL_WIDTH,
                height = ROW_HEIGHT,
                dragSensitivity = PositionSensitivity,
                value = cache.position,
                onChanged = value => { cache.position = value; cache.Apply(); },
                onReset = () => { cache.position = Vector3.zero; cache.Apply(); },
            });

            // 回転は SelfModelPlacer のオイラー角キャッシュを使う。
            // ギズモ操作分も軸単位で足し込まれるため、ハンドル操作が該当軸の数値だけを動かす
            view.DrawVector3Row(new GUIView.Vector3RowOption
            {
                label = "回転",
                labelWidth = LABEL_WIDTH,
                height = ROW_HEIGHT,
                dragSensitivity = RotationSensitivity,
                value = placer.GetEulerAngles(model),
                onChanged = value => placer.SetEulerAngles(model, value),
                onReset = () => placer.SetEulerAngles(model, Vector3.zero),
            });

            view.DrawVector3Row(new GUIView.Vector3RowOption
            {
                label = "拡縮",
                labelWidth = LABEL_WIDTH,
                height = ROW_HEIGHT,
                dragSensitivity = ScaleSensitivity,
                value = cache.scale,
                onChangedAxis = (value, index) => ApplyScale(cache, value, index),
                onReset = () => { cache.scale = Vector3.one; cache.Apply(); },
            });
```

不要になった `AxisNames` (:72) も削除する (他に参照が無いことを grep で確認)。

**注意:** GUIView 版はラベルスタイル指定が無いため、ラベルが左寄せになる (従来は `gsLabelRight`)。ギズモ行 (Task 3 部品は labelStyle 指定可) と揃えるため、`Vector3RowOption` にも `public GUIStyle labelStyle;` を追加して `DrawLabel` へ渡すこと (MTEUtils 側の変更。Task 3 でまとめて入れてもよい)。

- [ ] **Step 3: ビルド確認 (MTE)**

Run: `cd /w/COM3D2_5/work/COM3D2.ModItemExplorer.Plugin && cmd //c debug.bat com3d25`
Expected: `ビルドに成功しました`

- [ ] **Step 4: Commit**

MTEUtils に変更が出た場合は先にサブモジュールでコミットする:

```bash
cd /w/COM3D2_5/work/COM3D2.ModItemExplorer.Plugin/source/COM3D2.ModItemExplorer.Plugin/MTEUtils
git add GUIView.cs && git commit -m "feat(gui): Vector3 行にラベルスタイル指定を追加"
cd /w/COM3D2_5/work/COM3D2.ModItemExplorer.Plugin
git add source/COM3D2.ModItemExplorer.Plugin/MTEUtils \
    source/COM3D2.ModItemExplorer.Plugin/ModelOperationWindow.cs \
    source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/SelfModelPlacer.cs
git commit -m "refactor(model-window): ギズモ行と Transform 行を MTEUtils の共通部品へ置換"
```

---

### Task 6: EW — InspectorHost 新設 + InspectorWindow の委譲

**Files:**
- Create: `W:\COM3D2_5\work\COM3D2.EditorWindow.Plugin\source\COM3D2.EditorWindow.Plugin\Manager\InspectorHost.cs`
- Modify: `W:\COM3D2_5\work\COM3D2.EditorWindow.Plugin\source\COM3D2.EditorWindow.Plugin\InspectorWindow.cs` (:106 `DrawContent`)
- Modify: `W:\COM3D2_5\work\COM3D2.EditorWindow.Plugin\source\COM3D2.EditorWindow.Plugin\COM3D2.EditorWindow.Plugin.csproj` (Manager の Compile 行の並びに `InspectorHost.cs` を追加)

**Interfaces:**
- Produces (Task 7 のブリッジが参照。**公開後シグネチャ変更禁止**):
  - `public static object Register(string name, Func<GameObject, bool> canDraw, Action<GameObject, Rect> draw)`
  - `public static void Unregister(object handle)`
  - `public static bool TryDraw(GameObject go, Rect contentRect)` (InspectorWindow 内部用だが同居させる)

- [ ] **Step 1: InspectorHost.cs を作成**

```csharp
using System;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.EditorWindow.Plugin
{
    /// <summary>
    /// Inspector の内容描画を外部プラグインへ委譲する公開 API。
    /// MTEUtils の InspectorHostClient からリフレクションで発見・呼び出しされるため、
    /// クラス名・メソッドシグネチャは公開後変更禁止 (変更時は Register2 等の別名で追加する)。
    /// 契約はプリミティブ + UnityEngine 型 + デリゲートのみ (プラグイン定義型は DLL 間で共有できない)
    /// </summary>
    public static class InspectorHost
    {
        private class Entry
        {
            public string name;
            public Func<GameObject, bool> canDraw;
            public Action<GameObject, Rect> draw;
        }

        private static readonly List<Entry> _entries = new List<Entry>();

        public static object Register(
            string name,
            Func<GameObject, bool> canDraw,
            Action<GameObject, Rect> draw)
        {
            if (canDraw == null || draw == null)
            {
                MTEUtils.LogError("InspectorHost.Register: デリゲートに null は指定できません");
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
                canDraw = canDraw,
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
            _entries.Remove(entry);
        }

        /// <summary>
        /// 選択オブジェクトを管理下に持つ登録者がいれば内容描画を委譲して true を返す。
        /// 例外は登録者単位で隔離し、失敗した委譲はそのフレームだけ既定描画へ戻す
        /// </summary>
        public static bool TryDraw(GameObject go, Rect contentRect)
        {
            foreach (var entry in _entries)
            {
                try
                {
                    if (!entry.canDraw(go))
                    {
                        continue;
                    }
                    entry.draw(go, contentRect);
                    return true;
                }
                catch (Exception e)
                {
                    // 外部プラグインの例外でホストの描画を止めない
                    MTEUtils.LogException(e);
                }
            }
            return false;
        }
    }
}
```

- [ ] **Step 2: InspectorWindow.DrawContent に委譲分岐を追加**

`DrawContent` (:106) の `else` 分岐 (go != null、:140-197) の先頭、`_view.BeginScrollView` の**手前**に追加:

```csharp
                // 外部プラグイン管理下のオブジェクトは内容描画を丸ごと委譲する
                // (スクロールも委譲先が必要に応じて自前で行う)
                if (InspectorHost.TryDraw(go, _view.viewRect))
                {
                    ComboBoxPopupWindow.instance.ProcessFocus(_rootView, this);
                    return;
                }
```

**注意:** `_view.viewRect` が `GUIView.Init` に渡した Rect を公開していない場合は、`ToLocalRect(contentRect)` の値をローカル変数に取って `_view.Init` と委譲の両方で使う形にする (実装時に GUIView の公開メンバーを確認して選ぶ)。

- [ ] **Step 3: csproj に追加**

`COM3D2.EditorWindow.Plugin.csproj` の Manager 配下 Compile 行の並び (アルファベット順) に追加:

```xml
    <Compile Include="Manager\InspectorHost.cs" />
```

- [ ] **Step 4: ビルド確認 (EW)**

Run: `cd /w/COM3D2_5/work/COM3D2.EditorWindow.Plugin && cmd //c debug.bat com3d25`
Expected: `ビルドに成功しました`

- [ ] **Step 5: Commit (EW リポ)**

```bash
cd /w/COM3D2_5/work/COM3D2.EditorWindow.Plugin
git add source/COM3D2.EditorWindow.Plugin/Manager/InspectorHost.cs \
    source/COM3D2.EditorWindow.Plugin/InspectorWindow.cs \
    source/COM3D2.EditorWindow.Plugin/COM3D2.EditorWindow.Plugin.csproj
git commit -m "feat(inspector): 外部プラグインへ内容描画を委譲する InspectorHost を追加"
```

---

### Task 7: MTEUtils — InspectorHostClient 新設

**Files:**
- Create: `source/COM3D2.ModItemExplorer.Plugin/MTEUtils/InspectorHostClient.cs`
- Modify: `source/COM3D2.ModItemExplorer.Plugin/COM3D2.ModItemExplorer.Plugin.csproj` (`<Compile Include="MTEUtils\GizmoToolRowDrawer.cs" />` 行の後に `<Compile Include="MTEUtils\InspectorHostClient.cs" />` を追加。アルファベット順なら GizmoHostClient と SelectionClient の間)

**Interfaces:**
- Consumes: `DockingClient.FindHostType(string)`、EW の `InspectorHost.Register` / `Unregister` (Task 6)
- Produces (Task 8 が使用): `public static bool isAvailable`、`public static object Register(string name, Func<GameObject, bool> canDraw, Action<GameObject, Rect> draw)`、`public static void Unregister(object handle)`

- [ ] **Step 1: InspectorHostClient.cs を作成**

`GizmoHostClient` と同じ「型が見つかるまで再試行、シグネチャ不一致で確定フォールバック」パターン:

```csharp
using System;
using System.Reflection;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor
{
    /// <summary>
    /// EditorWindow プラグインの InspectorHost へのリフレクションブリッジ。
    /// 登録すると、EW Inspector で対象オブジェクト選択時に内容描画が丸ごと委譲される。
    /// EditorWindow が不在・旧バージョンの場合は isAvailable が false になり、
    /// 呼び出し側は登録しない (EW Inspector は従来描画のまま)
    /// </summary>
    public static class InspectorHostClient
    {
        private delegate object RegisterDelegate(
            string name,
            Func<GameObject, bool> canDraw,
            Action<GameObject, Rect> draw);

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
            var type = DockingClient.FindHostType("InspectorHost");
            if (type == null)
            {
                return;
            }

            // ここから先はホストの型は見つかっている。シグネチャ不一致は
            // バージョン差による恒久的な問題なので、この場合のみ無効へ確定する
            _initialized = true;

            try
            {
                var register = type.GetMethod("Register", BindingFlags.Public | BindingFlags.Static);
                var unregister = type.GetMethod("Unregister", BindingFlags.Public | BindingFlags.Static);
                if (register == null || unregister == null)
                {
                    MTEUtils.LogWarning("InspectorHostClient: InspectorHost にシグネチャの一致するメソッドが見つかりませんでした");
                    return;
                }

                _register = (RegisterDelegate)Delegate.CreateDelegate(typeof(RegisterDelegate), register);
                _unregister = (Action<object>)Delegate.CreateDelegate(typeof(Action<object>), unregister);
            }
            catch (Exception e)
            {
                // ホスト側のバージョン差でシグネチャが合わない場合は登録を無効化する
                MTEUtils.LogWarning("InspectorHostClient: InspectorHost との接続に失敗しました: " + e.Message);
                _register = null;
                _unregister = null;
            }
        }

        /// <summary>Inspector 描画をホストへ登録する。戻り値はハンドル (ホスト不在なら null)</summary>
        public static object Register(
            string name,
            Func<GameObject, bool> canDraw,
            Action<GameObject, Rect> draw)
        {
            if (!isAvailable)
            {
                return null;
            }

            try
            {
                return _register(name, canDraw, draw);
            }
            catch (Exception e)
            {
                MTEUtils.LogWarning("InspectorHostClient: InspectorHost への登録に失敗しました: " + e.Message);
                return null;
            }
        }

        public static void Unregister(object handle)
        {
            if (handle == null || !isAvailable)
            {
                return;
            }

            try
            {
                _unregister(handle);
            }
            catch (Exception e)
            {
                MTEUtils.LogWarning("InspectorHostClient: InspectorHost からの登録解除に失敗しました: " + e.Message);
            }
        }
    }
}
```

- [ ] **Step 2: csproj に追加、ビルド確認 (MTE)**

Run: `cd /w/COM3D2_5/work/COM3D2.ModItemExplorer.Plugin && cmd //c debug.bat com3d25`
Expected: `ビルドに成功しました`

- [ ] **Step 3: Commit (MTEUtils サブモジュール)**

```bash
cd /w/COM3D2_5/work/COM3D2.ModItemExplorer.Plugin/source/COM3D2.ModItemExplorer.Plugin/MTEUtils
git add InspectorHostClient.cs
git commit -m "feat(inspector): InspectorHost へのブリッジ InspectorHostClient を追加"
```

---

### Task 8: MTE — ModelTransformPanel 抽出と InspectorHost 登録

**Files:**
- Create: `source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/ModelInspectorDrawer.cs`
- Modify: `source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/SelfModelPlacer.cs`
- Modify: `source/COM3D2.ModItemExplorer.Plugin/COM3D2.ModItemExplorer.Plugin.csproj`

**Interfaces:**
- Consumes: `InspectorHostClient` (Task 7)、`GizmoToolRowDrawer` / `GUIView.DrawVector3Row` (Task 3)、`SelfModelPlacer.FindModelByGameObject` (既存) / `dragType` / `useLocalSpace` / `GetEulerAngles` / `SetEulerAngles` / `GetAttachState` (既存)
- Produces: `ModelInspectorDrawer` (SelfModelPlacer が保持・登録する)

**計画時判断 (spec からの縮小):** アタッチ先コンボは MTE の `ComboBoxPopupWindow` / `ProcessFocus` がウィンドウ所属前提のため、EW ウィンドウ内への委譲描画では動作保証できない。EW Inspector 側では**アタッチ状態をラベル表示のみ**とし、変更は従来通り ModelOperationWindow で行う (コンボ対応は必要になったら別プラン)。

- [ ] **Step 1: ModelInspectorDrawer.cs を作成**

```csharp
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.ModItemExplorer.Plugin
{
    /// <summary>
    /// EW Inspector へ委譲描画する MTE 管理モデルの内容。
    /// ギズモ行・Transform 行は ModelOperationWindow と同じ共通部品で描く。
    /// アタッチ先はコンボのポップアップ基盤が使えないため表示のみ (変更は操作ウィンドウで行う)
    /// </summary>
    public class ModelInspectorDrawer
    {
        private const float LabelWidth = 40f;
        private const float RowHeight = 20f;
        private const float PositionSensitivity = 0.01f;
        private const float RotationSensitivity = 1f;
        private const float ScaleSensitivity = 0.01f;

        private readonly GUIView _view = new GUIView();

        private static SelfModelPlacer placer => SelfModelPlacer.instance;

        /// <summary>
        /// InspectorHost の canDraw。自プラグイン管理のモデルだけ引き受ける。
        /// ホスト経由の描画は Update とは独立に呼ばれるため、無効化中に委譲描画が
        /// 残らないよう入口でプラグイン有効状態も確認する (ModelGizmoManager と同じパターン)
        /// </summary>
        public bool CanDraw(GameObject go)
        {
            var plugin = ModItemExplorer.instance;
            if (plugin == null || !plugin.isEnable)
            {
                return false;
            }
            return placer.FindModelByGameObject(go) != null;
        }

        /// <summary>InspectorHost の draw。contentRect は EW Inspector のウィンドウローカル領域</summary>
        public void Draw(GameObject go, Rect contentRect)
        {
            var model = placer.FindModelByGameObject(go);
            if (model == null)
            {
                return;
            }

            _view.Init(contentRect);

            _view.BeginHorizontal();
            {
                _view.DrawToggle(go.activeSelf, 20, RowHeight, value => go.SetActive(value));
                _view.DrawLabel(go.name, -1, RowHeight);
            }
            _view.EndLayout();

            GizmoToolRowDrawer.Draw(_view, new GizmoToolRowOption
            {
                labelWidth = LabelWidth,
                height = RowHeight,
                getTool = () => SelfModelPlacer.ToGizmoTool(placer.dragType),
                setTool = tool => placer.dragType = SelfModelPlacer.FromGizmoTool(tool),
                getUseLocalSpace = () => placer.useLocalSpace,
                setUseLocalSpace = value => placer.useLocalSpace = value,
            });

            var cache = _view.GetTransformCache(go.transform);

            _view.DrawVector3Row(new GUIView.Vector3RowOption
            {
                label = "位置",
                labelWidth = LabelWidth,
                height = RowHeight,
                dragSensitivity = PositionSensitivity,
                value = cache.position,
                onChanged = value => { cache.position = value; cache.Apply(); },
                onReset = () => { cache.position = Vector3.zero; cache.Apply(); },
            });

            // 回転は SelfModelPlacer のオイラー角キャッシュを使う (ModelOperationWindow と同じ)
            _view.DrawVector3Row(new GUIView.Vector3RowOption
            {
                label = "回転",
                labelWidth = LabelWidth,
                height = RowHeight,
                dragSensitivity = RotationSensitivity,
                value = placer.GetEulerAngles(model),
                onChanged = value => placer.SetEulerAngles(model, value),
                onReset = () => placer.SetEulerAngles(model, Vector3.zero),
            });

            _view.DrawVector3Row(new GUIView.Vector3RowOption
            {
                label = "拡縮",
                labelWidth = LabelWidth,
                height = RowHeight,
                dragSensitivity = ScaleSensitivity,
                value = cache.scale,
                onChanged = value => { cache.scale = value; cache.Apply(); },
                onReset = () => { cache.scale = Vector3.one; cache.Apply(); },
            });

            var state = placer.GetAttachState(model);
            if (state != null)
            {
                _view.DrawLabel("アタッチ: " + state.boneName + " (変更は操作ウィンドウで)",
                    -1, RowHeight);
            }
        }
    }
}
```

**注意:** `SelfModelPlacer.instance` / `FindModelByGameObject` / `GetAttachState` の実際のシグネチャ・可視性は実装時に確認し、private なら public へ昇格する。`AttachState.boneName` も同様。

- [ ] **Step 2: SelfModelPlacer で InspectorHost へ登録**

`TryRegisterSelectionHandler` と同じ再試行パターンで登録する。`_selectionHandlerRegistered` フィールド群の近くに追加:

```csharp
        // EW の InspectorHost へ登録済みか。EW は後からロードされる可能性があるため
        // Update で成功するまで再試行する (選択購読と同じパターン)
        private object _inspectorHandle;
        private ModelInspectorDrawer _inspectorDrawer;

        private void TryRegisterInspector()
        {
            if (_inspectorHandle != null || !InspectorHostClient.isAvailable)
            {
                return;
            }

            if (_inspectorDrawer == null)
            {
                _inspectorDrawer = new ModelInspectorDrawer();
            }

            _inspectorHandle = InspectorHostClient.Register(
                "ModItemExplorer",
                _inspectorDrawer.CanDraw,
                _inspectorDrawer.Draw);

            if (_inspectorHandle != null)
            {
                MTEUtils.LogDebug("SelfModelPlacer: InspectorHost へ登録しました");
            }
        }
```

`Update` (:490) の `TryRegisterSelectionHandler();` の直後に 1 行追加:

```csharp
            TryRegisterInspector();
```

**注意:** 既存の選択購読再試行が間隔制御 (`RETRY_INTERVAL_FRAMES` 相当) を持つ場合は、そのガードの内側に相乗りさせて呼び出し頻度を揃える (実装時に `TryRegisterSelectionHandler` の実装を確認)。

- [ ] **Step 3: csproj に追加、ビルド確認 (MTE)**

`<Compile Include="ModelPlacement\ModelGizmoManager.cs" />` 行の並びに `<Compile Include="ModelPlacement\ModelInspectorDrawer.cs" />` を追加。

Run: `cd /w/COM3D2_5/work/COM3D2.ModItemExplorer.Plugin && cmd //c debug.bat com3d25`
Expected: `ビルドに成功しました`

- [ ] **Step 4: Commit (MTE リポ、submodule bump 込み)**

```bash
cd /w/COM3D2_5/work/COM3D2.ModItemExplorer.Plugin
git add source/COM3D2.ModItemExplorer.Plugin/MTEUtils \
    source/COM3D2.ModItemExplorer.Plugin/COM3D2.ModItemExplorer.Plugin.csproj \
    source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/ModelInspectorDrawer.cs \
    source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/SelfModelPlacer.cs
git commit -m "feat(inspector): MTE 管理モデルの EW Inspector 委譲描画を追加"
```

---

### Task 9: 実機確認

**前提:** ゲーム起動中に MCP `com3d25-devbridge` で確認する。DLL の配置は debug.bat が行う (ゲーム再起動が必要)。

- [ ] **Step 1: 同期の確認 (Section 1)**

1. MTE でモデルを配置しモデル編集モードへ入る
2. MTE の操作ウィンドウで「回転」を押す → EW Inspector のギズモ行も「回転」になること
3. EW Inspector で「拡縮」を押す → MTE 側トグルとギズモが拡縮になること
4. EW 側で Z/X/C キー → MTE 側が追従すること。MTE 側の Z/X/C → EW 側が追従すること
5. MTE の Local/Global ボタン → EW 側表示が追従し、ギズモの軸がワールド軸に変わること (逆方向も)
6. MTE で「なし」→ MTE ギズモ非表示、EW 側ツールも「なし」になること

実機での状態読み出し例 (eval_csharp):

```csharp
COM3D2.EditorWindow.Plugin.GizmoRenderer.currentTool.ToString() + " / " +
COM3D2.EditorWindow.Plugin.GizmoRenderer.useLocalSpace
```

- [ ] **Step 2: リファクタの非破壊確認 (Section 2)**

1. EW Inspector: 通常オブジェクト選択時のギズモ行・位置/回転/拡縮行の表示と編集が従来通り動くこと (ドラッグラベル、数値入力、R ボタン、Local/Global で座標系表示が切り替わること)
2. MTE 操作ウィンドウ: 位置/回転/拡縮行 + XYZ連動 + アタッチ行が従来通り動くこと
3. MTE 操作ウィンドウ: 数値入力の幅が固定 62px → 動的計算 (残り幅/3、下限 40) に変わるため、最小ウィンドウ幅 (380) で行が折り返し・はみ出ししないこと

- [ ] **Step 3: Inspector 委譲の確認 (Section 3)**

1. MTE 管理モデルを選択 → EW Inspector に MTE の委譲描画 (ヘッダー + ギズモ行 + Transform 行 + アタッチ表示) が出ること
2. EW Inspector 側の Transform 編集がモデルに反映され、MTE 操作ウィンドウの数値とも一致すること
3. 非管理オブジェクト (メイド等) を選択 → 従来の EW Inspector 描画に戻ること
4. MTE プラグイン無効化 (isEnable = false) 中は委譲描画されず従来描画に戻ること (CanDraw のガードで抑止済みの確認)
5. 委譲描画中のドラッグラベル・数値入力のフォーカスが EW 側ウィンドウの入力と競合しないこと (EW と MTE で別 GUIView インスタンスが同一 OnGUI 内に同居するため)

- [ ] **Step 4: 問題があれば修正してコミット、なければ完了報告**

---

## レビュー却下メモ

- InspectorHostClient の Unregister 呼び出し経路が無い — 既存の GizmoHost 登録 (ModelGizmoManager) も明示解除せず「同名再登録で置換」の流儀で運用しており、それと統一するため見送り (ホスト側 Register が同名エントリを置換するのでリロード時のリークは起きない)

