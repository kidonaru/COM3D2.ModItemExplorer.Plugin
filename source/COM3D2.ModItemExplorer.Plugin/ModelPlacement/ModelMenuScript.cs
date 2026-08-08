using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using COM3D2.MotionTimelineEditor;

namespace COM3D2.ModItemExplorer.Plugin
{
    /// <summary>
    /// マテリアル変更コマンドの内容
    /// </summary>
    public struct MaterialChange
    {
        public int materialNo;
        public string fileName;
    }

    /// <summary>
    /// テクスチャ変更コマンドの内容
    /// </summary>
    public struct TextureChange
    {
        public int materialNo;
        public string propName;
        public string fileName;
    }

    /// <summary>
    /// 配置に必要なコマンドだけを .menu から抜き出したもの。
    /// MenuInfo はキャッシュ都合でマテリアル/テクスチャ変更を保持しないため、配置時に改めてパースする。
    ///
    /// マテリアル番号には Menu.ProcScript が解釈する "mpn=番号&amp;mpn=番号" 形式もあるが、
    /// 番号の選択に装着スロットの MPN が必要で単体配置では決まらないため、その形式は解決不能として捨てる。
    /// </summary>
    public class ModelMenuScript
    {
        public string modelFileName;
        public readonly List<MaterialChange> materialChanges = new List<MaterialChange>();
        public readonly List<TextureChange> textureChanges = new List<TextureChange>();

        // 採用した additem のスロット名。マテリアル/テクスチャ変更は同一スロット宛のものだけ適用する
        private string _slotName;

        /// <summary>
        /// .menu を読み込み、配置用コマンドを抽出する。失敗時は null を返す。
        /// </summary>
        public static ModelMenuScript Load(string menuFileName)
        {
            try
            {
                byte[] buffer;
                using (var fileBase = GameUty.FileOpen(menuFileName))
                {
                    if (fileBase == null || !fileBase.IsValid())
                    {
                        MTEUtils.LogWarning("menuファイルが開けませんでした。{0}", menuFileName);
                        return null;
                    }
                    buffer = fileBase.ReadAll();
                }

                var script = new ModelMenuScript();

                using (var reader = new BinaryReader(new MemoryStream(buffer), Encoding.UTF8))
                {
                    var header = reader.ReadString();
                    if (header != "CM3D2_MENU")
                    {
                        MTEUtils.LogWarning("menuのヘッダーが不正です。{0} header={1}", menuFileName, header);
                        return null;
                    }

                    reader.ReadInt32();     // version
                    reader.ReadString();    // path
                    reader.ReadString();    // name
                    reader.ReadString();    // category
                    reader.ReadString();    // setumei
                    reader.ReadInt32();     // bodySize

                    // #if で無効化されたブロックを読み飛ばすための状態。Menu.ProcScript と同じ扱い
                    var isSkipping = false;
                    var wasConditionTrue = false;

                    for (;;)
                    {
                        var argCount = reader.ReadByte();
                        if (argCount == 0)
                        {
                            break;
                        }

                        var text = string.Empty;
                        for (int i = 0; i < argCount; i++)
                        {
                            text = text + "\"" + reader.ReadString() + "\"";
                        }
                        if (string.IsNullOrEmpty(text))
                        {
                            continue;
                        }

                        var command = UTY.GetStringCom(text);
                        if (command == "end")
                        {
                            break;
                        }

                        var strings = UTY.GetStringList(text);
                        if (ProcConditional(command, strings, ref isSkipping, ref wasConditionTrue))
                        {
                            continue;
                        }
                        if (isSkipping)
                        {
                            continue;
                        }

                        script.ProcCommand(command, strings, menuFileName);
                    }
                }

                return script;
            }
            catch (Exception e)
            {
                MTEUtils.LogWarning("menuの解析に失敗しました。{0}", menuFileName);
                MTEUtils.LogException(e);
                return null;
            }
        }

        /// <summary>
        /// 条件ディレクティブを処理する。Menu.ProcScript の #if / #else / #endif と同じ判定。
        /// 条件ディレクティブそのものだった場合に true を返す
        /// </summary>
        private static bool ProcConditional(
            string command,
            string[] strings,
            ref bool isSkipping,
            ref bool wasConditionTrue)
        {
            switch (command)
            {
                case "#if":
                    // "#if isplugin in プラグイン名&プラグイン名" 形式のみ。1つでも無効なら丸ごとスキップ
                    if (strings.Length >= 4
                        && string.Equals(strings[1], "isplugin", StringComparison.OrdinalIgnoreCase)
                        && strings[2] == "in")
                    {
                        var shouldSkip = false;
                        foreach (var name in strings[3].Split('&'))
                        {
                            shouldSkip |= string.Equals(name, "isCREditSystemSupport", StringComparison.OrdinalIgnoreCase)
                                ? !Product.isCREditSystemSupport
                                : !PluginData.IsEnabled(name);
                        }
                        isSkipping = shouldSkip;
                        wasConditionTrue = !shouldSkip;
                    }
                    return true;

                case "#else":
                    isSkipping = wasConditionTrue;
                    wasConditionTrue = false;
                    return true;

                case "#endif":
                    isSkipping = false;
                    wasConditionTrue = false;
                    return true;
            }

            return false;
        }

        private void ProcCommand(string command, string[] strings, string menuFileName)
        {
            switch (command)
            {
                case "additem":
                    // strings[1] = モデルファイル名, strings[2] = スロット名。
                    // セット物は additem を複数持つが、配置対象は最初の1つだけ
                    if (modelFileName == null && strings.Length >= 2)
                    {
                        modelFileName = strings[1];
                        _slotName = strings.Length >= 3 ? strings[2] : null;
                    }
                    break;

                case "マテリアル変更":
                    // strings[1]=スロット名, strings[2]=マテリアル番号, strings[3]=.mateファイル名
                    if (strings.Length >= 4 && IsTargetSlot(strings[1]))
                    {
                        int materialNo;
                        if (int.TryParse(strings[2], out materialNo))
                        {
                            materialChanges.Add(new MaterialChange
                            {
                                materialNo = materialNo,
                                fileName = strings[3],
                            });
                        }
                        else
                        {
                            MTEUtils.LogDebug("マテリアル番号を解決できないため無視します。{0} no={1}", menuFileName, strings[2]);
                        }
                    }
                    break;

                case "tex":
                case "テクスチャ変更":
                    // strings[1]=スロット名, strings[2]=マテリアル番号, strings[3]=プロパティ名, strings[4]=.texファイル名
                    // "tex" にはリセット用の短い形式があるため引数数で判別する
                    if (strings.Length >= 5 && IsTargetSlot(strings[1]))
                    {
                        int materialNo;
                        if (int.TryParse(strings[2], out materialNo))
                        {
                            textureChanges.Add(new TextureChange
                            {
                                materialNo = materialNo,
                                propName = strings[3],
                                fileName = strings[4],
                            });
                        }
                        else
                        {
                            MTEUtils.LogDebug("マテリアル番号を解決できないため無視します。{0} no={1}", menuFileName, strings[2]);
                        }
                    }
                    break;
            }
        }

        /// <summary>
        /// 採用した additem と同じスロット宛の変更かどうか。
        /// スロット名が分からない menu では絞り込めないため全て受け入れる
        /// </summary>
        private bool IsTargetSlot(string slotName)
        {
            return _slotName == null || _slotName == slotName;
        }
    }
}
