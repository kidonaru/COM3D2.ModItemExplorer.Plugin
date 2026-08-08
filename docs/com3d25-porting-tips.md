# COM3D2.5 移植 Tips

COM3D2 用 UnityInjector プラグインを COM3D2.5 に移植した際の知見まとめ。
本リポジトリでの実作業（v1.7.0.1 → COM3D2.5 両対応）に基づく。

## 環境の違い

| 項目 | COM3D2 | COM3D2.5 |
|---|---|---|
| Unity | 5.6 系 | 2022.3 系 |
| .NET (Managed) | 3.5 相当 | 4.x 相当 |
| プラグインローダー | Sybaris + UnityInjector | BepInEx + SybarisLoader Patcher + UnityInjectorLoader シム |
| UnityEngine | 単一 `UnityEngine.dll` | モジュール分割（`UnityEngine.CoreModule.dll` 等）+ facade |
| プラグイン配置先 | `Sybaris\UnityInjector\` | 同じ（シム経由でロードされる） |

- COM3D2.5 に `UnityInjector.dll` の実体は無い。`BepInEx\plugins\UnityInjectorLoader\BepInEx.UnityInjectorLoader.dll` が
  `UnityInjector.PluginBase` 等の型をランタイム提供し、アセンブリ名 `UnityInjector` への参照を解決する。
  そのためビルド時の参照は **COM3D2 側の `Sybaris\lib\UnityInjector.dll`** をそのまま使う。

## 遭遇した非互換と対処

### 1. `Texture2D.LoadImage(byte[])` が存在しない（最重要）

- Unity 2017 以降、`LoadImage` / `EncodeToPNG` 等は `UnityEngine.ImageConversionModule` の
  **拡張メソッド**（`ImageConversion` クラス）に移動した。
- 旧 DLL をそのまま動かすと `MissingMethodException: Texture2D.LoadImage(byte[])` になる。
- **対処**: csproj に `UnityEngine.ImageConversionModule.dll` の参照を追加するだけ。
  `texture.LoadImage(bytes)` というソース記述は拡張メソッドとしてそのままコンパイルが通る（ソース修正不要）。

### 2. `CharacterMgr.PresetLoad(BinaryReader, string)` が static 化

- 2.5 ではインスタンスメソッドから static メソッドに変更された。
- **対処**: `#if COM3D25` で `CharacterMgr.PresetLoad(...)` に分岐（TempPresetManager.cs 参照）。

### 3. `SysShortcut.VisibleExplanation` の第 1 引数が `string` → `int` に変更

- 2.5 では説明文リストの番号を受け取る。`visible=false` のときは番号を参照しない
  （`SystemShortcut.cs` のデコンパイルで確認済み）ので、非表示呼び出しは `(0, false)` で良い。
- **対処**: `#if COM3D25` で分岐（MTEUtils/COM3D2.GUIExt.cs 参照）。

### 4. 組み込み GUIStyle を OnGUI の外で複製すると空スタイルになる

- `new GUIStyle("button")` は `GUIStyle(string)` コンストラクタではなく、
  **`string` → `GUIStyle` の暗黙変換 + コピーコンストラクタ**にコンパイルされる。
  暗黙変換は `GUISkin.current` を参照し、null なら空の `StyleNotFoundError` を返す。

  ```csharp
  public static implicit operator GUIStyle(string str) {
      if (GUISkin.current == null) {
          Debug.LogError("Unable to use a named GUIStyle without a current skin...");
          return GUISkin.error;   // 背景なし・border 0・padding 0・textColor 黒
      }
      return GUISkin.current.GetStyle(str);
  }
  ```

- Unity 5.6 では OnGUI 終了後も `GUISkin.current` が保持されていたため、static フィールド初期化子や
  コンストラクタで複製しても正しいスタイルが取れていた。Unity 2022 では GUI ループ終了時にクリアされ、
  **OnGUI の外では常に null** になる。
- **症状**: ボタン・ボックスの枠テクスチャ・border・padding・margin が全て失われ、
  文字色も黒（`StyleNotFoundError` の既定値）になる。「文字が黒くなる」だけの問題に見えるが、
  textColor を白に塗る対症療法では枠や余白は戻らない。逆に遅延生成さえ直せば
  組み込みスタイル本来の色（0.9 グレー白）が入るので、textColor の明示設定は不要。
- **対処**: 組み込みスタイル由来の `GUIStyle` は**すべて遅延生成**にし、OnGUI 内で初めて生成されるようにする
  （`GUIView.InitStyles` / `WindowManager.OnGUI` 参照）。`Event.current == null` で OnGUI 外を判定できる。
- 実測値（COM3D2.5 の OnGUI 内で複製した `"button"`）:
  `border=(6,6,6,4) padding=(6,6,3,3) margin=(4,4,4,4) bg=button 12x12 textColor=RGBA(0.9,0.9,0.9,1)`
- **注意**: OnGUI 冒頭でまとめて初期化する場合、その初期化が例外を投げると以降の描画処理に
  到達せず、**全ウィンドウが描画されないまま毎フレーム同じ例外とログを繰り返す**。
  初期化は try-catch で包み、失敗しても初期化済みフラグを立てて再試行を止めること。

### 5. GUIStyle が持つ動的生成テクスチャがシーン遷移で破棄される

- `GUIStyle` は `UnityEngine.Object` ではないため、`GUIStyleState.background` に代入しただけでは
  参照とみなされず、シーン遷移時の `Resources.UnloadUnusedAssets()` でテクスチャが破棄される。
- **対処**: 動的生成した `Texture2D` に `hideFlags = HideFlags.HideAndDontSave` を設定する
  （`GUIView.CreateColorTexture` 参照）。COM3D2 でも潜在的に同じ問題がある。

### 変更不要だったもの

- レガシー `Input` API（`Input.GetKey` 等）: `UnityEngine.InputLegacyModule.dll` が同梱されており動作する
- IMGUI（`GUI.Window` / `OnGUI`）: `UnityEngine.IMGUIModule.dll` 参照で動作する
- `SystemShortcut` の `m_labelExplanation` / `m_spriteExplanation` へのリフレクション: フィールド型は同一

## ビルド設定（csproj）

1. **TargetFrameworkVersion**: `v3.5` のままでは 2.5 の Managed（.NET 4.x）を参照解決できない
   （MSB3258 で参照が黙って落とされ、大量の型解決エラーになる）。`v4.7.1` 以降に上げる。
2. **UnityEngine のモジュール参照**: 2.5 の `UnityEngine.dll` は type forwarder を持つ facade だが、
   転送先モジュールを参照に加えないと CS1069（型は転送されました）になる。
   本プラグインで必要だったのは CoreModule / IMGUIModule / AnimationModule / ImageConversionModule /
   InputLegacyModule / TextRenderingModule / ScreenCaptureModule / PhysicsModule / UnityWebRequestModule。
3. **切替方法**: `GameVersion` プロパティ（`COM3D2` 既定 / `COM3D25`）で TFM・参照・出力先・
   `DefineConstants`（`COM3D25` シンボル）を切り替える。詳細は csproj を参照。
4. **パスの外部化**: ゲームのインストール先は `.env`（gitignore 対象、`.env.sample` をコピーして作成）で
   開発者ごとに指定し、build.bat が MSBuild の `/p:` に渡す。

## デバッグの勘所

- **例外が握りつぶされて無症状になる**: 本プラグインは `Update` / `OnGUI` を try-catch しているため、
  初期化中の `MissingMethodException` が **ログに出ず UI が出ないだけ** の症状になった。
  BepInEx の `LogOutput.log` にエラーが無い ≠ 正常動作。
- **MissingMethodException は JIT 時に発生する**: 該当メソッドを含むメソッド（静的コンストラクタ含む）が
  最初に実行される瞬間に投げられる。型のロード自体は成功するので、プラグインのロード成功ログは当てにならない。
- **静的コンストラクタの例外は致命的**: `TypeInitializationException` になり、以降その型は
  AppDomain 内で恒久的に使用不能になる。静的コンストラクタで環境依存処理をするなら try-catch で保護する。
- **稼働中のゲームで検証する**: devbridge（C# REPL）があれば、ビルドした DLL を
  `Assembly.Load(bytes)` で稼働中のゲームに読み込み、問題のクラスを `Activator.CreateInstance` して
  再起動なしで修正を検証できる。GUIStyle のようなランタイム状態もリフレクションで直接書き換えて
  見た目を先に確認してからソースを直すと速い。
- **IMGUI の状態は使い捨て MonoBehaviour で覗く**: `GUI.skin` は OnGUI 外から触ると
  `ArgumentException: You can only call GUI functions from inside OnGUI.` になるため、REPL から直接は読めない。
  `OnGUI` で計測して static 変数に結果を書き、自分を `Destroy` する使い捨てコンポーネントを
  `AddComponent` すれば、OnGUI 内の実値を REPL 側で読み取れる。ロード済みプラグインの
  GUIStyle をリフレクションで読み出して比較すれば、生成タイミングの誤りも特定できる。

## バッチファイルの罠（ビルドスクリプト改修時）

- **改行コードは CRLF 必須**: LF のみだと cmd が `for` 文などを誤パースする（エディタ・ツールによっては
  LF で保存されるので注意。`unix2dos` で変換）。
- **文字コードは UTF-8 + 先頭で `chcp 65001`**: 日本語コメント・メッセージを含む場合に必要。
- **if ブロック内の `rem` コメントに ASCII の `( )` を書かない**: ブロックが壊れる。
  echo 内では `^(` `^)` でエスケープする。
