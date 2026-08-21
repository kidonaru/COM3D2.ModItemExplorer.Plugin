using System;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.ModItemExplorer.Plugin
{
    /// <summary>
    /// Mod 配下の .asset_bg アセットバンドルを読み、prefab を取り出す。
    ///
    /// BgMgr.CreateAssetBundle は GameUty.BgFiles (システム側のみ) しか見ないため
    /// Mod 配下のバンドルには届かない。そこで FileSystemMod から自前で読む。
    ///
    /// 同じバンドルを二重に LoadFromMemory すると
    /// 「同じファイルを含む AssetBundle が既にロード済み」で例外になるため、
    /// キャッシュは高速化ではなく正しさのために必須。
    /// ロードしたバンドルはアンロードしない (公式 BgMgr.asset_bundle_dic と同じ方針)
    /// </summary>
    public static class BgObjectAssetLoader
    {
        public const string AssetBgExtension = ".asset_bg";

        private static readonly Dictionary<string, GameObject> _prefabCache
            = new Dictionary<string, GameObject>(16, StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// アセットバンドル名 (拡張子なし) から prefab を返す。失敗時は null。
        /// 返る GameObject はバンドル所有の原本なので、呼び出し側は Instantiate して使い、
        /// これ自体を Destroy してはいけない
        /// </summary>
        public static GameObject LoadPrefab(string assetBundleName)
        {
            if (string.IsNullOrEmpty(assetBundleName))
            {
                return null;
            }

            GameObject cached;
            if (_prefabCache.TryGetValue(assetBundleName, out cached) && cached != null)
            {
                return cached;
            }

            try
            {
                var fileName = assetBundleName + AssetBgExtension;
                if (!GameUty.FileSystemMod.IsExistentFile(fileName))
                {
                    MTEUtils.LogWarning("アセットバンドルが見つかりません。{0}", fileName);
                    return null;
                }

                byte[] buffer;
                using (var file = GameUty.FileSystemMod.FileOpen(fileName))
                {
                    if (file == null || !file.IsValid())
                    {
                        MTEUtils.LogWarning("アセットバンドルが開けません。{0}", fileName);
                        return null;
                    }
                    buffer = file.ReadAll();
                }

                var assetBundle = AssetBundle.LoadFromMemory(buffer);
                if (assetBundle == null)
                {
                    MTEUtils.LogWarning("アセットバンドルの読み込みに失敗しました。{0}", fileName);
                    return null;
                }

                var assets = assetBundle.LoadAllAssets<GameObject>();
                if (assets == null || assets.Length == 0)
                {
                    MTEUtils.LogWarning("アセットバンドルにGameObjectがありません。{0}", fileName);
                    return null;
                }

                // 公式 BgMgr も mainAsset が無ければ先頭を使うが、複数入っている場合の
                // 並び順は Unity が保証していない。意図しないものを掴んだ疑いを追えるよう知らせる
                if (assets.Length > 1)
                {
                    MTEUtils.LogWarning(
                        "アセットバンドルに複数のGameObjectがあります。先頭を使います。{0} (count={1}, name={2})",
                        fileName, assets.Length, assets[0].name);
                }

                _prefabCache[assetBundleName] = assets[0];
                return assets[0];
            }
            catch (Exception e)
            {
                MTEUtils.LogWarning("アセットバンドルの読み込みに失敗しました。{0}", assetBundleName);
                MTEUtils.LogException(e);
                return null;
            }
        }
    }
}
