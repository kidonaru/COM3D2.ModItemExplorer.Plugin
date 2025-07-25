# COM3D2.ModItemExplorer.Plugin

v1.7.0.1


- [COM3D2.ModItemExplorer.Plugin](#com3d2moditemexplorerplugin)
  - [概要](#概要)
  - [インストール方法](#インストール方法)
  - [起動方法](#起動方法)
  - [必須プラグイン](#必須プラグイン)
  - [推奨プラグイン](#推奨プラグイン)
  - [機能一覧](#機能一覧)
  - [機能詳細](#機能詳細)
    - [ヘッダー](#ヘッダー)
      - [メイド衣装ヘッダー](#メイド衣装ヘッダー)
      - [モデル配置ヘッダー](#モデル配置ヘッダー)
    - [アドレスバー](#アドレスバー)
    - [検索バー](#検索バー)
    - [サイドビュー](#サイドビュー)
    - [ライブラリビュー](#ライブラリビュー)
    - [フッター](#フッター)
    - [設定画面](#設定画面)
    - [カラーパレットウィンドウ](#カラーパレットウィンドウ)
    - [カスタムパーツウィンドウ](#カスタムパーツウィンドウ)
    - [髪の長さウィンドウ](#髪の長さウィンドウ)
    - [モーションウィンドウ](#モーションウィンドウ)
  - [変更履歴](#変更履歴)
    - [2025/07/06 v1.7.0.1](#20250706-v1701)
    - [2025/06/07 v1.7.0.0](#20250607-v1700)
    - [2025/04/09 v1.6.0.1](#20250409-v1601)
    - [2025/04/06 v1.6.0.0](#20250406-v1600)
    - [2025/03/23 v1.5.0.0](#20250323-v1500)
    - [2025/02/15 v1.4.0.0](#20250215-v1400)
    - [2025/02/08 v1.3.0.0](#20250208-v1300)
    - [2025/02/02 v1.2.0.1](#20250202-v1201)
    - [2025/02/01 v1.2.0.0](#20250201-v1200)
    - [2025/01/26 v1.1.0.0](#20250126-v1100)
    - [2025/01/25 v1.0.0.2](#20250125-v1002)
    - [2025/01/25 v1.0.0.1](#20250125-v1001)
    - [2025/01/24 v1.0.0.0](#20250124-v1000)
  - [規約](#規約)
    - [MOD規約](#mod規約)
    - [プラグイン開発者向け](#プラグイン開発者向け)


## 概要

Modアイテム用のファイラーです。

検索機能付きでメイドの衣装切り替え、アイテムの配置が可能。

https://github.com/user-attachments/assets/7ae1772f-b8cf-4d72-8587-7fb77fd6707b



## インストール方法

[Releases](https://github.com/kidonaru/COM3D2.ModItemExplorer.Plugin/releases)
から最新の`COM3D2.ModItemExplorer.Plugin-vX.X.X.zip`をダウンロードします。

zip解凍後、`UnityInjector`フォルダの中身を、`Sybaris\UnityInjector`フォルダに配置してください。

各ファイルの説明:
- `COM3D2.ModItemExplorer.Plugin.dll`
  - プラグインの本体。
- `Config/ModItemExplorer_OfficialName.csv`
  - 公式のフォルダ名称一覧ファイル

COM3D2 Ver.2.38.0で動作確認済みです。


## 起動方法

ゲーム起動後、執務室などで`Alt` + `M`キーを押すと、プラグインが有効になります。
（`Config/ModItemExplorer.xml`を編集することでキーの変更も可能です）

初回有効化時、menuファイルのキャッシュを作成するためロードに時間がかかります。
別スレッドで処理をしているので、ロード中も操作可能です。

エディット画面では重くなる可能性があるため、執務室で複数メイドやMeidoPhotoStudioを立ち上げた状態で使用することを推奨します。


## 必須プラグイン

拡張プリセットの読み込みに以下のプラグインが必要です。

- COM3D2.ExternalPreset
- COM3D2.ExternalSaveData


## 推奨プラグイン

MotionTimelineEditorプラグインが入っている場合、モデル配置が可能になります。
（入っていない場合でもメイドの衣装切り替えは可能です）

- **COM3D2.MotionTimelineEditor.Plugin**
  - https://github.com/kidonaru/COM3D2.MotionTimelineEditor.Plugin

モデル配置機能には別途モデル配置プラグイン（複数メイド、MeidoPhotoStudio、SceneCapture）のいずれかが必要になります。
MotionTimelineEditorのREADMEを参考に導入してください。


## 機能一覧

- メイドの衣装切り替え、モデル配置、モーション再生
- Modフォルダ以下のアイテムをフォルダ単位で表示
- サイドビューでフォルダ構造の表示、移動
- プリセットの保存、読み込み、一時記録
  - Modフォルダ以下のプリセットにも対応しています
  - Presetフォルダ以下は、メイド毎にグループ化して表示します
- プリセットの一時記録
  - 着用中のアイテムをメモリ上に一時保存する機能です
- 指定フォルダ以下のアイテム検索
- menuのキャッシュ
  - 初回プラグイン有効化時、全てのmenuファイルを読み込んでキャッシュします
  - 2回目以降はキャッシュを利用するため、ロードが早くなります
  - menuを更新した場合は、更新日時を比較して自動でキャッシュの再生成を行います
- 別スレッドでのロード
  - ファイルのロードは別スレッドで行うため、ロード中も操作可能です
  - サムネ、プリセットは表示時に動的ロードしています
- 重複ファイルの検出


## 機能詳細

![機能詳細](img/img_02.jpg)

### ヘッダー

- **編集モード**: モード切り替え
  - **メイド**: メイド衣装の編集モードに切り替えます
  - **モデル**: モデルの配置モードに切り替えます
  - **設定**: プラグインの設定画面を開きます
- **表示**: 表示する場所の指定
  - **公式**: 公式アイテムを表示します
  - **Mod**: Modフォルダ以下のアイテムを表示します
  - **着用中**: 現在メイドが着用しているアイテム一覧を表示します
  - **配置中**: 現在配置しているモデル一覧を表示します
  - **Preset**: Presetフォルダ以下のプリセット一覧を表示します
  - **一時記録**: 一時保存したプリセットを表示します
  - **検索結果**: 検索結果を表示します

#### メイド衣装ヘッダー

メイド衣装の編集モード時に表示されます。

- **メイド**: 操作対象のメイドの選択
- **サムネ更新**: 現在のメイドのサムネイルを更新します
- **マスク**: メイドのマスク設定
  - **なし**: 全ての衣装を表示します
  - **下着**: 下着のみ表示します
  - **水着**: 水着のみ表示します
  - **裸**: すべての衣装を非表示にします
- **プリセット**: プリセットの種類を選択
  - **服**: メイドの服装のみを保存します
  - **体**: メイドの体型のみを保存します
  - **服/体**: メイドの服装、体型を保存します
- **セーブ**: プリセットの保存
- **一時記録**: 対象の一時記録を選択
  - 着用中のアイテムを一時的に保存することができます
  - **セーブ**: 一時記録の保存
  - **ロード**: 一時記録のロード

#### モデル配置ヘッダー

モデル配置モード時に表示されます。

- **配置プラグイン**: 配置に使用するプラグインの選択
- **配置**: 選択したモデルを配置


### アドレスバー

現在表示している場所のパスを表示します。
各フォルダ名をクリックすると、そのフォルダに移動します。

- **<**: 一つ前に表示していた場所に戻ります
- **>**: 次に進みます
- **フォルダアイコン**: OSのエクスプローラーでフォルダを開きます
- **リストアイコン**: フラットビューの切り替えが可能
  - フラットビューを有効にすると、サブフォルダの中身を展開してすべてのアイテムを一覧で表示します
- **ソートアイコン**: アイテムのソート順を変更します
  - **基本**: カテゴリ/プライオリティでソートします
  - **名前**: アイテム名でソートします
  - **更新日時**: 更新日時でソートします


### 検索バー

表示中のフォルダ以下のアイテムを検索します。


### サイドビュー

フォルダ構造の表示、移動が可能です。
`>`をクリックすると、中身を展開してサブフォルダを表示します。


### ライブラリビュー

選択したフォルダ内のアイテム、サブフォルダを表示します。

着用中のアイテムは緑枠で表示されます。アイコン左上の`x`ボタンをクリックすると、アイテムを外すことができます。`☆`ボタンをクリックすると、お気に入りに登録することができます。

メイド編集モード時には、アイテムをクリックするとメイドに着用させることができます。
モデル配置モード時には、アイテムをクリックするとモデルを選択し、`配置`ボタンをクリックすると配置することができます。

`Shift`キーを押しながらアイテムをクリックすると、格納されているフォルダをOSのエクスプローラーで開くことができます。


### フッター

マウスオーバー時にアイテムの詳細を表示します。

- **□**: ドラッグしてウィンドウサイズを変更できます


### 設定画面


- **公式アイテムをMPN毎に表示する**: 公式アイテムをMPN毎にグループ化して表示します
  - チェックを外した場合、menuのパス毎に表示します
- **ModアイテムをMPN毎に表示する**: ModアイテムをMPN毎にグループ化して表示します
  - チェックを外した場合、格納フォルダのパス毎に表示します
- **アイテム選択時に詳細ログ表示**: アイテムを選択した際に、アイテムの詳細をログに表示します
- **メニューの説明欄も検索する**: メニューの説明欄も検索対象にします
- **アイテム名 背景透過度**: アイテム名の背景の透過度を設定します
- **タグ 背景透過度**: タグの背景の透過度を設定します
- **フラットビューの基本アイテム数**: 選択フォルダ内の合計アイテム数がこの数以下の場合に、デフォルトでフラットビューにします
- **カスタムパーツ選択時の自動編集**: 選択したアイテムのカスタムパーツが有効な場合、自動で編集モードに切り替えます
- **カスタムパーツの移動範囲**: カスタムパーツの移動範囲を設定します
- **重複ファイルチェック**: 重複ファイルを検出します。チェックを入れた拡張子が対象になります
  - **出力**: 重複ファイルを検出したファイルパスを出力します
- **アイテム更新**: アイテム一覧を更新します
- **設定をリセット**: 設定を初期化します
- **キャッシュを再構築**: menuのキャッシュを再構築します


### カラーパレットウィンドウ

![カラーパレットウィンドウ](img/img_03.jpg)

選択中のアイテムの無限色変更を行うことができます。

- **リセット**: 現在のカテゴリ色(基本色/影色/輪郭色)をリセットします
  - リセット時の色は、アイテム選択時点の色になります
- **全リセット**: 全カテゴリ色をリセットします


### カスタムパーツウィンドウ

![カスタムパーツウィンドウ](img/img_04.jpg)

選択中のアイテムの位置調整などができます。

- **有効**: カスタムを有効にします
- **編集**: カスタムパーツの編集モードに切り替えます
- **リセット**: アイテム選択時のパラメータにリセットします
- **コピー**: 現在のパラメータをコピーします
- **ペースト**: コピーしたパラメータをペーストします
- **X/Y/Z**: 位置調整
- **RX/RY/RZ**: 回転調整
- **SX/SY/SZ**: スケール調整


### 髪の長さウィンドウ

![髪の長さウィンドウ](img/img_05.jpg)

選択中のアイテムの髪の長さ調整ができます。

- **リセット**: 髪の長さをリセットします
- **C**: 現在の髪の長さをコピーします
- **P**: コピーした髪の長さをペーストします


### モーションウィンドウ

![モーションウィンドウ](img/img_06.jpg)

モーションの再生を制御できます。

- **拡張**: 有効にすると複数アニメレイヤーでの再生制御ができます
- **レイヤー選択**: チェックボックスでレイヤーを選択した状態でアイテムを選択すると、そのレイヤーでモーションを再生します
- **再生時間**: モーションの再生時間を指定します
- **重み**: モーションの重みを指定します
- **速度**: モーションの再生速度を指定します


## 変更履歴


### 2025/07/06 v1.7.0.1

- FBフェイスでハイライトが片目にしか反映されないバグの修正


### 2025/06/07 v1.7.0.0

- 他のプラグインでモデル配置後に初期化するとクラッシュするバグの修正


### 2025/04/09 v1.6.0.1

- メイド読込中のエラー修正
- モーションウィンドウのUI調整


### 2025/04/06 v1.6.0.0

- 複数アニメレイヤーでのモーション再生対応
  - 複数同時にモーションを再生して、任意の重み/速度での再生が可能です
  - 最大8アニメレイヤーまで設定できます


### 2025/03/23 v1.5.0.0

- モーション読み込み対応
  - スタジオモードで再生できるモーションと、Modフォルダ以下のanmファイルが再生できます
- ルート以下の空ディレクトリをタブから開けるように修正


### 2025/02/15 v1.4.0.0

- 一部バリエーションのあるアイテムが表示されない問題を修正
  - 今回のアプデでキャッシュが再生成されます
- アイテムの名前/更新日時順ソート追加


### 2025/02/08 v1.3.0.0

- カスタムパーツの編集機能追加
- 髪の長さ調整機能追加
- バリエーションアイテム内も検索対象に追加
- メニューの説明欄も検索対象に追加
  - 重いのでデフォルトでは無効、設定画面から有効化可能です
- 非表示のメイドは選択できないように修正
- スタジオモードでBackgroundCustom.Pluginでの配置に対応


### 2025/02/02 v1.2.0.1

- カラーパレットの色相変更軽量化
- キャッシュ生成時の不要なログ削除


### 2025/02/01 v1.2.0.0

- カラーセット付きバリエーション(髪の毛など)に対応
  - バリエーション/カラーセットの切り替えは別ウィンドウ化しました
- サムネ更新ボタン追加
- カラーパレットウィンドウ追加
- 描画周り最適化

※キャッシュにカラー情報を追加するためアップデートでキャッシュの再生成が走ります


### 2025/01/26 v1.1.0.0

- お気に入り機能追加
- キャッシュ更新時にファイルサイズが小さくなった場合、ロード時にクラッシュしていたのを修正
  - ※アップデートでキャッシュの再生成が走ります


### 2025/01/25 v1.0.0.2

- 特定の環境でログが大量に出力されるバグの修正
- キャッシュ再構築時に公式menuが読み込めないバグの修正


### 2025/01/25 v1.0.0.1

- Modフォルダ以下のプリセットと、一時記録の拡張プリセット対応
- タグのセット系名称を省略


### 2025/01/24 v1.0.0.0

- 公開版リリース


## 規約

### MOD規約

※MODはKISSサポート対象外です。
※MODを利用するに当たり、問題が発生してもKISSは一切の責任を負いかねます。
※「カスタムメイド3D2」か「カスタムオーダーメイド3D2」か「CR EditSystem」を購入されている方のみが利用できます。
※「カスタムメイド3D2」か「カスタムオーダーメイド3D2」か「CR EditSystem」上で表示する目的以外の利用は禁止します。
※これらの事項は http://kisskiss.tv/kiss/diary.php?no=558 を優先します。


他の機能追加などをしたい場合は、リポジトリを公開しているのでこちらにPRをお願いします。
https://github.com/kidonaru/COM3D2.ModItemExplorer.Plugin

質問、要望などは@kidonaruまで (可能な範囲で対応します)
https://twitter.com/kidonaru


### プラグイン開発者向け

このプラグインの開発に手伝っていただける場合、下記手順でプルリクエストを送信してください。

1. このリポジトリをフォークします

2. フォークしたリポジトリを、ローカルの`COM3D2\Sybaris`以下にクローン
```bash
cd [COM3D2のインストールフォルダ]\Sybaris
git clone https://github.com/[自分のユーザー名]/COM3D2.ModItemExplorer.Plugin.git
```

3. クローンしたフォルダをVS Codeなどで開く

4. コード修正後、デバッグ用ビルドスクリプトを実行し動作確認
(自動でUnityInjector内にコピーされます)
```bash
.\debug.bat
```

5. 差分をリモートにプッシュして、フォーク元に対してプルリクエストを送信
