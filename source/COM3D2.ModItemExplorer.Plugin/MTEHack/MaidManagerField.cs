using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using COM3D2.MotionTimelineEditor;

namespace COM3D2.ModItemExplorer.Plugin
{
    public class MaidManagerField : CustomFieldBase
    {
        public Type MaidManagerType;

        public PropertyInfo instance;
        public FieldInfo maidCaches;
        public PropertyInfo maidSlotNo;

        public override Dictionary<string, string> typeNames { get; } = new Dictionary<string, string>
        {
            { "MaidManagerType", "COM3D2.MotionTimelineEditor.Plugin.MaidManager" },
        };

        public override bool PrepareLoadFields()
        {
            defaultParentType = MaidManagerType;
            return base.PrepareLoadFields();
        }
    }
}