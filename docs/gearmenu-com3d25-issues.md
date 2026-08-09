# ギアメニューが COM3D2.5 で表示されないことがある問題の調査

`source/COM3D2.ModItemExplorer.Plugin/MTEUtils/COM3D2.GUIExt.cs`（[COM3D2.GUIExt](https://github.com/tsuneko/COM3D2.GUIExt) 由来）を、同じ用途で COM3D2.5 対応済みの `COM3D2.KissYourMaid_0.2.3.2/src/GearMenu.cs`（[cm3d2_plugins_okiba/Lib/GearMenu.cs](https://github.com/neguse11/cm3d2_plugins_okiba/blob/master/Lib/GearMenu.cs) 由来）と突き合わせた結果。

疑わしい順に列挙する。**1 と 2 が最小修正の対象**。

## 1. `_SysShortcut` を static フィールドで一度だけキャッシュしている（最有力）

`COM3D2.GUIExt.cs:14`

```csharp
private static SystemShortcut _SysShortcut = GameMain.Instance.SysShortcut;
```

KYM 側は毎回取り直している（`GearMenu.cs:449`）。

```csharp
public static SystemShortcut SysShortcut { get { return GameMain.Instance.SysShortcut; } }
```

static フィールド初期化子が走るのは「`GUIExt` に最初に触れた瞬間」＝ `Initialize()` → `AddGearMenu()`（`COM3D2.ModItemExplorer.Plugin.cs:205`、プラグインの `Start()`）。この時点で `GameMain.Instance` または `SysShortcut` が未生成だと NRE となり **TypeInitializationException** になる。

CLR は失敗した型初期化子の結果をキャッシュするため、以後そのセッションでは `GUIExt` に触るたび例外が再送出され、ギアアイコンは二度と追加されない。例外は `Initialize()` の catch でログに落ちるだけなので静かに死ぬ。ロード順に依存するため「表示されないことがある」という症状と一致する。

**対処**: プロパティ化して型初期化子から `GameMain` 依存を外す。加えて `AddGearMenu` を `SysShortcut` 生成後まで遅延させる。

## 2. 追加が `Start()` 1 回きりで、リトライも再生成もない

KYM は `OnLevelWasLoaded` ごとに `if (!gearMenuButton)` で作り直す（`COM3D2.KissYourMaid.Plugin.cs:1050-1069`）。Unity の `==` オーバーロードにより破棄済みオブジェクトも検出できる。

本プラグインは `Initialize()` で 1 回呼ぶだけ。`RemoveGearMenu()` はどこからも呼ばれていない死にコード。

**対処**: `OnChangedSceneLevel` で `gearMenuIcon == null` なら `AddGearMenu()` を再実行する。

## 3. 重複チェックがない

KYM の `Add` は先頭で既存ボタンを消してから続行する（`GearMenu.cs:68`）。

```csharp
if (Contains(name)) { Remove(name); }
```

`GUIExt` には無いので、2 の再追加を入れる場合は必須。

## 4. `UIGrid.onReposition` を握っていない（他プラグインとの相互破壊）

`UIGrid.Reposition()` は `arrangement == CellSnap` なら座標を丸めるだけ（`Assembly-CSharp/UIGrid.cs:255-265`）。`GUIExt` が `CellSnap` + `pivot = TopLeft` にしている間は自前レイアウトが生き残るので、単体では問題ない。

問題は **他のギアメニュー系プラグインが後から Add/Remove したとき**。KYM の `PreOnReposition` は `pivot = UIWidget.Pivot.TopRight` に変える（`GearMenu.cs:495`）。TopLeft でなくなると `ResetPosition` 末尾のピボット補正（`UIGrid.cs:288-319`）が Reposition のたびに全子要素を左へシフトするようになり、しかも KYM の再レイアウトは NGUI の `Reposition()` からは呼ばれないため補正が累積する。結果アイコンが Base の外・画面外へ飛ぶ。

KYM 側は `onReposition` に登録したオブジェクトの `Version` フィールドを見て所有権を調停する仕組み（`SetAndCallOnReposition` / `GetOnRepositionVersion`）を持つが、`GUIExt` はこの土俵に乗っていないので一方的に負ける。

**再現条件**: 他のギアメニュー系プラグイン（KissYourMaid 等）を併用しているときだけ発生する。単体で再現するなら原因は別。

## 5. COM3D2.5 で増えたボタンが `DefaultUIButtons` に無い

`COM3D2.GUIExt.cs:15`

```csharp
{ "Config", "Ss", "SsUi", "ToTitle", "Info", "Help", "Dic", "Exit" }
```

COM3D2.5 の `SystemShortcut` は Grid 配下に **`GP003Help`** も持つ（`Assembly-CSharp/SystemShortcut.cs:36-37`）。未知ボタン扱いになり自作アイコンと同じ「その他」枠へ回るため並び順がずれる。

さらに 2.5 は環境次第で以下を `SetActive(false)` する（`SystemShortcut.cs:202-226`）。

| ボタン | 非表示条件 |
|---|---|
| `Dic` | `!Product.isEnglish` |
| `Shop` | `!GameMain.Instance.CMSystem.NetUse` |
| `GP003Help` | `!Product.isCREditSystemSupport` |

`GUIExt` は

- `numButtons = children.Count` で Base 幅を決める（`COM3D2.GUIExt.cs:99-113`）
- 配置ループは非アクティブでも `i++` してスロットを消費する（`COM3D2.GUIExt.cs:142-156`）

の二本立てになっており、`UIGrid.hideInactive` の値次第で枠数と Base 幅がズレる。`GetChildList()` は `hideInactive` が true のとき非アクティブを除外する（`UIGrid.cs:70-81`）。

KYM は `Array.IndexOf(specialNames, name)` で既知ボタンだけ順序固定、残りは出現順という壊れにくい方式（`GearMenu.cs:521-533`）。

## 6. `SystemShortcut.Awake` が後に走ると Base 幅を上書きされる

`SystemShortcut.cs:227`

```csharp
m_uiBase.width = 460 - num * 40;
```

`GUIExt` が計算した幅を潰す。`GUIExt.Add` が `Awake` より先に走る順序になった場合、追加アイコン分の幅が失われて背景の外に出る。1 の対処（`SysShortcut` 生成待ち）と併せれば塞がる。

## 参考: 調査に使ったファイル

| パス | 内容 |
|---|---|
| `source/COM3D2.ModItemExplorer.Plugin/MTEUtils/COM3D2.GUIExt.cs` | 本プラグインのギアメニュー実装 |
| `source/COM3D2.ModItemExplorer.Plugin/COM3D2.ModItemExplorer.Plugin.cs:205-242` | `AddGearMenu` / `RemoveGearMenu` / `UpdateGearMenu` |
| `W:\COM3D2_5\work\COM3D2.KissYourMaid_0.2.3.2\src\GearMenu.cs` | 比較対象（COM3D2.5 対応済み） |
| `W:\COM3D2_5\work\Assembly-CSharp\SystemShortcut.cs` | ゲーム側のギアメニュー本体 |
| `W:\COM3D2_5\work\Assembly-CSharp\UIGrid.cs` | NGUI グリッド。`Reposition` / `ResetPosition` の挙動確認 |
