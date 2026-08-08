# 自前モデル配置 残タスク（タスク3→1→2→4→5）実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 自前モデル配置機能に「カメラ前配置・Transform 数値編集・回転/拡縮ギズモ・表示トグル・プリセット保存/復元」を追加し、基本操作性を完成させる。

**Architecture:** 配置ロジックは `ModelPlacement/SelfModelPlacer.cs` に集約し、UI は `ModItemWindow.DrawModelInfo()` の情報ペインを拡張する（配置済み自前モデル選択時のみ Transform 編集行を表示、情報ペイン高さを動的化）。保存/復元は `ConfigManager` と同じ `XmlSerializer` パターンで独立 XML に永続化し、復元は既存の `CreateModel` 生成経路を再実行して Transform を適用する。

**Tech Stack:** .NET Framework 3.5 相当の C#（Unity 5.6 / UnityInjector プラグイン）、MSBuild（`build.bat`）、実機検証は MCP `com3d25-devbridge`。

## Global Constraints

- **テスト基盤なし**: 本リポジトリに単体テストは存在しない（ゲーム内実行前提）。各タスクの検証は「`build.bat debug com3d25` がエラー 0 で通ること」＋「ゲーム起動中なら `com3d25-devbridge` / 手動での実機確認」で行う。TDD のテストステップはビルド＋実機検証に置き換える。
- **ビルドコマンド**: `source\COM3D2.ModItemExplorer.Plugin\build.bat debug com3d25`（cmd で実行。`.env` に `COM3D2_DIR` / `COM3D25_DIR` が必要）。最終タスク完了時のみ `build.bat`（引数なし = Release/all）で 2.0 版もビルドが通ることを確認する。
- **コメント・ログは日本語**で書く（既存コードと同様）。
- Unity 型を devbridge の REPL で評価するときは完全修飾名（`UnityEngine.Vector3` 等）で書く。
- コミットメッセージは Conventional Commits 形式の日本語（例: `feat: 配置初期位置をカメラ前に変更`）。
- 既存の命名・スタイル（`_camelCase` フィールド、`MTEUtils.LogWarning/LogException`、try-catch でゲームを落とさない方針）に従う。

## 主要な既存コード（前提知識）

**注意: 作業ツリーには未コミットの変更があるため、行番号ではなくシンボル名で該当箇所を探すこと。**

- `SelfModelPlacer.CreateModel(fileName, group, visible)`（`ModelPlacement/SelfModelPlacer.cs:46`）: menu 解析 → `ModelMeshLoader.LoadMesh` → `ResolveGroup` で group 解決 → ラッパー GameObject 生成 → `AddGizmo` → `StudioModelStatWrapper` を `_models` に追加。生成した Unity オブジェクトは `_disposables` で管理。ラッパー GameObject（`wrapper.obj`）を動かすのが Transform 操作の正であり、モデル本体の Transform は触らない。
- `SelfModelPlacer.AddGizmo`（同ファイル:328）: **`GizmoRenderTarget` ではなく基底の `GizmoRender` を使っている**。派生側の `Update` が `new` で基底を隠蔽して `base.Update()` を呼ばず、ドラッグ判定フラグが立たないため（既存コメント参照）。この方針は維持すること。ギズモ倍率は定数 `GizmoScale`（0.25f）。
- `SelfModelPlacer.Owns(model)`: `pluginName == PluginName` で自前配置かを判定。
- `ModelPlacerManager`（`ModelPlacement/ModelPlacerManager.cs`）: MTE と自前配置のファサード。UI からは常にここを経由する。
- `ModItemWindow.DrawModelInfo()`（`ModItemWindow.cs:808`）: 情報ペイン（`_infoView`、高さ `INFO_HEIGHT`）に配置プラグイン選択＋「配置」ボタンを 1 行描画。
- `ModelMenuItem`（`ModItemBase.cs:272`）: 配置済みモデルのツリー項目。`model` プロパティに `StudioModelStatWrapper` を持つ（`ModItemManager.DelItem` の `modelItem?.model` 参照）。
- `GUIView.DrawFloatField(FloatFieldOption)`（`MTEUtils/GUIView.cs:1145`）: 数値入力部品。`FloatFieldOption` は `label / labelWidth / value / width / height / minValue / maxValue / fieldType / fieldCache / onChanged / onReset` を持ち、`onReset` を渡すと "R" ボタン付きになる。`view.GetFieldCache(label, value)` でキャッシュ取得。
- `GUIView.DrawToggle(label, value, width, height, onChanged)`（`MTEUtils/GUIView.cs:956`）。
- `ConfigManager.SaveConfigXml/LoadConfigXml`（`Manager/ConfigManager.cs`）: `XmlSerializer` + `FileStream` の保存パターン。プリセット保存はこれを踏襲する。
- `ModItemManager.OnChangedSceneLevel`（`Manager/ModItemManager.cs:2409`）: シーン遷移時に `modelHackManager.DeleteAllSelfModels()` で自前配置分を全破棄する（この仕様は維持する）。

---

### Task 1: 配置初期位置をカメラ前に（バックログ タスク3）

**Files:**
- Modify: `source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/SelfModelPlacer.cs`（`CreateModel` 内、`AddGizmo(wrapperGo)` の直前）

**Interfaces:**
- Consumes: `GameMain.Instance.MainCamera`（ゲーム本体）
- Produces: なし（`CreateModel` のシグネチャは変えない）

- [ ] **Step 1: カメラ前方の床上位置を計算する private メソッドを追加**

`SelfModelPlacer` に以下を追加する:

```csharp
/// <summary>
/// カメラ前方の床上（y=0）の配置初期位置を返す。原点固定だと画面外に配置されて見えないため
/// </summary>
private static Vector3 GetDefaultPosition()
{
    try
    {
        // CameraMain は MonoBehaviour なので transform を直接参照できる
        var camTransform = GameMain.Instance.MainCamera.transform;
        var forward = camTransform.forward;
        forward.y = 0f;
        forward = forward.sqrMagnitude > 0.001f ? forward.normalized : Vector3.forward;

        var pos = camTransform.position + forward * 1.5f;
        pos.y = 0f;
        return pos;
    }
    catch (Exception e)
    {
        MTEUtils.LogException(e);
        return Vector3.zero;
    }
}
```

- [ ] **Step 2: `CreateModel` でラッパーに初期位置を設定**

`wrapperGo.transform.SetParent(GetOrCreateParent().transform, false);` の直後に追加:

```csharp
wrapperGo.transform.position = GetDefaultPosition();
```

- [ ] **Step 3: ビルド確認**

Run: `source\COM3D2.ModItemExplorer.Plugin\build.bat debug com3d25`
Expected: `COM3D2.5 版をビルド中` の後、エラーなく終了（exit code 0）

- [ ] **Step 4: 実機確認（ゲーム起動中の場合）**

devbridge の `eval_csharp` で `GameMain.Instance.MainCamera.transform.position` が評価できることを確認。ゲーム内でモデルを配置し、カメラ前に出現することを目視確認。

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/SelfModelPlacer.cs
git commit -m "feat: 配置初期位置をカメラ前方の床上に変更"
```

---

### Task 2: Transform 数値入力 UI（バックログ タスク1）

**Files:**
- Modify: `source/COM3D2.ModItemExplorer.Plugin/ModItemWindow.cs`（`DrawModelInfo`、`InitView`、フィールド追加）

**Interfaces:**
- Consumes: `SelfModelPlacer.instance.Owns(StudioModelStatWrapper)`、`ModelMenuItem.model`、`GUIView.DrawFloatField(FloatFieldOption)`、`GUIView.GetFieldCache(string, float)`
- Produces: `private bool isSelfModelSelected { get; }`、`private StudioModelStatWrapper selectedSelfModel { get; }`、`private void DrawModelTransform(GUIView view)` — Task 3・4 がこのペインに UI を相乗りさせる。

- [ ] **Step 1: 選択中の自前配置モデルを返すプロパティを追加**

`ModItemWindow` に追加:

```csharp
private StudioModelStatWrapper selectedSelfModel
{
    get
    {
        var modelItem = selectedMenuItem as ModelMenuItem;
        var model = modelItem?.model;
        return SelfModelPlacer.instance.Owns(model) ? model : null;
    }
}

private bool isSelfModelSelected => selectedSelfModel != null;
```

※ `selectedMenuItem` の実際の型/名前は `DrawModelInfo` 内の既存参照（`modItemManager.CreateModel(selectedMenuItem, pluginName)`）に合わせる。

- [ ] **Step 2: 情報ペインの高さを動的化**

`INFO_HEIGHT` 定数の参照箇所を確認し、以下の方針で動的化する。追加行数はマジックナンバーの手動同期を避けるため 1 箇所の定数で管理する（Task 3・4 はこの定数だけを更新する）:

```csharp
// Transform 編集の追加行数（Task 2: 位置/回転/拡縮の3行。Task 3 でギズモトグル行 +1）
private const int ModelTransformRowCount = 3;
private const float ModelTransformRowHeight = 20;

// Transform 編集行を出すときだけ情報ペインを広げる
private float infoHeight => isSelfModelSelected
    ? INFO_HEIGHT + ModelTransformRowHeight * ModelTransformRowCount
    : INFO_HEIGHT;
```

`InitView()` 内の `INFO_HEIGHT` 参照を `infoHeight` に置き換える。高さ変化の検知は既存の `_windowHeight` / `_naviWidth` 変化検知と同じ `OnGUI()` 内（`ModItemWindow.cs` の `OnGUI` にあるサイズ変化 → `InitView()` 呼び直しブロック）に追加する（検知ロジックの置き場所を分散させない）:

```csharp
private float _lastInfoHeight = -1f;

// OnGUI() の既存サイズ変化検知ブロックに追加
if (infoHeight != _lastInfoHeight)
{
    _lastInfoHeight = infoHeight;
    InitView();
}
```

- [ ] **Step 3: Transform 編集 UI を実装**

`ModItemWindow` に追加する。**呼び出し位置に注意**: `DrawModelInfo()` には「MTE 未導入」「プラグイン未選択」等の早期 `return` が複数あり、その後段に置くと配置済みモデル選択中でも Transform UI が出ないケースが生じる。既存の 1 行目の描画を try-catch ごと `DrawModelPlacementRow(view)` に切り出し、`DrawModelInfo()` を次の形にする:

```csharp
private void DrawModelInfo()
{
    var view = _infoView;

    DrawModelPlacementRow(view); // 既存の配置プラグイン選択＋「配置」ボタン行（早期returnはこの中に閉じる）

    if (isSelfModelSelected)
    {
        DrawModelTransform(view);
    }
}
```

```csharp
/// <summary>
/// 選択中の自前配置モデルの位置・回転・拡縮を数値編集する行を描画
/// </summary>
private void DrawModelTransform(GUIView view)
{
    var model = selectedSelfModel;
    var go = model?.obj as GameObject;
    if (go == null)
    {
        return;
    }

    var transform = go.transform;

    DrawVector3Row(view, "位置", transform.position,
        v => transform.position = v,
        () => transform.position = Vector3.zero);

    DrawVector3Row(view, "回転", transform.eulerAngles,
        v => transform.eulerAngles = v,
        () => transform.rotation = Quaternion.identity);

    DrawVector3Row(view, "拡縮", transform.localScale,
        v => transform.localScale = v,
        () => transform.localScale = Vector3.one);
}

/// <summary>
/// ラベル + XYZ 数値入力 + リセットボタンの1行を描画
/// </summary>
private void DrawVector3Row(
    GUIView view,
    string label,
    Vector3 value,
    Action<Vector3> onChanged,
    Action onReset)
{
    view.BeginHorizontal();
    {
        view.DrawLabel(label, 40, 20);

        for (var i = 0; i < 3; i++)
        {
            var index = i;
            view.DrawFloatField(new GUIView.FloatFieldOption
            {
                label = null,
                value = value[index],
                width = 70,
                height = 20,
                fieldType = FloatFieldType.F3,
                fieldCache = view.GetFieldCache(label + "_" + index, value[index]),
                onChanged = newValue =>
                {
                    value[index] = newValue;
                    onChanged(value);
                },
            });
        }

        if (view.DrawButton("R", 20, 20))
        {
            onReset();
        }
    }
    view.EndLayout();
}
```

※ `FloatFieldOption` のフィールド名・`GetFieldCache` の使い方は `GUIView.cs:1145` 付近と既存呼び出し元（`CustomPartsWindow.cs` 等の `SliderOption` 利用箇所）を確認して合わせる。ラッパー越し（`model.obj`）の Transform を編集するため、モデル内部の Transform は壊れない。

- [ ] **Step 4: ビルド確認**

Run: `source\COM3D2.ModItemExplorer.Plugin\build.bat debug com3d25`
Expected: エラーなく終了

- [ ] **Step 5: 実機確認**

ゲーム内でモデルを配置 → Model ツリーで選択 → 情報ペインが広がり位置/回転/拡縮の数値行が出ること、数値入力とギズモ移動が相互に反映されること、R ボタンで初期値に戻ることを確認。非選択時・メイドアイテム選択時はペインが元の高さに戻ることも確認。

- [ ] **Step 6: コミット**

```bash
git add source/COM3D2.ModItemExplorer.Plugin/ModItemWindow.cs
git commit -m "feat: 配置モデルのTransform数値編集UIを追加"
```

---

### Task 3: 回転・拡縮ギズモの解禁（バックログ タスク2）

**Files:**
- Modify: `source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/SelfModelPlacer.cs`（ドラッグ種別の状態と `AddGizmo` の書き換え）
- Modify: `source/COM3D2.ModItemExplorer.Plugin/ModItemWindow.cs`（`DrawModelTransform` に排他トグルを追加）

**Interfaces:**
- Consumes: Task 2 の `DrawModelTransform(GUIView view)`、`GUIView.DrawToggle(string, bool, float, float, Action<bool>)`
- Produces: `SelfModelPlacer.GizmoDragType`（enum: `Move, Rotate, Scale`）、`SelfModelPlacer.dragType`（get/set プロパティ、set で全モデルのギズモに即時反映）

- [ ] **Step 1: SelfModelPlacer にドラッグ種別の状態を追加**

```csharp
public enum GizmoDragType
{
    Move,
    Rotate,
    Scale,
}

private GizmoDragType _dragType = GizmoDragType.Move;

/// <summary>
/// ギズモの操作種別。誤操作防止のため移動/回転/拡縮は排他で1つだけ有効にする
/// </summary>
public GizmoDragType dragType
{
    get => _dragType;
    set
    {
        if (_dragType == value)
        {
            return;
        }
        _dragType = value;
        ApplyDragType();
    }
}

/// <summary>
/// 配置済み全モデルのギズモに現在の操作種別を反映
/// </summary>
private void ApplyDragType()
{
    foreach (var model in modelList)
    {
        var go = model.obj as GameObject;
        var gizmo = go != null ? go.GetComponent<GizmoRender>() : null;
        if (gizmo == null)
        {
            continue;
        }
        gizmo.eAxis = _dragType == GizmoDragType.Move;
        gizmo.eRotate = _dragType == GizmoDragType.Rotate;
        gizmo.eScal = _dragType == GizmoDragType.Scale;
    }
}
```

**重要: コンポーネントは既存どおり基底の `GizmoRender` を使うこと。** `GizmoRenderTarget` は `Update` を `new` で隠蔽して `base.Update()` を呼ばないため、ドラッグ判定フラグ（`is_drag_` 等）が更新されず移動を含む全ドラッグ操作が機能しない（`AddGizmo` の既存コメントおよび `W:\COM3D2_5\work\Assembly-CSharp\GizmoRenderTarget.cs:35` で確認済みの既知の罠）。

- [ ] **Step 2: `AddGizmo` を現在の種別で初期化するよう変更（static → instance メソッド化）**

```csharp
/// <summary>
/// 操作ギズモを付ける。種別は dragType に従い排他。
/// GizmoRenderTarget ではなく基底の GizmoRender を使う。派生側の Update は new で基底を隠蔽していて
/// base.Update() を呼ばないため、ドラッグ判定フラグが立たず移動できない
/// </summary>
private void AddGizmo(GameObject target)
{
    var gizmo = target.AddComponent<GizmoRender>();
    gizmo.offsetScale = GizmoScale;
    gizmo.eAxis = _dragType == GizmoDragType.Move;
    gizmo.eRotate = _dragType == GizmoDragType.Rotate;
    gizmo.eScal = _dragType == GizmoDragType.Scale;
    gizmo.Visible = true;
}
```

既存コメントの「回転・拡縮は専用 UI が無く誤操作の戻し手段が無いため無効にしている」の一文のみ削除し（Task 2 でリセット手段が入ったため）、**GizmoRender を使う理由の記述は必ず残す**。

- [ ] **Step 3: 排他トグル UI を追加**

`DrawModelTransform` の先頭（位置行の前）に追加:

```csharp
var placer = SelfModelPlacer.instance;

view.BeginHorizontal();
{
    view.DrawLabel("ギズモ", 40, 20);
    view.DrawToggle("移動", placer.dragType == SelfModelPlacer.GizmoDragType.Move, 60, 20,
        _ => placer.dragType = SelfModelPlacer.GizmoDragType.Move);
    view.DrawToggle("回転", placer.dragType == SelfModelPlacer.GizmoDragType.Rotate, 60, 20,
        _ => placer.dragType = SelfModelPlacer.GizmoDragType.Rotate);
    view.DrawToggle("拡縮", placer.dragType == SelfModelPlacer.GizmoDragType.Scale, 60, 20,
        _ => placer.dragType = SelfModelPlacer.GizmoDragType.Scale);
}
view.EndLayout();
```

あわせて Task 2 の定数 `ModelTransformRowCount` を `3` → `4` に更新する（ギズモトグル行の追加分。行数管理はこの定数 1 箇所のみ）。

- [ ] **Step 4: ビルド確認**

Run: `source\COM3D2.ModItemExplorer.Plugin\build.bat debug com3d25`
Expected: エラーなく終了

- [ ] **Step 5: 実機確認**

トグル切替で既存配置モデル・新規配置モデルの両方のギズモが移動/回転/拡縮に切り替わること、回転・拡縮の誤操作を R ボタンで戻せることを確認。

- [ ] **Step 6: コミット**

```bash
git add source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/SelfModelPlacer.cs source/COM3D2.ModItemExplorer.Plugin/ModItemWindow.cs
git commit -m "feat: 回転・拡縮ギズモを解禁し操作種別の排他トグルを追加"
```

---

### Task 4: 表示/非表示トグル（バックログ タスク4）

**Files:**
- Modify: `source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/SelfModelPlacer.cs`（`SetVisible` 追加）
- Modify: `source/COM3D2.ModItemExplorer.Plugin/ModItemWindow.cs`（`DrawModelTransform` にトグル追加）

**Interfaces:**
- Consumes: Task 2 の `selectedSelfModel` / `DrawModelTransform`
- Produces: `SelfModelPlacer.SetVisible(StudioModelStatWrapper model, bool visible)` — Task 5 の復元処理も使用する。

- [ ] **Step 1: SelfModelPlacer に SetVisible を追加**

```csharp
/// <summary>
/// 配置モデルの表示状態を切り替える。自前配置分でなければ何もしない
/// （MTE 側モデルの visible は MTE の管轄のため触らない）
/// </summary>
public void SetVisible(StudioModelStatWrapper model, bool visible)
{
    if (!Owns(model))
    {
        return;
    }

    model.visible = visible;
    var go = model.obj as GameObject;
    if (go != null)
    {
        go.SetActive(visible);
    }
}
```

- [ ] **Step 2: UI に表示トグルを追加**

`DrawModelTransform` のギズモトグル行（Task 3）に追記（同じ行の右端に置き、行数は増やさない）:

```csharp
view.DrawToggle("表示", model.visible, 60, 20,
    v => placer.SetVisible(model, v));
```

※ `model` はメソッド冒頭で取得済みの `selectedSelfModel`。行に収まらない場合のみ独立行にし、その場合は `ModelTransformRowCount` を +1 する。

- [ ] **Step 3: ビルド確認**

Run: `source\COM3D2.ModItemExplorer.Plugin\build.bat debug com3d25`
Expected: エラーなく終了

- [ ] **Step 4: 実機確認**

トグル OFF でモデルが非表示（ギズモも消える）、ON で再表示されること、非表示中も削除・Transform 編集が機能することを確認。

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/SelfModelPlacer.cs source/COM3D2.ModItemExplorer.Plugin/ModItemWindow.cs
git commit -m "feat: 配置モデルの表示/非表示トグルを追加"
```

---

### Task 5: プリセット保存・復元（バックログ タスク5）

**設計判断（このタスクで確定させる方針）:** 自動復元ではなく**明示的なプリセット保存/読込**とする。理由: シーン遷移時の全破棄（`ModItemManager.OnChangedSceneLevel`）という現行仕様と衝突せず、意図しないシーンへの復元事故もない。まずは単一プリセット（`default.xml`）とし、複数プリセット管理はニーズが出てからバックログ化する（YAGNI）。

**Files:**
- Create: `source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/ModelPlacementPreset.cs`
- Modify: `source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/SelfModelPlacer.cs`（`CreateModel` の戻り値化、`SavePreset` / `LoadPreset`）
- Modify: `source/COM3D2.ModItemExplorer.Plugin/ModItemWindow.cs`（`DrawModelInfo` に保存/読込ボタン）
- Modify: `source/COM3D2.ModItemExplorer.Plugin/MTEUtils/PluginUtils.cs`（プリセット保存パスの追加。`ConfigPath` の定義箇所を確認して同じ流儀で追加）

**Interfaces:**
- Consumes: `SelfModelPlacer.CreateModel` / `SetVisible`（Task 4）、`ConfigManager` の XmlSerializer パターン、`PluginUtils.ConfigPath` と同階層のディレクトリ解決
- Produces: `SelfModelPlacer.SavePreset()` / `SelfModelPlacer.LoadPreset()`（戻り値 void、失敗はログのみ）、`ModelPlacementPreset` / `ModelPlacementPresetItem`（XML シリアライズ用 DTO）

- [ ] **Step 1: DTO を作成（新規ファイル）**

```csharp
using System.Collections.Generic;

namespace COM3D2.ModItemExplorer.Plugin
{
    /// <summary>
    /// 自前配置モデル1体分の保存データ。Transform は復元時にラッパー GameObject へ適用する
    /// </summary>
    public class ModelPlacementPresetItem
    {
        public string fileName;
        public int group;
        public bool visible = true;

        public float posX, posY, posZ;
        public float rotX, rotY, rotZ;
        public float sclX = 1f, sclY = 1f, sclZ = 1f;
    }

    public class ModelPlacementPreset
    {
        public int version = 1;
        public List<ModelPlacementPresetItem> items = new List<ModelPlacementPresetItem>();
    }
}
```

※ SceneCapture の保存キー不一致バグ（`ModelCastShadow` vs `CastShadow`）の轍を踏まないよう、保存・読込とも同一の DTO フィールドを XmlSerializer に任せ、手書きのキー文字列は使わない。

- [ ] **Step 2: `CreateModel` が生成した wrapper を返すよう変更**

`SelfModelPlacer.CreateModel` の戻り値を `void` → `StudioModelStatWrapper` にする。成功時は `wrapper` を、失敗時（menu 解析失敗・LoadMesh 失敗・例外）は `null` を返す。呼び出し元 `ModelPlacerManager.CreateModel` は戻り値を捨てるだけなので変更不要（コンパイルが通ることを確認）。

- [ ] **Step 3: プリセットパスを追加**

`PluginUtils` の `ConfigPath` 定義を確認し、同じ設定ディレクトリ配下で以下を追加:

```csharp
public static string ModelPresetPath
    => CombinePaths(/* ConfigPath と同じディレクトリ */, "ModelPlacementPreset.xml");
```

- [ ] **Step 4: SavePreset / LoadPreset を実装**

`SelfModelPlacer` に追加:

```csharp
/// <summary>
/// 自前配置分の配置内容をプリセット XML に保存する
/// </summary>
public void SavePreset()
{
    try
    {
        var preset = new ModelPlacementPreset();
        foreach (var model in modelList)
        {
            var go = model.obj as GameObject;
            if (go == null)
            {
                continue;
            }

            var t = go.transform;
            preset.items.Add(new ModelPlacementPresetItem
            {
                fileName = model.infoWrapper?.fileName,
                group = model.group,
                visible = model.visible,
                posX = t.position.x, posY = t.position.y, posZ = t.position.z,
                rotX = t.eulerAngles.x, rotY = t.eulerAngles.y, rotZ = t.eulerAngles.z,
                sclX = t.localScale.x, sclY = t.localScale.y, sclZ = t.localScale.z,
            });
        }

        var serializer = new XmlSerializer(typeof(ModelPlacementPreset));
        using (var stream = new FileStream(PluginUtils.ModelPresetPath, FileMode.Create))
        {
            serializer.Serialize(stream, preset);
        }
        MTEUtils.Log("配置プリセットを保存しました。{0}体", preset.items.Count);
    }
    catch (Exception e)
    {
        MTEUtils.LogWarning("配置プリセットの保存に失敗しました");
        MTEUtils.LogException(e);
    }
}

/// <summary>
/// プリセット XML から配置を復元する。既存の自前配置分は置き換える
/// </summary>
public void LoadPreset()
{
    try
    {
        var path = PluginUtils.ModelPresetPath;
        if (!File.Exists(path))
        {
            MTEUtils.LogWarning("配置プリセットがありません。{0}", path);
            return;
        }

        ModelPlacementPreset preset;
        var serializer = new XmlSerializer(typeof(ModelPlacementPreset));
        using (var stream = new FileStream(path, FileMode.Open))
        {
            preset = (ModelPlacementPreset)serializer.Deserialize(stream);
        }

        DeleteAll();

        var restored = 0;
        foreach (var item in preset.items)
        {
            // 保存時と同じ生成経路を再実行してから Transform を適用する
            var wrapper = CreateModel(item.fileName, item.group, item.visible);
            var go = wrapper?.obj as GameObject;
            if (go == null)
            {
                continue;
            }

            var t = go.transform;
            t.position = new Vector3(item.posX, item.posY, item.posZ);
            t.eulerAngles = new Vector3(item.rotX, item.rotY, item.rotZ);
            t.localScale = new Vector3(item.sclX, item.sclY, item.sclZ);
            restored++;
        }

        // 個別失敗はスキップされるため、実際に復元できた数を報告する
        MTEUtils.Log("配置プリセットを復元しました。{0}/{1}体", restored, preset.items.Count);
    }
    catch (Exception e)
    {
        MTEUtils.LogWarning("配置プリセットの復元に失敗しました");
        MTEUtils.LogException(e);
    }
}
```

`using System.Xml.Serialization;` / `using System.IO;` を必要に応じ追加。

※ 既知の制約（許容する）: `CreateModel` 内の `ResolveGroup` は呼び出し時点の `_models` から group を再計算するため、保存時の `group` は hint 扱いとなり、部分削除後に再保存したプリセット等では復元後の group（＝表示名の連番）が保存時と完全一致しない場合がある。表示名のズレのみで実害はないため対応しない。

- [ ] **Step 5: UI に保存/読込ボタンを追加**

`ModItemWindow.DrawModelInfo()` の 1 行目（「配置」ボタンの後）に追加:

```csharp
if (view.DrawButton("保存", 50, 20))
{
    SelfModelPlacer.instance.SavePreset();
}
if (view.DrawButton("読込", 50, 20))
{
    SelfModelPlacer.instance.LoadPreset();
    modItemManager.UpdateModelItems();
}
```

※ `UpdateModelItems` のアクセシビリティを確認し、private ならツリー更新の既存 public 経路（`ModItemManager.CreateModel` 末尾で呼んでいる同名メソッド）に合わせて public 化または既存の更新 API を使う。

- [ ] **Step 6: ビルド確認**

Run: `source\COM3D2.ModItemExplorer.Plugin\build.bat debug com3d25`
Expected: エラーなく終了

- [ ] **Step 7: 実機確認**

複数モデル配置 → 移動/回転/拡縮/非表示を設定 → 保存 → 全削除（またはシーン遷移）→ 読込で Transform・visible まで復元されること、Model ツリーに復元分が並ぶことを確認。

- [ ] **Step 8: 2.0 版を含む Release ビルド確認**

Run: `source\COM3D2.ModItemExplorer.Plugin\build.bat`
Expected: COM3D2 版・COM3D2.5 版ともエラーなく終了

- [ ] **Step 9: コミット**

```bash
git add source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/ModelPlacementPreset.cs source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/SelfModelPlacer.cs source/COM3D2.ModItemExplorer.Plugin/ModItemWindow.cs source/COM3D2.ModItemExplorer.Plugin/MTEUtils/PluginUtils.cs
git commit -m "feat: 配置モデルのプリセット保存・復元を追加"
```

---

## スコープ外（バックログ据え置き）

- タスク6: アタッチポイント / メイド追従（ニーズ待ち）
- タスク7: BGObject / MyRoomObject 配置、anime コマンド、影トグル（意図的な未対応のまま）
- 複数名前付きプリセット管理（Task 5 で単一プリセットに絞った分。ニーズが出たらバックログへ）

## レビュー却下メモ（plan-review 2026-08-08）

- LoadPreset の原子性・失敗時ロールバック — YAGNI として許容（個別失敗はスキップ、復元数をログで報告する対応のみ取り込み）
- 空配置での保存によるプリセット上書き（確認ダイアログ提案）— YAGNI として却下。単一プリセットで操作は明示的な「保存」ボタンのみのため誤操作リスクは小さい
