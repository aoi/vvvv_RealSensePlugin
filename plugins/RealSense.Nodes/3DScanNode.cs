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

    [PluginInfo(Name = "3DScan", Category = "RealSense", Version = "Intel", Help = "RealSense 3DScan.", Tags = "RealSense, DX11, texture", Author = "aoi")]
    public class _3DScanNode : BaseNode
    {
        private PXCM3DScan scanner;
        private PXCM3DScan.FileFormat fileFormat = PXCM3DScan.FileFormat.OBJ;

        [Input("ScanningMode", IsSingle = true, DefaultEnumEntry = "FACE")]
        private ISpread<PXCM3DScan.ScanningMode> FInScanningMode;

        [Input("ReconstructionOption", IsSingle = true, DefaultEnumEntry = "NONE")]
        private ISpread<PXCM3DScan.ReconstructionOption> FInReconstructionOption;


        [Input("Reconstruct", IsSingle = true, DefaultBoolean = false)]
        private ISpread<bool> FInReconstruct;

        protected override void Initialize()
        {
            this.GetSessionAndSenseManager();

            var sts = this.senseManager.Enable3DScan();
            if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                throw new Exception("初期化に失敗しました");
            }

            this.InitSenseManager();
            this.GetDevice();
            this.SetMirrorMode();

            this.Initialize3DScan();

            this.initialized = true;
        }

        private void Initialize3DScan()
        {
            // スキャナーを取得する
            this.scanner = senseManager.Query3DScan();
            if (this.scanner == null)
            {
                throw new Exception("スキャナーの取得に失敗しました");
            }

            PXCM3DScan.Configuration config = new PXCM3DScan.Configuration();
            config.startScan = true;
            config.mode = FInScanningMode[0];
            config.options = FInReconstructionOption[0];
            config.maxTriangles = 1000;
            config.maxVertices = 10000;

            pxcmStatus sts = this.scanner.SetConfiguration(config);
            if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                throw new Exception("スキャナーの設定に失敗しました");
            }

        }

        private void Reconstruct()
        {
            if (this.scanner.IsScanning())
            {
                string desktop =  System.Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
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
            pxcmStatus sts = this.senseManager.AcquireFrame(false);
            if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                return;
            }

            if (this.scanner != null)
            {
                this.image = this.scanner.AcquirePreviewImage();
            }

            if (FInReconstruct[0])
            {
                this.Reconstruct();
            }

            this.senseManager.ReleaseFrame();
        }

        protected override byte[] GetImageBuffer()
        {
            if (this.image == null) { return null; }

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
            FLogger.Log(LogType.Debug, info.width.ToString());

            var length = data.pitches[0] * info.height;

            var buffer = data.ToByteArray(0, length);

            // データを開放する
            this.image.ReleaseAccess(data);

            return buffer;
        }

        protected override void Uninitialize()
        {
            if (this.scanner != null)
            {
                this.scanner.Dispose();
            }

            base.Uninitialize();
        }
    }
}
