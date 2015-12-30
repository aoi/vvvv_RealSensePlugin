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
        Color,
        Depth,
        Mask
    }

    [PluginInfo(Name = "Hand", Category = "RealSense", Version = "Intel", Help = "RealSense Hand Image.", Tags = "RealSense, DX11, texture", Author = "aoi")]
    public class HandNode : IPluginEvaluate, IDX11ResourceProvider, IDisposable
    {

        private const int DEPTH_WIDTH = 640;
        private const int DEPTH_HEIGHT = 480;

        private const int COLOR_WIDTH = 640;
        private const int COLOR_HEIGHT = 480;
        private const int FPS = 30;
        private const int BYTE_PER_PIXEL = 4;

        private PXCMProjection projection;

        private PXCMSession session;
        private PXCMSession colorSession;
        private PXCMSenseManager senseManager;
        private PXCMSenseManager colorSenseManager;
        private PXCMCapture.Device device;
        private PXCMCapture.Device colorDevice;
        private PXCMHandModule handAnalyzer;
        private PXCMHandData handData;

        [Input("Apply", IsBang = true, DefaultValue = 1)]
        protected ISpread<bool> FApply;

        [Input("Mode", IsSingle = true, DefaultEnumEntry = "Depth")]
        protected ISpread<Mode> FInMode;

        [Input("Enabled", IsSingle = true, DefaultValue = 0)]
        protected ISpread<bool> FInEnabled;

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

        private PXCMImage image;
        //private byte[] imageBufferDepth = new byte[WIDTH * HEIGHT];
        private byte[] imageBuffer = new byte[DEPTH_WIDTH * DEPTH_HEIGHT * BYTE_PER_PIXEL];

        [Import()]
        public ILogger FLogger;

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

            if (FInMode.IsChanged)
            {
                this.Uninitialize();
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
                }
            }
        }

        public void Initialize()
        {
            this.session = PXCMSession.CreateInstance();
            if (this.session == null)
            {
                throw new Exception("PXCMSessionの取得に失敗しました");
            }
            this.colorSession = PXCMSession.CreateInstance();
            if (this.colorSession == null)
            {
                throw new Exception("カラー画像用PXCMSessionの取得に失敗しました");
            }

            this.senseManager = this.session.CreateSenseManager();
            if (this.senseManager == null)
            {
                throw new Exception("PXCMSenseManagerの取得に失敗しました");
            }
            this.colorSenseManager = this.colorSession.CreateSenseManager();
            if (this.colorSenseManager == null)
            {
                throw new Exception("カラー画像用PXCMSenseManagerの取得に失敗しました");
            }

            pxcmStatus sts;
            if (FInMode[0] == Mode.Color)
            {
                //PXCMVideoModule.DataDesc ddesc = new PXCMVideoModule.DataDesc();
                //ddesc.deviceInfo.streams = PXCMCapture.StreamType.STREAM_TYPE_COLOR | PXCMCapture.StreamType.STREAM_TYPE_DEPTH;
                //sts = this.senseManager.EnableStreams(ddesc);
                //if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
                //{
                //    throw new Exception("カラーストリーム、Depthストリームの有効化に失敗しました");
                //}

                // カラーストリームを有効にする
                sts = this.colorSenseManager.EnableStream(PXCMCapture.StreamType.STREAM_TYPE_COLOR, COLOR_WIDTH, COLOR_HEIGHT, FPS);
                if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
                {
                    throw new Exception("カラーストリームの有効化に失敗しました");
                }

                
            }
            // Depthストリームを有効にする
            sts = this.senseManager.EnableStream(PXCMCapture.StreamType.STREAM_TYPE_DEPTH, DEPTH_WIDTH, DEPTH_HEIGHT, FPS);
            if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                throw new Exception("Depthストリームの有効化に失敗しました");
            }
            

            // 手の検出を有効にする
            sts = this.senseManager.EnableHand();
            if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                throw new Exception("手の検出の有効化に失敗しました");
            }

            // パイプラインを初期化する
            sts = this.senseManager.Init();
            if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                throw new Exception("初期化に失敗しました: " + sts.ToString());
            }

            sts = this.colorSenseManager.Init();
            if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                throw new Exception("カラー画像の初期化に失敗しました");
            }

            // ミラー表示にする
            this.device = this.senseManager.QueryCaptureManager().QueryDevice();
            sts = this.device.SetMirrorMode(PXCMCapture.Device.MirrorMode.MIRROR_MODE_HORIZONTAL);
            if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                throw new Exception("ミラー表示の設定に失敗しました");
            }
            this.colorDevice = this.colorSenseManager.QueryCaptureManager().QueryDevice();
            sts = this.colorDevice.SetMirrorMode(PXCMCapture.Device.MirrorMode.MIRROR_MODE_HORIZONTAL);
            if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                throw new Exception("カラー画像のミラー表示の設定に失敗しました");
            }

            // 座標変換オブジェクトを作成
            projection = this.device.CreateProjection(); 

            // 手の検出の初期化
            this.InitializeHandTracking(senseManager);

            this.initialized = true;
        }

        private void InitializeHandTracking(PXCMSenseManager senseManager)
        {
            // 手の検出器を取得する
            this.handAnalyzer = senseManager.QueryHand();
            if (this.handAnalyzer == null)
            {
                throw new Exception("手の検出器の取得に失敗しました");
            }

            // 手のデータを作成する
            this.handData = handAnalyzer.CreateOutput();
            if (this.handData == null)
            {
                throw new Exception("手のデータの作成に失敗しました。");
            }

            // RealSense カメラであればプロパティを設定する
            PXCMCapture.DeviceInfo dinfo;
            this.device.QueryDeviceInfo(out dinfo);
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
            FLogger.Log(LogType.Debug, "UpdateFrame");

            // フレームを取得する
            pxcmStatus ret = this.senseManager.AcquireFrame(true);
            if (ret < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                if (ret == pxcmStatus.PXCM_STATUS_EXEC_ABORTED || ret == pxcmStatus.PXCM_STATUS_DEVICE_LOST)
                {
                    this.Uninitialize();
                }

                throw new Exception("フレームの取得に失敗しました: " + ret.ToString());
            }
            ret = this.colorSenseManager.AcquireFrame(true);
            if (ret < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                if (ret == pxcmStatus.PXCM_STATUS_EXEC_ABORTED || ret == pxcmStatus.PXCM_STATUS_DEVICE_LOST)
                {
                    this.Uninitialize();
                }

                throw new Exception("フレームの取得に失敗しました: " + ret.ToString());
            }

            // フレームデータを取得する
            PXCMCapture.Sample sample = this.senseManager.QuerySample();
            if (sample == null)
            {
                FLogger.Log(LogType.Debug, "フレームデータの取得に失敗しました");
                return;
            }

            PXCMCapture.Sample colorSample = this.colorSenseManager.QuerySample();
            if (colorSample == null)
            {
                FLogger.Log(LogType.Debug, "カラー画像用フレームデータの取得に失敗しました");
                return;
            }

            if (FInMode[0] == Mode.Color)
            {
                FLogger.Log(LogType.Debug, "Update Color Image");
                this.image = colorSample.color;
            }
            else
            {
                FLogger.Log(LogType.Debug, "Update Depth Image");
                this.image = sample.depth;
            }

            // 手のデータを更新する
            this.UpdateHandFrame();

            senseManager.ReleaseFrame();
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
                pxcmStatus sts = this.handData.QueryHandId(PXCMHandData.AccessOrderType.ACCESS_ORDER_BY_TIME, i, out handID);
                if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
                {
                    FLogger.Log(LogType.Debug, "手のIDの取得に失敗しました");
                    continue;
                }
                FOutHandID.SliceCount = 1 + i;
                FOutHandID[i] = handID;

                // 手を取得する
                PXCMHandData.IHand hand;
                sts = this.handData.QueryHandData(PXCMHandData.AccessOrderType.ACCESS_ORDER_BY_ID, i, out hand);
                if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
                {
                    FLogger.Log(LogType.Debug, "手のデータの取得に失敗しました");
                    continue;
                }

                // 手の画像を取得する
                PXCMImage img;
                sts = hand.QuerySegmentationImage(out img);
                if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
                {
                    FLogger.Log(LogType.Debug, "手の画像の取得に失敗しました");
                    continue;
                }

                PXCMImage.ImageData data;
                
                if (FInMode[0] == Mode.Mask)
                {
                    // マスク画像を取得する
                    sts = img.AcquireAccess(PXCMImage.Access.ACCESS_READ, PXCMImage.PixelFormat.PIXEL_FORMAT_Y8, out data);
                    if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
                    {
                        FLogger.Log(LogType.Debug, "マスク画像の取得に失敗しました");
                        continue;
                    }

                    // マスク画像のサイズはDepthに依存
                    // 手は2つまで
                    var info = img.QueryInfo();

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

                    img.ReleaseAccess(data);
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

                    if (FInMode[0] == Mode.Color)
                    {
                        // Depth座標系をカラー座標系に変換する
                        var depthPoint = new PXCMPoint3DF32[1];
                        var colorPoint = new PXCMPointF32[1];
                        depthPoint[0].x = jointData.positionImage.x;
                        depthPoint[0].y = jointData.positionImage.y;
                        depthPoint[0].z = jointData.positionWorld.z * 1000;
                        projection.MapDepthToColor(depthPoint, colorPoint);

                        Vector3D posWorld = new Vector3D(colorPoint[0].x, jointData.positionWorld.y, jointData.positionWorld.z);
                        Vector3D posImage = new Vector3D(colorPoint[0].x, colorPoint[0].y, 0.0f);

                        FOutJointPositionWorld[sliceIndex] = posWorld;
                        FOutJointPositionImage[sliceIndex] = posImage;

                    }
                    else
                    {
                        Vector3D posWorld = new Vector3D(jointData.positionWorld.x, jointData.positionWorld.y, jointData.positionWorld.z);
                        Vector3D posImage = new Vector3D(jointData.positionImage.x, jointData.positionImage.y, jointData.positionImage.z);

                        FOutJointPositionWorld[sliceIndex] = posWorld;
                        FOutJointPositionImage[sliceIndex] = posImage;
                    }

                }

                // 手の重心を表示する
                var center = hand.QueryMassCenterWorld();
                Vector3D centerPosition = new Vector3D(center.x, center.y, center.z);
                FOutMassCenter[0] = centerPosition;
            }

        }

        public byte[] GetDepthImage()
        {
            if (this.image == null)
            {
                return null;
            }

            // データを取得する
            PXCMImage.ImageData data;
            pxcmStatus ret = this.image.AcquireAccess(
                PXCMImage.Access.ACCESS_READ,
                PXCMImage.PixelFormat.PIXEL_FORMAT_RGB32, out data
            );

            if (ret < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                throw new Exception("Depth画像の取得に失敗");
            }

            // バイト配列に変換する
            var info = image.QueryInfo();
            var length = data.pitches[0] * info.height;

            var buffer = data.ToByteArray(0, length);

            image.ReleaseAccess(data);

            return buffer;
        }

        public void Update(IPluginIO pin, DX11RenderContext context)
        {
            if (!this.initialized) { return; }

            if (this.FTextureOutput.SliceCount == 0) { return; }

            if (this.FInvalidate || !this.FTextureOutput[0].Contains(context))
            {

                SlimDX.DXGI.Format fmt　= SlimDX.DXGI.Format.B8G8R8A8_UNorm;

                Texture2DDescription desc;

                if (this.FTextureOutput[0].Contains(context))
                {
                    desc = this.FTextureOutput[0][context].Resource.Description;

                    if (desc.Width != DEPTH_WIDTH || desc.Height != DEPTH_HEIGHT || desc.Format != fmt)
                    {
                        this.FTextureOutput[0].Dispose(context);
                        DX11DynamicTexture2D t2D = new DX11DynamicTexture2D(context, DEPTH_WIDTH, DEPTH_HEIGHT, fmt);

                        this.FTextureOutput[0][context] = t2D;
                    }
                }
                else
                {
                    this.FTextureOutput[0][context] = new DX11DynamicTexture2D(context, DEPTH_WIDTH, DEPTH_HEIGHT, fmt);
#if DEBUG
                    this.FTextureOutput[0][context].Resource.DebugName = "DynamicTexture";
#endif
                }

                desc = this.FTextureOutput[0][context].Resource.Description;

                var t = this.FTextureOutput[0][context];

                if (FInMode[0] == Mode.Depth || FInMode[0] == Mode.Color)
                {

                    var buffer = this.GetDepthImage();
                    if (buffer != null)
                    {
                        FLogger.Log(LogType.Debug, "WriteDate image");
                        t.WriteData(buffer);
                        this.FInvalidate = false;
                    }

                }
                else
                {
                    if (imageBuffer != null)
                    {
                        FLogger.Log(LogType.Debug, "WriteDate imageBuffer");
                        t.WriteData(imageBuffer);
                        this.FInvalidate = false;
                    }
                }

                
            }
        }

        public void Dispose()
        {
            FLogger.Log(LogType.Debug, "Dispose");
            if (this.FTextureOutput.SliceCount > 0)
            {
                if (this.FTextureOutput[0] != null)
                {
                    this.FTextureOutput[0].Dispose();
                }
            }

            this.Uninitialize();
            
        }

        public void Destroy(IPluginIO pin, DX11RenderContext context, bool force)
        {
            FLogger.Log(LogType.Debug, "Destory");
            this.FTextureOutput[0].Dispose(context);
        }

        private void Uninitialize()
        {
            this.initialized = false;

            if (this.handData != null)
            {
                this.handData.Dispose();
                this.handData = null;
            }

            if (this.handAnalyzer != null)
            {
                this.handAnalyzer.Dispose();
                this.handAnalyzer = null;
            }

            if (this.device != null)
            {
                this.device.Dispose();
                this.device = null;
            }
            if (this.colorDevice != null)
            {
                this.colorDevice.Dispose();
                this.colorDevice = null;
            }

            if (this.senseManager != null)
            {
                this.senseManager.Close();
                this.senseManager.Dispose();
                this.senseManager = null;
            }
            if (this.colorSenseManager != null)
            {
                this.colorSenseManager.Close();
                this.colorSenseManager.Dispose();
                this.colorSenseManager = null;
            }

            if (this.session != null)
            {
                this.session.Dispose();
                this.session = null;
            }
            if (this.colorSession != null)
            {
                this.colorSession.Dispose();
                this.colorSession = null;
            }
        }
    }
}
