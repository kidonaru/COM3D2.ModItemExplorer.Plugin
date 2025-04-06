using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.ModItemExplorer.Plugin
{
    public class AnimationLayerInfoField : CustomFieldBase
    {
        public Type AnimationLayerInfoType;

        public FieldInfo layer;
        public PropertyInfo anmName;
        public FieldInfo startTime;
        public FieldInfo weight;
        public FieldInfo speed;
        public FieldInfo loop;
        public FieldInfo state;

        public override Dictionary<string, string> typeNames { get; } = new Dictionary<string, string>
        {
            { "AnimationLayerInfoType", "COM3D2.MotionTimelineEditor.AnimationLayerInfo" },
        };

        private static AnimationLayerInfoField _instance = null;
        public static AnimationLayerInfoField instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new AnimationLayerInfoField();
                    _instance.Init();
                }
                return _instance;
            }
        }

        public override bool LoadAssembly()
        {
            var assemblyPath = Path.GetFullPath(MTEUtils.CombinePaths(
                "Sybaris", "UnityInjector", "COM3D2.MotionTimelineEditor.Plugin.dll"));
            if (!File.Exists(assemblyPath))
            {
                MTEUtils.LogWarning("MotionTimelineEditor.Plugin" + " not found");
                return false;
            }

            assembly = Assembly.LoadFile(assemblyPath);
            return true;
        }

        public override bool PrepareLoadFields()
        {
            defaultParentType = AnimationLayerInfoType;
            return base.PrepareLoadFields();
        }

        public AnimationLayerInfo ConvertFromObject(object obj)
        {
            var wrapper = new AnimationLayerInfo(0);
            FromObject(wrapper, obj);
            return wrapper;
        }

        public void FromObject(AnimationLayerInfo wrapper, object obj)
        {
            wrapper.original = obj;
            wrapper.layer = (int)layer.GetValue(obj);
            wrapper.anmName = (string)anmName.GetValue(obj, null);
            wrapper.startTime = (float)startTime.GetValue(obj);
            wrapper.weight = (float)weight.GetValue(obj);
            wrapper.speed = (float)speed.GetValue(obj);
            wrapper.loop = (bool)loop.GetValue(obj);
            wrapper.state = (AnimationState)state.GetValue(obj);
        }

        public void ApplyToObject(AnimationLayerInfo wrapper)
        {
            var obj = wrapper.original;
            if (obj == null)
            {
                return;
            }

            layer.SetValue(obj, wrapper.layer);
            anmName.SetValue(obj, wrapper.anmName, null);
            startTime.SetValue(obj, wrapper.startTime);
            weight.SetValue(obj, wrapper.weight);
            speed.SetValue(obj, wrapper.speed);
            loop.SetValue(obj, wrapper.loop);
            state.SetValue(obj, wrapper.state);
        }
    }

    public static partial class Extensions
    {
        private static AnimationLayerInfoField _field => AnimationLayerInfoField.instance;

        public static AnimationLayerInfo FromObject(this AnimationLayerInfo wrapper, object obj)
        {
            if (!_field.initialized) return null;
            _field.FromObject(wrapper, obj);
            return wrapper;
        }

        public static void ApplyToObject(this AnimationLayerInfo wrapper)
        {
            if (!_field.initialized) return;
            _field.ApplyToObject(wrapper);
        }
    }
}