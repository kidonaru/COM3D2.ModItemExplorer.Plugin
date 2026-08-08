using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using COM3D2.MotionTimelineEditor;

namespace COM3D2.ModItemExplorer.Plugin
{
    public class ModelHackManagerField : CustomFieldBase
    {
        public Type ModelHackManagerType;

        public PropertyInfo instance;
        public PropertyInfo modelList;
        public PropertyInfo pluginNames;
        public PropertyInfo studioHack;

        public MethodInfo DeleteModel;
        public MethodInfo CreateModel;

        public override Dictionary<string, string> typeNames { get; } = new Dictionary<string, string>
        {
            { "ModelHackManagerType", "COM3D2.MotionTimelineEditor.Plugin.ModelHackManager" },
        };

        public override bool PrepareLoadFields()
        {
            defaultParentType = ModelHackManagerType;
            return base.PrepareLoadFields();
        }
    }
}