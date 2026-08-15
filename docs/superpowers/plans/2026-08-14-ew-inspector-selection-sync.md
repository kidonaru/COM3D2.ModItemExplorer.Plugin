# モデル選択の EW Inspector 連携 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** ModItemExplorer (MTE) でモデルを選択したとき、EditorWindow (EW) の Inspector に選択状態として表示させる。逆に EW 側でモデルをクリック選択したら MTE 側の選択も追従させる。

**Architecture:** EW の `SelectionManager.Select` に `showGizmo` 引数付きオーバーロードと抑止フラグ `gizmoSuppressed` を追加し、`GizmoRenderer` はギズモ対象の決定時のみこのフラグを見る（選択バウンズ・ライトギズモは従来通り）。MTE 側は既存のリフレクションブリッジパターン（`GizmoHostClient` と同型）で `SelectionClient` を MTEUtils に新設し、`SelfModelPlacer.selectedModel` setter から `Select(go, showGizmo: false)` を呼ぶ。ギズモは常に MTE の `ModelGizmoManager` 側だけを使い、EW 側ギズモとの二重化を抑止フラグで防ぐ。逆同期は `onSelectionChanged` 購読で行い、無限ループは両者の「同値なら no-op」で自然終息させる（明示ガード不要、根拠は Task 4 参照）。

**Tech Stack:** C# (.NET 3.5 相当 / Unity プラグイン)、リフレクションブリッジ、MSBuild (`debug.bat`)

**Spec:** 本計画のみ（会話ベースで設計確定済み）。関連背景: `docs/superpowers/plans/2026-08-13-shared-gizmo.md`（EW の externalTargetProvider・GizmoHost 構成）

## Global Constraints

- 思考は英語、回答・コードコメント・ログメッセージは日本語
- EW が不在・旧バージョンの環境では従来動作を完全に維持する（ブリッジは `isAvailable == false` で何もしない）
- 旧 EW（2 引数 `Select` オーバーロードが無い）では同期自体を無効化する。1 引数 `Select` に落とすとギズモ二重化するため
- テスト基盤は無い。各タスクの検証は `debug.bat` ビルド成功 + 最終タスクの実機確認
- リポジトリは 3 つ: EW 本体 `W:\COM3D2_5\work\COM3D2.EditorWindow.Plugin`、MTEUtils サブモジュール `W:\COM3D2_5\work\COM3D2.ModItemExplorer.Plugin\source\COM3D2.ModItemExplorer.Plugin\MTEUtils`、MTE 親リポ `W:\COM3D2_5\work\COM3D2.ModItemExplorer.Plugin`
- コミットメッセージは Conventional Commits 形式の日本語

---

### Task 1: EW — SelectionManager に showGizmo オーバーロードと抑止フラグを追加

**Files:**
- Modify: `W:\COM3D2_5\work\COM3D2.EditorWindow.Plugin\source\COM3D2.EditorWindow.Plugin\Manager\SelectionManager.cs`

**Interfaces:**
- Produces: `public void Select(GameObject go, bool showGizmo)`、`public bool gizmoSuppressed`（Task 2 の GizmoRenderer と Task 3 のブリッジが参照）
- 既存の `Select(GameObject go)` は `Select(go, true)` へ委譲（既存呼び出し元は無変更で従来動作）

- [ ] **Step 1: フィールドとプロパティを追加**

`_selectedBoneDef` フィールド群（SelectionManager.cs:28 付近）の後に追加:

```csharp
        private bool _gizmoSuppressed = false;

        /// <summary>
        /// 外部プラグイン起点の選択で EW 側ギズモを抑止中か。
        /// 外部側が自前ギズモを持つ場合の二重表示・二重掴みを防ぐ
        /// </summary>
        public bool gizmoSuppressed => _gizmoSuppressed;
```

- [ ] **Step 2: Select をオーバーロード化**

既存の `Select(GameObject go)`（SelectionManager.cs:67-79）を以下に置き換え:

```csharp
        public void Select(GameObject go)
        {
            Select(go, true);
        }

        /// <summary>
        /// showGizmo = false で選択すると Inspector 等には選択が反映されるが
        /// EW 側ギズモは表示されない（外部プラグインが自前ギズモを持つケース用）
        /// </summary>
        public void Select(GameObject go, bool showGizmo)
        {
            // 通常選択はボーン選択を解除する（白丸クリック以外の経路で上書きされたケース）
            _selectedBoneMaid = null;
            _selectedBoneDef = null;

            // 同一オブジェクトの再選択は選択イベントを出さないが、抑止状態の切替だけは反映する
            _gizmoSuppressed = go != null && !showGizmo;

            if (_selectedObject == go)
            {
                return;
            }
            _selectedObject = go;
            onSelectionChanged?.Invoke(go);
        }
```

- [ ] **Step 3: SelectBone / ClearSelection で抑止を解除**

`SelectBone`（:86）の `_selectedBoneDef = def;` の直後に 1 行追加:

```csharp
            _gizmoSuppressed = false;
```

`ClearSelection`（:266）の先頭（`_selectedBoneMaid = null;` の直前）に 1 行追加:

```csharp
            _gizmoSuppressed = false;
```

- [ ] **Step 4: ビルド確認**

Run: `cd /w/COM3D2_5/work/COM3D2.EditorWindow.Plugin && cmd //c debug.bat com3d25`
Expected: `ビルドに成功しました`

- [ ] **Step 5: Commit（EW リポ）**

```bash
cd /w/COM3D2_5/work/COM3D2.EditorWindow.Plugin
git add source/COM3D2.EditorWindow.Plugin/Manager/SelectionManager.cs
git commit -m "feat(selection): 外部プラグイン向けに showGizmo 付き Select と gizmoSuppressed を追加"
```

---

### Task 2: EW — GizmoRenderer でギズモ対象にのみ抑止を反映

**Files:**
- Modify: `W:\COM3D2_5\work\COM3D2.EditorWindow.Plugin\source\COM3D2.EditorWindow.Plugin\Manager\GizmoRenderer.cs`

**Interfaces:**
- Consumes: `SelectionManager.gizmoSuppressed`（Task 1）
- 選択バウンズ枠（:165）とライトギズモ（:253）は既存の `target` を使い続ける（抑止中も選択の視覚フィードバックとして描く）

- [ ] **Step 1: gizmoTarget を追加し SyncGizmo で使う**

`target` プロパティ（GizmoRenderer.cs:112-119）の直後に追加:

```csharp
        /// <summary>
        /// ギズモ本体の対象。抑止中は選択オブジェクトを対象にしない
        /// （選択バウンズ・ライトギズモは target を使い続けるので抑止の影響を受けない）
        /// </summary>
        private GameObject gizmoTarget
        {
            get
            {
                var external = externalTargetProvider != null ? externalTargetProvider() : null;
                if (external != null)
                {
                    return external;
                }
                return selectionManager.gizmoSuppressed ? null : selectionManager.selectedObject;
            }
        }
```

`SyncGizmo`（:122-128）の `var go = target;` を `var go = gizmoTarget;` に変更。

- [ ] **Step 2: ビルド確認**

Run: `cd /w/COM3D2_5/work/COM3D2.EditorWindow.Plugin && cmd //c debug.bat com3d25`
Expected: `ビルドに成功しました`

- [ ] **Step 3: Commit（EW リポ）**

```bash
cd /w/COM3D2_5/work/COM3D2.EditorWindow.Plugin
git add source/COM3D2.EditorWindow.Plugin/Manager/GizmoRenderer.cs
git commit -m "feat(gizmo): gizmoSuppressed 中は選択オブジェクトをギズモ対象にしない"
```

---

### Task 3: MTEUtils — SelectionClient リフレクションブリッジを新設

**Files:**
- Create: `W:\COM3D2_5\work\COM3D2.ModItemExplorer.Plugin\source\COM3D2.ModItemExplorer.Plugin\MTEUtils\SelectionClient.cs`（MTEUtils サブモジュール内）
- Modify: `W:\COM3D2_5\work\COM3D2.ModItemExplorer.Plugin\source\COM3D2.ModItemExplorer.Plugin\COM3D2.ModItemExplorer.Plugin.csproj`（`<Compile Include="MTEUtils\SelectionClient.cs" />` を :140 の `DockingClient.cs` 行付近へアルファベット順で追加）

**Interfaces:**
- Consumes: `DockingClient.FindHostType(string)`（既存、DockingClient.cs:181）、EW の `SelectionManager.instance` / `Select(GameObject, bool)` / `selectedObject` / `onSelectionChanged`（Task 1）
- Produces（Task 4 が使用）:
  - `public static bool isAvailable`
  - `public static void Select(GameObject go, bool showGizmo)`
  - `public static GameObject selectedObject`（取得失敗・EW 不在時は null）
  - `public static bool AddSelectionChangedHandler(Action<GameObject> handler)`（登録成功で true）

- [ ] **Step 1: SelectionClient.cs を作成**

`GizmoHostClient.cs` と同じ「型が見つかるまで再試行、シグネチャ不一致で確定フォールバック」パターン。`SelectionManager` はインスタンスクラスなので、static プロパティ `instance` を取得してインスタンス束縛デリゲートを作る点だけが異なる。

```csharp
using System;
using System.Reflection;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor
{
    /// <summary>
    /// EditorWindow プラグインの SelectionManager へのリフレクションブリッジ。
    /// showGizmo = false で選択すると EW の Inspector には選択が反映されるが
    /// EW 側ギズモは表示されない（呼び出し側が自前ギズモを持つケース用）。
    /// EditorWindow が不在・旧バージョン（2 引数 Select が無い）の場合は
    /// isAvailable が false になり、呼び出し側は同期しない
    /// </summary>
    public static class SelectionClient
    {
        private static Action<GameObject, bool> _select;
        private static Func<GameObject> _getSelectedObject;
        private static EventInfo _selectionChangedEvent;
        private static object _instance;
        private static bool _initialized;

        public static bool isAvailable
        {
            get
            {
                Initialize();
                return _select != null;
            }
        }

        /// <summary>EW 側の現在の選択オブジェクト。EW 不在・取得失敗時は null</summary>
        public static GameObject selectedObject
        {
            get
            {
                if (!isAvailable)
                {
                    return null;
                }

                try
                {
                    return _getSelectedObject();
                }
                catch (Exception e)
                {
                    MTEUtils.LogWarning("SelectionClient: 選択オブジェクトの取得に失敗しました: " + e.Message);
                    return null;
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
            var type = DockingClient.FindHostType("SelectionManager");
            if (type == null)
            {
                return;
            }

            // ここから先はホストの型は見つかっている。シグネチャ不一致は
            // バージョン差による恒久的な問題なので、この場合のみ無効へ確定する
            _initialized = true;

            try
            {
                var instanceProp = type.GetProperty("instance", BindingFlags.Public | BindingFlags.Static);
                // 2 引数オーバーロードを明示指定する。旧 EW（1 引数のみ）では null になり
                // 同期自体を無効化する（1 引数へ落とすとギズモが二重表示されるため）
                var select = type.GetMethod("Select", BindingFlags.Public | BindingFlags.Instance,
                    null, new[] { typeof(GameObject), typeof(bool) }, null);
                var selectedProp = type.GetProperty("selectedObject", BindingFlags.Public | BindingFlags.Instance);
                var changedEvent = type.GetEvent("onSelectionChanged", BindingFlags.Public | BindingFlags.Instance);
                if (instanceProp == null || select == null || selectedProp == null || changedEvent == null)
                {
                    MTEUtils.LogWarning("SelectionClient: SelectionManager にシグネチャの一致するメンバーが見つかりませんでした");
                    return;
                }

                var instance = instanceProp.GetValue(null, null);
                if (instance == null)
                {
                    MTEUtils.LogWarning("SelectionClient: SelectionManager のインスタンスを取得できませんでした");
                    return;
                }

                _instance = instance;
                _selectionChangedEvent = changedEvent;
                _select = (Action<GameObject, bool>)Delegate.CreateDelegate(
                    typeof(Action<GameObject, bool>), instance, select);
                _getSelectedObject = (Func<GameObject>)Delegate.CreateDelegate(
                    typeof(Func<GameObject>), instance, selectedProp.GetGetMethod());
            }
            catch (Exception e)
            {
                // ホスト側のバージョン差でシグネチャが合わない場合は同期を無効化する
                MTEUtils.LogWarning("SelectionClient: SelectionManager との接続に失敗しました: " + e.Message);
                _instance = null;
                _selectionChangedEvent = null;
                _select = null;
                _getSelectedObject = null;
            }
        }

        /// <summary>
        /// EW 側の選択を設定する。go = null で選択解除。
        /// showGizmo = false なら EW 側ギズモを抑止する
        /// </summary>
        public static void Select(GameObject go, bool showGizmo)
        {
            if (!isAvailable)
            {
                return;
            }

            try
            {
                _select(go, showGizmo);
            }
            catch (Exception e)
            {
                MTEUtils.LogWarning("SelectionClient: 選択の設定に失敗しました: " + e.Message);
            }
        }

        /// <summary>
        /// EW 側の選択変更イベントを購読する。登録できたら true。
        /// EW 不在時は false を返すので、呼び出し側は true になるまで再試行してよい
        /// </summary>
        public static bool AddSelectionChangedHandler(Action<GameObject> handler)
        {
            if (!isAvailable || handler == null)
            {
                return false;
            }

            try
            {
                _selectionChangedEvent.AddEventHandler(_instance, handler);
                return true;
            }
            catch (Exception e)
            {
                MTEUtils.LogWarning("SelectionClient: 選択変更イベントの購読に失敗しました: " + e.Message);
                return false;
            }
        }
    }
}
```

- [ ] **Step 2: csproj に追加**

`COM3D2.ModItemExplorer.Plugin.csproj` の `<Compile Include="MTEUtils\InputRemapperClient.cs" />` 付近（アルファベット順）に追加:

```xml
    <Compile Include="MTEUtils\SelectionClient.cs" />
```

（正確な既存行は csproj 内の `MTEUtils\` プレフィックスの並びを確認して合わせること）

- [ ] **Step 3: ビルド確認**

Run: `cd /w/COM3D2_5/work/COM3D2.ModItemExplorer.Plugin && cmd //c debug.bat com3d25`
Expected: `ビルドに成功しました`

- [ ] **Step 4: Commit（MTEUtils サブモジュール）**

```bash
cd /w/COM3D2_5/work/COM3D2.ModItemExplorer.Plugin/source/COM3D2.ModItemExplorer.Plugin/MTEUtils
git add SelectionClient.cs
git commit -m "feat(selection): EditorWindow の SelectionManager へのリフレクションブリッジを追加"
```

**注意:** MTEUtils サブモジュールに既存の変更（親リポの `git status` で `M ... MTEUtils`）がある場合、その変更は含めず `SelectionClient.cs` のみをコミットすること。csproj の変更は親リポなのでここではコミットしない（Task 4 でまとめる）。

---

### Task 4: MTE — SelfModelPlacer の選択を EW へ同期（双方向）

**Files:**
- Modify: `W:\COM3D2_5\work\COM3D2.ModItemExplorer.Plugin\source\COM3D2.ModItemExplorer.Plugin\ModelPlacement\SelfModelPlacer.cs`

**Interfaces:**
- Consumes: `SelectionClient.isAvailable` / `Select(GameObject, bool)` / `selectedObject` / `AddSelectionChangedHandler(Action<GameObject>)`（Task 3）
- 既存メンバー: `_models`（:35, `List<StudioModelStatWrapper>`）、`selectedModel` setter（:157-175）、`Update()`（:384）

**ループ防止の設計メモ（ガードフラグ不要の根拠）:**
- MTE → EW: setter → `Select(go, false)` → `onSelectionChanged` 発火 → 逆同期ハンドラ → `selectedModel = 同じモデル` → setter の同値 no-op（:167）で終息
- EW → MTE: EW クリック → `onSelectionChanged` → ハンドラ → setter → `Select(同じ go, false)` → EW 側の同値 no-op（イベント再発火なし、抑止フラグだけ更新）で終息。これにより「MTE 管理モデルは EW 側で選んでもギズモは常に MTE 側」という規約になる

**仕様メモ（BoneEdit との相互作用）:** `Select(go, bool)` は既存の 1 引数版と同様に無条件でボーン選択を解除する。そのため EW で BoneEdit 中に MTE 側でモデルを選択すると、同期呼び出しが EW のボーン選択を解除する。これは既存 `Select(go)` の規約（通常選択はボーン選択を解除）に従った意図的な挙動であり、新規の副作用ではない。

- [ ] **Step 1: setter に同期呼び出しを追加**

`selectedModel` setter（:157-175）を以下に変更（旧 GameObject を先に控える）:

```csharp
            set
            {
                // 他プラグイン配置分は対象外。ハイライトでマテリアルを書き換えてしまうため弾く
                if (value != null && !Owns(value))
                {
                    return;
                }

                // 破棄済み判定のある getter ではなくフィールドと比べる。
                // 破棄済みモデルから null への切替もハイライト解除として通す必要があるため
                if (_selectedModel == value)
                {
                    return;
                }

                var previousGo = _selectedModel?.obj as GameObject;
                _selectedModel = value;
                RefreshHighlight();
                SyncSelectionToHost(previousGo);
            }
```

- [ ] **Step 2: 同期メソッドと逆同期ハンドラを追加**

`RefreshHighlight`（:251）の手前など、選択関連の近くに追加:

```csharp
        // EW の選択変更イベントを購読済みか。EW は後からロードされる可能性があるため
        // Update で成功するまで再試行する（GizmoHostClient と同じパターン）
        private bool _selectionHandlerRegistered;

        /// <summary>
        /// 選択状態を EW の SelectionManager へ反映する。Inspector に選択として表示されるが、
        /// ギズモは常に ModelGizmoManager 側を使うため showGizmo = false で抑止する
        /// </summary>
        private void SyncSelectionToHost(GameObject previousGo)
        {
            if (!SelectionClient.isAvailable)
            {
                return;
            }

            var go = _selectedModel?.obj as GameObject;
            if (go != null)
            {
                SelectionClient.Select(go, showGizmo: false);
            }
            else if (previousGo != null && SelectionClient.selectedObject == previousGo)
            {
                // 自分が選ばせたオブジェクトだけ解除する。
                // EW 側でユーザーが選び直した別オブジェクトの選択は奪わない
                SelectionClient.Select(null, showGizmo: true);
            }
        }

        /// <summary>
        /// EW 側の選択変更を MTE 側へ追従させる。自プラグイン管理のモデルなら選択し、
        /// それ以外（他オブジェクト・選択解除）なら MTE 側の選択を外す
        /// </summary>
        private void OnHostSelectionChanged(GameObject go)
        {
            selectedModel = FindModelByGameObject(go);
        }

        /// <summary>配置済みモデルを GameObject から逆引きする。管理外なら null</summary>
        private StudioModelStatWrapper FindModelByGameObject(GameObject go)
        {
            if (go == null)
            {
                return null;
            }

            foreach (var model in _models)
            {
                if ((model.obj as GameObject) == go)
                {
                    return model;
                }
            }
            return null;
        }
```

- [ ] **Step 3: Update で購読を再試行**

`Update()`（:384）の先頭、`UpdateGizmoKeyInput();` の直前に追加:

```csharp
            if (!_selectionHandlerRegistered)
            {
                _selectionHandlerRegistered = SelectionClient.AddSelectionChangedHandler(OnHostSelectionChanged);
            }
```

- [ ] **Step 4: ビルド確認（両ターゲット）**

Run: `cd /w/COM3D2_5/work/COM3D2.ModItemExplorer.Plugin && cmd //c debug.bat`
Expected: `ビルドに成功しました`（com3d2 / com3d25 両方）

- [ ] **Step 5: Commit（MTE 親リポ、サブモジュールポインタ含む）**

```bash
cd /w/COM3D2_5/work/COM3D2.ModItemExplorer.Plugin
git add source/COM3D2.ModItemExplorer.Plugin/MTEUtils \
        source/COM3D2.ModItemExplorer.Plugin/COM3D2.ModItemExplorer.Plugin.csproj \
        source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/SelfModelPlacer.cs
git commit -m "feat(selection): モデル選択を EW Inspector へ双方向同期 (ギズモは MTE 側に一本化)"
```

**注意:** MTEUtils サブモジュールに今回と無関係の既存変更が混ざっている場合は、サブモジュールポインタのコミット可否をユーザーに確認すること。

---

### Task 5: 実機確認

**Files:** なし（検証のみ）

ゲーム起動中なら MCP `com3d25-devbridge` が使える（`ping` → `eval_csharp` / `screenshot`）。起動していなければユーザーに手動確認を依頼する。

- [ ] **Step 1: 順方向（MTE → EW Inspector）**

1. MTE でモデルを配置 → 配置直後の自動選択（SelfModelPlacer.cs:356 経由）で EW Inspector にそのモデルの Transform が表示されること
2. ModelOperationWindow の一覧クリックで選択を切り替えると Inspector が追従すること
3. このとき EW の SceneView/GameView に **EW 側ギズモが出ない**こと（MTE のギズモのみ）。SceneView のオレンジ色の選択バウンズ枠は出てよい

devbridge での状態確認例:

```csharp
var sm = COM3D2.EditorWindow.Plugin.SelectionManager.instance;
(sm.selectedObject != null ? sm.selectedObject.name : "null") + " / suppressed=" + sm.gizmoSuppressed
```

- [ ] **Step 2: 逆方向（EW → MTE）**

1. EW の SceneView で配置モデルをクリック → MTE 側の選択（ハイライト明滅）が追従し、EW ギズモは出ないこと（抑止が再適用される）
2. EW の Hierarchy でメイド等の別オブジェクトを選択 → MTE 側の選択が外れ、EW ギズモがそのオブジェクトに通常表示されること
3. Inspector のギズモ切替 UI・ボーン編集（BoneEdit）が従来通り動くこと

- [ ] **Step 3: 解除・破棄系**

1. MTE でモデル選択を解除（一覧の再クリック） → EW の選択も外れること
2. 選択中モデルを MTE から削除 → EW の選択が外れ、破棄に伴うエラーが出ないこと（削除時は `selectedModel = null` → `SyncSelectionToHost` の明示同期でクリアされる。EW の毎フレーム破棄監視 SelectionManager.cs:58-65 は発火しない安全網）
3. シーン切替（エディット⇔メイド選択等） → 双方の選択がクリアされエラーが出ないこと（EW 側 OnChangedSceneLevel :279）
4. EW プラグインを外した環境（または旧 EW）でも MTE の選択・ギズモが従来通り動くこと（可能なら確認、難しければログに SelectionClient の警告が出ない/一度だけ出ることの確認で代替）

- [ ] **Step 4: 問題なければ完了報告**

不具合があれば superpowers:systematic-debugging で原因を特定してから修正すること。

---

## レビュー却下メモ

- MTE 独自のシーンクリック選択経路の確認が計画にない — 誤検知。MTE の選択変更経路は `selectedModel` setter に一元化されており（SelfModelPlacer.cs:144 のコメントどおり）、書き込み元は ModelOperationWindow のクリック・配置直後の自動選択・削除時クリアのみと確認済み。シーンクリックによる独自選択経路は存在しない
- `SelectionClient` にイベントハンドラの登録解除経路がない — 未確認のまま見送りではなく YAGNI 判断で却下。`SelfModelPlacer` はプロセス寿命のシングルトンで再初期化経路が無く、二重登録は起きない。ホットリロード対応が入る際に `RemoveSelectionChangedHandler` を追加する
