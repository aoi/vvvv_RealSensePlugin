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
#endregion

namespace RealSense.Nodes
{

    public enum Mode
    {
        Depth,
        Mask
    }

    [PluginInfo(Name = "Hand", Category = "RealSense", Version = "Intel", Help = "RealSense Hand Image.", Tags = "RealSense, DX11, texture", Author = "aoi")]
    public class HandNode : IPluginEvaluate, IDX11ResourceProvider, IDisposable
    {

        private const int WIDTH = 640;
        private const int HEIGHT = 480;
        private const int FPS = 30;
        private const int BYTE_PER_PIXEL = 4;

        private PXCMHandModule handAnalyzer;
        private PXCMHandData handData;

        [Input("Apply", IsBang = true, DefaultValue = 1)]
        protected ISpread<bool> FApply;

        [Input("Manager", IsSingle = true)]
        protected ISpread<PXCMSenseManager> FInManager;

        [Input("Mode", IsSingle = true, DefaultEnumEntry = "Depth")]
        protected ISpread<Mode> FInMode;

        [Output("Texture Out")]
        protected Pin<DX11Resource<DX11DynamicTexture2D>> FTextureOutput;

        [Output("Hand ID")]
        protected ISpread<int> FOutHandID;

        [Output("Joint Position World Out")]
        protected ISpread<Vector3D> FOutJointPositionWorld;

        [Output("Joint Position Image Out")]
        protected ISpread<Vector3D> FOutJointPositionImage;

        [Output("Mass Center Out")]
        protected Pin<Vector3D> FOutMassCenter;

        private bool initialized = false;

        private bool FInvalidate = false;

        private PXCMImage depthImage;
        //private byte[] imageBufferDepth = new byte[WIDTH * HEIGHT];
        private byte[] imageBuffer = new byte[WIDTH * HEIGHT * BYTE_PER_PIXEL];

        [Import()]
        public ILogger FLogger;

        public void Evaluate(int SpreadMax)
        {
            if (this.FApply[0])
            {
                this.FInvalidate = true;
            }

            this.FTextureOutput.SliceCount = 1;
            if (this.FTextureOutput[0] == null) {
                this.FTextureOutput[0] = new DX11Resource<DX11DynamicTexture2D>();
            }


            if (!initialized)
            {
                try
                {
                    this.Initialize();
                }
                catch(Exception e)
                {
                    FLogger.Log(LogType.Error, e.Message);
                    this.initialized = false;
                }
            }
            else
            {
                this.UpdateFrame();
            }
        }

        public void Initialize()
        {

            PXCMSenseManager senseManager = FInManager[0];
            if (senseManager == null)
            {
                return;
            }

            // Depthストリームを有効にする
            pxcmStatus sts = senseManager.EnableStream(PXCMCapture.StreamType.STREAM_TYPE_DEPTH,
                WIDTH, HEIGHT, FPS);
            if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                FLogger.Log(LogType.Error, Convert.ToString(sts.IsSuccessful()));
                throw new Exception("Depthストリームの有効化に失敗しました");
            }

            // 手の検出を有効にする
            sts = senseManager.EnableHand();
            if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                FLogger.Log(LogType.Error, Convert.ToString(sts.IsSuccessful()));
                throw new Exception("手の検出の有効化に失敗しました");
            }

            // パイプラインを初期化する
            sts = senseManager.Init();
            if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                throw new Exception("初期化に失敗しました");
            }

            // ミラー表示にする
            senseManager.QueryCaptureManager().QueryDevice().SetMirrorMode(PXCMCapture.Device.MirrorMode.MIRROR_MODE_HORIZONTAL);

            // 手の検出の初期化
            this.InitializeHandTracking();

            this.initialized = true;
        }

        private void InitializeHandTracking()
        {
            // 手の検出器を取得する
            PXCMSenseManager senseManager = FInManager[0];
            if (senseManager == null)
            {
                return;
            }

            handAnalyzer = senseManager.QueryHand();
            if (handAnalyzer == null)
            {
                throw new Exception("手の検出器の取得に失敗しました");
            }

            // 手のデータを作成する
            handData = handAnalyzer.CreateOutput();
            if (handData == null)
            {
                throw new Exception("手のデータの作成に失敗しました。");
            }

            // RealSense カメラであればプロパティを設定する
            var device = senseManager.QueryCaptureManager().QueryDevice();
            PXCMCapture.DeviceInfo dinfo;
            device.QueryDeviceInfo(out dinfo);
            if (dinfo.model == PXCMCapture.DeviceModel.DEVICE_MODEL_IVCAM) // = Intel RealSense 3D Camera (F200)
            {
                // 手を検出しやすいパラメータを設定
                // RealSense開発チームが設定した一番検出しやすい設定(感覚値)とのこと...(from Intel RealSense SDK センサープログラミング p.135)
                device.SetDepthConfidenceThreshold(1);
                //device.SetMirrorMode(PXCMCapture.Device.MirrorMode.MIRROR_MODE_DISABLED);
                device.SetIVCAMFilterOption(6);
            }

            // 手の検出の設定
            var config = handAnalyzer.CreateActiveConfiguration();
            config.EnableSegmentationImage(true);
            config.ApplyChanges();
            config.Update();
            
        }

        private void UpdateFrame()
        {
            try
            {
                PXCMSenseManager senseManager = FInManager[0];
                if (senseManager == null) {
                    return;
                }
                // フレームを取得する
                pxcmStatus ret = senseManager.AcquireFrame(false);
                if (ret < pxcmStatus.PXCM_STATUS_NO_ERROR)
                {
                    return;
                }

                // フレームデータを取得する
                PXCMCapture.Sample sample = senseManager.QuerySample();
                if (sample != null)
                {
                    depthImage = sample.depth;
                }

                // 手のデータを更新する
                this.UpdateHandFrame();

                senseManager.ReleaseFrame();
            }
            catch (Exception e)
            {
                FLogger.Log(LogType.Error, e.Message);
            }
        }

        private void UpdateHandFrame()
        {
            handData.Update();

            //ピクセルデータを初期化する
            Array.Clear(imageBuffer, 0, imageBuffer.Length);

            // 検出した手の数を取得する
            var numOfHands = handData.QueryNumberOfHands();
            FOutHandID.SliceCount = 0;
            for (int i=0; i<numOfHands; i++)
            {
                int handID = -1;
                pxcmStatus sts = handData.QueryHandId(PXCMHandData.AccessOrderType.ACCESS_ORDER_BY_TIME, i, out handID);
                if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
                {
                    continue;
                }
                FOutHandID.SliceCount = 1 + i;
                FOutHandID[i] = handID;

                // 手を取得する
                PXCMHandData.IHand hand;
                sts = handData.QueryHandData(PXCMHandData.AccessOrderType.ACCESS_ORDER_BY_ID, i, out hand);
                if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
                {
                    continue;
                }

                // 手の画像を取得する
                PXCMImage image;
                sts = hand.QuerySegmentationImage(out image);
                if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
                {
                    continue;
                }

                PXCMImage.ImageData data;
                
                if (FInMode[0] == Mode.Mask)
                {
                    // マスク画像を取得する
                    sts = image.AcquireAccess(PXCMImage.Access.ACCESS_READ, PXCMImage.PixelFormat.PIXEL_FORMAT_Y8, out data);
                    if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
                    {
                        continue;
                    }

                    // マスク画像のサイズはDepthに依存
                    // 手は2つまで
                    var info = image.QueryInfo();

                    // マスク画像をバイト列に変換する
                    var buffer = data.ToByteArray(0, data.pitches[0] * info.height);

                    for (int j = 0; j < info.height * info.width; ++j)
                    {
                        if (buffer[j] != 0)
                        {
                            var index = j * BYTE_PER_PIXEL;
                            // 手のインデックスで色を決める
                            // ID = 0 : 127
                            // ID = 1 : 254
                            var value = (byte)((i + 1) * 127);

                            imageBuffer[index + 0] = value;
                            imageBuffer[index + 1] = value;
                            imageBuffer[index + 2] = value;
                            imageBuffer[index + 3] = 255;
                        }
                    }

                    image.ReleaseAccess(data);
                }

                // 指の関節を列挙する
                int jointCount = PXCMHandData.NUMBER_OF_JOINTS;
                FOutJointPositionWorld.SliceCount = jointCount * (1 + i);
                FOutJointPositionImage.SliceCount = jointCount * (1 + i);
                for (int j = 0; j < jointCount; j++)
                {
                    int sliceIndex = i * jointCount + j;

                    PXCMHandData.JointData jointData;
                    sts = hand.QueryTrackedJoint((PXCMHandData.JointType)j, out jointData);
                    if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
                    {
                        FOutJointPositionWorld[sliceIndex] = new Vector3D(0.0f, 0.0f, 0.0f);
                        FOutJointPositionImage[sliceIndex] = new Vector3D(0.0f, 0.0f, 0.0f);
                        continue;
                    }

                    Vector3D posWorld = new Vector3D(jointData.positionWorld.x, jointData.positionWorld.y, jointData.positionWorld.z);
                    Vector3D posImage = new Vector3D(jointData.positionImage.x, jointData.positionImage.y, jointData.positionImage.z);

                    FOutJointPositionWorld[sliceIndex] = posWorld;
                    FOutJointPositionImage[sliceIndex] = posImage;
                }

                // 手の重心を表示する
                var center = hand.QueryMassCenterWorld();
                Vector3D centerPosition = new Vector3D(center.x, center.y, center.z);
                FOutMassCenter[0] = centerPosition;
            }

        }

        public byte[] GetDepthImage()
        {
            if (depthImage == null)
            {
                return null;
            }

            // データを取得する
            PXCMImage.ImageData data;
            pxcmStatus ret = depthImage.AcquireAccess(
                PXCMImage.Access.ACCESS_READ,
                PXCMImage.PixelFormat.PIXEL_FORMAT_RGB32, out data
            );

            if (ret < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                throw new Exception("Depth画像の取得に失敗");
            }

            // バイト配列に変換する
            var info = depthImage.QueryInfo();
            var length = data.pitches[0] * info.height;

            var buffer = data.ToByteArray(0, length);

            depthImage.ReleaseAccess(data);

            return buffer;
        }

        public void Update(IPluginIO pin, DX11RenderContext context)
        {
            if (this.FTextureOutput.SliceCount == 0) { return; }

            if (this.FInvalidate || !this.FTextureOutput[0].Contains(context))
            {

                SlimDX.DXGI.Format fmt;

                if (FInMode[0] == Mode.Depth)
                {
                    fmt = SlimDX.DXGI.Format.B8G8R8A8_UNorm;
                }
                else if (FInMode[0] == Mode.Mask)
                {
                    fmt = SlimDX.DXGI.Format.B8G8R8A8_UNorm;
                }
                else
                {
                    FLogger.Log(LogType.Error, "モードが不正です");
                    return;
                }

                Texture2DDescription desc;

                if (this.FTextureOutput[0].Contains(context))
                {
                    desc = this.FTextureOutput[0][context].Resource.Description;

                    if (desc.Width != WIDTH || desc.Height != HEIGHT || desc.Format != fmt)
                    {
                        this.FTextureOutput[0].Dispose(context);
                        DX11DynamicTexture2D t2D = new DX11DynamicTexture2D(context, WIDTH, HEIGHT, fmt);

                        this.FTextureOutput[0][context] = t2D;
                    }
                }
                else
                {
                    this.FTextureOutput[0][context] = new DX11DynamicTexture2D(context, WIDTH, HEIGHT, fmt);
#if DEBUG
                    this.FTextureOutput[0][context].Resource.DebugName = "DynamicTexture";
#endif
                }

                desc = this.FTextureOutput[0][context].Resource.Description;

                var t = this.FTextureOutput[0][context];

                if (FInMode[0] == Mode.Depth)
                {
                    if (depthImage != null)
                    {
                        t.WriteData(this.GetDepthImage());
                        this.FInvalidate = false;
                    }

                }
                else
                {
                    if (imageBuffer != null)
                    {
                        t.WriteData(imageBuffer);
                        this.FInvalidate = false;
                    }
                }

                
            }
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

        public void Destroy(IPluginIO pin, DX11RenderContext context, bool force)
        {
            this.FTextureOutput[0].Dispose(context);

            /*PXCMSenseManager senseManager = FInManager[0];
            if (senseManager != null)
            {
                senseManager.Dispose();
                senseManager = null;
            }

            if (handData != null)
            {
                handData.Dispose();
                handData = null;
            }

            if (handAnalyzer != null)
            {
                handAnalyzer.Dispose();
                handAnalyzer = null;
            }*/
        }
    }
}
