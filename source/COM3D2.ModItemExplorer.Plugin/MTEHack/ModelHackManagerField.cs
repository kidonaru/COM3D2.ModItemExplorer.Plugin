using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using COM3D2.MotionTimelineEditor;

namespace COM3D2.ModItemExplorer.Plugin
{
    public class ModelHackManagerField : CustomFieldBase
    {
        public override Type assemblyType { get; set; }

        public Type ModelHackManagerType;

        public PropertyInfo instance;
        public PropertyInfo modelList;
        public PropertyInfo pluginNames;

        public MethodInfo DeleteModel;
        public MethodInfo CreateModel;

        public override Dictionary<string, string> typeNames { get; } = new Dictionary<string, string>
        {
            { "ModelHackManagerType", "COM3D2.MotionTimelineEditor.Plugin.ModelHackManager" },
        };

        public override bool LoadAssembly()
        {
            var assemblyPath = Path.GetFullPath(MTEUtils.CombinePaths(
                "Sybaris", "UnityInjector", "COM3D2.MotionTimelineEditor.Plugin.dll"));
            if (File.Exists(assemblyPath))
            {
                assembly = Assembly.LoadFile(assemblyPath);
            }

            MTEUtils.AssertNull(assembly != null, "MotionTimelineEditor.Plugin" + " not found");
            return assembly != null;
        }

        public override bool PrepareLoadFields()
        {
            defaultParentType = ModelHackManagerType;
            return base.PrepareLoadFields();
        }
    }
}