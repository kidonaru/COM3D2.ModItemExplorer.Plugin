using System;
using System.Globalization;
using System.Xml;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.ModItemExplorer.Plugin
{
    /// <summary>
    /// SceneCapture プリセット XML の Models セクションを自前配置へ取り込む。
    /// 仕様は SceneEditor プラグインの docs/scenecapture-import-guide.md を参照
    /// </summary>
    internal static class SceneCaptureImporter
    {
        /// <summary>ModelType の MaidEquip。menu 由来のモデルのみ自前配置で再現できる</summary>
        private const int ModelTypeMaidEquip = 0;

        private const string MenuExtension = ".menu";

        /// <summary>
        /// SceneCapture プリセット XML を反映する。既存の自前配置分は置き換える。
        /// Models セクションが無い・空なら現在の配置には触れず true を返す
        /// </summary>
        public static bool Apply(string xml)
        {
            if (string.IsNullOrEmpty(xml))
            {
                MTEUtils.LogWarning("SceneCaptureのXMLが空です");
                return false;
            }

            try
            {
                var doc = new XmlDocument();
                // 外部から配られたプリセットも読むため、DTD の外部実体参照を解決させない
                doc.XmlResolver = null;
                doc.LoadXml(xml);

                var modelNodes = doc.SelectNodes("/Preset/Models/Model");

                // 担当セクションが空なら、他プラグインの担当分だけのプリセットとして黙って受け流す
                if (modelNodes == null || modelNodes.Count == 0)
                {
                    return true;
                }

                var preset = new ModelPlacementPreset();
                // 想定内の除外（menu を持たない種別）と異常な除外を混ぜると、ログで区別できなくなる
                var unsupported = 0;
                var invalid = 0;
                foreach (XmlNode node in modelNodes)
                {
                    if (ReadInt(node, "ModelType", ModelTypeMaidEquip) != ModelTypeMaidEquip)
                    {
                        unsupported++;
                        continue;
                    }

                    var menuFileName = ResolveMenuFileName(ReadText(node, "MenuFileName"));
                    if (menuFileName == null)
                    {
                        invalid++;
                        continue;
                    }

                    preset.items.Add(BuildItem(node, menuFileName));
                }

                if (unsupported > 0)
                {
                    // 背景・マイルーム家具などは menu を持たず、自前配置の生成経路に乗らない
                    MTEUtils.Log("menuを持たないモデル{0}体は配置できないため除外しました", unsupported);
                }
                if (invalid > 0)
                {
                    MTEUtils.LogWarning("menuファイル名が不正なモデル{0}体を除外しました", invalid);
                }

                var restored = SelfModelPlacer.instance.ApplyPreset(preset);
                MTEUtils.Log("SceneCaptureのモデルを反映しました。({0}/{1}体)", restored, modelNodes.Count);
                return true;
            }
            catch (Exception e)
            {
                MTEUtils.LogWarning("SceneCaptureのモデルの反映に失敗しました");
                MTEUtils.LogException(e);
                return false;
            }
        }

        /// <summary>Model 要素 1 つを配置データへ変換する</summary>
        private static ModelPlacementPresetItem BuildItem(XmlNode node, string menuFileName)
        {
            var item = new ModelPlacementPresetItem
            {
                fileName = menuFileName,
                // 同一 menu が複数あっても SelfModelPlacer が採番し直すため、常に 0 で渡す
                group = 0,
                // SceneCapture は表示状態を持たないため、常に表示で復元する
                visible = true,
            };

            Vector3 position;
            if (TryParseVector3(ReadText(node, "Position"), out position))
            {
                // 自前配置の親は原点・無回転のため、ワールド座標をそのままローカル値として使える
                item.posX = position.x;
                item.posY = position.y;
                item.posZ = position.z;
            }

            Quaternion rotation;
            if (TryParseQuaternion(ReadText(node, "Rotation"), out rotation))
            {
                // 保存側はクォータニオン。UI 表示に合わせて -180〜180 のオイラー角へ直す
                var euler = rotation.eulerAngles;
                item.rotX = Mathf.DeltaAngle(0f, euler.x);
                item.rotY = Mathf.DeltaAngle(0f, euler.y);
                item.rotZ = Mathf.DeltaAngle(0f, euler.z);
            }

            Vector3 scale;
            if (TryParseVector3(ReadText(node, "LocalScale"), out scale))
            {
                item.sclX = scale.x;
                item.sclY = scale.y;
                item.sclZ = scale.z;
            }

            return item;
        }

        /// <summary>
        /// GameUty で開ける menu ファイル名へ寄せる。menu 以外・空なら null
        /// </summary>
        private static string ResolveMenuFileName(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            // パス付きで保存されている場合があるため、ファイル名だけを取り出す
            var separator = text.LastIndexOfAny(new[] { '/', '\\' });
            var fileName = separator >= 0 ? text.Substring(separator + 1) : text;

            return fileName.EndsWith(MenuExtension, StringComparison.OrdinalIgnoreCase) ? fileName : null;
        }

        private static string ReadText(XmlNode node, string name)
        {
            var child = node.SelectSingleNode(name);
            return child?.InnerText.Trim();
        }

        private static int ReadInt(XmlNode node, string name, int defaultValue)
        {
            int value;
            var text = ReadText(node, name);
            if (string.IsNullOrEmpty(text)
                || !int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                return defaultValue;
            }
            return value;
        }

        private static bool TryParseVector3(string text, out Vector3 value)
        {
            value = Vector3.zero;

            float[] values;
            if (!TryParseFloats(text, 3, out values))
            {
                return false;
            }

            value = new Vector3(values[0], values[1], values[2]);
            return true;
        }

        private static bool TryParseQuaternion(string text, out Quaternion value)
        {
            value = Quaternion.identity;

            float[] values;
            if (!TryParseFloats(text, 4, out values))
            {
                return false;
            }

            value = new Quaternion(values[0], values[1], values[2], values[3]);
            return true;
        }

        /// <summary>"a,b,c" 形式を count 個の float として読む（数値は InvariantCulture）</summary>
        private static bool TryParseFloats(string text, int count, out float[] values)
        {
            values = null;

            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            var parts = text.Split(',');
            if (parts.Length != count)
            {
                return false;
            }

            var parsed = new float[count];
            for (int i = 0; i < count; i++)
            {
                if (!TryParseFloatText(parts[i], out parsed[i]))
                {
                    return false;
                }
            }

            values = parsed;
            return true;
        }

        private static bool TryParseFloatText(string text, out float value)
        {
            return float.TryParse(
                text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
    }
}
