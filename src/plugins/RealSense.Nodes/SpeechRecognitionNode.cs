#region usings
using System;
using System.ComponentModel.Composition;

using VVVV.PluginInterfaces.V1;
using VVVV.PluginInterfaces.V2;

using VVVV.Core.Logging;
using System.Collections;
using System.Collections.Generic;

#endregion

namespace RealSense.Nodes
{
    [PluginInfo(Name = "SpeechRecognition", Category = "RealSense", Version = "Intel(R)", Help = "RealSense Speech Recognition.", Tags = "RealSense, DX11, texture", Author = "aoi")]
    public class SpeechRecognitionNode : IPluginEvaluate, IDisposable, IPartImportsSatisfiedNotification
    {
        private PXCMAudioSource audioSource;
        private PXCMSpeechRecognition recognition;

        [Input("Audio Device", EnumName = "AudioDevice", IsSingle = true)]
        protected ISpread<EnumEntry> FInAudioDevice;

        [Input("Language", EnumName = "Language", IsSingle = true)]
        protected IDiffSpread<EnumEntry> FInLanguage;

        [Output("Recognition Data", IsSingle = true, DefaultString = "")]
        protected ISpread<string> FOutRecognitionData;

        [Input("Enabled", IsSingle = true, DefaultValue = 0)]
        protected ISpread<bool> FInEnabled;

        [Import()]
        protected ILogger FLogger;

        private List<PXCMSession.ImplDesc> descs;
        private List<PXCMAudioSource.DeviceInfo> deviceInfos;
        private List<PXCMSpeechRecognition.ProfileInfo> profileInfos;

        private bool initialized = false;
        private bool initializedLanguage = false;

        private PXCMSession session;
        private PXCMSenseManager senseManager;
        private PXCMCapture.Device device;

        public void Evaluate(int SpreadMax)
        {
            if (this.initialized && !FInEnabled[0])
            {
                this.Uninitialize();
            }

            if (!this.initializedLanguage)
            {
                try
                {
                    this.GetLanguages();
                }
                catch (Exception e)
                {
                    FLogger.Log(LogType.Error, e.Message + e.StackTrace);
                }

                return;
            }

            if (!FInEnabled[0]) { return; }

            if (!this.initialized)
            {
                try
                {
                    this.Initialize();
                }
                catch (Exception e)
                {
                    FLogger.Log(LogType.Error, e.Message + e.StackTrace);
                    this.Uninitialize();
                }
            }
        }

        private void Initialize()
        {
            this.GetSessionAndSenseManager();

            this.audioSource = this.session.CreateAudioSource();

            pxcmStatus sts = this.session.CreateImpl<PXCMSpeechRecognition>(this.descs[0], out this.recognition);
            if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                throw new Exception("Could not create audio source.");
            }

            // 音声入力デバイスを設定する
            //PXCMAudioSource.DeviceInfo dinfo = (PXCMAudioSource.DeviceInfo)deviceInfos[FInAudioDevice[0]];
            //FLogger.Log(LogType.Debug, dinfo.name);
            //this.audioSource.SetDevice(dinfo);
            for (int i = 0; i < deviceInfos.Count; i++)
            {
                PXCMAudioSource.DeviceInfo dinfo = deviceInfos[i];
                if (dinfo.name.Equals(FInAudioDevice[0]))
                {
                    this.audioSource.SetDevice(dinfo);
                }
            }

            // set language
            for (int i = 0; i < profileInfos.Count; i++ )
            {
                PXCMSpeechRecognition.ProfileInfo pinfo = profileInfos[i];
                if (pinfo.language.ToString().Equals(FInLanguage[0]))
                {
                    this.recognition.SetProfile(pinfo);
                }
            }

            // set dictation mode
            sts = this.recognition.SetDictation();
            if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                throw new Exception("Could not set dictation mode. " + sts.ToString());
            }

            PXCMSpeechRecognition.Handler handler = new PXCMSpeechRecognition.Handler();
            handler.onRecognition = OnRecognition;

            sts = this.recognition.StartRec(this.audioSource, handler);
            if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                throw new Exception("Could not start recording.");
            }

            this.initialized = true;
        }

        private void GetSessionAndSenseManager()
        {
            if (this.senseManager != null) { return; }
            this.senseManager = PXCMSenseManager.CreateInstance();
            if (this.senseManager == null)
            {
                throw new Exception("Could not create Sense Manager.");
            }

            this.session = this.senseManager.session;
        }

        private void OnRecognition(PXCMSpeechRecognition.RecognitionData data)
        {
            if (data == null || data.scores == null || data.scores.Length == 0)
            {
                return;
            }

            FOutRecognitionData.SliceCount = 1;
            FOutRecognitionData[0] = data.scores[0].sentence;
        }

        public void OnImportsSatisfied()
        {
            FLogger.Log(LogType.Debug, "OnImport");
            this.descs = new List<PXCMSession.ImplDesc>();
            
            this.GetSessionAndSenseManager();

            pxcmStatus sts = pxcmStatus.PXCM_STATUS_NO_ERROR;

            PXCMAudioSource audio = this.session.CreateAudioSource();
            if (audio == null)
            {
                throw new Exception("Could not create audio source.");
            }

            // enumrate audio source
            // scan available devices
            this.deviceInfos = new List<PXCMAudioSource.DeviceInfo>();
            audio.ScanDevices();

            int deviceNum = audio.QueryDeviceNum();
            string[] deviceNames = new string[deviceNum];
            for (int i = 0; i < deviceNum; ++i)
            {
                PXCMAudioSource.DeviceInfo tmpDeviceInfo;
                sts = audio.QueryDeviceInfo(i, out tmpDeviceInfo);
                if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
                {
                    throw new Exception("Could not get audio device.");
                }

                FLogger.Log(LogType.Debug, "audio device info: " + tmpDeviceInfo.name);
                deviceNames[i] = tmpDeviceInfo.name;
                this.deviceInfos.Add(tmpDeviceInfo);
            }

            EnumManager.UpdateEnum("AudioDevice", deviceNames[0], deviceNames);
            audio.Dispose();


            PXCMSession.ImplDesc inDesc = new PXCMSession.ImplDesc();
            inDesc.cuids[0] = PXCMSpeechRecognition.CUID;

            for (int i = 0; ; ++i)
            {
                // get speech recognition engine
                PXCMSession.ImplDesc outDesc = null;
                sts = this.session.QueryImpl(inDesc, i, out outDesc);
                if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
                {
                    break;
                }

                FLogger.Log(LogType.Debug, "speech recognition engine: " + outDesc.friendlyName);
                this.descs.Add(outDesc);
            }
        }

        private void GetLanguages()
        {

            if (this.descs == null || this.descs.Count == 0) { return; }

            PXCMSpeechRecognition sr;
            // enumrate available language
            pxcmStatus sts = this.session.CreateImpl<PXCMSpeechRecognition>(this.descs[0], out sr);
            if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                if (sr != null)
                {
                    sr.Dispose();
                }
                throw new Exception("Could not set language.");
            }

            List<string> languages = new List<string>();
            this.profileInfos = new List<PXCMSpeechRecognition.ProfileInfo>();
            for (int i = 0; ; ++i)
            {
                // get profile that speech recognition engine have
                PXCMSpeechRecognition.ProfileInfo pinfo;
                sts = sr.QueryProfile(i, out pinfo);
                if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
                {
                    break;
                }

                // display available languages
                FLogger.Log(LogType.Debug, "available languages: " + pinfo.language);
                languages.Add(pinfo.language.ToString());
                profileInfos.Add(pinfo);
            }

            if (0 == languages.Count)
            {
                sr.Dispose();
                throw new Exception("Could not find available languages.");
            }

            EnumManager.UpdateEnum("Language", languages[0], languages.ToArray());

            if (profileInfos.Count == 0)
            {
                sr.Dispose();
                throw new Exception("Could not find speech recognition engine.");
            }

            sr.Dispose();

            this.initializedLanguage = true;
        }

        protected void Uninitialize()
        {
            try
            {
                if (this.device != null)
                {
                    this.device.Dispose();
                    this.device = null;
                }

                if (this.recognition != null)
                {
                    this.recognition.Dispose();
                    this.recognition = null;
                }

                if (this.audioSource != null)
                {
                    this.audioSource.Dispose();
                    this.audioSource = null;
                }

                if (this.senseManager != null)
                {
                    this.senseManager.Close();
                    this.senseManager.Dispose();
                    this.senseManager = null;
                }

                if (this.session != null)
                {
                    this.session.Dispose();
                    this.session = null;
                }
            }
            catch( Exception e)
            {
                FLogger.Log(LogType.Debug, e.Message, e.StackTrace);
            }

            this.initialized = false;
        }

        public void Dispose()
        {
            this.Uninitialize();
        }
    }
}
