using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.ModItemExplorer.Plugin
{
    public class MaidCacheWrapper
    {
        public object original = null;

        public int slotNo = 0;
        public Maid maid = null;
        public List<AnimationLayerInfo> animationLayerInfos = new List<AnimationLayerInfo>(10);

        public MaidCacheWrapper()
        {
            for (var i = 0; i < 10; i++)
            {
                animationLayerInfos.Add(new AnimationLayerInfo(i));
            }
        }
    }

    public class MaidCacheField : CustomFieldBase
    {
        public Type MaidCacheType;

        public FieldInfo slotNo;
        public FieldInfo maid;
        public FieldInfo animationLayerInfos;

        public override Dictionary<string, string> typeNames { get; } = new Dictionary<string, string>
        {
            { "MaidCacheType", "COM3D2.MotionTimelineEditor.Plugin.MaidCache" },
        };

        public override bool PrepareLoadFields()
        {
            defaultParentType = MaidCacheType;
            return base.PrepareLoadFields();
        }

        public MaidCacheWrapper ConvertToWrapper(object obj)
        {
            var wrapper = new MaidCacheWrapper();
            UpdateWrapper(wrapper, obj);
            return wrapper;
        }

        public void UpdateWrapper(MaidCacheWrapper wrapper, object obj)
        {
            wrapper.original = obj;

            wrapper.slotNo = (int)slotNo.GetValue(obj);
            wrapper.maid = (Maid)maid.GetValue(obj);

            var infos = (IList)animationLayerInfos.GetValue(obj);
            for (var i = 0; i < infos.Count; i++)
            {
                var source = infos[i];
                var dest = wrapper.animationLayerInfos[i];
                dest.FromObject(source);
            }
        }
    }
}