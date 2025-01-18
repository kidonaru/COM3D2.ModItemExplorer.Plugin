using System.Collections;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;

namespace COM3D2.ModItemExplorer.Plugin
{
    public class ModelHackManagerWrapper
    {
        private ModelHackManagerField modelHackManagerField = new ModelHackManagerField();
        private StudioModelStatField studioModelStatField = new StudioModelStatField();
        private StudioModelManagerField studioModelManagerField = new StudioModelManagerField();
        private OfficialObjectInfoField officialObjectInfoField = new OfficialObjectInfoField();

        private object _modelHackManager = null;
        public object modelHackManager
        {
            get
            {
                if (_modelHackManager == null)
                {
                    _modelHackManager = modelHackManagerField.instance.GetValue(null, null);
                }
                return _modelHackManager;
            }
        }

        private object _studioModelManager = null;
        public object studioModelManager
        {
            get
            {
                if (_studioModelManager == null)
                {
                    _studioModelManager = studioModelManagerField.instance.GetValue(null, null);
                }
                return _studioModelManager;
            }
        }

        public IList modelListOriginal
        {
            get => (IList)modelHackManagerField.modelList.GetValue(modelHackManager, null);
        }

        public List<string> pluginNames
        {
            get => (List<string>)modelHackManagerField.pluginNames.GetValue(modelHackManager, null);
        }

        public List<StudioModelStatWrapper> modelList
        {
            get
            {
                try
                {
                    var modelListOriginal = this.modelListOriginal;
                    var modelList = new List<StudioModelStatWrapper>(modelListOriginal.Count);
                    foreach (var model in modelListOriginal)
                    {
                        var modelWrapper = studioModelStatField.ConvertToWrapper(model);
                        modelWrapper.infoWrapper = officialObjectInfoField.ConvertToWrapper(modelWrapper.info);
                        modelList.Add(modelWrapper);
                    }

                    return modelList;
                }
                catch (System.Exception e)
                {
                    MTEUtils.LogException(e);
                    return null;
                }
            }
        }

        private static ModelHackManagerWrapper _instance = null;
        public static ModelHackManagerWrapper instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new ModelHackManagerWrapper();
                    _instance.Init();
                }
                return _instance;
            }
        }

        public bool initialized { get; private set; } = false;

        public bool Init()
        {
            if (!modelHackManagerField.Init())
            {
                return false;
            }

            var assembly = modelHackManagerField.assembly;

            if (!studioModelManagerField.Init(assembly))
            {
                return false;
            }

            if (!studioModelStatField.Init(assembly))
            {
                return false;
            }

            if (!officialObjectInfoField.Init(assembly))
            {
                return false;
            }

            initialized = true;

            return true;
        }

        public bool IsValid()
        {
            return initialized;
        }

        public void DeleteModel(StudioModelStatWrapper model)
        {
            if (model == null)
            {
                return;
            }
            modelHackManagerField.DeleteModel.Invoke(modelHackManager, new object[] { model.original });
        }

        public void CreateModel(StudioModelStatWrapper model)
        {
            var original = studioModelStatField.ConvertToOriginal(model);
            modelHackManagerField.CreateModel.Invoke(modelHackManager, new object[] { original });
        }

        public object FindOfficialObject(
            string label,
            string fileName,
            int myRoomId,
            long bgObjectId
        )
        {
            return studioModelManagerField.FindOfficialObject.Invoke(studioModelManager, new object[]
            {
                label, fileName, myRoomId, bgObjectId
            });
        }

        public void CreateModel(
            string label,
            string fileName,
            int group,
            string pluginName,
            bool visible
        )
        {
            var info = FindOfficialObject(label, fileName, 0, 0);
            if (info != null)
            {
                var wrapper = new StudioModelStatWrapper();
                wrapper.info = info;
                wrapper.group = group;
                wrapper.pluginName = pluginName;
                wrapper.visible = visible;
                CreateModel(wrapper);
            }
        }
    }
}