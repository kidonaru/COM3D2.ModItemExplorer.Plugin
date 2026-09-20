using System;
using System.IO;

namespace COM3D2.ModItemExplorer.Plugin
{
    /// <summary>既存MODの再読み込み前に、同じ導入先へ追加されたファイルを検索対象へ反映する。</summary>
    internal static class RuntimeAssetReload
    {
        /// <summary>MaidProp.type がファイル指定（menu）を表す値。</summary>
        public const int FilePropType = 3;

        public static string GetActiveMenuName(MaidProp prop)
        {
            if (prop == null) return null;
            return string.IsNullOrEmpty(prop.strTempFileName) ? prop.strFileName : prop.strTempFileName;
        }

        public static void MarkEquippedPropDirty(MaidProp prop)
        {
            if (prop == null || prop.type != FilePropType || string.IsNullOrEmpty(GetActiveMenuName(prop))) return;
            var temporary = !string.IsNullOrEmpty(prop.strTempFileName);
            prop.boTempDut = temporary;
            prop.boDut = !temporary;
        }

        public static void RefreshSearchPath(string menuName)
        {
            if (string.IsNullOrEmpty(menuName) || Path.GetFileName(menuName) != menuName
                || menuName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new InvalidOperationException("再読み込み対象のmenu名が不正です。");

            var root = ModItemManager.instance.modRootItem.fullPath;
            if (!Directory.Exists(root)) return;

            var paths = Directory.GetFiles(root, menuName, SearchOption.AllDirectories);
            if (paths.Length > 1)
                throw new InvalidOperationException("同名のmenuが複数のMODフォルダーにあります。重複を解消してください。");
            if (paths.Length == 0) return;

            var fileSystem = GameUty.FileSystemMod as FileSystemWindows;
            if (fileSystem == null)
                throw new InvalidOperationException("この環境のMOD検索一覧の更新には対応していません。");

            // AddFolder は未登録のフォルダーを登録したときだけ true を返し、その時だけネイティブ側が中身を走査する。
            // 同一セッションで2回目以降はフォルダーが登録済みのため走査が走らず、
            // そのフォルダーへ新しく追加したファイルはゲーム終了まで検索対象に入らない（実機で確認済み）。
            // 既存ファイルの上書き更新は FileOpen が都度ディスクを読むため、この制限を受けない。
            fileSystem.AddFolder(Path.GetDirectoryName(paths[0]));
            fileSystem.AddAutoPathForAllFolder(false);
            fileSystem.existedCache.Clear();
        }
    }
}
