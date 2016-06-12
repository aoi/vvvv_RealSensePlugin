#region usings
using System;
using System.ComponentModel.Composition;
using System.Threading.Tasks;

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

    [PluginInfo(Name = "3DScan", Category = "RealSense", Version = "Intel(R)", Help = "RealSense 3DScan.", Tags = "RealSense, DX11, texture", Author = "aoi")]
    public class _3DScanNode : BaseNode
    {
        private PXCM3DScan scanner;
        private PXCM3DScan.FileFormat fileFormat = PXCM3DScan.FileFormat.OBJ;

        [Input("ScanningMode", IsSingle = true, DefaultEnumEntry = "FACE")]
        private ISpread<PXCM3DScan.ScanningMode> FInScanningMode;

        [Input("ReconstructionOption", IsSingle = true, DefaultEnumEntry = "NONE")]
        private ISpread<PXCM3DScan.ReconstructionOption> FInReconstructionOption;

        [Input("MaxTriangles", IsSingle = true, DefaultValue = 100)]
        private ISpread<int> FInMaxTriangles;

        [Input("MaxVertices", IsSingle = true, DefaultValue = 100)]
        private ISpread<int> FInMaxVertices;

        [Input("Scan", IsSingle = true, DefaultBoolean = false)]
        private IDiffSpread<bool> FInScan;

        //[Input("Reconstruct", IsSingle = true, DefaultBoolean = false)]
        //private ISpread<bool> FInReconstruct;

        [Output("Status", IsSingle = true, DefaultString = "")]
        private ISpread<string> FOutStatus;

        private int prevWidth = 0;
        private int prevHeight = 0;


        protected override bool Initialize()
        {
            this.GetSessionAndSenseManager();

            pxcmStatus sts = this.senseManager.Enable3DScan();
            if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                throw new Exception("Could not enable 3D Scan.");
            }

            this.StartScan();

            this.initialized = true;

            return true;
        }

        private void StartScan()
        {
            // get scanner
            this.scanner = this.senseManager.Query3DScan();
            if (this.scanner == null)
            {
                throw new Exception("Could not get 3D Scan.");
            }

            if (this.scanner == null)
            {
                return;
            }

            PXCM3DScan.Configuration config = new PXCM3DScan.Configuration();
            config.startScan = true;
            config.mode = FInScanningMode[0];
            config.options = FInReconstructionOption[0];
            config.maxTriangles = FInMaxTriangles[0];
            config.maxVertices = FInMaxVertices[0];

            pxcmStatus sts = this.scanner.SetConfiguration(config);
            if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                throw new Exception("Could not set 3D Scan configuration.");
            }

            this.InitSenseManager();
        }

        private void EndScan()
        {
            if (this.scanner == null)
            {
                return;
            }

            this.Reconstruct();

            PXCM3DScan.Configuration config = new PXCM3DScan.Configuration();
            config.startScan = false;
            config.mode = FInScanningMode[0];
            config.options = FInReconstructionOption[0];
            config.maxTriangles = FInMaxTriangles[0];
            config.maxVertices = FInMaxVertices[0];

            pxcmStatus sts = this.scanner.SetConfiguration(config);
            if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                throw new Exception("Could not set 3D Scan configuration.");
            }

            this.Uninitialize();
        }

        private void Reconstruct()
        {
            if (this.scanner.IsScanning())
            {
                string desktop = System.Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                var time = DateTime.Now.ToString("hhmmss", System.Globalization.CultureInfo.CurrentUICulture.DateTimeFormat);
                var fileName = desktop + "\\" + string.Format("model-{0}.{1}", time, PXCM3DScan.FileFormatToString(this.fileFormat));
                FLogger.Log(LogType.Debug, "fileName: " + fileName);

                var sts = this.scanner.Reconstruct(this.fileFormat, fileName);
                if (sts != pxcmStatus.PXCM_STATUS_NO_ERROR)
                {
                    FLogger.Log(LogType.Debug, sts.ToString());
                }
            }
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

                this.image = this.scanner.AcquirePreviewImage();
                if (this.image != null)
                {
                    this.invalidate = true;
                }

                this.senseManager.ReleaseFrame();
            }

            FOutStatus.SliceCount = 1;
            if (this.scanner.IsScanning())
            {
                FOutStatus[0] = "Scanning";
            }
            else
            {
                FOutStatus[0] = "Not Scanning";
            }
            
        }

        protected override byte[] GetImageBuffer()
        {
            if (this.image == null) { return null; }

            PXCMImage.ImageData data;
            pxcmStatus ret = this.image.AcquireAccess(
                PXCMImage.Access.ACCESS_READ,
                PXCMImage.PixelFormat.PIXEL_FORMAT_RGB32, out data
            );

            if (ret < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                throw new Exception("Could not get Color image.");
            }

            var info = this.image.QueryInfo();

            this.width = info.width;
            this.height = info.height;

            if (this.width != this.prevWidth || this.height != this.prevHeight)
            {
                this.isResized = true;
            }
            else
            {
                this.isResized = false;
            }

            this.prevWidth = this.width;
            this.prevHeight = this.height;
            
            var length = data.pitches[0] * this.height;

            var buffer = data.ToByteArray(0, length);

            this.image.ReleaseAccess(data);

            return buffer;
        }

        protected override void Uninitialize()
        {
            if (this.scanner != null)
            {
                this.scanner.Dispose();
                this.scanner = null;
            }

            base.Uninitialize();
        }
    }
}
