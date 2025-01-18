using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using COM3D2.MotionTimelineEditor;

namespace COM3D2.ModItemExplorer.Plugin
{
    public class OfficialObjectInfoWrapper
    {
        public object original { get; set; }

        public int type { get; set; }
        public string label { get; set; }
        public string fileName { get; set; }
        public string prefabName { get; set; }
        public int myRoomId { get; set; }
        public long bgObjectId { get; set; }

        public OfficialObjectInfoWrapper()
        {
        }
    }

    public class OfficialObjectInfoField : CustomFieldBase
    {
        public Type OfficialObjectInfoType;

        public FieldInfo type;
        public FieldInfo label;
        public FieldInfo fileName;
        public FieldInfo prefabName;
        public FieldInfo myRoomId;
        public FieldInfo bgObjectId;

        public override Dictionary<string, string> typeNames { get; } = new Dictionary<string, string>
        {
            { "OfficialObjectInfoType", "COM3D2.MotionTimelineEditor.Plugin.OfficialObjectInfo" },
        };

        public override bool PrepareLoadFields()
        {
            defaultParentType = OfficialObjectInfoType;
            return base.PrepareLoadFields();
        }

        public OfficialObjectInfoWrapper ConvertToWrapper(object obj)
        {
            var wrapper = new OfficialObjectInfoWrapper();
            wrapper.original = obj;

            wrapper.type = (int)type.GetValue(obj);
            wrapper.label = (string)label.GetValue(obj);
            wrapper.fileName = (string)fileName.GetValue(obj);
            wrapper.prefabName = (string)prefabName.GetValue(obj);
            wrapper.myRoomId = (int)myRoomId.GetValue(obj);
            wrapper.bgObjectId = (long)bgObjectId.GetValue(obj);

            return wrapper;
        }

        public object ConvertToOriginal(OfficialObjectInfoWrapper wrapper)
        {
            object obj = Activator.CreateInstance(OfficialObjectInfoType);
            type.SetValue(obj, wrapper.type);
            label.SetValue(obj, wrapper.label);
            fileName.SetValue(obj, wrapper.fileName);
            prefabName.SetValue(obj, wrapper.prefabName);
            myRoomId.SetValue(obj, wrapper.myRoomId);
            bgObjectId.SetValue(obj, wrapper.bgObjectId);

            return obj;
        }
    }
}