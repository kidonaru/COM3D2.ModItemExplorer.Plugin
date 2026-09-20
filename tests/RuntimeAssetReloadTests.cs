using System;
using System.Collections.Generic;
using System.IO;
using COM3D2.ModItemExplorer.Plugin;

// ゲームを起動せず、検索対象の制限と既存装備の保持を確認するための代替型。
public class MaidProp
{
    public int type;
    public string strFileName, strTempFileName;
    public int nFileNameRID, nTempFileNameRID;
    public bool boDut, boTempDut, bNoScale;
}
public class FileSystemWindows
{
    public readonly List<string> folders = new List<string>();
    public readonly Dictionary<string, bool> existedCache = new Dictionary<string, bool>();
    public bool refreshed;
    public void AddFolder(string path) { folders.Add(path); }
    public void AddAutoPathForAllFolder(bool value) { refreshed = !value; }
}
public static class GameUty { public static object FileSystemMod; }
namespace COM3D2.ModItemExplorer.Plugin
{
    public class ModItemManager
    {
        public static ModItemManager instance = new ModItemManager();
        public class Root { public string fullPath; }
        public Root modRootItem = new Root();
    }
}
public static class RuntimeAssetReloadTests
{
    private static int checks;
    private static void Check(bool result, string message)
    {
        if (!result) throw new Exception("検証失敗: " + message);
        checks++;
    }
    private static void Reject(Action action, string message)
    {
        try { action(); }
        catch (InvalidOperationException) { checks++; return; }
        throw new Exception("拒否されませんでした: " + message);
    }
    public static void Run()
    {
        checks = 0;
        var prop = new MaidProp { type = 3, strFileName = "normal.menu", nFileNameRID = 12, bNoScale = true };
        RuntimeAssetReload.MarkEquippedPropDirty(prop);
        Check(prop.boDut && !prop.boTempDut && prop.strFileName == "normal.menu"
            && prop.nFileNameRID == 12 && prop.bNoScale, "通常装備とスケール設定を保持する");
        prop.strTempFileName = "temporary.menu";
        prop.nTempFileNameRID = 34;
        RuntimeAssetReload.MarkEquippedPropDirty(prop);
        Check(!prop.boDut && prop.boTempDut && prop.strFileName == "normal.menu"
            && prop.nFileNameRID == 12 && prop.strTempFileName == "temporary.menu"
            && prop.nTempFileNameRID == 34 && prop.bNoScale, "一時装備と通常装備の両方を保持する");
        Check(RuntimeAssetReload.GetActiveMenuName(prop) == "temporary.menu", "一時装備を対象にする");
        var numeric = new MaidProp { type = 1, strFileName = "unused.menu" };
        RuntimeAssetReload.MarkEquippedPropDirty(numeric);
        Check(!numeric.boDut && !numeric.boTempDut, "体型値を再処理対象にしない");
        var empty = new MaidProp { type = 3 };
        RuntimeAssetReload.MarkEquippedPropDirty(empty);
        RuntimeAssetReload.MarkEquippedPropDirty(null);
        Check(!empty.boDut && !empty.boTempDut, "未装備を変更しない");

        var root = Path.Combine(Path.GetTempPath(), "ModItemExplorerReloadTest-" + Guid.NewGuid().ToString("N"));
        var first = Path.Combine(root, "first");
        var second = Path.Combine(root, "second");
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(second);
        var a = Path.Combine(first, "test.menu");
        var b = Path.Combine(second, "test.menu");
        try
        {
            ModItemManager.instance.modRootItem.fullPath = root;
            var fs = new FileSystemWindows();
            GameUty.FileSystemMod = fs;
            fs.existedCache["new.mate"] = false;
            RuntimeAssetReload.RefreshSearchPath("official.menu");
            Check(fs.folders.Count == 0 && fs.existedCache.Count == 1, "公式menuではMOD検索対象を変更しない");
            Reject(() => RuntimeAssetReload.RefreshSearchPath("../test.menu"), "フォルダー外への参照");
            Reject(() => RuntimeAssetReload.RefreshSearchPath("*.menu"), "ワイルドカード");
            File.WriteAllText(a, "");
            RuntimeAssetReload.RefreshSearchPath("test.menu");
            Check(fs.folders.Count == 1 && fs.folders[0] == first && fs.refreshed
                && fs.existedCache.Count == 0, "対象フォルダーだけ登録し存在確認キャッシュを更新する");
            File.WriteAllText(b, "");
            Reject(() => RuntimeAssetReload.RefreshSearchPath("test.menu"), "同名menuの二重導入");
            Check(fs.folders.Count == 1, "重複検出時は検索対象を変更しない");
            File.Delete(b);
            GameUty.FileSystemMod = new object();
            Reject(() => RuntimeAssetReload.RefreshSearchPath("test.menu"), "未対応ファイルシステム");
        }
        finally
        {
            // このテストが生成した固定ファイルだけを削除する。再帰削除は行わない。
            File.Delete(a);
            File.Delete(b);
            Directory.Delete(first);
            Directory.Delete(second);
            Directory.Delete(root);
        }
        Console.WriteLine("再読み込み補助処理: " + checks + " 件成功");
    }
}
