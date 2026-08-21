using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;

namespace COM3D2.ModItemExplorer.Plugin
{
    /// <summary>
    /// モデル配置先の振り分けファサード。MTE(ModelHackManagerWrapper)連携と
    /// 自前配置(SelfModelPlacer)を束ね、既存UIからは単一の配置窓口に見せる。
    /// </summary>
    public class ModelPlacerManager
    {
        private static ModelPlacerManager _instance = null;
        public static ModelPlacerManager instance
            => _instance ?? (_instance = new ModelPlacerManager());

        private ModelHackManagerWrapper mteWrapper => ModelHackManagerWrapper.instance;
        private SelfModelPlacer selfPlacer => SelfModelPlacer.instance;

        public List<string> pluginNames
        {
            get
            {
                var names = new List<string> { SelfModelPlacer.PluginName };
                if (mteWrapper.IsValid())
                {
                    var mteNames = mteWrapper.pluginNames;
                    if (mteNames != null)
                    {
                        names.AddRange(mteNames);
                    }
                }
                return names;
            }
        }

        public List<StudioModelStatWrapper> modelList
        {
            get
            {
                var list = new List<StudioModelStatWrapper>(selfPlacer.modelList);
                if (mteWrapper.IsValid())
                {
                    var mteList = mteWrapper.modelList;
                    if (mteList != null)
                    {
                        list.AddRange(mteList);
                    }
                }
                return list;
            }
        }

        public void CreateModel(string label, string fileName, int group, string pluginName, bool visible)
        {
            if (pluginName == SelfModelPlacer.PluginName)
            {
                selfPlacer.CreateModel(fileName, group, visible);
                return;
            }

            if (!mteWrapper.IsValid())
            {
                MTEUtils.LogWarning("MotionTimelineEditorが無効なため配置できません。{0}", pluginName);
                return;
            }
            mteWrapper.CreateModel(label, fileName, group, pluginName, visible);
        }

        /// <summary>
        /// 背景オブジェクト (.asset_bg) を配置する。
        /// MTE / StudioMode の配置経路は .menu 名を渡す前提でアセットバンドルを扱えないため、
        /// 配置先は常に自前配置になる
        /// </summary>
        public void CreateBgObject(string assetBundleName, int group, bool visible)
        {
            selfPlacer.CreateBgObject(assetBundleName, group, visible);
        }

        public void DeleteModel(StudioModelStatWrapper model)
        {
            if (model == null)
            {
                return;
            }

            if (selfPlacer.Owns(model))
            {
                selfPlacer.DeleteModel(model);
                return;
            }

            if (!mteWrapper.IsValid())
            {
                MTEUtils.LogWarning("MotionTimelineEditorが無効なため削除できません。{0}", model.name);
                return;
            }
            mteWrapper.DeleteModel(model);
        }

        /// <summary>
        /// 自前配置分を全て破棄する。MTE 側は MTE が自分で後始末するため触らない
        /// </summary>
        public void DeleteAllSelfModels()
        {
            selfPlacer.DeleteAll();
        }
    }
}
