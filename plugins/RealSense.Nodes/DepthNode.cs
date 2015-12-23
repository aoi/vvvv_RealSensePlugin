#region usings
using System;

using VVVV.PluginInterfaces.V1;
using VVVV.PluginInterfaces.V2;

using VVVV.DX11;

using SlimDX.Direct3D11;

using FeralTic.DX11.Resources;
using FeralTic.DX11;

#endregion

namespace RealSense.Nodes
{
    [PluginInfo(Name = "Depth", Category = "RealSense", Version = "Intel", Help = "RealSense Depth Image.", Tags = "RealSense, DX11, texture", Author = "aoi")]
    public class DepthNode : IPluginEvaluate, IDX11ResourceProvider, IDisposable
    {
        private const int WIDTH = 640;
        private const int HEIGHT = 480;
        private const int FPS = 30;

        [Input("Apply", IsBang = true, DefaultValue = 1)]
        protected ISpread<bool> FApply;

        [Input("Manager", IsSingle = true)]
        protected ISpread<PXCMSenseManager> FInManager;

        [Output("Texture Out")]
        protected Pin<DX11Resource<DX11DynamicTexture2D>> FTextureOutput;

        private bool initialized = false;

        private PXCMImage image;
        private bool FInvalidate;

        public void Evaluate(int SpreadMax)
        {
            if (this.FApply[0])
            {
                this.FInvalidate = true;
            }

            if (this.FTextureOutput[0] == null) {
               this.FTextureOutput[0] = new DX11Resource<DX11DynamicTexture2D>();
               this.FTextureOutput.SliceCount = 1;
            }


            if (!initialized)
            {
                this.Initialize();
            }
            else
            {
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

            // Depthストリームを有効にする
            pxcmStatus sts = senseManager.EnableStream(PXCMCapture.StreamType.STREAM_TYPE_DEPTH,
                WIDTH, HEIGHT, FPS);
            if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                throw new Exception("Depthストリームの有効化に失敗しました");
            }

            // パイプラインを初期化する
            sts = senseManager.Init();
            if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                throw new Exception("初期化に失敗しました");
            }

            // ミラー表示にする
            senseManager.QueryCaptureManager().QueryDevice().SetMirrorMode(PXCMCapture.Device.MirrorMode.MIRROR_MODE_HORIZONTAL);

            this.initialized = true;

        }

        public void UpdateFrame()
        {
            PXCMSenseManager senseManager = FInManager[0];
            if (senseManager == null)
            {
                return;
            }

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
                image = sample.depth;
            }

            // フレームを開放する
            senseManager.ReleaseFrame();
        }

        public void Destroy(IPluginIO pin, DX11RenderContext context, bool force)
        {
            this.FTextureOutput[0].Dispose(context);

            /*PXCMSenseManager senseManager = FInManager[0];
            if (senseManager != null)
            {
                senseManager.Dispose();
                senseManager = null;
            }*/
        }

        public void Update(IPluginIO pin, DX11RenderContext context)
        {
            if (this.FTextureOutput.SliceCount == 0) { return; }

            if (this.FInvalidate || !this.FTextureOutput[0].Contains(context))
            {

                SlimDX.DXGI.Format fmt = SlimDX.DXGI.Format.B8G8R8A8_UNorm;

                Texture2DDescription desc;

                if (this.FTextureOutput[0].Contains(context))
                {
                    desc = this.FTextureOutput[0][context].Resource.Description;

                    if (desc.Width != WIDTH || desc.Height != HEIGHT || desc.Format != fmt)
                    {
                        this.FTextureOutput[0].Dispose(context);
                        DX11DynamicTexture2D t2D = new DX11DynamicTexture2D(context, WIDTH, HEIGHT, fmt);

                        this.FTextureOutput[0][context] = t2D;
                    }
                }
                else
                {
                    this.FTextureOutput[0][context] = new DX11DynamicTexture2D(context, WIDTH, HEIGHT, fmt);
#if DEBUG
                    this.FTextureOutput[0][context].Resource.DebugName = "DynamicTexture";
#endif
                }

                desc = this.FTextureOutput[0][context].Resource.Description;

                var t = this.FTextureOutput[0][context];

                byte[] buffer = this.GetDepthImage();
                if (buffer != null)
                {
                    t.WriteData(buffer);
                    this.FInvalidate = false;
                }
            }
        }

        public byte[] GetDepthImage()
        {
            if (image == null)
            {
                return null;
            }

            // データを取得する
            PXCMImage.ImageData data;
            pxcmStatus ret = image.AcquireAccess(
                PXCMImage.Access.ACCESS_READ,
                PXCMImage.PixelFormat.PIXEL_FORMAT_RGB32, out data
            );

            if (ret < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                throw new Exception("Depth画像の取得に失敗");
            }

            // バイト配列に変換する
            var info = image.QueryInfo();
            var length = data.pitches[0] * info.height;

            var buffer = data.ToByteArray(0, length);

            image.ReleaseAccess(data);

            return buffer;
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
