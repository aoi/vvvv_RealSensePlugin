#region usings
using System;
using System.ComponentModel.Composition;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using VVVV.PluginInterfaces.V1;
using VVVV.PluginInterfaces.V2;

using VVVV.DX11;

using SlimDX.Direct3D11;
using SlimDX;

using FeralTic.DX11.Resources;
using FeralTic.DX11;

using VVVV.Core.Logging;
#endregion

namespace RealSense.Nodes
{
    [PluginInfo(Name = "RGB", Category = "RealSense", Version = "Intel", Help = "RealSense RGB Image.", Tags = "RealSense, DX11, texture", Author = "aoi")]
    public class RGBNode : IPluginEvaluate, IDX11ResourceProvider, IDisposable
    {
        private const int COLOR_WIDTH = 640;
        private const int COLOR_HEIGHT = 480;
        private const int COLOR_FPS = 30;

        [Import()]
        public ILogger FLogger;

        [Input("Apply", IsBang = true, DefaultValue = 1)]
        protected ISpread<bool> FApply;

        [Input("Manager", IsSingle = true)]
        protected ISpread<PXCMSenseManager> FInManager;

        [Input("Enabled", IsSingle = true)]
        protected ISpread<bool> FInEnabled;

        [Output("Texture Out")]
        protected Pin<DX11Resource<DX11DynamicTexture2D>> FTextureOutput;

        private bool initialized = false;

        private PXCMImage image;
        private bool FInvalidate;



        public void Evaluate(int SpreadMax)
        {
            if (!FInEnabled[0])
            {
                return;
            }

            if (this.FApply[0])
            {
                this.FInvalidate = true;
            }

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


            if (!initialized)
            {
                this.Initialize();
            }
            else
            {
                FLogger.Log(LogType.Debug, "Do UpdateFrame.");
                this.UpdateFrame();
            }
        }

        public void Initialize()
        {
            this.image = null;

            PXCMSenseManager senseManager = FInManager[0];
            if (senseManager == null)
            {
                return;
            }

            // カラーストリームを有効にする
            pxcmStatus sts = senseManager.EnableStream(PXCMCapture.StreamType.STREAM_TYPE_COLOR,
                COLOR_WIDTH, COLOR_HEIGHT, COLOR_FPS);
            if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                throw new Exception("カラーストリームの有効化に失敗しました");
            }

            // パイプラインを初期化する
            sts = senseManager.Init();
            if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                throw new Exception("初期化に失敗しました");
            }

            // ミラー表示にする
            senseManager.QueryCaptureManager().QueryDevice().SetMirrorMode(PXCMCapture.Device.MirrorMode.MIRROR_MODE_HORIZONTAL);

            FLogger.Log(LogType.Debug, "Initialized.");

            this.initialized = true;

        }

        public void UpdateFrame()
        {
            PXCMSenseManager senseManager = FInManager[0];

            // フレームを取得する
            pxcmStatus ret = senseManager.AcquireFrame(false);
            if (ret < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                return;
            }

            // フレームデータを取得する
            PXCMCapture.Sample sample = senseManager.QuerySample();
            if (sample != null)
            {
                // 画像データを更新
                image = sample.color;
            }

            // フレームを開放する
            senseManager.ReleaseFrame();
        }

        public byte[] GetColorImage(PXCMImage colorFrame)
        {
            if (colorFrame == null)
            {
                return null;
            }

            // データを取得する
            PXCMImage.ImageData data;
            pxcmStatus ret = colorFrame.AcquireAccess(
                PXCMImage.Access.ACCESS_READ,
                PXCMImage.PixelFormat.PIXEL_FORMAT_RGB32, out data
            );

            if (ret < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                throw new Exception("カラー画像の取得に失敗");
            }

            // バイト配列に変換する
            var info = colorFrame.QueryInfo();
            var length = data.pitches[0] * info.height;

            var buffer = data.ToByteArray(0, length);

            /*var width = (int)data.pitches[0] / sizeof(Int32);
            var height = (int)image.info.height;
            var length = width * height;
            var buffer = data.ToByteArray(0, length);*/
            FLogger.Log(LogType.Debug, "length: " + length.ToString());
            colorFrame.ReleaseAccess(data);

            return buffer;
        }

        public void Update(IPluginIO pin, DX11RenderContext context)
        {
            FLogger.Log(LogType.Debug, "Update.");

            if (this.FTextureOutput.SliceCount == 0) { return; }

            if (this.FInvalidate || !this.FTextureOutput[0].Contains(context))
            {

                SlimDX.DXGI.Format fmt = SlimDX.DXGI.Format.B8G8R8A8_UNorm;

                Texture2DDescription desc;

                if (this.FTextureOutput[0].Contains(context))
                {
                    desc = this.FTextureOutput[0][context].Resource.Description;

                    if (desc.Width != COLOR_WIDTH || desc.Height != COLOR_HEIGHT || desc.Format != fmt)
                    {
                        this.FTextureOutput[0].Dispose(context);
                        DX11DynamicTexture2D t2D = new DX11DynamicTexture2D(context, COLOR_WIDTH, COLOR_HEIGHT, fmt);

                        this.FTextureOutput[0][context] = t2D;
                    }
                }
                else
                {
                    this.FTextureOutput[0][context] = new DX11DynamicTexture2D(context, COLOR_WIDTH, COLOR_HEIGHT, fmt);
#if DEBUG
                    this.FTextureOutput[0][context].Resource.DebugName = "DynamicTexture";
#endif
                }

                desc = this.FTextureOutput[0][context].Resource.Description;

                var t = this.FTextureOutput[0][context];

                byte[] buffer = this.GetColorImage(image);
                if (buffer != null)
                {
                    t.WriteData(buffer);
                    this.FInvalidate = false;
                }
            }
        }


        public void Destroy(IPluginIO pin, DX11RenderContext context, bool force)
        {
            this.FTextureOutput[0].Dispose(context);
        }

        public void Dispose()
        {
            if (this.FTextureOutput.SliceCount > 0)
            {
                if (this.FTextureOutput[0] != null)
                {
                    this.FTextureOutput[0].Dispose();
                }
            }
        }
    }
}
