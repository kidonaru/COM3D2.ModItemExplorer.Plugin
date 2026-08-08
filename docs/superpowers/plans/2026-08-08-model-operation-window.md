# モデル操作ウィンドウ Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 配置モデルの操作を独立した「モデル操作ウィンドウ」に集約し、回転のローカルオイラー統一・アタッチポイント・名前付き複数プリセットを実装する。

**Architecture:** 既存の `IWindow` パターン(`MotionWindow` 参照)で `ModelOperationWindow` を新設し `WindowManager` に登録。モデル操作ロジックは `SelfModelPlacer` に集約(アタッチ・複数プリセット)。UI 部品は `GUIView` に `DrawDragLabel` を追加。

**Tech Stack:** C# (.NET Framework / Unity IMGUI)、BepInEx/UnityInjector プラグイン。COM3D2 / COM3D2.5 両対応(`#if COM3D25` 分岐は今回不要のはず)。

**Spec:** `docs/superpowers/specs/2026-08-08-model-operation-window-design.md`

## Global Constraints

- コメント・ログメッセージは日本語
- 自動テスト基盤なし。各タスクは `cmd /c debug.bat com3d25`(リポジトリルートで実行)でビルド確認し、実機検証は MCP `com3d25-devbridge` で行う(ゲーム起動中のみ)。実機検証ができない場合はビルド成功+ユーザーへの動作確認依頼で代替
- ドラッグ感度: 位置 0.01m/px、回転 1°/px、拡縮 0.01/px。Shift 押下中 0.1 倍
- 回転・位置・拡縮は UI・プリセットとも local 系(`localPosition` / `localEulerAngles` / `localScale`)に統一
- コミットメッセージは既存に倣い `feat:` / `refactor:` プレフィックス+日本語

## 主要な既存コード(前提知識)

- `SelfModelPlacer` (`source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/SelfModelPlacer.cs`): 自前配置の本体。`CreateModel(fileName, group, visible)` がラッパー GO(`GizmoRender` 付き)を作る。`dragType`(Move/Rotate/Scale 排他)、`SavePreset()` / `LoadPreset()`(単一 XML)、`Owns(model)`、`SetVisible(model, visible)`、`DeleteModel(model)`、`modelList`(コピーを返す)
- `ModelPlacementPreset` / `ModelPlacementPresetItem` (`ModelPlacement/ModelPlacementPreset.cs`): XML シリアライズ用 DTO。item は fileName/group/visible/posX..sclZ
- `ModItemWindow` (`ModItemWindow.cs`): 情報ペインに `DrawModelInfo()` → `DrawModelPlacementRow()`(配置プラグイン選択+配置+プリセット保存/読込)と `DrawModelTransform()`(ギズモトグル+XYZ数値行、`MODEL_TRANSFORM_ROW_COUNT` で情報ペイン高さを拡張)がある。`selectedSelfModel` / `isSelfModelSelected` プロパティあり。`_contentMode`(enum `ContentMode`、`メイド`/`モデル`/`設定` 等)
- `WindowManager` (`Manager/WindowManager.cs`): `Init()` で各ウィンドウを `AddWindow()`。`IWindow` インターフェース(windowIndex/isShowWnd/windowRect/Init/Update/Close/OnLoad/OnScreenSizeChanged/OnChangedSceneLevel/OnGUI)
- `MotionWindow` (`MotionWindow.cs`): 独立ウィンドウの雛形。`GUI.Window(WINDOW_ID, ...)`、`_rootView`/`_headerView`/`_contentView` の GUIView 構成、`config.motionWindowPosX/Y` への位置保存
- `GUIView` (`MTEUtils/GUIView.cs`): IMGUI ラッパー。`DrawLabel`/`DrawButton`/`DrawToggle`/`DrawFloatField(FloatFieldOption)`/`GetFieldCache(label, FloatFieldType)`/`GetTransformCache(transform)`/`BeginHorizontal`/`BeginScrollView` 等
- `TransformCache` (`MTEUtils/TransformCache.cs`): localPosition/localEulerAngles/localScale を保持し、`Update(transform)` はクォータニオン比較でオイラー角の飛びを抑制、`Apply()` で書き戻す
- `GUIComboBox<T>` (`MTEUtils/GUIComboBox.cs`): `items`/`getName`/`currentIndex`/`currentItem`/`onSelected`/`DrawButton(label, view)`。描画側ウィンドウで `view.DrawComboBox()`(ルート)呼び出しが必要
- `PluginUtils` (`PluginUtils.cs`): `UserDataPath`(Config フォルダ)、`ModelPresetPath`(旧単一プリセット)、`PluginConfigDirPath`(`Config/COM3D2.ModItemExplorer.Plugin/`、無ければ作成)
- ビルド: リポジトリルートで `cmd /c debug.bat com3d25`(COM3D2.5 のみ)。両対応確認は `cmd /c debug.bat`

---

### Task 1: Transform のローカル系統一と TransformCache 化(回転バグ修正)

**Files:**
- Modify: `source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/SelfModelPlacer.cs`(SavePreset/LoadPreset の Transform 読み書き)
- Modify: `source/COM3D2.ModItemExplorer.Plugin/ModItemWindow.cs`(`DrawModelTransform` を TransformCache ベースに)

**Interfaces:**
- Consumes: `GUIView.GetTransformCache(Transform)`, `TransformCache.eulerAngles/position/scale/Apply()`
- Produces: プリセット XML の pos/rot/scl がローカル値になる(Task 4 のフォーマット前提)

**原因の裏取り済み(2026-08-08):** `W:\COM3D2_5\work\Assembly-CSharp\GizmoRender.cs` を確認した結果、回転ハンドルは `transform.RotateAround(transform.up / forward / right, angle)` による**自身のローカル軸回転**で、ワールド軸ではない。よって「X/Y 操作で全成分が変動」はオイラー分解表示の問題(既存 UI が毎フレーム `transform.eulerAngles` を生読みしているため)。GizmoRender 自体の補正は不要で、UI 側の TransformCache 化+ローカル系統一で対処する。ギズモドラッグ中の表示は分解値がそのまま出る(操作を離した後の数値編集が安定していればよい)。ドラッグ中の表示まで軸単位に安定させるのはスコープ外。

- [ ] **Step 0: 実機での症状再確認(ゲーム起動中のみ、任意)**

devbridge でモデルを1体配置し、回転ギズモ X/Y ハンドル操作中/操作後の `localEulerAngles` の変化を観察して上記の裏取りと一致するか確認する。ゲーム未起動ならスキップしてよい(静的裏取り済みのため)。

- [ ] **Step 1: SavePreset/LoadPreset をローカル系に変更**

`SavePreset()` 内(現在 `t.position` / `t.eulerAngles` / `t.localScale`):

```csharp
posX = t.localPosition.x, posY = t.localPosition.y, posZ = t.localPosition.z,
rotX = t.localEulerAngles.x, rotY = t.localEulerAngles.y, rotZ = t.localEulerAngles.z,
sclX = t.localScale.x, sclY = t.localScale.y, sclZ = t.localScale.z,
```

`LoadPreset()` 内も対応:

```csharp
t.localPosition = new Vector3(item.posX, item.posY, item.posZ);
t.localEulerAngles = new Vector3(item.rotX, item.rotY, item.rotZ);
t.localScale = new Vector3(item.sclX, item.sclY, item.sclZ);
```

(ラッパーの親 `ParentObjectName` GO は原点固定なので既存プリセットとも実質互換)

- [ ] **Step 2: ModItemWindow.DrawModelTransform を TransformCache ベースに変更**

`DrawModelTransform` の Vector3 行呼び出し(943〜953 行付近)を差し替え:

```csharp
var cache = view.GetTransformCache(transform);

DrawVector3Row(view, "位置", cache.position,
    value => { cache.position = value; cache.Apply(); },
    () => { cache.position = Vector3.zero; cache.Apply(); });

// eulerAngles は TransformCache がクォータニオン比較で外部変更を検出するため、
// ギズモ回転中でも編集中の値が飛ばない
DrawVector3Row(view, "回転", cache.eulerAngles,
    value => { cache.eulerAngles = value; cache.Apply(); },
    () => { cache.eulerAngles = Vector3.zero; cache.Apply(); });

DrawVector3Row(view, "拡縮", cache.scale,
    value => { cache.scale = value; cache.Apply(); },
    () => { cache.scale = Vector3.one; cache.Apply(); });
```

- [ ] **Step 3: ビルド**

Run: `cmd /c debug.bat com3d25`
Expected: `ビルドに成功しました`

- [ ] **Step 4: 実機検証(ゲーム起動中のみ)**

devbridge の `eval_csharp` でモデルを1体配置し、回転ギズモの X/Y ハンドル操作後に UI の回転数値が該当軸中心に変化すること、数値入力が飛ばないことを確認。起動していなければユーザーに確認を依頼。

- [ ] **Step 5: Commit**

```bash
git add -A source/
git commit -m "fix: 配置モデルのTransformをローカル系に統一し回転数値の飛びを修正"
```

---

### Task 2: ModelOperationWindow 新設+ドラッグラベル+旧 UI 撤去

**Files:**
- Create: `source/COM3D2.ModItemExplorer.Plugin/ModelOperationWindow.cs`
- Modify: `source/COM3D2.ModItemExplorer.Plugin/MTEUtils/GUIView.cs`(`DrawDragLabel` 追加)
- Modify: `source/COM3D2.ModItemExplorer.Plugin/Manager/WindowManager.cs`(登録)
- Modify: `source/COM3D2.ModItemExplorer.Plugin/ModItemWindow.cs`(Transform 行撤去、モデルモード公開プロパティ追加)
- Modify: `source/COM3D2.ModItemExplorer.Plugin/Manager/ConfigManager.cs` 相当の Config クラス(ウィンドウ位置保存。`motionWindowPosX/Y` と同じパターンで `modelOperationWindowPosX/Y` を追加)

**Interfaces:**
- Consumes: `SelfModelPlacer.instance.modelList / dragType / SetVisible / DeleteModel`, `GUIView` 各種, `WindowManager.windows`
- Produces:
  - `public class ModelOperationWindow : IWindow` — `public StudioModelStatWrapper selectedModel { get; set; }`(Task 3/4 の UI が使う)
  - `ModItemWindow.isModelMode`(`public bool isModelMode => _contentMode == ContentMode.モデル;`)
  - `GUIView.DrawDragLabel(string text, float width, float height, float sensitivity, Action<float> onDelta)`

- [ ] **Step 1: GUIView に DrawDragLabel を追加**

`DrawLabel` の近くに追加。ドラッグ状態はコントロール ID で管理し、ドラッグ中はウィンドウドラッグを抑止するため Event を Use する:

```csharp
/// <summary>
/// 左右ドラッグで数値を増減できるラベル。1pxあたり sensitivity、Shift押下中は0.1倍
/// </summary>
public void DrawDragLabel(
    string text,
    float width,
    float height,
    float sensitivity,
    Action<float> onDelta,
    GUIStyle style = null)
{
    var drawRect = GetDrawRect(width, height);
    var controlId = GUIUtility.GetControlID(FocusType.Passive);

    GUI.Label(drawRect, text, style ?? gsLabel);
    this.NextElement(drawRect);

    var e = Event.current;
    switch (e.GetTypeForControl(controlId))
    {
        case EventType.MouseDown:
            if (e.button == 0 && drawRect.Contains(e.mousePosition))
            {
                GUIUtility.hotControl = controlId;
                e.Use();
            }
            break;
        case EventType.MouseDrag:
            if (GUIUtility.hotControl == controlId)
            {
                var scale = e.shift ? 0.1f : 1f;
                if (e.delta.x != 0f)
                {
                    onDelta(e.delta.x * sensitivity * scale);
                }
                e.Use();
            }
            break;
        case EventType.MouseUp:
            if (GUIUtility.hotControl == controlId)
            {
                GUIUtility.hotControl = 0;
                e.Use();
            }
            break;
    }
}
```

- [ ] **Step 2: ModItemWindow に isModelMode を公開**

```csharp
/// <summary>モデル操作ウィンドウの表示条件。編集モードがモデルのときのみ</summary>
public bool isModelMode => _contentMode == ContentMode.モデル;
```

- [ ] **Step 3: ModelOperationWindow を新規作成**

`MotionWindow.cs` を雛形に作成。骨子:

```csharp
using System;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace COM3D2.ModItemExplorer.Plugin
{
    /// <summary>
    /// 配置モデルの操作ウィンドウ。編集モードがモデルのときのみ表示される
    /// </summary>
    public class ModelOperationWindow : IWindow
    {
        public readonly static int WINDOW_ID = 582880;
        public readonly static int WINDOW_WIDTH = 340;
        public readonly static int WINDOW_HEIGHT = 480;
        public readonly static int HEADER_HEIGHT = 20;
        public readonly static int MODEL_LIST_HEIGHT = 140;

        private static WindowManager windowManager => WindowManager.instance;
        private static SelfModelPlacer placer => SelfModelPlacer.instance;
        private static Config config => ConfigManager.instance.config;

        public int windowIndex { get; set; }
        public bool isShowWnd { get; set; }

        private Rect _windowRect;
        public Rect windowRect
        {
            get => _windowRect;
            set => _windowRect = value;
        }

        /// <summary>操作対象のモデル。破棄済みなら null に戻す</summary>
        private StudioModelStatWrapper _selectedModel;
        public StudioModelStatWrapper selectedModel
        {
            get
            {
                var go = _selectedModel?.obj as GameObject;
                if (go == null)
                {
                    _selectedModel = null;
                }
                return _selectedModel;
            }
            set => _selectedModel = value;
        }

        private GUIView _rootView = new GUIView();
        private GUIView _headerView = new GUIView();
        private GUIView _contentView = new GUIView();
        private bool _initializedGUI = false;

        public GUIStyle gsWin => GUIView.gsWin;

        public ModelOperationWindow()
        {
            this.windowIndex = 0;
            this.isShowWnd = false;
            this.windowRect = new Rect(
                Screen.width - WINDOW_WIDTH - 30,
                Screen.height - WINDOW_HEIGHT - 100,
                WINDOW_WIDTH,
                WINDOW_HEIGHT);
        }

        public void Init() { }

        public void Update()
        {
            // 編集モードがモデルの間だけ表示する
            isShowWnd = windowManager.modItemWindow != null
                && windowManager.modItemWindow.isModelMode;
        }

        public void Close() => isShowWnd = false;
        public void OnLoad() => MTEUtils.AdjustWindowPosition(ref _windowRect);
        public void OnScreenSizeChanged() => MTEUtils.AdjustWindowPosition(ref _windowRect);
        public void OnChangedSceneLevel(Scene scene, LoadSceneMode sceneMode) { }

        // InitGUI/OnGUI/DrawWindow は MotionWindow と同構成
        // (config.modelOperationWindowPosX/Y に位置を保存)
    }
}
```

`DrawWindow` の中身(`_contentView` に描画):

1. **モデルリスト** (`BeginScrollView`、高さ `MODEL_LIST_HEIGHT`): `placer.modelList` を列挙し、各行:
   - `DrawToggle("", model.visible, 20, 20, v => placer.SetVisible(model, v))`
   - `DrawToggle(model.displayName, model == selectedModel, 240, 20, _ => selectedModel = model)`(選択トグルとして使用)
   - `DrawButton("x", 20, 20)` → `placer.DeleteModel(model); if (model == selectedModel) selectedModel = null;` 削除後は `ModItemManager.instance.UpdateModelItems()` を呼ぶ
2. **操作種別**: 既存 `DrawModelTransform` のギズモトグル 3 つをそのまま移植。`dragType` は全配置モデル共通のグローバル状態のため、行頭ラベルを「ギズモ(全モデル共通)」とし選択モデルだけに効くという誤解を防ぐ
3. **Transform 編集**: `selectedModel` 非 null のとき、Task 1 の TransformCache 方式で 3 行描画。ただし各行の X/Y/Z ラベルを `DrawDragLabel` にする。1 行の構成(位置の例):

```csharp
private static readonly string[] AxisNames = { "X", "Y", "Z" };

private void DrawVector3Row(
    GUIView view,
    string label,
    float dragSensitivity,
    Vector3 value,
    Action<Vector3> onChanged,
    Action onReset)
{
    view.BeginHorizontal();
    {
        view.DrawLabel(label, 40, 20, style: GUIView.gsLabelRight);

        for (var i = 0; i < 3; i++)
        {
            var index = i;

            view.DrawDragLabel(AxisNames[index], 14, 20, dragSensitivity, delta =>
            {
                value[index] += delta;
                onChanged(value);
            });

            var fieldCache = view.GetFieldCache(label + index, FloatFieldType.F3);
            fieldCache.UpdateValue(value[index]);

            view.DrawFloatField(new GUIView.FloatFieldOption
            {
                value = value[index],
                width = 62,
                height = 20,
                fieldCache = fieldCache,
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

呼び出し感度: 位置 `0.01f`、回転 `1f`、拡縮 `0.01f`。

- [ ] **Step 4: WindowManager に登録**

フィールド `public ModelOperationWindow modelOperationWindow = null;` を追加し、`Init()` 末尾に:

```csharp
modelOperationWindow = new ModelOperationWindow();
AddWindow(modelOperationWindow);
```

- [ ] **Step 5: Config にウィンドウ位置フィールドを追加**

`motionWindowPosX/Y` の定義箇所と同じパターンで `modelOperationWindowPosX = -1` / `modelOperationWindowPosY = -1` を追加。

- [ ] **Step 6: ModItemWindow から旧 Transform UI を撤去**

- `DrawModelInfo()` から `DrawModelTransform` 呼び出しと `isSelfModelSelected` 分岐を削除(`DrawModelPlacementRow` は残す。プリセットボタンは Task 4 で移設)
- `DrawModelTransform` / `DrawVector3Row` メソッド、`MODEL_TRANSFORM_ROW_COUNT` / `MODEL_TRANSFORM_ROW_HEIGHT` 定数、`infoHeight` の拡張分岐(`isSelfModelSelected` 参照)を削除し、`infoHeight` は `INFO_HEIGHT` 固定に戻す
- `selectedSelfModel` / `isSelfModelSelected` が他で未使用になったら削除(モデル選択は ModelOperationWindow 側に一本化)

- [ ] **Step 7: ビルド**

Run: `cmd /c debug.bat com3d25`
Expected: `ビルドに成功しました`

- [ ] **Step 8: 実機検証**

編集モードをモデルに切替→ウィンドウ自動表示、モデル選択/表示切替/削除、ドラッグラベルでの数値変更(Shift 微調整含む)、ウィンドウがドラッグ中に動かないことを確認。

- [ ] **Step 9: Commit**

```bash
git add -A source/
git commit -m "feat: モデル操作ウィンドウを新設しTransform編集を移設"
```

---

### Task 3: アタッチポイント

**Files:**
- Modify: `source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/SelfModelPlacer.cs`(アタッチ API と状態管理)
- Modify: `source/COM3D2.ModItemExplorer.Plugin/ModelOperationWindow.cs`(アタッチ UI)

**Interfaces:**
- Consumes: `MTEUtils.GetMaid(slotNo)` 相当のメイド取得(既存コードの `modItemManager.currentMaid` パターンを確認して合わせる)、`maid.body0.GetBone(string)`
- Produces(SelfModelPlacer):
  - `public class AttachPoint { public string displayName; public string boneName; }`
  - `public static readonly List<AttachPoint> AttachPoints`(先頭は `displayName="なし", boneName=null`)
  - `public void Attach(StudioModelStatWrapper model, Maid maid, AttachPoint point)` — ラッパー GO を `SetParent(bone, false)`。`point.boneName == null` なら `GetOrCreateParent()` 配下へ戻す(ローカル Transform は維持)
  - `public AttachState GetAttachState(StudioModelStatWrapper model)` — `public class AttachState { public int maidSlotNo; public string boneName; }`、未アタッチは null

- [ ] **Step 1: AttachPoint 定義と Attach/GetAttachState を SelfModelPlacer に実装**

```csharp
/// <summary>アタッチ先ボーンの定義</summary>
public class AttachPoint
{
    public string displayName;
    public string boneName; // null = なし(ワールド)
}

/// <summary>スタジオ準拠の定番アタッチポイント。ボーン名は実機で裏取り済みのものを使う</summary>
public static readonly List<AttachPoint> AttachPoints = new List<AttachPoint>
{
    new AttachPoint { displayName = "なし", boneName = null },
    new AttachPoint { displayName = "頭", boneName = "Bip01 Head" },
    new AttachPoint { displayName = "首", boneName = "Bip01 Neck" },
    new AttachPoint { displayName = "胸", boneName = "Bip01 Spine1a" },
    new AttachPoint { displayName = "骨盤", boneName = "Bip01 Pelvis" },
    new AttachPoint { displayName = "左肩", boneName = "Bip01 L UpperArm" },
    new AttachPoint { displayName = "右肩", boneName = "Bip01 R UpperArm" },
    new AttachPoint { displayName = "左肘", boneName = "Bip01 L Forearm" },
    new AttachPoint { displayName = "右肘", boneName = "Bip01 R Forearm" },
    new AttachPoint { displayName = "左手", boneName = "Bip01 L Hand" },
    new AttachPoint { displayName = "右手", boneName = "Bip01 R Hand" },
    new AttachPoint { displayName = "左腿", boneName = "Bip01 L Thigh" },
    new AttachPoint { displayName = "右腿", boneName = "Bip01 R Thigh" },
    new AttachPoint { displayName = "左膝", boneName = "Bip01 L Calf" },
    new AttachPoint { displayName = "右膝", boneName = "Bip01 R Calf" },
    new AttachPoint { displayName = "左足", boneName = "Bip01 L Foot" },
    new AttachPoint { displayName = "右足", boneName = "Bip01 R Foot" },
};
```

**注意:** 実装前に devbridge で `maid.body0.GetBone("Bip01 Head")` 等が非 null を返すか必ず裏取りし、返らない名前はリストから修正する(COM3D2.5 の CRC ボディでは `maid.body0.GetBone` がリネーム済みボーンを解決するかも確認)。

状態管理とアタッチ処理:

```csharp
public class AttachState
{
    public int maidSlotNo;
    public string boneName;
}

private Dictionary<StudioModelStatWrapper, AttachState> _attachStates
    = new Dictionary<StudioModelStatWrapper, AttachState>();

public AttachState GetAttachState(StudioModelStatWrapper model)
{
    AttachState state;
    return _attachStates.TryGetValue(model, out state) ? state : null;
}

/// <summary>
/// モデルをメイドのボーンにアタッチする。point.boneName が null ならワールドに戻す。
/// 切替時はローカルTransformをリセットしてボーン直上に置く
/// </summary>
public void Attach(StudioModelStatWrapper model, Maid maid, AttachPoint point)
{
    if (!Owns(model))
    {
        return;
    }

    var go = model.obj as GameObject;
    if (go == null)
    {
        return;
    }

    Transform parent = null;
    if (point != null && point.boneName != null)
    {
        var bone = maid != null ? maid.body0.GetBone(point.boneName) : null;
        if (bone == null)
        {
            MTEUtils.LogWarning("アタッチ先ボーンが見つかりません。{0}", point.boneName);
            return;
        }
        parent = bone;
        _attachStates[model] = new AttachState
        {
            // Maid に GetMaidSlotNo() は存在しない。ActiveSlotNo を使う
            // (CharacterMgr.SwapActiveSlot の実装から存在確認済み。
            //  CharacterMgr.GetMaid(int) と対応するかは実装時に devbridge で裏取りし、
            //  対応しなければ GetMaidArray() の index 検索に置き換える)
            maidSlotNo = maid.ActiveSlotNo,
            boneName = point.boneName,
        };
    }
    else
    {
        parent = GetOrCreateParent().transform;
        _attachStates.Remove(model);
    }

    go.transform.SetParent(parent, false);
    go.transform.localPosition = Vector3.zero;
    go.transform.localRotation = Quaternion.identity;
    // 拡縮はアタッチ後も見た目を保ちたいため維持する
}
```

`DeleteModel` / `DeleteAll` で `_attachStates` からも除去すること。

アタッチ先メイドは `modItemManager.currentMaid` のみ対象とする(ModItemManager は単一 currentMaid しか保持しておらず、メイド選択 UI の新設はスコープ外。spec にも明記済み)。

- [ ] **Step 2: ModelOperationWindow にアタッチ UI を追加**

Transform 編集の下に 1 行。`GUIComboBox<AttachPoint>` をフィールドに持つ:

```csharp
private GUIComboBox<SelfModelPlacer.AttachPoint> _attachPointComboBox
    = new GUIComboBox<SelfModelPlacer.AttachPoint>
{
    items = SelfModelPlacer.AttachPoints,
    getName = (point, _) => point.displayName,
    buttonSize = new Vector2(100, 20),
};
```

描画(選択メイドは `modItemManager.currentMaid` を使う。複数メイド対応が既存 UI にあれば同じコンボを流用):

```csharp
view.BeginHorizontal();
{
    view.DrawLabel("アタッチ", 60, 20, style: GUIView.gsLabelRight);

    // 現在のアタッチ状態をコンボに反映
    var state = placer.GetAttachState(selectedModel);
    _attachPointComboBox.currentIndex = Mathf.Max(0,
        SelfModelPlacer.AttachPoints.FindIndex(
            p => p.boneName == state?.boneName));

    _attachPointComboBox.onSelected = (point, _) =>
        placer.Attach(selectedModel, modItemManager.currentMaid, point);
    _attachPointComboBox.DrawButton(view);
}
view.EndLayout();
```

`DrawWindow` 末尾で `_rootView.DrawComboBox();` を呼ぶこと(MotionWindow と同様。忘れるとコンボ内容が描画されない)。

- [ ] **Step 3: ビルド**

Run: `cmd /c debug.bat com3d25`
Expected: `ビルドに成功しました`

- [ ] **Step 4: 実機検証**

ボーン名の裏取り(Step 1 注意書き)→ アタッチ→ボーン直上に移動すること、メイドのポーズ変更に追従すること、「なし」でワールドに戻ること、アタッチ中でも Transform 数値編集(ローカル)が効くことを確認。

- [ ] **Step 5: Commit**

```bash
git add -A source/
git commit -m "feat: 配置モデルのアタッチポイントを追加"
```

---

### Task 4: 名前付き複数プリセット

**Files:**
- Modify: `source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/ModelPlacementPreset.cs`(アタッチ情報追加)
- Modify: `source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/SelfModelPlacer.cs`(名前付き保存/読込/削除/一覧)
- Modify: `source/COM3D2.ModItemExplorer.Plugin/PluginUtils.cs`(`ModelPresetDirPath` 追加、`ModelPresetPath` 削除)
- Modify: `source/COM3D2.ModItemExplorer.Plugin/ModelOperationWindow.cs`(プリセット UI)
- Modify: `source/COM3D2.ModItemExplorer.Plugin/ModItemWindow.cs`(`DrawModelPlacementRow` から旧プリセットボタン撤去)

**Interfaces:**
- Consumes: Task 3 の `AttachState` / `Attach` / `AttachPoints`
- Produces(SelfModelPlacer):
  - `public void SavePreset(string name)`
  - `public bool LoadPreset(string name)`(成功可否を返す)
  - `public void DeletePreset(string name)`
  - `public List<string> GetPresetNames()`(拡張子なしのファイル名一覧、名前順)

- [ ] **Step 1: ModelPlacementPresetItem にアタッチ情報を追加**

```csharp
/// <summary>アタッチ先メイドのスロット番号。-1 は未アタッチ</summary>
public int attachMaidSlotNo = -1;

/// <summary>アタッチ先ボーン名。null/空 は未アタッチ</summary>
public string attachBoneName = null;
```

- [ ] **Step 2: PluginUtils にプリセットフォルダを追加**

`ModelPresetPath` プロパティを削除し、代わりに:

```csharp
public static string ModelPresetDirPath
{
    get
    {
        var path = MTEUtils.CombinePaths(PluginConfigDirPath, "ModelPresets");
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
        return path;
    }
}
```

- [ ] **Step 3: SelfModelPlacer のプリセット API を名前付きに変更**

既存 `SavePreset()` / `LoadPreset()` を改名・拡張:

```csharp
private static string GetPresetPath(string name)
    => MTEUtils.CombinePaths(PluginUtils.ModelPresetDirPath, name + ".xml");

/// <summary>ファイル名に使えない文字を除去する</summary>
private static string SanitizePresetName(string name)
{
    foreach (var c in Path.GetInvalidFileNameChars())
    {
        name = name.Replace(c.ToString(), "");
    }
    return name.Trim();
}

public List<string> GetPresetNames()
{
    try
    {
        var names = new List<string>();
        foreach (var path in Directory.GetFiles(PluginUtils.ModelPresetDirPath, "*.xml"))
        {
            names.Add(Path.GetFileNameWithoutExtension(path));
        }
        names.Sort();
        return names;
    }
    catch (Exception e)
    {
        MTEUtils.LogException(e);
        return new List<string>();
    }
}

public void DeletePreset(string name)
{
    try
    {
        var path = GetPresetPath(SanitizePresetName(name));
        if (File.Exists(path))
        {
            File.Delete(path);
            MTEUtils.Log("配置プリセットを削除しました。{0}", name);
        }
    }
    catch (Exception e)
    {
        MTEUtils.LogWarning("配置プリセットの削除に失敗しました");
        MTEUtils.LogException(e);
    }
}
```

`SavePreset(string name)`: 既存処理をベースに、保存前に `SanitizePresetName`。空文字なら警告して return。書き込み途中の例外で既存ファイルが破損しないよう、一時ファイル(`path + ".tmp"`)に書いてから `File.Delete(path)` → `File.Move(tmp, path)` で置き換える。item 生成時に Task 3 の状態を反映:

```csharp
var attach = GetAttachState(model);
// item 初期化子に追加:
attachMaidSlotNo = attach != null ? attach.maidSlotNo : -1,
attachBoneName = attach?.boneName,
```

`LoadPreset(string name)`: 既存処理をベースに、復元後に再アタッチ:

```csharp
if (item.attachMaidSlotNo >= 0 && !string.IsNullOrEmpty(item.attachBoneName))
{
    var maid = GameMain.Instance.CharacterMgr.GetMaid(item.attachMaidSlotNo);
    var point = AttachPoints.Find(p => p.boneName == item.attachBoneName);
    if (maid != null && point != null)
    {
        Attach(wrapper, maid, point);
    }
    // メイド不在時はワールド配置のままフォールバック
}
// アタッチの後にローカル Transform を適用する(Attach はローカルをリセットするため順序必須)
t.localPosition = new Vector3(item.posX, item.posY, item.posZ);
...
```

- [ ] **Step 4: ModelOperationWindow にプリセット UI を追加**

ウィンドウ最下部に 2 行:

```csharp
private string _presetName = "";
private GUIComboBox<string> _presetComboBox = new GUIComboBox<string>
{
    getName = (name, _) => name,
    buttonSize = new Vector2(140, 20),
};

// 行1: 名前入力 + 保存
view.BeginHorizontal();
{
    view.DrawLabel("プリセット", 60, 20, style: GUIView.gsLabelRight);
    view.DrawTextField(new GUIView.TextFieldOption
    {
        value = _presetName,
        width = 140,
        hiddenButton = true,
        onChanged = value => _presetName = value,
    });
    if (view.DrawButton("保存", 50, 20, enabled: _presetName.Trim().Length > 0))
    {
        placer.SavePreset(_presetName);
    }
}
view.EndLayout();

// 行2: 一覧 + 読込/削除
view.BeginHorizontal();
{
    view.AddSpace(60);
    _presetComboBox.items = placer.GetPresetNames();
    _presetComboBox.DrawButton(view);

    var current = _presetComboBox.currentItem;
    if (view.DrawButton("読込", 50, 20, enabled: current != null))
    {
        placer.LoadPreset(current);
        ModItemManager.instance.UpdateModelItems();
        selectedModel = null;
    }
    if (view.DrawButton("削除", 50, 20, enabled: current != null))
    {
        placer.DeletePreset(current);
        // 削除した項目を指したままにならないよう先頭に戻す
        _presetComboBox.currentIndex = 0;
    }
}
view.EndLayout();
```

(`DrawTextField` / `DrawButton` の enabled 引数は既存シグネチャを確認し、無ければ `BeginEnabled`/`EndEnabled` で囲む)

- [ ] **Step 5: ModItemWindow の旧プリセットボタンを撤去**

`DrawModelPlacementRow` から「プリセット」ラベル+保存/読込ボタン(888〜899 行付近)を削除。

- [ ] **Step 6: ビルド(両ターゲット)**

Run: `cmd /c debug.bat`
Expected: `ビルドに成功しました`(COM3D2 / COM3D2.5 両方)

- [ ] **Step 7: 実機検証**

複数モデル(1体はアタッチ)を配置→名前を付けて保存→別名でもう1つ保存→一覧に両方出る→読込でアタッチ含め復元→削除で消えることを確認。

- [ ] **Step 8: Commit**

```bash
git add -A source/
git commit -m "feat: 配置モデルの名前付き複数プリセットに対応"
```

---

## レビュー却下メモ

plan-review (2026-08-08) の指摘のうち却下したもの:

- 「GizmoRender がワールド軸回転の場合の代替実装が未計画(🔴・確信度中)」 — Assembly-CSharp の GizmoRender.cs を精読し、回転は `RotateAround(transform.up/forward/right)` のローカル軸回転であることを確認。ワールド軸補正は不要と判明したため代替実装は用意しない(診断 Step 0 と裏取り結果は Task 1 に反映済み)
- 「TransformCache のインデックス割当が呼び出し順序依存(確信度中)」 — Transform 編集パネルは選択モデル1件分のみ描画されるため実害の可能性が低い。未確認のまま見送り(実機検証時に数値が他モデルと混ざる症状が出たら対処)
- 「プリセット読込時のメイドスロット入れ替わりで別メイドにアタッチされる可能性」 — プリセットはシーン構成が変われば完全一致しないのが前提の機能であり、スロット番号ベースの復元+不在時ワールドフォールバックで十分。未確認のまま見送り
