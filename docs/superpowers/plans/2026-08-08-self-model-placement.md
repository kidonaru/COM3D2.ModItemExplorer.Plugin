# プラグイン単体でのモデル配置機能 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** MotionTimelineEditor (MTE) が無くても、ModItemExplorer 単体で .menu MOD アイテムをシーンに配置・削除できるようにする。

**Architecture:** 既存の `ModelHackManagerWrapper`（MTE リフレクション連携）の前段に、同一シグネチャのファサード `ModelPlacerManager` を新設する。ファサードは MTE 連携と自作配置 `SelfModelPlacer` を束ね、`pluginName` で振り分ける。自作配置は SceneCapture.Plugin の方式（ラッパー GameObject + GizmoRenderTarget）を踏襲しつつ、メッシュ読み込みは COM3D2.5 の逆コンパイル済み `ImportCM.LoadSkinMesh_R` を bodyskin 非依存に移植して行う（2.5 の crc/新フォーマット ver2100〜2200 に対応するため）。

**Tech Stack:** C# (.NET Framework / Unity 5.6 系 + COM3D2.5), UnityInjector プラグイン。単体テスト基盤は無し（ビルド + 実機検証で代替）。

## Global Constraints

- コードのコメント・ログメッセージは日本語で書く（ユーザー CLAUDE.md 指示）
- 既存コードのスタイル（`MTEUtils.LogDebug/LogWarning/LogException`、4スペースインデント、`var` 多用）に合わせる
- ビルド確認コマンド: `cmd /c "cd /d W:\COM3D2_5\work\COM3D2.ModItemExplorer.Plugin && source\COM3D2.ModItemExplorer.Plugin\build.bat debug com3d25"`（成功時 ERRORLEVEL 0）
- 実機検証は MCP `com3d25-devbridge` の `eval_csharp` を使う（ゲーム起動中のみ）。Unity 型は完全修飾名で書くこと
- ゲーム API 参照元（逆コンパイル済みソース）: `W:\COM3D2_5\work\Assembly-CSharp\`
- 自作配置のプラグイン名定数: `"ModItemExplorer"`
- `Menu.ProcScript` は使わない（メイドスロットに紐づくため単体配置に不適）。`ImportCM.LoadMaterial(filename, null)` / `ImportCM.CreateTexture` は bodyskin null で使用可（2.5 で確認済み）
- `ImportCM.LoadSkinMesh_R` は `bodyskin.m_OriVert` / `bodyskin.body.boMAN` / `bodyskin.listDEL` を触るため **null 不可** → メッシュローダーは自前移植が必須

## 既存コードの前提知識（実装者向け）

- 配置 UI: `source/COM3D2.ModItemExplorer.Plugin/ModItemWindow.cs` の `DrawModelInfo()`（:808-856）。`modelHackManager.IsValid()` → `pluginNames` をコンボボックスへ → 「配置」で `modItemManager.CreateModel(selectedMenuItem, pluginName)`
- 配置ロジック: `Manager/ModItemManager.cs` の `CreateModel(MenuItem, string)`（:656-705）、削除は `DelItem`（:736-748）、ツリー同期は `UpdateModelItems()`（:1877-1911）
- `ManagerBase.cs:13` に `protected static ModelHackManagerWrapper modelHackManager => ModelHackManagerWrapper.instance;` があり、全 Manager が参照。`ModItemWindow.cs` にも同様のプロパティがある（:31 付近）
- DTO は `MTEHack/StudioModelStatField.cs` の `StudioModelStatWrapper`（original/info/group/name/displayName/attachPoint/attachMaidSlotNo/obj/pluginName/visible/infoWrapper）と `MTEHack/OfficialObjectInfoField.cs` の `OfficialObjectInfoWrapper`（type/label/fileName/prefabName/myRoomId/bgObjectId）。どちらも MTE 非依存の POCO
- menu 情報 DTO は `MTEUtils/ModMenuLoader.cs` の `MenuInfo`。`modelFileName`（additem のモデルファイル名）を既に保持しているが、マテリアル変更 / テクスチャ変更コマンドは保持していない → 配置時に .menu を再パースする
- `UpdateModelItems()` は `modelList.Contains(item.model)`（参照比較）で消えたモデルを検出する。自作側は **配置中モデルごとに同一の Wrapper インスタンスを返し続ける**こと（毎回 new すると全アイテムが消えて再生成される）

---

### Task 1: ModelMeshLoader — .model ファイルの単体読み込み

**Files:**
- Create: `source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/ModelMeshLoader.cs`

**Interfaces:**
- Consumes: ゲーム API（`GameUty.FileOpen`, `ImportCM.LoadMaterial`, `Resources.Load("seed")`）
- Produces: `public static GameObject LoadMesh(string modelFileName, int layer)` — 失敗時は null を返し警告ログ。戻り値はルート GameObject（SkinnedMeshRenderer 構築済み、マテリアル適用済み）

- [ ] **Step 1: 移植元を読む**

`W:\COM3D2_5\work\Assembly-CSharp\ImportCM.cs` の `LoadSkinMesh_R`（92 行目〜約 449 行目、メソッド末尾まで）を Read で全文読むこと。これが移植元の正であり、バイナリ読み取り順（ヘッダ → version → ボーン → 親子 → Transform → 頂点/サブメッシュ → マテリアル）を一切変えてはならない。

- [ ] **Step 2: ModelMeshLoader.cs を実装**

`LoadSkinMesh_R` を以下の差分で書き写す（それ以外は逐語的に維持）:

1. シグネチャを `public static GameObject LoadMesh(string modelFileName, int layer)` にする（`morph` / `slotname` / `bodyskin` / `ref modelVersion` 引数を削除）
2. `bodyskin` 依存の行を除去:
   - `TBodySkin.OriVert oriVert = bodyskin.m_OriVert;` と `oriVert.*` への代入 → 削除
   - `bodyskin.body.boMAN` 分岐（head/chikubi/seieki の castShadows 無効化）→ 削除（ver2104+ の `shadowCastingMode` 文字列適用は残す）
   - `bodyskin.listDEL.Add(...)` → 削除（破棄は SelfModelPlacer が担当）
3. `morph` 依存の行（morph 登録・BlendShape 処理があれば）→ 削除
4. マテリアル読み込みは元コード同様 `ImportCM.ReadMaterial(binaryReader, null, null, num)` を呼ぶ（2.5 で bodyskin はデフォルト null 可を確認済み）。元コードが private メンバや `m_skinTempFile` バッファを使う箇所は `GameUty.FileOpen(modelFileName).ReadAll()` によるローカル byte[] で置き換える
5. ファイルオープン失敗・ヘッダ不正は `NDebug.Assert` ではなく `MTEUtils.LogWarning` + `return null` にする（プラグインからゲームを落とさない）
6. `Resources.Load("seed")` の Instantiate はそのまま使う（2.5 本体コードが現役で使用しているため存在保証あり）
7. クラス冒頭コメントに移植元（`ImportCM.LoadSkinMesh_R`）と削った依存を 1〜2 行で記す

ファイル骨格:

```csharp
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace COM3D2.ModItemExplorer.Plugin
{
    /// <summary>
    /// ImportCM.LoadSkinMesh_R (COM3D2.5) を bodyskin/morph 非依存に移植したモデル単体ローダー。
    /// メイド装着を前提としない配置用途のため、OriVert 登録・listDEL 管理・スロット別影制御を除いている。
    /// </summary>
    public static class ModelMeshLoader
    {
        public static GameObject LoadMesh(string modelFileName, int layer)
        {
            // ここに移植本体（Step 2 の差分適用済み）
        }
    }
}
```

- [ ] **Step 3: ビルド確認**

Run: `cmd /c "cd /d W:\COM3D2_5\work\COM3D2.ModItemExplorer.Plugin && source\COM3D2.ModItemExplorer.Plugin\build.bat debug com3d25"`
Expected: エラー 0 でビルド成功（csproj はワイルドカードコンパイルでなければ新規 .cs の追加登録が必要。ビルドエラーになったら csproj に `<Compile Include="ModelPlacement\ModelMeshLoader.cs" />` を追加）

- [ ] **Step 4: 実機検証（ゲーム起動中のみ、起動していなければスキップして Task 5 でまとめて検証）**

`mcp__com3d25-devbridge__eval_csharp` で:

```csharp
var go = COM3D2.ModItemExplorer.Plugin.ModelMeshLoader.LoadMesh("odogu_vibe.model", 10);
go == null ? "null" : go.name + " smr=" + (go.GetComponentsInChildren<UnityEngine.SkinnedMeshRenderer>(true).Length)
```

（model 名は実在する任意の menu の modelFileName でよい。`GameMain.Instance.MenuDataBase` から適当に拾って可）
Expected: `_SM_〜 smr=1` のような文字列。検証後 `UnityEngine.Object.Destroy(go)` すること

- [ ] **Step 5: コミット**

```bash
git add source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/ModelMeshLoader.cs source/COM3D2.ModItemExplorer.Plugin/COM3D2.ModItemExplorer.Plugin.csproj
git commit -m "feat: .model単体ローダーを追加（ImportCM.LoadSkinMesh_Rのbodyskin非依存移植）"
```

---

### Task 2: ModelMenuScript — 配置用 .menu 再パース

**Files:**
- Create: `source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/ModelMenuScript.cs`

**Interfaces:**
- Consumes: `GameUty.FileOpen`, `UTY.GetStringCom`, `UTY.GetStringList`
- Produces:

```csharp
public class ModelMenuScript
{
    public string modelFileName;                       // additem の第1引数
    public List<MaterialChange> materialChanges;       // マテリアル変更
    public List<TextureChange> textureChanges;         // tex / テクスチャ変更
    public static ModelMenuScript Load(string menuFileName); // 失敗時 null
}
public struct MaterialChange { public int materialNo; public string fileName; }
public struct TextureChange { public int materialNo; public string propName; public string fileName; }
```

- [ ] **Step 1: 実装**

.menu バイナリのコマンドループを実装する。フォーマットはヘッダ（`"CM3D2_MENU"` 文字列 → int32 version → string×4 → int32 bodySize）の後、`(byte 引数個数, string×個数)` のコマンドブロック列。既存の同型パーサ `MTEUtils/ModMenuLoader.cs`（このリポジトリ内。`ParseMenu` 相当の処理）を先に読み、読み取りループの書き方を合わせること。

拾うコマンドハンドリング（`strings[0]` で分岐）:

```csharp
switch (command)
{
    case "additem":
        // strings[1] = モデルファイル名。最初の additem のみ採用
        if (modelFileName == null && strings.Length >= 2) modelFileName = strings[1];
        break;
    case "マテリアル変更":
        // strings[1]=スロット名, strings[2]=マテリアル番号, strings[3]=.mate ファイル名
        materialChanges.Add(new MaterialChange {
            materialNo = int.Parse(strings[2]), fileName = strings[3] });
        break;
    case "tex":
    case "テクスチャ変更":
        // strings[1]=スロット名, strings[2]=マテリアル番号, strings[3]=プロパティ名, strings[4]=.tex ファイル名
        // strings.Length >= 5 のときのみ（"tex" にはリセット系の短い形式がある）
        if (strings.Length >= 5)
        {
            textureChanges.Add(new TextureChange {
                materialNo = int.Parse(strings[2]), propName = strings[3], fileName = strings[4] });
        }
        break;
}
```

引数の並びは `W:\COM3D2_5\work\Assembly-CSharp\Menu.cs` の `ProcScript` 内の同名コマンド処理を必ず確認して合わせること（上記は SceneCapture 実装ベースの想定であり、Menu.cs が正）。`Load` は全体を try/catch し、例外時 `MTEUtils.LogException` + null 返却。

- [ ] **Step 2: ビルド確認**

Run: Global Constraints のビルドコマンド
Expected: 成功

- [ ] **Step 3: 実機検証（起動中のみ、なければ Task 5 に委ねる）**

```csharp
var ms = COM3D2.ModItemExplorer.Plugin.ModelMenuScript.Load("dress419_onepiece_i_.menu");
ms == null ? "null" : ms.modelFileName + " mat=" + ms.materialChanges.Count + " tex=" + ms.textureChanges.Count
```

（menu 名は実在するものなら何でもよい）
Expected: モデルファイル名が返る

- [ ] **Step 4: コミット**

```bash
git add source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/ModelMenuScript.cs
git commit -m "feat: 配置用の.menu再パーサーを追加"
```

---

### Task 3: SelfModelPlacer — 自作配置の本体

**Files:**
- Create: `source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/SelfModelPlacer.cs`

**Interfaces:**
- Consumes: Task 1 `ModelMeshLoader.LoadMesh(string, int)`, Task 2 `ModelMenuScript.Load(string)`, `ImportCM.LoadMaterial(string, TBodySkin, Material)`, `ImportCM.CreateTexture(string)`, `GizmoRender` / `GizmoRenderTarget`（ゲーム本体型）
- Produces:

```csharp
public class SelfModelPlacer
{
    public const string PluginName = "ModItemExplorer";
    public static SelfModelPlacer instance { get; }               // 遅延生成シングルトン
    public List<StudioModelStatWrapper> modelList { get; }        // 配置中モデル（安定した同一インスタンスを返す）
    public void CreateModel(string fileName, int group, bool visible); // fileName は .menu 名
    public void DeleteModel(StudioModelStatWrapper model);        // 自作分のみ処理
    public bool Owns(StudioModelStatWrapper model);               // pluginName == PluginName
    public void DeleteAll();                                      // シーン遷移時の全削除
}
```

- [ ] **Step 1: 実装**

内部状態: `private readonly List<StudioModelStatWrapper> _models = new List<StudioModelStatWrapper>();` と、Wrapper→生成済みマテリアルの対応 `private readonly Dictionary<StudioModelStatWrapper, List<Material>> _createdMaterials = ...`。

`CreateModel` の流れ（SceneCapture `ModelWindow.LoadModel` の MaidEquip 分岐を踏襲）:

```csharp
public void CreateModel(string fileName, int group, bool visible)
{
    var script = ModelMenuScript.Load(fileName);
    if (script == null || string.IsNullOrEmpty(script.modelFileName))
    {
        MTEUtils.LogWarning("menuの解析に失敗しました。" + fileName);
        return;
    }

    var modelGo = ModelMeshLoader.LoadMesh(script.modelFileName, 10); // 10 = Characterレイヤー
    if (modelGo == null)
    {
        return;
    }

    // マテリアル/テクスチャ差し替え
    var created = new List<Material>();
    foreach (var smr in modelGo.GetComponentsInChildren<SkinnedMeshRenderer>(true))
    {
        var materials = smr.materials; // 複製が返る
        foreach (var mc in script.materialChanges)
        {
            if (mc.materialNo < materials.Length)
            {
                materials[mc.materialNo] = ImportCM.LoadMaterial(mc.fileName, null);
                created.Add(materials[mc.materialNo]);
            }
        }
        foreach (var tc in script.textureChanges)
        {
            if (tc.materialNo < materials.Length)
            {
                materials[tc.materialNo].SetTexture(tc.propName, ImportCM.CreateTexture(tc.fileName));
            }
        }
        smr.materials = materials;
    }

    // ラッパーGameObjectでくるみ、配置親の下へ（ギズモ操作でモデル内部Transformを壊さないため）
    var parent = GameObject.Find("Deployment Object Parent")
        ?? new GameObject("Deployment Object Parent");
    var wrapperGo = new GameObject(GetModelName(fileName, group));
    modelGo.transform.SetParent(wrapperGo.transform, true);
    wrapperGo.transform.SetParent(parent.transform, false);
    modelGo.transform.localPosition = Vector3.zero;
    modelGo.transform.localScale = Vector3.one;
    AddGizmo(wrapperGo);
    wrapperGo.SetActive(visible);

    var wrapper = new StudioModelStatWrapper
    {
        original = null,
        group = group,
        name = GetModelName(fileName, group),
        displayName = GetModelName(fileName, group),
        obj = wrapperGo,
        pluginName = PluginName,
        visible = visible,
        infoWrapper = new OfficialObjectInfoWrapper { fileName = fileName, label = fileName },
    };
    _models.Add(wrapper);
    _createdMaterials[wrapper] = created;
}
```

補足事項:
- `GetModelName(fileName, group)` は `Path.GetFileNameWithoutExtension(fileName)` + （group が 0 以外なら `" (" + group + ")"`）。`ModItemManager.GetModelItemName`（`"model_" + model.name`）がツリーのキーに使うため一意であること
- `AddGizmo` は SceneCapture 同様に:

```csharp
private void AddGizmo(GameObject target)
{
    var gizmo = target.AddComponent<GizmoRenderTarget>();
    gizmo.offsetScale = 0.25f;
    gizmo.eAxis = true;    // 移動
    gizmo.eRotate = false;
    gizmo.eScal = false;
    gizmo.Visible = true;
}
```

（`GizmoRenderTarget` は `GizmoRender` 派生のゲーム本体型。メンバ名は `W:\COM3D2_5\work\Assembly-CSharp\GizmoRenderTarget.cs` / `GizmoRender.cs` を確認して合わせること）
- `DeleteModel`: `Owns(model)` でなければ何もしない。`UnityEngine.Object.Destroy((GameObject)model.obj)`、`_createdMaterials` の Material を全て `Object.Destroy`、リストから除去
- `DeleteAll`: 全モデルに `DeleteModel`。呼び出しはファサード経由で `ModItemManager.OnChangedSceneLevel` から行う（Task 4）
- `modelList` はライブ参照ではなくコピー（`new List<>(_models)`）を返す

- [ ] **Step 2: ビルド確認**

Run: Global Constraints のビルドコマンド
Expected: 成功

- [ ] **Step 3: コミット**

```bash
git add source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/SelfModelPlacer.cs
git commit -m "feat: 自前モデル配置マネージャーを追加"
```

---

### Task 4: ModelPlacerManager ファサードと既存コードの接続

**Files:**
- Create: `source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/ModelPlacerManager.cs`
- Modify: `source/COM3D2.ModItemExplorer.Plugin/Manager/ManagerBase.cs:13`
- Modify: `source/COM3D2.ModItemExplorer.Plugin/ModItemWindow.cs`（`modelHackManager` プロパティ、:31 付近）
- Modify: `source/COM3D2.ModItemExplorer.Plugin/Manager/ModItemManager.cs`（`OnChangedSceneLevel` に全削除追加）

**Interfaces:**
- Consumes: `ModelHackManagerWrapper.instance`（既存 MTE 連携）、Task 3 `SelfModelPlacer`
- Produces: `ModelPlacerManager.instance` — 既存呼び出し側が使う 5 メンバ: `IsValid()` / `pluginNames` / `modelList` / `CreateModel(label, fileName, group, pluginName, visible)` / `DeleteModel(wrapper)`

- [ ] **Step 1: ModelPlacerManager.cs を実装**

```csharp
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;

namespace COM3D2.ModItemExplorer.Plugin
{
    /// <summary>
    /// モデル配置先の振り分けファサード。MTE(ModelHackManagerWrapper)連携と
    /// 自前配置(SelfModelPlacer)を束ね、既存UIからは単一の配置窓口に見せる。
    /// </summary>
    public class ModelPlacerManager
    {
        private static ModelPlacerManager _instance = null;
        public static ModelPlacerManager instance
            => _instance ?? (_instance = new ModelPlacerManager());

        private ModelHackManagerWrapper mteWrapper => ModelHackManagerWrapper.instance;
        private SelfModelPlacer selfPlacer => SelfModelPlacer.instance;

        // 自前配置が常に使えるため、常に有効
        public bool IsValid() => true;

        public List<string> pluginNames
        {
            get
            {
                var names = new List<string> { SelfModelPlacer.PluginName };
                if (mteWrapper.IsValid())
                {
                    names.AddRange(mteWrapper.pluginNames);
                }
                return names;
            }
        }

        public List<StudioModelStatWrapper> modelList
        {
            get
            {
                var list = new List<StudioModelStatWrapper>(selfPlacer.modelList);
                if (mteWrapper.IsValid())
                {
                    var mteList = mteWrapper.modelList;
                    if (mteList != null)
                    {
                        list.AddRange(mteList);
                    }
                }
                return list;
            }
        }

        public void CreateModel(string label, string fileName, int group, string pluginName, bool visible)
        {
            if (pluginName == SelfModelPlacer.PluginName)
            {
                selfPlacer.CreateModel(fileName, group, visible);
            }
            else
            {
                mteWrapper.CreateModel(label, fileName, group, pluginName, visible);
            }
        }

        public void DeleteModel(StudioModelStatWrapper model)
        {
            if (model == null)
            {
                return;
            }
            if (selfPlacer.Owns(model))
            {
                selfPlacer.DeleteModel(model);
            }
            else
            {
                mteWrapper.DeleteModel(model);
            }
        }

        public void DeleteAllSelfModels()
        {
            selfPlacer.DeleteAll();
        }
    }
}
```

- [ ] **Step 2: 参照の差し替え**

`Manager/ManagerBase.cs:13` を:

```csharp
protected static ModelPlacerManager modelHackManager => ModelPlacerManager.instance;
```

`ModItemWindow.cs` の同名プロパティ（:31 付近、`ModelHackManagerWrapper` 型で宣言されている）も同様に `ModelPlacerManager` へ差し替える。他に `ModelHackManagerWrapper.instance` を直接参照する箇所が無いか `Grep ModelHackManagerWrapper` で確認し、あれば同様に差し替える。

- [ ] **Step 3: シーン遷移時の全削除**

`Manager/ModItemManager.cs` の `OnChangedSceneLevel(Scene, LoadSceneMode)` オーバーライド（無ければ追加）に:

```csharp
// シーンをまたぐと配置モデルは無効になるため自前配置分を破棄する
modelHackManager.DeleteAllSelfModels();
```

（`UpdateModelItems()` が既存の消滅検出でツリー側を追従させる）

- [ ] **Step 4: 一意 group の確認**

`ModItemManager.CreateModel`（:675-684）の group 採番は `modelList` 全体を見るため、ファサードで自作+MTE を連結すれば無改修で機能する。`pluginName == "StudioMode"` の拡張子除去分岐（:687-690）はそのまま残す。変更が不要なことをコードを読んで確認だけする。

- [ ] **Step 5: ビルド確認**

Run: Global Constraints のビルドコマンド
Expected: 成功

- [ ] **Step 6: コミット**

```bash
git add -A source/COM3D2.ModItemExplorer.Plugin
git commit -m "feat: モデル配置をファサード化し自前配置プラグインを選択可能に"
```

---

### Task 5: 実機での結合検証

**Files:** なし（検証のみ。不具合があれば該当 Task のファイルを修正）

- [ ] **Step 1: ゲーム起動確認**

`mcp__com3d25-devbridge__ping` で応答確認。応答が無い場合はユーザーに COM3D2.5 の起動を依頼して中断。

- [ ] **Step 2: デバッグビルドの配備**

Run: `cmd /c "cd /d W:\COM3D2_5\work\COM3D2.ModItemExplorer.Plugin && debug.bat com3d25"`
Expected: 「ビルドに成功しました」。その後ゲーム再起動が必要（ユーザーに依頼、またはプラグインのホットリロード手段があればそれを使う）。

- [ ] **Step 3: UI 経由の配置検証**

エディット画面等で ModItemExplorer を開き、任意のアイテム選択 → 配置プラグインに「ModItemExplorer」が出ること → 配置ボタンでシーンに実体が出ること、を `screenshot` / `eval_csharp` で確認:

```csharp
var p = UnityEngine.GameObject.Find("Deployment Object Parent");
p == null ? "parent none" : "children=" + p.transform.childCount
```

Expected: `children=1` 以上。および ModItemExplorer の「Model」フォルダに配置アイテムが出る。

- [ ] **Step 4: 同一アイテム2個目・削除・シーン遷移の検証**

- 同じアイテムをもう一度配置 → 名前に "(2)" が付き 2 個共存すること
- ツリーから削除 → GameObject が消える（`children` が減る）こと
- シーン遷移（エディット→メイン等）→ 自前配置分が破棄され例外が出ないこと（`tail_log` でエラー確認）

- [ ] **Step 5: MTE 共存確認（MTE 導入環境なら）**

配置プラグインリストに MTE 由来の名前と "ModItemExplorer" が併記され、MTE 側の配置・削除が従来どおり動くこと。

- [ ] **Step 6: 発見した不具合を修正してコミット**

修正の都度、該当 Task のファイルを直し再ビルド・再検証。最後に:

```bash
git add -A source/COM3D2.ModItemExplorer.Plugin
git commit -m "fix: 実機検証で見つかった配置機能の不具合を修正"
```

（不具合ゼロならこのコミットは不要）

---

## Self-Review メモ（作成時に確認済み）

- スコープ: MaidEquip（.menu）配置のみ。BGObject / MyRoom / 背景 / アニメ（anime コマンド）/ 移動・回転・スケールの専用 UI は今回のスコープ外（YAGNI）。ギズモは移動のみ有効で最小限の操作性を確保
- 型整合: `ModelPlacerManager` の 5 メンバは既存呼び出し側（`ModItemManager.cs:666,692,741,1883,1888` / `ModItemWindow.cs:816,825`)のシグネチャと一致。`StudioModelStatWrapper` / `OfficialObjectInfoWrapper` は既存 POCO を流用し `original == null` を自作分の目印とするが、判定は `Owns()`（pluginName 比較）に集約
- 既知リスク: `GizmoRenderTarget` のメンバ名は 2.5 実物確認を Task 3 に組み込み済み。menu コマンドの引数並びは Menu.cs 照合を Task 2 に組み込み済み
