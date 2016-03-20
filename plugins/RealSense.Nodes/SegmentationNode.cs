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
    [PluginInfo(Name = "Segmentation", Category = "RealSense", Version = "Intel", Help = "RealSense Segmentation Image.", Tags = "RealSense, DX11, texture", Author = "aoi")]
    public class SegmentationNode : BaseNode
    {

        private PXCM3DSeg segmentation;

        protected override void Initialize()
        {
            this.session = PXCMSession.CreateInstance();
            if (this.session == null)
            {
                throw new Exception("セッションを作成できませんでした");
            }

            this.senseManager = this.session.CreateSenseManager();
            if (this.senseManager == null)
            {
                throw new Exception("マネージャを作成できませんでした");
            }

            this.EnableColorStream();
            //this.EnableDepthStream();
            pxcmStatus sts = this.senseManager.Enable3DSeg();
            if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                throw new Exception("セグメンテーションの有効化に失敗しました");
            }

            // パイプラインを初期化する
            this.InitSenseManager();

            this.segmentation = this.senseManager.Query3DSeg();
            if (this.segmentation == null)
            {
                throw new Exception("セグメンテーションの取得に失敗しました");
            }

            this.GetDevice();

            this.SetMirrorMode();

            this.initialized = true;
        }

        protected override void UpdateFrame()
        {
            // フレームを取得する
            pxcmStatus sts = this.senseManager.AcquireFrame(false);
            if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                return;
            }

            if (this.segmentation != null)
            {
                this.image = this.segmentation.AcquireSegmentedImage();
            }
            else
            {
                this.image = this.senseManager.QuerySample().color;
            }

            // フレームを開放する
            this.senseManager.ReleaseFrame();
        }

        protected override byte[] GetImageBuffer()
        {
            if (this.image == null) { return null; }

            byte[] imageBuffer = new byte[WIDTH * HEIGHT * BYTE_PER_PIXEL];

            // データを取得する
            PXCMImage.ImageData data;
            pxcmStatus sts = this.image.AcquireAccess(PXCMImage.Access.ACCESS_READ, PXCMImage.PixelFormat.PIXEL_FORMAT_RGB32, out data);
            if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                return null;
            }

            // ピクセルデータを取得する
            Array.Clear(imageBuffer, 0, imageBuffer.Length);

            // セグメンテーション画像をバイト列に変換する
            var info = this.image.QueryInfo();
            var b = data.ToByteArray(0, data.pitches[0] * info.height);

            for (int i=0; i<(info.height*info.width); ++i)
            {
                var index = i * BYTE_PER_PIXEL;

                // α値が0でない場合には有効な場所として色をコピーする
                if (b[index + 3] != 0)
                {
                    imageBuffer[index + 0] = b[index + 0];
                    imageBuffer[index + 1] = b[index + 1];
                    imageBuffer[index + 2] = b[index + 2];
                    imageBuffer[index + 3] = 255;
                }
                // α値が0の場合はピクセルデータのα値を0にする
                else
                {
                    imageBuffer[index + 3] = 0;
                }
            }

            // ピクセルデータを更新する
            // Update()で行う

            // データを開放する
            this.image.ReleaseAccess(data);

            return imageBuffer;
        }

        protected override void Uninitialize()
        {
            FLogger.Log(LogType.Debug, "child uninitialize");
            if (this.segmentation != null)
            {
                this.segmentation.Dispose();
                this.segmentation = null;
            }

            base.Uninitialize();
        }
    }
}
