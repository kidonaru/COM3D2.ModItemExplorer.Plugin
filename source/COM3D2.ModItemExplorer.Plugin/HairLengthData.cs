using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.ModItemExplorer.Plugin
{
    public class HairLengthData
    {
        public Maid maid;
        public string groupName;
        public TBodySkin.HairLengthCtrl.HairLength hairLength;

        public float lenghtRate;
        public float initialLenghtRate;

        public HairLengthData()
        {
        }

        public void Init(
            Maid maid = null,
            string groupName = null,
            TBodySkin.HairLengthCtrl.HairLength hairLength = null)
        {
            MTEUtils.LogDebug($"HairLengthData.Init: {groupName}, {hairLength?.GetLengthRate()}");

            this.maid = maid;
            this.groupName = groupName;
            this.hairLength = hairLength;

            lenghtRate = hairLength?.GetLengthRate() ?? 0f;
            initialLenghtRate = lenghtRate;
        }

        public void Reset()
        {
            lenghtRate = initialLenghtRate;
            Apply();
        }

        public void Apply()
        {
            if (maid != null && hairLength != null)
            {
                if (lenghtRate != hairLength.GetLengthRate())
                {
                    hairLength.SetLengthRate(lenghtRate);
                    maid.body0.HairLengthBlend();
                }
            }
        }

        public HairLengthXml ToXml()
        {
            return new HairLengthXml(this);
        }

        public void ApplyXml(HairLengthXml xml)
        {
            lenghtRate = xml.lenghtRate;
            Apply();
        }
    }

    public class HairLengthXml
    {
        [XmlElement("GroupName")]
        public string groupName;
        [XmlElement("LenghtRate")]
        public float lenghtRate;

        public HairLengthXml()
        {
        }

        public HairLengthXml(HairLengthData data)
        {
            groupName = data.groupName;
            lenghtRate = data.lenghtRate;
        }
    }

    [XmlRoot("HairLengthList")]
    public class HairLengthListXml
    {
        [XmlElement("HairLength")]
        public List<HairLengthXml> list = new List<HairLengthXml>();

        public HairLengthListXml()
        {
        }

        public HairLengthListXml(List<HairLengthData> dataList)
        {
            list = dataList.Select(d => d.ToXml()).ToList();
        }
    }
}