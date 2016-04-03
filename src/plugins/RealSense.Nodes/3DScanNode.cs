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

    [PluginInfo(Name = "3DScan", Category = "RealSense", Version = "Intel", Help = "RealSense 3DScan.", Tags = "RealSense, DX11, texture", Author = "aoi")]
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
        

        private int prevWidth = 0;
        private int prevHeight = 0;

        protected override void Initialize()
        {
            this.width = 320;
            this.height = 240;

            this.GetSessionAndSenseManager();

            pxcmStatus sts = this.senseManager.Enable3DScan();
            if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                throw new Exception("3Dスキャンの有効化に失敗しました");
            }

            this.InitSenseManager();

            this.Initialize3DScan();

            this.initializing = false;
            this.initialized = true;
        }

        private void Initialize3DScan()
        {
            // スキャナーを取得する
            this.scanner = this.senseManager.Query3DScan();
            if (this.scanner == null)
            {
                throw new Exception("スキャナーの取得に失敗しました");
            }
        }

        private void StartScan()
        {
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
                throw new Exception("スキャナーの設定に失敗しました");
            }

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
                throw new Exception("スキャナーの設定に失敗しました");
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
            // フレームを取得する
            pxcmStatus ret = this.senseManager.AcquireFrame(false);
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

            if (this.scanner != null)
            {
                this.image = this.scanner.AcquirePreviewImage();
                if (this.image != null)
                {
                    this.invalidate = true;
                }

                if (FInScan.IsChanged)
                {
                    if (FInScan[0])
                    {
                        this.StartScan();
                    }
                    else
                    {
                        this.EndScan();
                    }
                }
            }

            this.senseManager.ReleaseFrame();

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

            // データを開放する
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
