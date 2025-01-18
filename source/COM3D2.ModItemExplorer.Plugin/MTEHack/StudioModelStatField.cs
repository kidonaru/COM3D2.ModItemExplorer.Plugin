using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using COM3D2.MotionTimelineEditor;

namespace COM3D2.ModItemExplorer.Plugin
{
    public class StudioModelStatWrapper
    {
        public object original { get; set; }

        public object info { get; set; }
        public int group { get; set; }
        public string name { get; set; }
        public string displayName { get; set; }
        public int attachPoint { get; set; }
        public int attachMaidSlotNo { get; set; }
        public object obj { get; set; }
        public string pluginName { get; set; }
        public bool visible { get; set; }

        public OfficialObjectInfoWrapper infoWrapper { get; set; }

        public StudioModelStatWrapper()
        {
        }
    }

    public class StudioModelStatField : CustomFieldBase
    {
        public Type StudioModelStatType;

        public PropertyInfo info;
        public PropertyInfo group;
        public PropertyInfo name;
        public PropertyInfo displayName;
        public PropertyInfo attachPoint;
        public PropertyInfo attachMaidSlotNo;
        public PropertyInfo obj;
        public PropertyInfo pluginName;
        public PropertyInfo visible;

        public override Dictionary<string, string> typeNames { get; } = new Dictionary<string, string>
        {
            { "StudioModelStatType", "COM3D2.MotionTimelineEditor.Plugin.StudioModelStat" },
        };

        public override bool PrepareLoadFields()
        {
            defaultParentType = StudioModelStatType;
            return base.PrepareLoadFields();
        }

        public StudioModelStatWrapper ConvertToWrapper(object obj)
        {
            var wrapper = new StudioModelStatWrapper();
            wrapper.original = obj;

            wrapper.info = info.GetValue(obj, null);
            wrapper.group = (int)group.GetValue(obj, null);
            wrapper.name = (string)name.GetValue(obj, null);
            wrapper.displayName = (string)displayName.GetValue(obj, null);
            wrapper.attachPoint = (int)attachPoint.GetValue(obj, null);
            wrapper.attachMaidSlotNo = (int)attachMaidSlotNo.GetValue(obj, null);
            wrapper.obj = this.obj.GetValue(obj, null);
            wrapper.pluginName = (string)pluginName.GetValue(obj, null);
            wrapper.visible = (bool)visible.GetValue(obj, null);

            return wrapper;
        }

        public object ConvertToOriginal(StudioModelStatWrapper wrapper)
        {
            object obj = Activator.CreateInstance(StudioModelStatType);
            info.SetValue(obj, wrapper.info, null);
            group.SetValue(obj, wrapper.group, null);
            name.SetValue(obj, wrapper.name, null);
            displayName.SetValue(obj, wrapper.displayName, null);
            attachPoint.SetValue(obj, wrapper.attachPoint, null);
            attachMaidSlotNo.SetValue(obj, wrapper.attachMaidSlotNo, null);
            this.obj.SetValue(obj, wrapper.obj, null);
            pluginName.SetValue(obj, wrapper.pluginName, null);
            visible.SetValue(obj, wrapper.visible, null);

            return obj;
        }
    }
}