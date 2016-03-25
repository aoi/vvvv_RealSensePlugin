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
    public class HandNode : BaseNode
    {

        private const int COLOR_WIDTH = 640;
        private const int COLOR_HEIGHT = 480;

        private PXCMProjection projection;
        private PXCMHandModule handAnalyzer;
        private PXCMHandData handData;

        [Input("Mode", IsSingle = true, DefaultEnumEntry = "Depth")]
        protected ISpread<Mode> FInMode;

        [Output("Hand ID")]
        protected ISpread<int> FOutHandID;

        [Output("Joint Position World Out")]
        protected ISpread<Vector3D> FOutJointPositionWorld;

        [Output("Joint Position Image Out")]
        protected ISpread<Vector3D> FOutJointPositionImage;

        [Output("Mass Center Out")]
        protected Pin<Vector3D> FOutMassCenter;

        private byte[] imageBuffer;

        public HandNode()
        {
            this.imageBuffer = new byte[this.width * this.height * BYTE_PER_PIXEL];
        }

        protected override void Initialize()
        {
            this.imageBuffer = new byte[this.width * this.height * BYTE_PER_PIXEL];

            this.GetSessionAndSenseManager();

            pxcmStatus sts;
            if (FInMode[0] == Mode.Color)
            {
                this.EnableColorStream();
            }

            // Depthストリームを有効にする
            this.EnableDepthStream();

            // 手の検出を有効にする
            sts = this.senseManager.EnableHand();
            if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                throw new Exception("手の検出の有効化に失敗しました");
            }

            // パイプラインを初期化する
            this.InitSenseManager();

            // ミラー表示にする
            this.GetDevice();
            this.SetMirrorMode();

            // 座標変換オブジェクトを作成
            this.projection = this.device.CreateProjection();

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
            this.handData = this.handAnalyzer.CreateOutput();
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
                device.SetIVCAMFilterOption(6);
            }

            // 手の検出の設定
            var config = this.handAnalyzer.CreateActiveConfiguration();
            config.EnableSegmentationImage(true);
            config.ApplyChanges();
            config.Update();

        }

        protected override void UpdateFrame()
        {
            // フレームを取得する
            pxcmStatus ret = this.senseManager.AcquireFrame(true);
            if (ret < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                if (ret == pxcmStatus.PXCM_STATUS_EXEC_ABORTED)
                {
                    // do noting
                }
                else
                {
                    throw new Exception("フレームの取得に失敗しました: " + ret.ToString());
                }
            }

            // フレームデータを取得する
            PXCMCapture.Sample sample = this.senseManager.QuerySample();
            if (sample == null)
            {
                FLogger.Log(LogType.Debug, "フレームデータの取得に失敗しました");
                return;
            }

            if (FInMode[0] == Mode.Color)
            {
                this.image = sample.color;
            }
            else
            {
                this.image = sample.depth;
            }

            // 手のデータを更新する
            this.UpdateHandFrame();

            this.senseManager.ReleaseFrame();
        }

        private void UpdateHandFrame()
        {
            if (this.handData == null) { return; }
            this.handData.Update();

            //ピクセルデータを初期化する
            Array.Clear(this.imageBuffer, 0, this.imageBuffer.Length);

            // 検出した手の数を取得する
            var numOfHands = this.handData.QueryNumberOfHands();
            FOutHandID.SliceCount = 0;
            for (int i = 0; i < numOfHands; i++)
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
                

                PXCMImage.ImageData data;

                if (FInMode[0] == Mode.Mask)
                {
                    // 手の画像を取得する
                    sts = hand.QuerySegmentationImage(out this.image);
                    if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
                    {
                        FLogger.Log(LogType.Debug, "手の画像の取得に失敗しました");
                        continue;
                    }

                    // マスク画像を取得する
                    sts = this.image.AcquireAccess(PXCMImage.Access.ACCESS_READ, PXCMImage.PixelFormat.PIXEL_FORMAT_Y8, out data);
                    if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
                    {
                        FLogger.Log(LogType.Debug, "マスク画像の取得に失敗しました");
                        continue;
                    }

                    // マスク画像のサイズはDepthに依存
                    // 手は2つまで
                    var info = this.image.QueryInfo();

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

                    this.image.ReleaseAccess(data);
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

        protected override byte[] GetImageBuffer()
        {
            if (this.image == null)
            {
                return null;
            }


            // データを取得する
            PXCMImage.ImageData data;

            if (FInMode[0] == Mode.Mask)
            {
                return this.imageBuffer;
            }
            else
            {
                pxcmStatus ret = this.image.AcquireAccess(
                    PXCMImage.Access.ACCESS_READ,
                    PXCMImage.PixelFormat.PIXEL_FORMAT_RGB32, out data
                );

                if (ret < pxcmStatus.PXCM_STATUS_NO_ERROR)
                {
                    throw new Exception("画像の取得に失敗しました");
                }

                // バイト配列に変換する
                var info = this.image.QueryInfo();
                var length = data.pitches[0] * info.height;

                var buffer = data.ToByteArray(0, length);

                this.image.ReleaseAccess(data);

                return buffer;
            }

        }

        protected override void Uninitialize()
        {
            this.initialized = false;

            if (this.projection != null)
            {
                this.projection.Dispose();
                this.projection = null;
            }

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

            base.Uninitialize();
        }
    }
}
