using System.Collections.Generic;
using System.IO;
using System.Xml;
using CM3D2.ExternalPreset.Managed;
using CM3D2.ExternalSaveData.Managed;
using COM3D2.MotionTimelineEditor;
using UnityEngine.SceneManagement;

namespace COM3D2.ModItemExplorer.Plugin
{
    public class ExPresetWrapper
    {
        private ExPresetField field = new ExPresetField();

        public static HashSet<string> exsaveNodeNameMap
        {
            get => (HashSet<string>)instance.field.exsaveNodeNameMap.GetValue(null);
        }

        public static XmlDocument xmlMemory
        {
            get => (XmlDocument)instance.field.xmlMemory.GetValue(null);
            set => instance.field.xmlMemory.SetValue(null, value);
        }

        private static ExPresetWrapper _instance = null;
        public static ExPresetWrapper instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new ExPresetWrapper();
                    _instance.Init();
                }
                return _instance;
            }
        }

        public bool initialized { get; private set; } = false;

        private bool Init()
        {
            if (!field.Init())
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

        public static void ExPresetLoad(
            Maid maid,
            string presetPath = null,
            XmlDocument xmlMemory = null)
        {
            instance._ExPresetLoad(maid, presetPath, xmlMemory);
        }

        private void _ExPresetLoad(
            Maid maid,
            string presetPath,
            XmlDocument xmlMemory)
		{
            if (!IsValid())
            {
                return;
            }

			MTEUtils.LogDebug("ExPresetLoad presetPath={0} xmlMemory={1}", presetPath, xmlMemory);

            try
            {
                XmlDocument xmlDocument = null;
                if (xmlMemory != null)
                {
                    xmlDocument = xmlMemory;
                }
                else if (presetPath != null)
                {
                    xmlDocument = LoadExFile(presetPath + ".expreset.xml");
                }
                if (xmlDocument == null)
                {
                    return;
                }

                MTEUtils.LogDebug("ExPresetLoad Apply ExPreset");

                foreach (string text in exsaveNodeNameMap)
                {
                    XmlNode xmlNode = xmlDocument.SelectSingleNode("//plugin[@name='" + text + "']");
                    if (xmlNode != null)
                    {
                        ExSaveData.SetXml(maid, text, xmlNode);
                    }
                }

                if (SceneManager.GetActiveScene().name == "SceneEdit")
                {
                    ExPreset.loadNotify.Invoke();
                }
            }
            catch (System.Exception e)
            {
                MTEUtils.LogException(e);
            }
		}

        private XmlDocument LoadExFile(string filePath)
		{
			if (filePath == null || !File.Exists(filePath))
			{
				return null;
			}

			XmlDocument xmlDocument = new XmlDocument();
			xmlDocument.Load(filePath);
			return xmlDocument;
		}
    }
}