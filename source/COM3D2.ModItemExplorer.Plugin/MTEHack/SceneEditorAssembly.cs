using System;
using System.Reflection;

namespace COM3D2.ModItemExplorer.Plugin
{
    /// <summary>
    /// MTE の型 (MaidManager 等) を持つ SceneEditor プラグインのアセンブリを解決する。
    /// ファイルから読み直すと静的状態が別物の複製になり、SceneEditor 本体の
    /// シングルトンと値を共有できないため、UnityInjector が読み込んだものを AppDomain から探す
    /// </summary>
    public static class SceneEditorAssembly
    {
        private const string AssemblyName = "COM3D2.SceneEditor.Plugin";

        /// <summary>未ロード (SceneEditor 不在・ロード順が自分より後) なら null</summary>
        public static Assembly Find()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetName().Name == AssemblyName)
                {
                    return assembly;
                }
            }
            return null;
        }
    }
}
