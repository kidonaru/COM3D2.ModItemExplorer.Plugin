using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.ModItemExplorer.Plugin
{
    public class CustomPartsData
    {
        public Maid maid;
        public TBody.SlotID slotId;
        public string apName;

        private bool _enabled;
        public bool enabled
        {
            get => _enabled;
            set
            {
                if (_enabled == value)
                {
                    return;
                }

                _enabled = value;

                if (!value)
                {
                    maid.body0.ResetAttachPoint(slotId, apName);
                    UpdateWorldTransform();
                    ApplyWorldTransform();
                }

                maid.body0.SetEnableAttachPointEdit(value, slotId, apName);
            }
        }

        public Vector3 position;
        private Quaternion _rotation;
        private Vector3 _eulerAngles;
        public Vector3 scale;

        public Vector3 localPosition;
        public Quaternion localRotation;
        public Vector3 localScale;
        public int vidx;

        public Vector3 basePosition;

        public Quaternion rotation
        {
            get => _rotation;
            set
            {
                _rotation = value;
                _eulerAngles = MTEUtils.GetNormalizedEulerAngles(value.eulerAngles);
            }
        }
        public Vector3 eulerAngles
        {
            get => _eulerAngles;
            set
            {
                _eulerAngles = value;
                _rotation = Quaternion.Euler(value);
            }
        }

        public Vector3 initialLocalPosition;
        public Quaternion initialLocalRotation;
        public Vector3 initialLocalScale;
        public int initialVidx;

        public TBodySkin slot => maid.body0.goSlot[(int)slotId];
        public TMorph morph => slot.morph;
        public TAttachPoint attachPoint => morph?.dicAttachPoint.GetOrDefault(apName);
        public TBodySkin bodyskin => morph?.bodyskin;
        public Transform obj_tr => bodyskin?.obj_tr;

        public CustomPartsData()
        {
        }

        public void Init()
        {
            Init(null, slotId, apName);
        }

        public void Init(Maid maid, TBody.SlotID slotId, string apName)
        {
            MTEUtils.LogDebug($"CustomPartsData.Init: {slotId}, {apName}");

            this.maid = maid;
            this.slotId = slotId;
            this.apName = apName;

            if (maid == null)
            {
                return;
            }

            _enabled = maid.body0.GetEnableAttachPointEdit(slotId, apName);

            UpdateLocalTransform();
            UpdateWorldTransform();

            initialLocalPosition = localPosition;
            initialLocalRotation = localRotation;
            initialLocalScale = localScale;
            initialVidx = vidx;
        }

        public void Reset()
        {
            localPosition = initialLocalPosition;
            localRotation = initialLocalRotation;
            localScale = initialLocalScale;
            vidx = initialVidx;

            ApplyLocalTransform();
        }

        public void UpdateLocalTransform()
        {
            localPosition = Vector3.zero;
            localRotation = Quaternion.identity;
            localScale = Vector3.one;
            vidx = 0;

            var attachPoint = this.attachPoint;
            var bodyskin = this.bodyskin;
            if (attachPoint != null)
            {
                localPosition = attachPoint.vOffsLocal;
                localRotation = attachPoint.qNow;
                localScale = attachPoint.vScaleRate;
                vidx = attachPoint.vidx;
            }
            else if (bodyskin != null)
            {
                localPosition = bodyskin.m_vPosLocal;
                localRotation = bodyskin.m_qRotLocal;
                localScale = bodyskin.m_vScaleRate;
            }
        }

        public void UpdateWorldTransform()
        {
            maid.body0.GetAttachPointWorld(slotId, apName, out position, out _rotation, out scale);
            _eulerAngles = MTEUtils.GetNormalizedEulerAngles(_rotation.eulerAngles);

            basePosition = position;
        }

        public void ApplyWorldTransform()
        {
            maid.body0.SetAttachPointWorld(slotId, apName, position, rotation, scale);
            UpdateLocalTransform();
        }

        public void ApplyLocalTransform()
        {
            var attachPoint = this.attachPoint;
            var bodyskin = this.bodyskin;
            var obj_tr = this.obj_tr;
            if (attachPoint != null)
            {
                attachPoint.vOffsLocal = localPosition;
                attachPoint.qNow = localRotation;
                attachPoint.vScaleRate = localScale;
                attachPoint.vidx = vidx;
                attachPoint.bw = morph.GetBoneWeight(vidx);

                maid.SetAttachPointPos(
                    bodyskin.m_ParentMPN,
                    slotId,
                    morph.m_vOriVert.Length,
                    apName,
                    attachPoint.vidx,
                    attachPoint.vOffsLocal,
                    attachPoint.qNow,
                    attachPoint.vScaleRate,
                    attachPoint.bEditable);
            }
            else if (bodyskin != null && obj_tr != null)
            {
                obj_tr.localPosition = bodyskin.m_vPosLocal = localPosition;
                obj_tr.localRotation = bodyskin.m_qRotLocal = localRotation;
                obj_tr.localScale = bodyskin.m_vScaleRate = localScale;

                maid.SetTBodySkinPos(
                    bodyskin.m_ParentMPN,
                    slotId,
                    bodyskin.m_vPosLocal,
                    bodyskin.m_qRotLocal,
                    bodyskin.m_vScaleRate,
                    bodyskin.EnablePartsPosEdit);
            }

            UpdateWorldTransform();
        }

        public CustomPartsXml ToXml()
        {
            return new CustomPartsXml(this);
        }

        public void ApplyXml(CustomPartsXml xml)
        {
            //maid = GameMain.Instance.CharacterMgr.GetMaid(xml.maidGuid);
            //slotId = xml.slotId;
            //apName = xml.apName;
            enabled = xml.enabled;
            localPosition = xml.localPosition;
            localRotation = Quaternion.Euler(xml.localEulerAngles);
            localScale = xml.localScale;
            vidx = xml.vidx;

            ApplyLocalTransform();
        }
    }

    public class CustomPartsXml
    {
        public string maidGuid;
        public TBody.SlotID slotId;
        public string apName;
        public bool enabled;
        public Vector3 localPosition;
        public Vector3 localEulerAngles;
        public Vector3 localScale;
        public int vidx;

        public CustomPartsXml()
        {
        }

        public CustomPartsXml(CustomPartsData data)
        {
            maidGuid = data.maid?.status.guid;
            slotId = data.slotId;
            apName = data.apName;
            enabled = data.enabled;
            localPosition = data.localPosition;
            localEulerAngles = data.localRotation.eulerAngles;
            localScale = data.localScale;
            vidx = data.vidx;
        }
    }
}