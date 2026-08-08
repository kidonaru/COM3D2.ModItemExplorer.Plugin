# モデル操作の改善 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 配置モデルの選択状態を `SelfModelPlacer` に一元化して 3D 上でも判別できるようにし、ギズモの表示条件と「なし」を追加し、操作ウィンドウの表示トグルと IMGUI ドラッグ中のギズモ横取り防止を実装する。

**Architecture:** 選択状態とハイライト（マテリアル `_Color` のフェード）は `SelfModelPlacer` が持ち、`ModelOperationWindow` は委譲プロパティで参照するだけにする。`SelfModelPlacer.isModelEditMode`（`ModelOperationWindow.Update()` が毎フレーム代入）がギズモの可視とハイライトの有効条件を兼ねる。ギズモ入力の抑止は既存の入力遮断（カメラ操作 / NGUI）と同じ場所 `WindowManager.UpdateInputBlock` に同じガード形式で追加する。

**Tech Stack:** C# (.NET Framework / Unity IMGUI)、BepInEx/UnityInjector プラグイン。COM3D2 / COM3D2.5 両対応（今回 `#if COM3D25` 分岐は不要）。

**Spec:** `docs/superpowers/specs/2026-08-08-model-operation-improvements-design.md`

## Global Constraints

- コメント・ログメッセージは日本語
- 自動テスト基盤なし。各タスクは リポジトリルートで `cmd /c debug.bat com3d25` を実行してビルド確認する。実機検証は MCP `com3d25-devbridge`（ゲーム起動中のみ）または画面での目視で行い、できない場合はビルド成功＋ユーザーへの動作確認依頼で代替する
- ビルド後の DLL 差し替えはゲーム起動中だとロックされて失敗する（`警告: COM3D2.5 へのデプロイに失敗しました` が出る）。実機で確認するときはゲームを終了させてからビルドし直す必要がある点に注意
- 変更対象は自前配置（`pluginName == SelfModelPlacer.PluginName`）のモデルのみ。MTE 側に配置したモデルには一切触らない
- コミットメッセージは既存に倣い `feat:` / `fix:` / `refactor:` プレフィックス＋日本語

## 主要な既存コード（前提知識）

- `SelfModelPlacer` (`source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/SelfModelPlacer.cs`): 自前配置の本体。`CreateModel(fileName, group, visible)` がラッパー GameObject（`GizmoRender` 付き）を作って `StudioModelStatWrapper` を返す。`dragType`（Move/Rotate/Scale 排他、setter で `ApplyDragType()`）、`Update()`（回転オイラー角の正規化を毎フレーム実行）、`GetEulerAngles` / `SetEulerAngles`、`Attach`、`Owns`、`SetVisible`、`DeleteModel`、`DeleteAll`、`SavePreset` / `LoadPreset` / `GetPresetNames` / `DeletePreset`、`modelList`（コピーを返す）
- `ModelOperationWindow` (`source/COM3D2.ModItemExplorer.Plugin/ModelOperationWindow.cs`): `IWindow` 実装。`Update()` で `isShowWnd` を編集モードから決め、`placer.Update()` も呼ぶ。タブ（操作 / プリセット）、`DrawModelList`（表示トグル・名前ラベル・削除）、`DrawGizmoRow`、`DrawTransform`、`DrawAttachRow`、`DrawPreset` を持つ。`selectedModel` プロパティを自前フィールドで保持している
- `ModItemWindow` (`source/COM3D2.ModItemExplorer.Plugin/ModItemWindow.cs`): メインウィンドウ。`isModelMode`（`_contentMode == ContentMode.モデル`）、情報ペインの `DrawModelPlacementRow()`（L822 付近、配置プラグインのコンボ＋「配置」ボタン）。静的アクセサ `modItemManager` / `windowManager` / `modelPlacerManager` が定義済み
- `WindowManager` (`source/COM3D2.ModItemExplorer.Plugin/Manager/WindowManager.cs`): 全ウィンドウの `Update` / `OnGUI` を回す。`UpdateInputBlock()` が「ウィンドウ上にカーソルがある間はゲーム側入力を止める」処理を持ち、`UpdateCameraControl` / `UpdateUIInput` は**自分が無効化したときだけ元に戻す**ガード形式で書かれている。`RestoreInputBlock()` はプラグイン無効化・シーン遷移時に呼ばれる。`modelOperationWindow` フィールドあり
- `GizmoRender` (ゲーム側 `W:\COM3D2_5\work\Assembly-CSharp\GizmoRender.cs`): `Visible`（false なら `OnRenderObject` が即 return し描画も操作も止まる）、`eAxis` / `eRotate` / `eScal`（3 つとも false なら何も描かれない）、`offsetScale`、`public static bool global_control_lock`（`beSelectedType == NONE` からの新規ハンドル選択を止める）。マウス押下判定は `NInput` と `UICamera.Raycast`（NGUI）だけを見るため IMGUI は考慮されない
- `GUIView` (`source/COM3D2.ModItemExplorer.Plugin/MTEUtils/GUIView.cs`): `DrawToggle(string label, bool value, float width, float height, Action<bool> onChanged)`、`DrawButton(string, float, float)`、`DrawLabel`、`DrawDragLabel` など
- 実機で確認済み（2026-08-08）: 配置モデルのシェーダは `CM3D2/Lighted` / `CM3D2/Lighted_Trans` で `_OutlineColor` / `_OutlineWidth` を**持たない**が `_Color` / `_ShadowColor` は持つ

## タスクの依存関係

Task 1 → Task 2 → Task 3 → Task 4 の順で同じメソッド（`SelfModelPlacer.selectedModel` の setter、`ModelOperationWindow.Update()` / `Close()`）を段階的に書き換える。順序を入れ替えると「置換前」コードが一致しなくなるため、番号順に実施すること。Task 5 は独立しており、いつ実施してもよい。

---

### Task 1: 選択状態を SelfModelPlacer へ移設し、配置時に自動選択する

**Files:**
- Modify: `source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/SelfModelPlacer.cs`
- Modify: `source/COM3D2.ModItemExplorer.Plugin/ModelOperationWindow.cs`

**Interfaces:**
- Produces: `SelfModelPlacer.selectedModel`（`StudioModelStatWrapper` 型の get/set プロパティ。破棄済み GameObject を持つ値は getter で `null` に戻る）。Task 3 がこの setter にハイライトの付け外しを差し込む
- Produces: `ModelOperationWindow.selectedModel` は `placer.selectedModel` への委譲プロパティになる（既存の呼び出し側は変更不要）

- [ ] **Step 1: SelfModelPlacer に selectedModel を追加**

`SelfModelPlacer.cs` の `dragType` プロパティ定義の直後に追加する。

```csharp
        private StudioModelStatWrapper _selectedModel = null;

        /// <summary>
        /// 操作対象として選択中のモデル。破棄済みなら null に戻す。
        /// UI 側（ModelOperationWindow）はこの値を参照するだけにして、選択の実体をここに一元化する
        /// </summary>
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
            set
            {
                if (selectedModel == value)
                {
                    return;
                }

                _selectedModel = value;
            }
        }
```

- [ ] **Step 2: 配置時に自動選択する**

`CreateModel()` の `_disposables[wrapper] = disposables;` の直後、`return wrapper;` の前に追加する。

```csharp
                // 配置直後は操作対象にする（3D 上のハイライトと一覧の選択表示を一致させる）
                selectedModel = wrapper;
```

- [ ] **Step 3: 削除・全削除・プリセット復元で選択を解除する**

`DeleteModel()` の `Owns(model)` ガードの直後（`try` の前）に追加する。GameObject を破棄する前に解除しておくこと（Task 3 でここからマテリアルの色を書き戻すため）。

```csharp
            if (_selectedModel == model)
            {
                selectedModel = null;
            }
```

`DeleteAll()` の先頭（`foreach` の前）に追加する。

```csharp
            selectedModel = null;
```

`LoadPreset()` の復元ループ（`foreach (var item in preset.items)`）の直後、`MTEUtils.Log(...)` の前に追加する。復元は `CreateModel` を繰り返すため、そのままだと最後の 1 体が選択された状態になる。

```csharp
                // 復元直後はどれも選択していない状態にする
                selectedModel = null;
```

- [ ] **Step 4: ModelOperationWindow の selectedModel を委譲に置き換える**

`ModelOperationWindow.cs` の以下のブロック（`_selectedModel` フィールドとプロパティ）を丸ごと置き換える。

置換前:

```csharp
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
```

置換後:

```csharp
        /// <summary>操作対象のモデル。実体は SelfModelPlacer が持つ</summary>
        public StudioModelStatWrapper selectedModel
        {
            get => placer.selectedModel;
            set => placer.selectedModel = value;
        }
```

- [ ] **Step 5: 重複した選択解除を取り除く**

placer 側で解除するようになったため、ウィンドウ側の解除は不要になる。

`DrawModelList()` の削除ボタンの中を次のように変える。

置換前:

```csharp
                    if (view.DrawButton("x", 20, ROW_HEIGHT))
                    {
                        placer.DeleteModel(model);
                        if (model == selectedModel)
                        {
                            selectedModel = null;
                        }
                        modItemManager.UpdateModelItems();
                    }
```

置換後:

```csharp
                    if (view.DrawButton("x", 20, ROW_HEIGHT))
                    {
                        // 選択の解除は SelfModelPlacer.DeleteModel が行う
                        placer.DeleteModel(model);
                        modItemManager.UpdateModelItems();
                    }
```

`DrawPreset()` の「読込」ボタンの中を次のように変える。

置換前:

```csharp
                    if (view.DrawButton("読込", 50, ROW_HEIGHT))
                    {
                        placer.LoadPreset(name);
                        modItemManager.UpdateModelItems();
                        selectedModel = null;
                    }
```

置換後:

```csharp
                    if (view.DrawButton("読込", 50, ROW_HEIGHT))
                    {
                        // 選択の解除は SelfModelPlacer.LoadPreset が行う
                        placer.LoadPreset(name);
                        modItemManager.UpdateModelItems();
                    }
```

- [ ] **Step 6: ビルドして通ることを確認**

Run: `cmd /c debug.bat com3d25`（リポジトリルート `W:\COM3D2_5\work\COM3D2.ModItemExplorer.Plugin` で実行）
Expected: `ビルドに成功しました`。コンパイルエラー・未使用フィールド警告が出ないこと

- [ ] **Step 7: 実機で確認（ゲーム起動中のみ／任意）**

モデルを 2 体配置し、操作ウィンドウの一覧で 2 体目が緑（選択中）になっていること、1 体目をクリックすると選択が移ること、選択中を削除すると選択が外れることを目視する。ゲーム未起動ならユーザーに確認を依頼してよい。

- [ ] **Step 8: コミット**

```bash
git add source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/SelfModelPlacer.cs source/COM3D2.ModItemExplorer.Plugin/ModelOperationWindow.cs
git commit -m "refactor: 配置モデルの選択状態をSelfModelPlacerへ移し配置時に自動選択する"
```

---

### Task 2: ギズモをモデル編集モード中のみ表示し、操作種別に「なし」を追加する

**Files:**
- Modify: `source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/SelfModelPlacer.cs`
- Modify: `source/COM3D2.ModItemExplorer.Plugin/ModelOperationWindow.cs`

**Interfaces:**
- Consumes: Task 1 の `SelfModelPlacer.selectedModel`、既存の `SelfModelPlacer.dragType`、`ModItemWindow.isModelMode`
- Produces: `SelfModelPlacer.GizmoDragType.None`（enum の新しい先頭要素）、`SelfModelPlacer.isModelEditMode`（bool の get/set プロパティ。setter で全ギズモへ即反映）。Task 3 がこのプロパティをハイライトの有効条件としても使う

**背景:** `GizmoRender.OnRenderObject` は `Visible == false` の場合に即 return するため、描画も操作判定も止まる。`eAxis` / `eRotate` / `eScal` が 3 つとも false でも何も描かれないが、`Visible` で切る方が判定ごと止まって確実。

- [ ] **Step 1: GizmoDragType に None を追加**

`SelfModelPlacer.cs` の enum を置き換える。

置換前:

```csharp
        /// <summary>ギズモの操作種別</summary>
        public enum GizmoDragType
        {
            Move,
            Rotate,
            Scale,
        }
```

置換後:

```csharp
        /// <summary>ギズモの操作種別。None はギズモ自体を隠す</summary>
        public enum GizmoDragType
        {
            None,
            Move,
            Rotate,
            Scale,
        }
```

`_dragType` の初期値は `GizmoDragType.Move` のまま変更しない（enum の先頭要素が変わるため、既定値を明示したままにしておくこと）。

- [ ] **Step 2: isModelEditMode プロパティを追加**

Task 1 Step 1 で追加した `selectedModel` プロパティの直後に追加する。

```csharp
        private bool _isModelEditMode = false;

        /// <summary>
        /// モデル編集モード中か。ModelOperationWindow が毎フレーム代入する。
        /// ギズモの表示条件であり、Task 3 で追加するハイライトの有効条件も兼ねる。
        /// 操作ウィンドウの開閉とは独立させる（閉じていてもギズモは操作できるべきなので）
        /// </summary>
        public bool isModelEditMode
        {
            get => _isModelEditMode;
            set
            {
                if (_isModelEditMode == value)
                {
                    return;
                }

                _isModelEditMode = value;
                ApplyDragType();
            }
        }
```

- [ ] **Step 3: ギズモの可視条件を ApplyDragType に集約**

`ApplyDragType(GizmoRender gizmo)` を置き換える。

置換前:

```csharp
        private void ApplyDragType(GizmoRender gizmo)
        {
            gizmo.eAxis = _dragType == GizmoDragType.Move;
            gizmo.eRotate = _dragType == GizmoDragType.Rotate;
            gizmo.eScal = _dragType == GizmoDragType.Scale;
        }
```

置換後:

```csharp
        private void ApplyDragType(GizmoRender gizmo)
        {
            gizmo.eAxis = _dragType == GizmoDragType.Move;
            gizmo.eRotate = _dragType == GizmoDragType.Rotate;
            gizmo.eScal = _dragType == GizmoDragType.Scale;

            // GizmoRender は Visible=false で描画も操作判定も止まる。
            // 「なし」と編集モード外はこれでまとめて切る
            gizmo.Visible = _isModelEditMode && _dragType != GizmoDragType.None;
        }
```

`AddGizmo(GameObject target)` から `gizmo.Visible = true;` の行を削除する。`ApplyDragType(gizmo)` が可視も決めるようになったため。置換後の `AddGizmo` 本体:

```csharp
            var gizmo = target.AddComponent<GizmoRender>();
            gizmo.offsetScale = GizmoScale;
            ApplyDragType(gizmo);
```

- [ ] **Step 4: 編集モードを毎フレーム placer へ流す**

`ModelOperationWindow.Update()` を置き換える。

置換前:

```csharp
            // 編集モードがモデルの間だけ表示する
            var showWnd = windowManager.modItemWindow != null
                && windowManager.modItemWindow.isModelMode;
```

置換後:

```csharp
            var isModelMode = windowManager.modItemWindow != null
                && windowManager.modItemWindow.isModelMode;

            // ギズモはウィンドウの開閉と独立。編集モード中は出しっぱなしにする
            placer.isModelEditMode = isModelMode;

            // 編集モードがモデルの間だけ表示する
            var showWnd = isModelMode;
```

- [ ] **Step 5: Close で編集モードを落とす**

`Close()` はプラグイン無効化時（`WindowManager.OnPluginDisable`）に呼ばれる。無効化後もギズモが出たままになるのを防ぐ。

置換前:

```csharp
        public void Close()
        {
            isShowWnd = false;
        }
```

置換後:

```csharp
        public void Close()
        {
            isShowWnd = false;

            // プラグイン無効化時にも呼ばれるため、ギズモをここで片付ける
            placer.isModelEditMode = false;
        }
```

- [ ] **Step 6: ギズモ行に「なし」を追加し、注記ラベルを削除**

`ModelOperationWindow.DrawGizmoRow()` の `BeginHorizontal` の中身を置き換える。

置換前:

```csharp
                view.DrawLabel("ギズモ", LABEL_WIDTH, ROW_HEIGHT, style: GUIView.gsLabelRight);

                view.DrawToggle("移動", placer.dragType == SelfModelPlacer.GizmoDragType.Move,
                    60, ROW_HEIGHT,
                    _ => placer.dragType = SelfModelPlacer.GizmoDragType.Move);
                view.DrawToggle("回転", placer.dragType == SelfModelPlacer.GizmoDragType.Rotate,
                    60, ROW_HEIGHT,
                    _ => placer.dragType = SelfModelPlacer.GizmoDragType.Rotate);
                view.DrawToggle("拡縮", placer.dragType == SelfModelPlacer.GizmoDragType.Scale,
                    60, ROW_HEIGHT,
                    _ => placer.dragType = SelfModelPlacer.GizmoDragType.Scale);

                // dragType は配置モデル全体に一括で効くことを明示する
                view.DrawLabel("(全モデル共通)", -1, ROW_HEIGHT, textColor: Color.gray);
```

置換後:

```csharp
                view.DrawLabel("ギズモ", LABEL_WIDTH, ROW_HEIGHT, style: GUIView.gsLabelRight);

                view.DrawToggle("なし", placer.dragType == SelfModelPlacer.GizmoDragType.None,
                    60, ROW_HEIGHT,
                    _ => placer.dragType = SelfModelPlacer.GizmoDragType.None);
                view.DrawToggle("移動", placer.dragType == SelfModelPlacer.GizmoDragType.Move,
                    60, ROW_HEIGHT,
                    _ => placer.dragType = SelfModelPlacer.GizmoDragType.Move);
                view.DrawToggle("回転", placer.dragType == SelfModelPlacer.GizmoDragType.Rotate,
                    60, ROW_HEIGHT,
                    _ => placer.dragType = SelfModelPlacer.GizmoDragType.Rotate);
                view.DrawToggle("拡縮", placer.dragType == SelfModelPlacer.GizmoDragType.Scale,
                    60, ROW_HEIGHT,
                    _ => placer.dragType = SelfModelPlacer.GizmoDragType.Scale);
```

ラベル幅は 70 + 60×4 = 310 でウィンドウ幅 380 に収まる。`ModelOperationWindow.cs` の `using UnityEngine;` は `Color.green` などで引き続き使うため残す。

- [ ] **Step 7: ビルドして通ることを確認**

Run: `cmd /c debug.bat com3d25`
Expected: `ビルドに成功しました`

- [ ] **Step 8: 実機で確認（ゲーム起動中のみ）**

1. モデル編集モードでモデルを配置 → 全配置モデルにギズモが出ること
2. 編集モードをメイド等へ切替 → ギズモが全部消えること。モデルへ戻すと再表示されること
3. ラジオで「なし」を選ぶ → ギズモが消え、3D をドラッグしてもモデルが動かないこと。「移動」に戻すと復帰すること
4. ラジオ行の右端に `(全モデル共通)` が表示されなくなっていること

- [ ] **Step 9: コミット**

```bash
git add source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/SelfModelPlacer.cs source/COM3D2.ModItemExplorer.Plugin/ModelOperationWindow.cs
git commit -m "feat: ギズモをモデル編集モード中のみ表示し操作種別になしを追加"
```

---

### Task 3: 選択中モデルを `_Color` のフェードでハイライトする

**Files:**
- Modify: `source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/SelfModelPlacer.cs`

**Interfaces:**
- Consumes: Task 1 の `SelfModelPlacer.selectedModel` setter、Task 2 の `SelfModelPlacer.isModelEditMode` setter、既存の `SelfModelPlacer.Update()`
- Produces: `SelfModelPlacer` の private メソッド `BeginHighlight(StudioModelStatWrapper)` / `EndHighlight()` / `RefreshHighlight()` / `UpdateHighlight()`。外部からは選択の切替と編集モードの切替だけでハイライトが付け外しされる

**背景:** 実機で確認したとおり配置モデルのシェーダ（`CM3D2/Lighted` / `CM3D2/Lighted_Trans`）にはアウトライン用プロパティが無く、`_Color` だけが共通して存在する。`ImportCM.LoadMaterial` はモデルごとに新しい `Material` インスタンスを作るため、色を書き換えても他モデルへは波及しない（`ModelMeshLoader.LoadMesh` / `ApplyMenuChanges` で確認済み）。

ハイライトは編集モード中に限る。ギズモは編集モード外で隠すのに `_Color` の明滅だけ続くと、メイド編集中などに配置モデルが緑に点滅し続けることになるため。選択自体は保持し、モードへ戻れば再開する。

- [ ] **Step 1: ハイライト用の定数と状態を追加**

`SelfModelPlacer.cs` のクラス先頭付近、`private const float DefaultDistance = 1.5f;` の直後に定数を追加する。

```csharp
        /// <summary>選択中モデルのハイライト色。元色との間を往復させる</summary>
        private static readonly Color HighlightColor = new Color(0.4f, 1f, 0.4f, 1f);

        /// <summary>ハイライトの明滅周期(秒)</summary>
        private const float HighlightCycle = 1.2f;
```

`_rotationCaches` の宣言の直後に、ハイライト対象の記録を追加する。

```csharp
        /// <summary>ハイライト中のマテリアルと、書き戻し用の元の色</summary>
        private class HighlightTarget
        {
            public Material material;
            public Color originalColor;
        }

        private readonly List<HighlightTarget> _highlightTargets = new List<HighlightTarget>();
```

- [ ] **Step 2: ハイライトの開始・解除・再評価・更新を実装**

Task 2 Step 2 で追加した `isModelEditMode` プロパティの直後に 4 つのメソッドを追加する。

```csharp
        /// <summary>
        /// 選択モデル配下の _Color を持つマテリアルを記録する。
        /// マテリアルはモデルごとに生成されるため、書き換えても他モデルには波及しない
        /// </summary>
        private void BeginHighlight(StudioModelStatWrapper model)
        {
            var go = model?.obj as GameObject;
            if (go == null)
            {
                return;
            }

            foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
            {
                // materials は複製を作ってしまうため sharedMaterials を使う
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material == null || !material.HasProperty("_Color"))
                    {
                        continue;
                    }

                    _highlightTargets.Add(new HighlightTarget
                    {
                        material = material,
                        originalColor = material.GetColor("_Color"),
                    });
                }
            }
        }

        /// <summary>
        /// 記録した元の色を書き戻す。モデル破棄が先行してマテリアルが消えている場合はスキップする
        /// </summary>
        private void EndHighlight()
        {
            foreach (var target in _highlightTargets)
            {
                if (target.material != null)
                {
                    target.material.SetColor("_Color", target.originalColor);
                }
            }

            _highlightTargets.Clear();
        }

        /// <summary>
        /// 選択状態と編集モードからハイライト対象を取り直す。
        /// 旧対象の色は必ず書き戻してから張り直すので、解除漏れの経路を作らない
        /// </summary>
        private void RefreshHighlight()
        {
            EndHighlight();

            if (_isModelEditMode)
            {
                BeginHighlight(selectedModel);
            }
        }

        /// <summary>
        /// ハイライト色を毎フレーム更新する。
        /// アルファは元の値のままにする（Lighted_Trans で透明度が明滅するのを避けるため）
        /// </summary>
        private void UpdateHighlight()
        {
            if (_highlightTargets.Count == 0)
            {
                return;
            }

            var t = (Mathf.Sin(Time.time * Mathf.PI * 2f / HighlightCycle) + 1f) * 0.5f;

            foreach (var target in _highlightTargets)
            {
                if (target.material == null)
                {
                    continue;
                }

                var color = Color.Lerp(target.originalColor, HighlightColor, t);
                color.a = target.originalColor.a;
                target.material.SetColor("_Color", color);
            }
        }
```

- [ ] **Step 3: 選択の切替でハイライトを取り直す**

Task 1 Step 1 で追加した `selectedModel` の setter を次のように変える。

置換前:

```csharp
            set
            {
                if (selectedModel == value)
                {
                    return;
                }

                _selectedModel = value;
            }
```

置換後:

```csharp
            set
            {
                if (selectedModel == value)
                {
                    return;
                }

                _selectedModel = value;
                RefreshHighlight();
            }
```

- [ ] **Step 4: 編集モードの切替でもハイライトを取り直す**

Task 2 Step 2 で追加した `isModelEditMode` の setter を次のように変える。

置換前:

```csharp
                _isModelEditMode = value;
                ApplyDragType();
```

置換後:

```csharp
                _isModelEditMode = value;
                ApplyDragType();
                RefreshHighlight();
```

- [ ] **Step 5: Update でハイライトを更新する**

`Update()` の末尾（`foreach` ループの閉じ括弧の後）に追加する。

```csharp
            UpdateHighlight();
```

- [ ] **Step 6: ビルドして通ることを確認**

Run: `cmd /c debug.bat com3d25`
Expected: `ビルドに成功しました`

- [ ] **Step 7: 実機で確認（ゲーム起動中のみ）**

1. モデルを配置し、3D 上でそのモデルの色が緑寄りに周期的にフェードすること
2. 2 体目を配置すると 1 体目の色が元に戻り、2 体目がフェードすること
3. 一覧で 1 体目をクリックするとハイライトが戻ること
4. 選択中モデルを削除しても他モデルの色が変わっていないこと
5. プリセット読込後はどのモデルもフェードしていないこと
6. 編集モードをメイド等へ切替 → 明滅が止まり元の色に戻ること。モデルへ戻すと再開し、選択も保たれていること

`devbridge` で色を数値確認する場合の例（`_Color` が元に戻っているかの確認）:

```csharp
var parent = GameObject.Find("ModItemExplorer Model Parent");
var sb = new System.Text.StringBuilder();
foreach (var smr in parent.GetComponentsInChildren<SkinnedMeshRenderer>(true)) {
  foreach (var m in smr.sharedMaterials) {
    if (m == null || !m.HasProperty("_Color")) continue;
    sb.AppendLine(smr.name + " / " + m.name + " = " + m.GetColor("_Color").ToString());
  }
}
sb.ToString()
```

- [ ] **Step 8: コミット**

```bash
git add source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/SelfModelPlacer.cs
git commit -m "feat: 選択中の配置モデルをマテリアル色のフェードでハイライトする"
```

---

### Task 4: 「操作」ボタンで操作ウィンドウの表示をトグルする

**Files:**
- Modify: `source/COM3D2.ModItemExplorer.Plugin/ModelOperationWindow.cs`
- Modify: `source/COM3D2.ModItemExplorer.Plugin/ModItemWindow.cs`（`DrawModelPlacementRow`、L822 付近）

**Interfaces:**
- Consumes: Task 2 で `Update()` に導入した `isModelMode` ローカル変数、Task 2 で書き換えた `Close()`、`WindowManager.modelOperationWindow`
- Produces: `ModelOperationWindow.ToggleVisible()`（引数なし・戻り値なし）

- [ ] **Step 1: 表示トグルのフィールドとメソッドを追加**

`ModelOperationWindow.cs` の `_tabType` フィールドの直後に追加する。設定ファイルには保存しない（セッション内のみ保持）。

```csharp
        /// <summary>ユーザーによる表示切替。設定には保存せずセッション内のみ保持する</summary>
        private bool _userVisible = true;

        /// <summary>「操作」ボタンから呼ぶ表示トグル</summary>
        public void ToggleVisible()
        {
            _userVisible = !_userVisible;
        }
```

- [ ] **Step 2: 表示判定にトグルを反映**

Task 2 の Step 4 で書き換えた `Update()` の該当行を置き換える。

置換前:

```csharp
            // 編集モードがモデルの間だけ表示する
            var showWnd = isModelMode;
```

置換後:

```csharp
            // 編集モードがモデルで、かつユーザーが閉じていないときだけ表示する
            var showWnd = isModelMode && _userVisible;
```

- [ ] **Step 3: Close でトグルも落とす**

Task 2 の Step 5 で書き換えた `Close()` に 1 行足す。落とさないと次フレームの `Update()` で再表示されてしまう。

置換前:

```csharp
        public void Close()
        {
            isShowWnd = false;

            // プラグイン無効化時にも呼ばれるため、ギズモをここで片付ける
            placer.isModelEditMode = false;
        }
```

置換後:

```csharp
        public void Close()
        {
            isShowWnd = false;
            _userVisible = false;

            // プラグイン無効化時にも呼ばれるため、ギズモとハイライトをここで片付ける
            placer.isModelEditMode = false;
        }
```

（`isModelEditMode = false` は Task 3 の `RefreshHighlight()` を通じてハイライトも解除する）

- [ ] **Step 4: 「操作」ボタンを配置行に追加**

`ModItemWindow.DrawModelPlacementRow()` の `try` ブロック内、プラグイン選択の分岐の直後に追加する。配置プラグインが未選択でもウィンドウの開閉はできるべきなので、分岐の外に置く。

置換前:

```csharp
                if (pluginName == null)
                {
                    view.DrawLabel("プラグインを選択してください", 200, 20, textColor: Color.yellow);
                }
                else if (view.DrawButton("配置", 50, 20))
                {
                    modItemManager.CreateModel(selectedMenuItem, pluginName);
                }
```

置換後:

```csharp
                if (pluginName == null)
                {
                    view.DrawLabel("プラグインを選択してください", 200, 20, textColor: Color.yellow);
                }
                else if (view.DrawButton("配置", 50, 20))
                {
                    modItemManager.CreateModel(selectedMenuItem, pluginName);
                }

                // プラグイン未選択でもウィンドウは開閉できるようにする
                if (view.DrawButton("操作", 50, 20))
                {
                    windowManager.modelOperationWindow.ToggleVisible();
                }
```

`DrawModelPlacementRow` のコメント（`/// 配置プラグインの選択・「配置」ボタン・プリセット保存/読込ボタンの行を描画`）は実態に合わせて次に書き換える。

```csharp
        /// <summary>
        /// 配置プラグインの選択・「配置」ボタン・操作ウィンドウの開閉ボタンの行を描画
        /// </summary>
```

- [ ] **Step 5: ビルドして通ることを確認**

Run: `cmd /c debug.bat com3d25`
Expected: `ビルドに成功しました`

- [ ] **Step 6: 実機で確認（ゲーム起動中のみ）**

1. モデル編集モードで「操作」ボタンを押す → 操作ウィンドウが閉じる。もう一度押すと開く
2. 閉じている間もギズモが表示され、3D 上でモデルを動かせること（回転操作後に開き直して数値が飛んでいないこと＝`placer.Update()` が回り続けていること）
3. 閉じている間もハイライトが継続すること（編集モード自体は続いているため）
4. 編集モードをメイド等へ切替 → ウィンドウが隠れる。モデルへ戻すと直前の開閉状態が保たれること

- [ ] **Step 7: コミット**

```bash
git add source/COM3D2.ModItemExplorer.Plugin/ModelOperationWindow.cs source/COM3D2.ModItemExplorer.Plugin/ModItemWindow.cs
git commit -m "feat: 配置行の操作ボタンでモデル操作ウィンドウを開閉できるようにする"
```

---

### Task 5: IMGUI ドラッグ中にギズモへ操作が奪われないようにする

**Files:**
- Modify: `source/COM3D2.ModItemExplorer.Plugin/Manager/WindowManager.cs`（`UpdateInputBlock` / `RestoreInputBlock` 付近、L116-199）

**Interfaces:**
- Consumes: `GizmoRender.global_control_lock`（ゲーム側の public static）、`GUIUtility.hotControl`
- Produces: `WindowManager` の private メソッド `UpdateGizmoLock(bool shouldLock)`

**背景:** `GizmoRender.Update()` はマウス押下フレームに `UICamera.Raycast`（NGUI）だけを見て `is_drag_` を決める。IMGUI は NGUI ではないため、プラグインのウィンドウ上で押下しても `is_drag_ = true` になり、その後 `OnRenderObject()` でカーソルがハンドルに重なった時点で操作を奪われる。`global_control_lock` は `beSelectedType == MOVETYPE.NONE` からの新規ハンドル選択を止めるので、これを立てておけば横取りを防げる。

**副作用として許容する範囲（レビュー指摘を受けての明示）:** `global_control_lock` は `GizmoRender` の静的フィールドで、派生クラス（MTE が使う `GizmoRenderTarget` 等）を含むシーン内の全ギズモに効く。`GUIUtility.hotControl` はこのプラグインのウィンドウに限らず立つため、他ウィンドウ・他プラグインの IMGUI ドラッグ中も一時的に MTE 側ギズモの新規ハンドル選択が止まる。止まるのは新規選択だけで進行中のドラッグは継続し、マウスを離せば即解除されるため一過性と判断して許容する。`isMouseOverWindow` との AND で絞る案は採らない — ドラッグ中にカーソルがウィンドウ外（＝ギズモ上）へ出た瞬間にロックが外れ、防ごうとしている横取りがそのまま再発するため。

- [ ] **Step 1: ロック状態のフィールドを追加**

`WindowManager.cs` の `_isUIInputDisabled` フィールドの隣に追加する（同ファイル内で `_isCameraControlDisabled` / `_isUIInputDisabled` が宣言されている場所）。

```csharp
        private bool _isGizmoLocked = false;
```

- [ ] **Step 2: UpdateInputBlock からロック更新を呼ぶ**

置換前:

```csharp
            UpdateCameraControl(isMouseOverWindow);
            UpdateUIInput(isMouseOverWindow || isExternalUIInputBlocked);
```

置換後:

```csharp
            UpdateCameraControl(isMouseOverWindow);
            UpdateUIInput(isMouseOverWindow || isExternalUIInputBlocked);
            UpdateGizmoLock(GUIUtility.hotControl != 0);
```

- [ ] **Step 3: UpdateGizmoLock を実装**

`UpdateUIInput` メソッドの直後に追加する。既存の `UpdateUIInput` と同じ「自分が立てたときだけ倒す」ガード形式にそろえる。

```csharp
        /// <summary>
        /// IMGUI が何らかのコントロールでマウスを掴んでいる間はギズモのハンドル選択を止める。
        /// GizmoRender は押下時に NGUI のヒット判定しか見ないため、
        /// ドラッグラベルの操作中でもカーソルがハンドルに重なった瞬間に操作を奪ってしまう。
        /// カーソルがウィンドウ外へ出てもロックを維持する必要があるため、
        /// カメラ操作・UI 入力と違い isMouseOverWindow では絞らない。
        /// global_control_lock はゲーム本体も使う共有フラグなので、
        /// 他と同様に「自分が立てたときだけ倒す」ガードを入れている
        /// </summary>
        private void UpdateGizmoLock(bool shouldLock)
        {
            if (shouldLock)
            {
                if (_isGizmoLocked || !GizmoRender.global_control_lock)
                {
                    GizmoRender.global_control_lock = true;
                    _isGizmoLocked = true;
                }
            }
            else if (_isGizmoLocked)
            {
                GizmoRender.global_control_lock = false;
                _isGizmoLocked = false;
            }
        }
```

- [ ] **Step 4: RestoreInputBlock で解除する**

`RestoreInputBlock()` の末尾（`_isUIInputDisabled` の復帰処理の後）に追加する。

```csharp
            if (_isGizmoLocked)
            {
                _isGizmoLocked = false;
                GizmoRender.global_control_lock = false;
            }
```

- [ ] **Step 5: ビルドして通ることを確認**

Run: `cmd /c debug.bat com3d25`
Expected: `ビルドに成功しました`

- [ ] **Step 6: 実機で確認（ゲーム起動中のみ）**

1. 操作ウィンドウを 3D 上のモデル・ギズモに重なる位置へ動かす
2. 位置 X のドラッグラベルを掴み、カーソルがギズモのハンドルを横切るように長く左右へドラッグする → X の数値だけが変わり、モデルが横取りされて動かないこと
3. ドラッグを離したあと 3D 上のギズモを直接ドラッグする → 従来どおり動くこと（ロックが解除されている）
4. ウィンドウのタイトルバーをドラッグしてギズモの上を通す → モデルが動かないこと

`devbridge` でロック状態を確認する例:

```csharp
GizmoRender.global_control_lock
```

- [ ] **Step 7: コミット**

```bash
git add source/COM3D2.ModItemExplorer.Plugin/Manager/WindowManager.cs
git commit -m "fix: IMGUIドラッグ中にギズモへ操作が奪われないようロックする"
```

---

## 完了後の確認

全タスク完了後、仕様書「テスト」節の 12 項目を通しで実機確認する。特に以下は複数タスクにまたがるため最後にまとめて見る。

- シーン遷移（`ModItemManager.OnChangedSceneLevel` → `DeleteAllSelfModels` → `DeleteAll`）でハイライトが残らないこと
- プラグイン無効化（`WindowManager.OnPluginDisable` → `Close()` / `RestoreInputBlock()`）でハイライト・ギズモ・ギズモロックがすべて解除されること
- COM3D2 版もビルドが通ること: `cmd /c debug.bat`（引数なしで両バージョン）

## レビュー却下メモ

- `UpdateGizmoLock` の呼び出し条件に `isMouseOverWindow` を足してロック範囲をこのプラグインの UI 操作中に絞る案 — 却下。ドラッグ中にカーソルがウィンドウ外（ギズモ上）へ出た瞬間にロックが外れ、本来防ぎたい横取りが再発する。副作用（他プラグインの IMGUI ドラッグ中も MTE 側ギズモの新規選択が止まる）は一過性・自己解除のため、絞り込みではなく Task 5 の背景節への明記で対応した
