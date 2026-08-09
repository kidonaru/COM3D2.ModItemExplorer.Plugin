using System;
using System.IO;
using System.Linq;
using System.Reflection;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.ModItemExplorer.Plugin
{
    /// <summary>
    /// NPRShader プラグインの AssetLoader をリフレクション経由で呼び、
    /// _NPRMAT_ 付き mate に NPR シェーダーを適用したマテリアルを生成する。
    ///
    /// ゲーム側の ImportCM.LoadMaterial は NPR シェーダーを知らないため、
    /// そのまま読むとバニラのシェーダーのままになり NPRShader が効いていないように見える。
    /// NPRShader 自身は写真モードの配置オブジェクト（名前が .menu で終わる GameObject）を
    /// 走査して差し替えるが、自前配置のモデルはその走査に乗らないためここで同じ処理を行う。
    ///
    /// NPRShader が導入されていない環境では常に null を返し、呼び出し側が通常ロードに戻る。
    /// </summary>
    public static class NprShaderLoader
    {
        /// <summary>NPRShader が NPR 適用対象を判別するマーカー。mate 名に含まれる</summary>
        private const string NprMarker = "_nprmat_";

        /// <summary>反射を使うシェーダーを示すマーカー。NPRShader 側と同じ判定</summary>
        private const string ReflectionMarker = "_reflection_";

        private const string AssetLoaderTypeName = "COM3D2.NPRShader.Plugin.AssetLoader";
        private const string LoadMethodName = "LoadMaterialWithSetShader";

        private static bool _resolved = false;
        private static MethodInfo _loadMaterialWithSetShader = null;

        /// <summary>
        /// NPR シェーダーの適用対象となる mate かどうか
        /// </summary>
        public static bool IsNprMaterial(string mateFileName)
        {
            return !string.IsNullOrEmpty(mateFileName)
                && mateFileName.ToLowerInvariant().Contains(NprMarker);
        }

        /// <summary>
        /// 反射（リフレクションプローブ）を使う NPR シェーダーかどうか
        /// </summary>
        public static bool IsReflectionMaterial(string mateFileName)
        {
            return !string.IsNullOrEmpty(mateFileName)
                && mateFileName.ToLowerInvariant().Contains(ReflectionMarker);
        }

        /// <summary>
        /// NPR シェーダーを適用したマテリアルを生成する。
        /// NPRShader が無い場合や生成に失敗した場合は null を返す
        /// </summary>
        public static Material LoadMaterial(string mateFileName)
        {
            var method = GetLoadMethod();
            if (method == null)
            {
                return null;
            }

            try
            {
                // 第3引数は再利用する既存マテリアル。常に新規生成するため null を渡す
                var material = method.Invoke(
                    null,
                    new object[] { mateFileName, GetShaderName(mateFileName), null }) as Material;
                if (material == null)
                {
                    MTEUtils.LogWarning("NPRマテリアルを生成できませんでした。{0}", mateFileName);
                }
                return material;
            }
            catch (Exception e)
            {
                MTEUtils.LogWarning("NPRマテリアルの読み込みに失敗しました。{0}", mateFileName);
                MTEUtils.LogException(e);
                return null;
            }
        }

        /// <summary>
        /// mate 名の "_NPRMAT_" 以降をシェーダー名として取り出す。NPRShader 側と同じ規則
        /// </summary>
        private static string GetShaderName(string mateFileName)
        {
            var baseName = Path.GetFileNameWithoutExtension(mateFileName).ToLowerInvariant();
            return baseName.Split(new[] { NprMarker }, StringSplitOptions.None).Last();
        }

        /// <summary>
        /// 解決結果は成否に関わらず 1 回だけキャッシュする。
        /// 配置操作はプラグインのロードが全て終わった後にしか起きないため、
        /// 「初回は見つからないが後から現れる」ケースは想定しなくてよい
        /// </summary>
        private static MethodInfo GetLoadMethod()
        {
            if (_resolved)
            {
                return _loadMaterialWithSetShader;
            }
            _resolved = true;

            // NPRShader はシェーダーの AssetBundle 一覧などを静的フィールドに Awake で溜め込むため、
            // Assembly.LoadFile で別インスタンスを掴むと未初期化の型を呼ぶことになる。
            // 必ず実行中のアセンブリから型を取る
            var type = FindLoadedType(AssetLoaderTypeName);
            if (type == null)
            {
                MTEUtils.LogDebug("NPRShaderが見つからないためNPRマテリアルの適用をスキップします");
                return null;
            }

            _loadMaterialWithSetShader = type.GetMethod(
                LoadMethodName,
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(string), typeof(string), typeof(Material) },
                null);
            if (_loadMaterialWithSetShader == null)
            {
                MTEUtils.LogWarning("NPRShaderの{0}が見つかりませんでした", LoadMethodName);
            }

            return _loadMaterialWithSetShader;
        }

        private static Type FindLoadedType(string typeName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var type = assembly.GetType(typeName, false);
                    if (type != null)
                    {
                        return type;
                    }
                }
                catch (Exception)
                {
                    // 型情報を取れないアセンブリは無視して次を探す
                }
            }
            return null;
        }
    }
}
