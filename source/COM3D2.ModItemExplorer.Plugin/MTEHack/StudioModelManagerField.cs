using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using COM3D2.MotionTimelineEditor;

namespace COM3D2.ModItemExplorer.Plugin
{
    public class StudioModelManagerField : CustomFieldBase
    {
        public Type StudioModelManagerType;

        public PropertyInfo instance;

        public MethodInfo FindOfficialObject;

        public override Dictionary<string, string> typeNames { get; } = new Dictionary<string, string>
        {
            { "StudioModelManagerType", "COM3D2.MotionTimelineEditor.Plugin.StudioModelManager" },
        };

        public override bool PrepareLoadFields()
        {
            defaultParentType = StudioModelManagerType;
            return base.PrepareLoadFields();
        }
    }
}