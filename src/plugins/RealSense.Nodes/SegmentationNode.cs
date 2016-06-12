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
    [PluginInfo(Name = "Segmentation", Category = "RealSense", Version = "Intel(R)", Help = "RealSense Segmentation Image.", Tags = "RealSense, DX11, texture", Author = "aoi")]
    public class SegmentationNode : BaseNode
    {

        private PXCM3DSeg segmentation;

        protected override bool Initialize()
        {

            this.GetSessionAndSenseManager();

            this.EnableColorStream();

            pxcmStatus sts = this.senseManager.Enable3DSeg();
            if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                throw new Exception("Could not enable 3D Segmentation.");
            }

            this.InitSenseManager();

            this.segmentation = this.senseManager.Query3DSeg();
            if (this.segmentation == null)
            {
                throw new Exception("Could not get 3D Segmentation.");
            }

            this.GetDevice();

            this.SetMirrorMode();

            this.initialized = true;

            return true;
        }

        protected override void UpdateFrame()
        {
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

            if (this.image != null)
            {
                this.invalidate = true;
            }

            this.senseManager.ReleaseFrame();
        }

        protected override byte[] GetImageBuffer()
        {
            if (this.image == null) { return null; }

            byte[] imageBuffer = new byte[width * height * BYTE_PER_PIXEL];

            PXCMImage.ImageData data;
            pxcmStatus sts = this.image.AcquireAccess(PXCMImage.Access.ACCESS_READ, PXCMImage.PixelFormat.PIXEL_FORMAT_RGB32, out data);
            if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                return null;
            }

            Array.Clear(imageBuffer, 0, imageBuffer.Length);

            var info = this.image.QueryInfo();
            var b = data.ToByteArray(0, data.pitches[0] * info.height);

            for (int i=0; i<(info.height*info.width); ++i)
            {
                var index = i * BYTE_PER_PIXEL;

                // if α value is not 0, set color
                if (b[index + 3] != 0)
                {
                    imageBuffer[index + 0] = b[index + 0];
                    imageBuffer[index + 1] = b[index + 1];
                    imageBuffer[index + 2] = b[index + 2];
                    imageBuffer[index + 3] = 255;
                }
                // if α value is 0, pixel color is nothing.
                else
                {
                    imageBuffer[index + 3] = 0;
                }
            }

            this.image.ReleaseAccess(data);

            return imageBuffer;
        }

        protected override void Uninitialize()
        {
            if (this.segmentation != null)
            {
                this.segmentation.Dispose();
                this.segmentation = null;
            }

            base.Uninitialize();
        }
    }
}
