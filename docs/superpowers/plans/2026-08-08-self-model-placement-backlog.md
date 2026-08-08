# 自前モデル配置機能 残タスクバックログ

初期実装（feat/self-model-placement、master へマージ済み）完了時点の残タスク一覧。
基盤: `ModelPlacement/`（ModelMeshLoader / ModelMenuScript / SelfModelPlacer / ModelPlacerManager）。

## 優先度: 高（操作性の基本が揃っていない）

### 1. 数値入力の Transform UI（位置・回転・スケール）
- **内容**: 配置済みモデル選択時の情報ペインに position / rotation(オイラー) / scale の数値入力＋リセットボタンを出す。
- **理由**: これが無い限り回転・拡縮ギズモも解禁できない（誤操作を戻す手段が無いため）。他の残タスクの前提。
- **実装の当たり**: `ModItemWindow.DrawModelInfo()` で `selectedMenuItem` が `ModelMenuItem` かつ `SelfModelPlacer.Owns(model)` のとき、`model.obj`（ラッパー GameObject）の Transform を編集する UI を追加。GUIView に既存のスライダー/フロート入力部品があるか確認して流用。
- **依存**: なし。

### 2. 回転・拡縮ギズモの解禁
- **内容**: `SelfModelPlacer.AddGizmo` の `eRotate` / `eScal` を有効化し、移動/回転/拡縮の排他トグル UI を付ける（SceneCapture の `ModelPane` / `UpdateModelPane` の排他制御が参考）。
- **理由**: 現状 `eAxis`（移動）のみ。リセット手段（タスク1）ができ次第解禁できる。
- **依存**: タスク1（リセット手段）。

### 3. 配置位置の初期値をカメラ前に
- **内容**: 生成時にワールド原点固定ではなく、カメラ前方の床上（例: `カメラ位置 + forward数m を y=0 に投影`）に置く。
- **理由**: 原点がカメラ外だと「配置したのに見えない」となり体験が悪い。
- **実装の当たり**: `SelfModelPlacer.CreateModel` でラッパー GameObject の position を設定するだけ。`GameMain.Instance.MainCamera` から算出。
- **依存**: なし。小粒なので最初に着手してもよい。

## 優先度: 中（機能の完成度）

### 4. 表示/非表示トグル UI
- **内容**: 配置済みモデルの visible を後から切り替える UI。`StudioModelStatWrapper.visible` ⇔ ラッパーの `SetActive` を同期。
- **実装の当たり**: タスク1と同じ情報ペイン拡張に相乗り。MTE 側モデルの visible 切り替えは MTE の管轄なので自前分のみ対象にする。
- **依存**: タスク1の UI 置き場。

### 5. 保存・復元（シーンをまたぐ持ち越し / プリセット）
- **内容**: 配置内容（menu fileName / group / visible / position / rotation / scale）を XML 等に保存し、復元時は同じ生成経路を再実行して Transform を適用する。
- **参考**: SceneCapture の `Instances.SaveModels` / `ModelInfo`。ただし保存キーと読込キーの不一致バグ（`ModelCastShadow` vs `CastShadow`）を踏まないこと。
- **設計判断が必要**: 自動復元（シーン遷移時に復元）か、明示的なプリセット保存/読込か。現状の「シーン遷移で全破棄」(`ModItemManager.OnChangedSceneLevel`) との整合を決める。
- **依存**: なし（ただしタスク1〜3 の後のほうが保存項目が確定する）。

## 優先度: 低（あれば嬉しい）

### 6. アタッチポイント / メイドへの追従
- **内容**: `attachPoint` / `attachMaidSlotNo` を使い、メイドのボーン（手・頭等）にモデルを追従させる。
- **実装の当たり**: MTE の attachPoint 列挙とボーン名対応を調べ、ラッパー GameObject をボーン Transform の子にする（またはLateUpdateで追従）。スケール汚染に注意（ボーン配下は _SCL_ の影響を受ける）。
- **依存**: なし。ニーズが出てから着手で十分。

### 7. スコープ外のままにしている項目（意図的な未対応）
- BGObject / MyRoomObject / 背景の配置（`PhotoBGObjectData` / `PlacementData` 分岐の追加で対応可能）
- .menu の `anime` / `animematerial` コマンド（アイテムアニメーション再生）
- 影の ON/OFF トグル

必要になった時点でバックログに昇格させる。

## 実装順の推奨

3（カメラ前配置・小粒） → 1（Transform UI） → 2（ギズモ解禁） → 4（visibleトグル） → 5（保存・復元） → 6以降はニーズ次第。
