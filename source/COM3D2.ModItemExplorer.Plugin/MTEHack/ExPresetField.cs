using System;
using System.IO;
using System.Reflection;
using CM3D2.ExternalPreset.Managed;
using COM3D2.MotionTimelineEditor;
using UnityEngine.Events;

namespace COM3D2.ModItemExplorer.Plugin
{
    public class ExPresetField : CustomFieldBase
    {
        public override Type assemblyType { get; set; } = typeof(ExPreset);

        public FieldInfo exsaveNodeNameMap;
        public FieldInfo xmlMemory;

        public override Type defaultParentType { get; set; } = typeof(ExPreset);
    }
}