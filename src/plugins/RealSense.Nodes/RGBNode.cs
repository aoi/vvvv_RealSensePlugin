#region usings
using System;
using System.ComponentModel.Composition;

using VVVV.PluginInterfaces.V1;
using VVVV.PluginInterfaces.V2;

using VVVV.DX11;

using SlimDX.Direct3D11;

using FeralTic.DX11.Resources;
using FeralTic.DX11;

using VVVV.Core.Logging;
using System.Threading.Tasks;
#endregion

namespace RealSense.Nodes
{
    [PluginInfo(Name = "RGB", Category = "RealSense", Version = "Intel", Help = "RealSense RGB Image.", Tags = "RealSense, DX11, texture", Author = "aoi")]
    public class RGBNode : BaseNode
    {
        protected override void Initialize()
        {
            this.image = null;

            this.GetSessionAndSenseManager();

            this.EnableColorStream();

            this.InitSenseManager();
            this.GetDevice();
            this.SetMirrorMode();

            this.initializing = false;
            this.initialized = true;
        }

        protected override void UpdateFrame()
        {

            // フレームを取得する
            pxcmStatus ret = this.senseManager.AcquireFrame(false);
            if (ret < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                return;
            }

            // フレームデータを取得する
            PXCMCapture.Sample sample = this.senseManager.QuerySample();
            if (sample != null)
            {
                // 画像データを更新
                this.image = sample.color;
            }

            if (this.image != null)
            {
                this.invalidate = true;
            }

            // フレームを開放する
            senseManager.ReleaseFrame();
        }

        protected override  byte[] GetImageBuffer()
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
                throw new Exception("カラー画像の取得に失敗");
            }

            // バイト配列に変換する
            var info = this.image.QueryInfo();
            var length = data.pitches[0] * info.height;

            var buffer = data.ToByteArray(0, length);

            this.image.ReleaseAccess(data);

            return buffer;
        }
    }
}
