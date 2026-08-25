using System;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.ModItemExplorer.Plugin
{
    /// <summary>
    /// SceneEditor プラグインのモデル配置プロバイダ規約用の属性。
    /// アセンブリ参照を避けるため、各プラグインが同名（短名一致）で自前定義する
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class ModelPlacerProviderAttribute : Attribute
    {
    }

    /// <summary>
    /// 自前配置モデルを SceneEditor のタイムラインへ公開するプロバイダ
    /// （規約の詳細は SceneEditor の docs-site/dev/model-placer-guest-guide.md 参照）。
    /// タイムラインは配置の生成・削除・表示・アタッチをここへ委譲する
    /// </summary>
    [ModelPlacerProvider]
    public static class ModelPlacerProvider
    {
        private static SelfModelPlacer placer => SelfModelPlacer.instance;

        public static string ModelPlacerId => SelfModelPlacer.PluginName;

        public static string ModelPlacerDisplayName => "モデル配置 (ModItemExplorer)";

        public static List<GameObject> GetModels()
        {
            var result = new List<GameObject>();
            foreach (var model in placer.modelList)
            {
                var go = model.obj as GameObject;
                if (go != null)
                {
                    result.Add(go);
                }
            }
            return result;
        }

        public static string GetModelFileName(GameObject obj)
        {
            var model = placer.FindModelByGameObject(obj);
            return model?.infoWrapper?.fileName ?? "";
        }

        public static string GetModelDisplayName(GameObject obj)
        {
            var model = placer.FindModelByGameObject(obj);
            return model?.displayName ?? "";
        }

        /// <summary>
        /// type は SceneEditor 側 StudioModelType の enum 名。
        /// 現時点で対応するのは Mod (.menu) と Asset (.asset_bg) のみで、
        /// Prefab / MyRoom は後続タスクで対応する
        /// </summary>
        public static GameObject CreateModel(
            string type, string fileName, int myRoomId, long bgObjectId, int group, bool visible)
        {
            StudioModelStatWrapper model = null;
            switch (type)
            {
                case "Mod":
                    model = placer.CreateModel(fileName, group, visible);
                    break;
                case "Asset":
                    model = placer.CreateBgObject(TrimAssetBgExtension(fileName), group, visible);
                    break;
                default:
                    MTEUtils.LogWarning("未対応のモデル種別です。{0} ({1})", type, fileName);
                    break;
            }
            return model?.obj as GameObject;
        }

        public static void DeleteModel(GameObject obj)
        {
            var model = placer.FindModelByGameObject(obj);
            if (model != null)
            {
                placer.DeleteModel(model);
            }
        }

        public static void DeleteAllModels()
        {
            placer.DeleteAll();
        }

        public static void SetModelVisible(GameObject obj, bool visible)
        {
            var model = placer.FindModelByGameObject(obj);
            if (model != null)
            {
                placer.SetVisible(model, visible);
            }
        }

        /// <summary>
        /// boneName は追従先ボーンの名前（SceneEditor 側で解決済み）。
        /// ボーン名でのアタッチは Task 6 で実装するため、現時点では解除のみ受け付ける
        /// </summary>
        public static void AttachModel(GameObject obj, Maid maid, string boneName)
        {
            var model = placer.FindModelByGameObject(obj);
            if (model == null)
            {
                return;
            }
            placer.Attach(model, null, null);
        }

        /// <summary>fileName から .asset_bg 拡張子を落としてアセットバンドル名に戻す</summary>
        private static string TrimAssetBgExtension(string fileName)
        {
            if (!string.IsNullOrEmpty(fileName)
                && fileName.EndsWith(BgObjectAssetLoader.AssetBgExtension, StringComparison.OrdinalIgnoreCase))
            {
                return fileName.Substring(
                    0, fileName.Length - BgObjectAssetLoader.AssetBgExtension.Length);
            }
            return fileName;
        }
    }
}
