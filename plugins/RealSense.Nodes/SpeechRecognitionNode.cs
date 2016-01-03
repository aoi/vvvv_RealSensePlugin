#region usings
using System;
using System.ComponentModel.Composition;

using VVVV.PluginInterfaces.V1;
using VVVV.PluginInterfaces.V2;

using VVVV.DX11;
using VVVV.Utils.VMath;

using SlimDX.Direct3D11;

using FeralTic.DX11.Resources;
using FeralTic.DX11;

using VVVV.Core.Logging;
using System.Collections;
using System.Collections.Generic;

#endregion

namespace RealSense.Nodes
{
    public enum AudioSources{
    HOGE
    }

    [PluginInfo(Name = "SpeechRecognition", Category = "RealSense", Version = "Intel", Help = "RealSense Speech Recognition.", Tags = "RealSense, DX11, texture", Author = "aoi")]
    public class SpeechRecognitionNode : BaseNode, IPartImportsSatisfiedNotification
    {
        private PXCMAudioSource audioSource;
        private PXCMSpeechRecognition recognition;

        [Input("Audio Source", IsSingle = true)]
        protected ISpread<AudioSources> FInAudioSource;

        [Output("Recognition Data", IsSingle = true, DefaultString = "")]
        protected ISpread<string> FOutRecognitionData;

        private List<PXCMAudioSource.DeviceInfo> deviceInfos;
        private List<PXCMSession.ImplDesc> descs;
        private List<PXCMSpeechRecognition.ProfileInfo> profileInfos;

        private void init()
        {
            // 初期化
            deviceInfos = new List<PXCMAudioSource.DeviceInfo>();
            descs = new List<PXCMSession.ImplDesc>();
            profileInfos = new List<PXCMSpeechRecognition.ProfileInfo>();

            this.GetSessionAndSenseManager();

            pxcmStatus sts = pxcmStatus.PXCM_STATUS_NO_ERROR;

            this.audioSource = this.session.CreateAudioSource();
            if (this.audioSource == null)
            {
                throw new Exception("音声入力デバイスの作成に失敗しました");
            }

            // 音声入力デバイスを列挙する
            // 使用可能なデバイスをスキャンする
            this.audioSource.ScanDevices();

            var deviceNum = this.audioSource.QueryDeviceNum();
            for (int i = 0; i < deviceNum; ++i)
            {
                PXCMAudioSource.DeviceInfo tmpDviceInfo;
                sts = this.audioSource.QueryDeviceInfo(i, out tmpDviceInfo);
                if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
                {
                    throw new Exception("デバイス情報の取得に失敗しました");
                }

                FLogger.Log(LogType.Debug, "デバイス情報: " + tmpDviceInfo.name);
                deviceInfos.Add(tmpDviceInfo);
            }

            this.audioSource.Dispose();


            PXCMSession.ImplDesc inDesc = new PXCMSession.ImplDesc();
            inDesc.cuids[0] = PXCMSpeechRecognition.CUID;

            for (int i = 0; ; ++i)
            {
                // 音声認識エンジンを取得する
                PXCMSession.ImplDesc outDesc = null;
                sts = this.session.QueryImpl(inDesc, i, out outDesc);
                if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
                {
                    break;
                }

                FLogger.Log(LogType.Debug, "音声認識エンジン: " + outDesc.friendlyName);
                descs.Add(outDesc);
            }

            // 対応言語を列挙する
            PXCMSpeechRecognition rec;
            PXCMSession.ImplDesc d = new PXCMSession.ImplDesc();
            d.cuids[0] = PXCMSpeechRecognition.CUID;
            d.iuid = descs[0].iuid;
            sts = this.session.CreateImpl<PXCMSpeechRecognition>(d, out rec);
            if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                throw new Exception("対応言語の設定に失敗しました ");
            }
            for (int i = 0; ; ++i)
            {
                // 音声認識エンジンが持っているプロファイルを取得する
                PXCMSpeechRecognition.ProfileInfo pinfo;
                sts = rec.QueryProfile(i, out pinfo);
                if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
                {
                    break;
                }

                // 対応言語を表示する
                FLogger.Log(LogType.Debug, "対応言語: " + pinfo.language);

                // 日本語のエンジンを使う
                FLogger.Log(LogType.Debug, "set pinfo");
                profileInfos.Add(pinfo);
            }
            rec.Dispose();

            if (profileInfos.Count == 0)
            {
                throw new Exception("音声認識エンジンが見つかりませんでした");
            }
        }

        protected override void Initialize()
        {
            this.GetSessionAndSenseManager();
            //this.EnableColorStream();
            //this.SenseManagerInit();
            //this.GetDevice();
            //this.SetMirrorMode();

            // 音声認識を初期化する
            this.audioSource = this.session.CreateAudioSource();
            if (this.audioSource == null)
            {
                throw new Exception("音声入力デバイスの作成に失敗しました");
            }
            this.audioSource.SetVolume(0.2f);

            pxcmStatus sts = pxcmStatus.PXCM_STATUS_NO_ERROR;

            // 音声入力デバイスを設定する
            sts = this.audioSource.SetDevice(this.deviceInfos[2]);
            if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                throw new Exception("音声入力デバイスの設定に失敗しました");
            }

            // 音声認識エンジンオブジェクトを作成する
            FLogger.Log(LogType.Debug, "set " + descs[0].friendlyName);
            //sts = this.session.CreateImpl<PXCMSpeechRecognition>(descs[0], out this.recognition);
            sts = this.session.CreateImpl<PXCMSpeechRecognition>(out this.recognition);
            if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                throw new Exception("音声認識エンジンオブジェクトの作成に失敗しました");
            }

            // 使用する言語を設定する
            FLogger.Log(LogType.Debug, "set " + profileInfos[0].language);
            sts = this.recognition.SetProfile(profileInfos[0]);
            if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                throw new Exception("音声認識エンジンオブジェクトの設定に失敗しました");
            }

            //this.recognition.AddVocabToDictation(PXCMSpeechRecognition.VocabFileType.VFT_LIST, "");
            // ディクテーションモードを設定する
            sts = this.recognition.SetDictation();
            if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                throw new Exception("ディクテーションモードの設定に失敗しました: " + sts.ToString());
            }

            // 音声認識の通知ハンドラを作成する
            PXCMSpeechRecognition.Handler handler = new PXCMSpeechRecognition.Handler();
            handler.onRecognition = OnRecognition;

            // 音声認識を開始する
            sts = this.recognition.StartRec(this.audioSource, handler);
            if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                throw new Exception("音声認識の開始に失敗しました");
            }

            this.initialized = true;
        }

        private void OnRecognition(PXCMSpeechRecognition.RecognitionData data)
        {
            FOutRecognitionData.SliceCount = 1;
            FOutRecognitionData[0] = data.scores[0].sentence;
        }

        protected override void UpdateFrame()
        {
        }

        protected override byte[] GetImageBuffer()
        {
            return null;
        }

        protected override void Uninitialize()
        {
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

            
            base.Uninitialize();
        }

        public void OnImportsSatisfied()
        {
            this.init();
        }
    }
}
