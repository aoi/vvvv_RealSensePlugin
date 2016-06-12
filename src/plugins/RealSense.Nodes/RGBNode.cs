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
#endregion

namespace RealSense.Nodes
{
    [PluginInfo(Name = "RGB", Category = "RealSense", Version = "Intel(R)", Help = "RealSense RGB Image.", Tags = "RealSense, DX11, texture", Author = "aoi")]
    public class RGBNode : BaseNode
    {
        protected override bool Initialize()
        {
            this.image = null;

            this.GetSessionAndSenseManager();

            this.EnableColorStream();

            this.InitSenseManager();
            this.GetDevice();
            this.SetMirrorMode();

            this.initialized = true;

            return true;

        }

        protected override void UpdateFrame()
        {

            pxcmStatus ret = this.senseManager.AcquireFrame(false);
            if (ret < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                return;
            }

            PXCMCapture.Sample sample = this.senseManager.QuerySample();
            if (sample != null)
            {
                this.image = sample.color;
            }

            if (this.image != null)
            {
                this.invalidate = true;
            }

            senseManager.ReleaseFrame();
        }

        protected override  byte[] GetImageBuffer()
        {
            if (this.image == null)
            {
                return null;
            }

            PXCMImage.ImageData data;
            pxcmStatus ret = this.image.AcquireAccess(
                PXCMImage.Access.ACCESS_READ,
                PXCMImage.PixelFormat.PIXEL_FORMAT_RGB32, out data
            );

            if (ret < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                throw new Exception("Could not acquire Color image.");
            }

            var info = this.image.QueryInfo();
            var length = data.pitches[0] * info.height;

            var buffer = data.ToByteArray(0, length);

            this.image.ReleaseAccess(data);

            return buffer;
        }
    }
}
