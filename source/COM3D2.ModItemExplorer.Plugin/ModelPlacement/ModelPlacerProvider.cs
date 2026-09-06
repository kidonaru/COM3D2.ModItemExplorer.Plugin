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
        /// type は SceneEditor 側 StudioModelType の enum 名
        /// （Mod = .menu、Asset = .asset_bg、Prefab = 公式 BG、MyRoom = マイルーム配置）
        /// </summary>
        public static GameObject CreateModel(
            string type, string fileName, int myRoomId, long bgObjectId, int group, bool visible)
        {
            StudioModelStatWrapper model = null;
            var resolvedType = ResolveType(type, fileName);
            switch (resolvedType)
            {
                case ModelPlacementType.Mod:
                    model = placer.CreateModel(fileName, group, visible);
                    break;
                case ModelPlacementType.Asset:
                    model = placer.CreateBgObject(SelfModelPlacer.GetAssetBundleName(fileName), group, visible);
                    break;
                case ModelPlacementType.Prefab:
                    model = placer.CreateGameModel(fileName, group, visible);
                    break;
                case ModelPlacementType.MyRoom:
                    model = placer.CreateMyRoomObject(myRoomId, group, visible);
                    break;
                default:
                    MTEUtils.LogWarning("未対応のモデル種別です。{0} ({1})", resolvedType, fileName);
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
        /// maid が null または boneName が空なら解除する
        /// </summary>
        public static void AttachModel(GameObject obj, Maid maid, string boneName)
        {
            var model = placer.FindModelByGameObject(obj);
            if (model != null)
            {
                placer.AttachByBoneName(model, maid, boneName);
            }
        }

        /// <summary>タイムライン読込のような一括操作の開始・終了を受け取る（任意メンバ）</summary>
        public static void BeginBatch()
        {
            placer.BeginBatch();
        }

        public static void EndBatch()
        {
            placer.EndBatch();
        }

        /// <summary>
        /// ホストから渡された種別を補正する。
        /// タイムラインは種別を保存せずモデル名から引き直すが、ホスト側は公式データに
        /// 無いファイル名をすべて既定値の Mod として扱うため、背景オブジェクトも
        /// Mod で渡ってくる。ここで拡張子から見分け直す
        /// （プリセット復元経路 SelfModelPlacer.RestoreModel と同じ範囲の補正）
        /// </summary>
        private static string ResolveType(string type, string fileName)
        {
            if (type == ModelPlacementType.Mod && SelfModelPlacer.IsBgObjectFileName(fileName))
            {
                return ModelPlacementType.Asset;
            }
            return type;
        }
    }
}
