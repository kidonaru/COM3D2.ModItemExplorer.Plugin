# 要望: 配置モデルを Charactor レイヤーから分離する

作成日: 2026-09-05 / 起票元: COM3D2.PostEffects.Plugin
対象: `source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/SelfModelPlacer.cs`

## 要望

MIE が配置するモデルを **Charactor(10) 以外のレイヤー（Default(0) を想定）へ載せてほしい**。

## 背景

PostEffects の環境遮蔽 (SSAO) には「キャラに適用しない」オプションがある。これはサブカメラで
**Charactor(10) レイヤーだけを白塗りしたマスク**を描き、遮蔽 RT からその画素を消し込む実装になっている
（`COM3D2.PostEffects.Plugin/source/COM3D25.PostEffects.Plugin/Effects/CharacterMask.cs`）。

MIE の配置モデルはメイドと同じ Charactor レイヤーに載るため、このマスクに巻き込まれ、
**「キャラに適用しない」を ON にすると配置モデルからも AO が消える**。

レイヤー決定箇所は `SelfModelPlacer.GetModelLayer()`（`ModelPlacement/SelfModelPlacer.cs:1839`）:

```csharp
private static int GetModelLayer()
{
    var layer = LayerMask.NameToLayer("Character");
    return layer >= 0 ? layer : 10;
}
```

COM3D2.5 の実レイヤー名は `Character` ではなく **`Charactor`**（綴りが異なる）。そのため
`NameToLayer` は常に -1 を返し、フォールバックの `10` = Charactor が必ず採用されている。
現状は「名前解決に失敗したときの保険」ではなく、実質的に 10 固定になっている。

## 実機で確認した挙動

COM3D2.5 稼働中に devbridge で計測。シーンは MIE 配置の Midnight Carnival ステージ 3 点（画面の約 47%）＋メイド 1 体。

### 1. AO の除外にモデルが巻き込まれる

配置モデルの占有画素だけを対象にした平均輝度（値が小さいほど AO が濃い）:

| 条件 | モデル領域の平均輝度 |
|---|---|
| モデル = Charactor(10) / 「キャラに適用しない」ON（現状） | 102.55 |
| モデル = Default(0) / 「キャラに適用しない」ON（要望後） | 91.38 |
| モデル = Default(0) / 「キャラに適用しない」OFF | 90.79 |

Charactor のままだと AO が完全に消えている。Default へ移すと AO 無効時とほぼ同じ濃さまで戻り、
メイドだけが除外された状態になる（残差 0.6 はメイドがモデルへ落とす AO 分）。
目視でもステージのトラス・床の接地部にコンタクトシャドウが復帰することを確認済み。

### 2. 「メイドを隠す」でもモデルが消える

同じレイヤー共有により、PostEffects の `MaidHideEffect`（Charactor/Face を cullingMask から外す実装）
でも配置モデルが道連れで消える。メイド非表示相当の cullingMask で 1 フレーム描画したところ、
**モデル領域の 56.1%（182675/325351 px）の画素が変化**した。

## 期待する変更

`GetModelLayer()` の戻り値を Charactor 以外にする。`Room` / `Chair` / `Tree` などゲーム側の
背景プロップは Default(0) に載っているので、配置モデルも **Default(0)** が素直だと考えている。

```csharp
private static int GetModelLayer()
{
    return 0; // Default。背景プロップと同じ扱いにする
}
```

CameraMain の cullingMask は `0xAFFF6EDF` で Default(0) を含むため、描画自体は問題なく行われることを実機で確認済み。

## 確認してほしい影響範囲

MIE 側の事情は把握していないため、以下は起票側では未検証:

- 配置モデルの選択・ドラッグ操作が Raycast のレイヤーマスクに依存していないか
  （`ModelPlacement/` 配下の grep では layerMask 指定つき Raycast は見つからなかった）
- 配置プリセットや SceneEditor 連携でレイヤー値を保存・復元していないか
- ゲーム側のレイヤー依存処理（被写界深度のフォーカス対象、VR 掴み、モザイク等）への影響
- 既存の配置済みモデルを含むシーン／プリセットを読み直したときの互換性

## 代替案（PostEffects 側で対応する場合）

MIE 側で動かせない場合、PostEffects 側で「Charactor レイヤー全体」ではなく
「`Maid.body0` 配下の Renderer だけ」をマスクする方式へ変更する余地はある。
ただしサブカメラのレイヤー描画では表現できず、`CommandBuffer` / `Graphics.DrawMesh` で
対象 Renderer を明示描画する実装に作り替える必要があり、コストは MIE 側の 1 行変更より大きい。
