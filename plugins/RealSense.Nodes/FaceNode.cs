#region usings
using System;
using System.ComponentModel.Composition;

using VVVV.PluginInterfaces.V1;
using VVVV.PluginInterfaces.V2;
using VVVV.Utils.VMath;

using VVVV.DX11;

using SlimDX.Direct3D11;

using FeralTic.DX11.Resources;
using FeralTic.DX11;

using VVVV.Core.Logging;
#endregion

namespace RealSense.Nodes
{
    [PluginInfo(Name = "Face", Category = "RealSense", Version = "Intel", Help = "RealSense Face.", Tags = "RealSense, DX11, texture", Author = "aoi")]
    public class FaceNode : IPluginEvaluate, IDX11ResourceProvider, IDisposable
    {
        private const int COLOR_WIDTH = 640;
        private const int COLOR_HEIGHT = 480;
        private const int COLOR_FPS = 30;

        [Import()]
        public ILogger FLogger;

        [Input("Enabled", IsSingle = true)]
        protected ISpread<bool> FInEnabled;

        [Input("Apply", IsBang = true, DefaultValue = 1)]
        protected ISpread<bool> FApply;

        [Output("FacePosition")]
        protected ISpread<Vector2D> FOutFacePosition;

        [Output("FaceWidth")]
        protected ISpread<int> FOutFaceWidth;
        [Output("FaceHeight")]
        protected ISpread<int> FOutFaceHeight;

        [Output("Texture Out")]
        protected Pin<DX11Resource<DX11DynamicTexture2D>> FTextureOutput;

        private PXCMSession session;
        private PXCMSenseManager senseManager;
        private PXCMCapture.Device device;
        private PXCMFaceConfiguration config;
        private PXCMFaceModule faceModule;
        private PXCMFaceData faceData;

        private bool initialized = false;
        private PXCMImage image; 
        private bool FInvalidate;

        public void Evaluate(int SpreadMax)
        {
            if (!FInEnabled[0]) { return; }

            if (this.FApply[0]) { this.FInvalidate = true; }

            if (this.image == null)
            {
                if (this.FTextureOutput.SliceCount == 1)
                {
                    if (this.FTextureOutput[0] != null) { this.FTextureOutput[0].Dispose(); }
                    this.FTextureOutput.SliceCount = 0;
                }
            }
            else
            {
                this.FTextureOutput.SliceCount = 1;
                if (this.FTextureOutput[0] == null) { this.FTextureOutput[0] = new DX11Resource<DX11DynamicTexture2D>(); }
            }

            if (!initialized)
            {
                try
                {
                    this.Initialize();
                }
                catch (Exception e)
                {
                    FLogger.Log(LogType.Error, e.Message);

                    this.Uninitialize();
                }
            }
            else
            {
                try
                {
                    this.UpdateFrame();
                }
                catch (Exception e)
                {
                    FLogger.Log(LogType.Error, e.Message);
                    this.Uninitialize();
                }
            }
        }

        private void Initialize()
        {
            this.session = PXCMSession.CreateInstance();
            if (this.session == null)
            {
                throw new Exception("セッションの作成に失敗しました");
            }
            this.senseManager = this.session.CreateSenseManager();
            if (this.senseManager == null)
            {
                throw new Exception("マネージャの作成に失敗しました");
            }

            pxcmStatus sts = this.senseManager.EnableStream(PXCMCapture.StreamType.STREAM_TYPE_COLOR, COLOR_WIDTH, COLOR_HEIGHT, COLOR_FPS);
            if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                throw new Exception("カラーストリームの有効化に失敗しました");
            }

            //sts = this.senseManager.EnableStream(PXCMCapture.StreamType.STREAM_TYPE_DEPTH, COLOR_WIDTH, COLOR_HEIGHT, COLOR_FPS);
            //if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
            //{
            //    throw new Exception("Depthストリームの有効化に失敗しました");
            //}

            this.InitializeFace();

            this.initialized = true;
            
        }

        private void InitializeFace()
        {
            // 顔検出を有効化する
            pxcmStatus sts = this.senseManager.EnableFace();
            if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                throw new Exception("顔検出の有効化に失敗しました");
            }

            // 顔検出器を生成する
            this.faceModule = this.senseManager.QueryFace();
            if (this.faceModule == null)
            {
                throw new Exception("顔検出器の取得に失敗しました");
            }

            // 顔検出のプロパティを取得
            this.config = this.faceModule.CreateActiveConfiguration();
            this.config.SetTrackingMode(PXCMFaceConfiguration.TrackingModeType.FACE_MODE_COLOR_PLUS_DEPTH);
            this.config.ApplyChanges();

            // パイプラインを初期化する
            sts = this.senseManager.Init();
            if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                throw new Exception("初期化に失敗しました: " + sts.ToString());
            }

            // デバイス情報の取得
            this.device = this.senseManager.QueryCaptureManager().QueryDevice();
            if (this.device == null)
            {
                throw new Exception("デバイスの作成に失敗しました");
            }

            // ミラー表示にする
            sts = this.device.SetMirrorMode(PXCMCapture.Device.MirrorMode.MIRROR_MODE_HORIZONTAL);
            if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                throw new Exception("ミラー表示の設定に失敗しました");
            }

            PXCMCapture.DeviceInfo deviceInfo;
            this.device.QueryDeviceInfo(out deviceInfo);
            if (deviceInfo.model == PXCMCapture.DeviceModel.DEVICE_MODEL_IVCAM)
            {
                this.device.SetDepthConfidenceThreshold(1);
                this.device.SetIVCAMFilterOption(6);
                //this.device.SetIVCAMMotionRangeTradeOff(21);
            }

            this.config.detection.isEnabled = true;
            this.config.detection.maxTrackedFaces = 2;
            this.config.ApplyChanges();

            this.faceData = faceModule.CreateOutput();
        }


        private void UpdateFrame()
        {
            // フレームを取得する
            pxcmStatus sts = this.senseManager.AcquireFrame(true);
            if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                //throw new Exception("フレームの取得に失敗しました");
                FLogger.Log(LogType.Debug, "フレームの取得に失敗しました: " + sts.ToString());
                return;
            }

            // 顔のデータを更新する
            this.updateFaceFrame();

            // フレームを開放する
            this.senseManager.ReleaseFrame();
        }

        private void updateFaceFrame()
        {
            // フレームデータを取得する
            PXCMCapture.Sample sample = this.senseManager.QuerySample();
            this.image = sample.color;

            // SenseManagerモジュールの顔のデータを更新する
            this.faceData.Update();

            // 検出した顔の数を取得する
            int numFaces = this.faceData.QueryNumberOfDetectedFaces();

            for (int i=0; i<numFaces; ++i)
            {
                // 顔の情報を取得する
                PXCMFaceData.Face face = this.faceData.QueryFaceByIndex(i);
                
                // 顔の位置を取得:Depthで取得する
                var detection = face.QueryDetection();
                if (detection != null)
                {
                    PXCMRectI32 faceRect;
                    detection.QueryBoundingRect(out faceRect);
                    int sliceCount = i + i;
                    FOutFacePosition.SliceCount = sliceCount;
                    FOutFacePosition[i] = new Vector2D(faceRect.x, faceRect.y);
                    FOutFaceWidth.SliceCount = sliceCount;
                    FOutFaceWidth[i] = faceRect.w;
                    FOutFaceHeight.SliceCount = sliceCount;
                    FOutFaceHeight[i] = faceRect.h;
                }
            }

        }

        public void Update(IPluginIO pin, DX11RenderContext context)
        {
            if (!this.initialized) { return; }

            if (this.FTextureOutput.SliceCount == 0) { return; }

            if (this.FInvalidate || !this.FTextureOutput[0].Contains(context))
            {

                SlimDX.DXGI.Format fmt = SlimDX.DXGI.Format.B8G8R8A8_UNorm;

                Texture2DDescription desc;

                if (this.FTextureOutput[0].Contains(context))
                {
                    desc = this.FTextureOutput[0][context].Resource.Description;

                    if (desc.Width != COLOR_WIDTH || desc.Height != COLOR_HEIGHT || desc.Format != fmt)
                    {
                        this.FTextureOutput[0].Dispose(context);
                        DX11DynamicTexture2D t2D = new DX11DynamicTexture2D(context, COLOR_WIDTH, COLOR_HEIGHT, fmt);

                        this.FTextureOutput[0][context] = t2D;
                    }
                }
                else
                {
                    this.FTextureOutput[0][context] = new DX11DynamicTexture2D(context, COLOR_WIDTH, COLOR_HEIGHT, fmt);
#if DEBUG
                    this.FTextureOutput[0][context].Resource.DebugName = "DynamicTexture";
#endif
                }

                desc = this.FTextureOutput[0][context].Resource.Description;

                var t = this.FTextureOutput[0][context];

                byte[] buffer = this.GetColorImage(image);
                if (buffer != null)
                {
                    t.WriteData(buffer);
                    this.FInvalidate = false;
                }
            }
        }

        public byte[] GetColorImage(PXCMImage colorFrame)
        {
            if (colorFrame == null)
            {
                return null;
            }

            // データを取得する
            PXCMImage.ImageData data;
            pxcmStatus ret = colorFrame.AcquireAccess(
                PXCMImage.Access.ACCESS_READ,
                PXCMImage.PixelFormat.PIXEL_FORMAT_RGB32, out data
            );

            if (ret < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                throw new Exception("カラー画像の取得に失敗");
            }

            // バイト配列に変換する
            var info = colorFrame.QueryInfo();
            var length = data.pitches[0] * info.height;

            var buffer = data.ToByteArray(0, length);

            /*var width = (int)data.pitches[0] / sizeof(Int32);
            var height = (int)image.info.height;
            var length = width * height;
            var buffer = data.ToByteArray(0, length);*/
            FLogger.Log(LogType.Debug, "length: " + length.ToString());
            colorFrame.ReleaseAccess(data);

            return buffer;
        }

        private void Uninitialize()
        {
            this.initialized = false;

            if (this.faceData != null)
            {
                this.faceData.Dispose();
                this.faceData = null;
            }
            if (this.config != null)
            {
                this.config.Dispose();
                this.config = null;
            }
            if (this.faceModule != null)
            {
                this.faceModule.Dispose();
                this.faceModule = null;
            }

            

            if (this.device != null)
            {
                this.device.Dispose();
                this.device = null;
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

        public void Destroy(IPluginIO pin, DX11RenderContext context, bool force)
        {
            this.FTextureOutput[0].Dispose(context);
        }

        public void Dispose()
        {
            if (this.FTextureOutput.SliceCount > 0)
            {
                if (this.FTextureOutput[0] != null)
                {
                    this.FTextureOutput[0].Dispose();
                }
            }
        }
    }
}
