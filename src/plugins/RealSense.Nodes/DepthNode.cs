#region usings
using System;

using VVVV.PluginInterfaces.V1;
using VVVV.PluginInterfaces.V2;
using VVVV.Utils.VMath;

#endregion

namespace RealSense.Nodes
{
    [PluginInfo(Name = "Depth", Category = "RealSense", Version = "Intel(R)", Help = "RealSense Depth Image.", Tags = "RealSense, DX11, texture", Author = "aoi")]
    public class DepthNode : BaseNode
    {

        [Input("Position", DefaultValues = new double[] { 0.0, 0.0})]
        private ISpread<Vector2D> FInPosition;

        [Output("Distance", DefaultValue=0.0)]
        private ISpread<float> FOutDistance;

        protected override bool Initialize()
        {
            this.image = null;

            this.GetSessionAndSenseManager();

            this.EnableDepthStream();

            this.InitSenseManager();
            this.GetDevice();
            this.SetMirrorMode();

            this.initialized = true;

            return true;

        }

        protected override void UpdateFrame()
        {
            pxcmStatus ret = this.senseManager.AcquireFrame(true);
            if (ret < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                if (ret == pxcmStatus.PXCM_STATUS_EXEC_ABORTED)
                {
                    // do noting
                }
                else
                {
                    throw new Exception("Could not acquire frame. " + ret.ToString());
                }
            }

            PXCMCapture.Sample sample = this.senseManager.QuerySample();
            if (sample != null)
            {
                // update image
                this.image = sample.depth;
            }

            this.senseManager.ReleaseFrame();

            if (this.image == null) { return; }
            this.invalidate = true;

            // get distance
            PXCMImage.ImageData data;
            ret = this.image.AcquireAccess(
                PXCMImage.Access.ACCESS_READ,
                PXCMImage.PixelFormat.PIXEL_FORMAT_DEPTH, out data
            );
            if (ret < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                throw new Exception("Could not acquire Depth image.");
            }
            var info = this.image.QueryInfo();

            var length = info.width * info.height;
            var buffer = data.ToShortArray(0, length);
            this.image.ReleaseAccess(data);

            for (int i=0; i<FInPosition.SliceCount; i++)
            {
                int index = (int)((FInPosition[i].y * this.width) + FInPosition[i].x);
                int max = this.width * this.height;

                float dst = 0.0f;
                if (0 <= index && index <= max)
                {
                    dst = (float)buffer[index];
                }

                FOutDistance.SliceCount = i + 1;
                FOutDistance[i] = dst;
            }
        }
        
        protected override byte[] GetImageBuffer()
        {
            if (this.image == null)
            {
                return null;
            }

            PXCMImage.ImageData data;
            pxcmStatus ret = this.image.AcquireAccess(
                PXCMImage.Access.ACCESS_READ,
                PXCMImage.PixelFormat.PIXEL_FORMAT_RGB32,
                out data
            );

            if (ret < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                throw new Exception("Could not acquire Depth image.");
            }

            var info = this.image.QueryInfo();
            var length = data.pitches[0] * info.height;
            var buffer = data.ToByteArray(0, length);
            this.image.ReleaseAccess(data);

            return buffer;
        }

        protected override void Uninitialize()
        {
            base.Uninitialize();
        }
    }
}
