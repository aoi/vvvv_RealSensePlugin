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
    public abstract class BaseNode : IPluginEvaluate, IDX11ResourceProvider, IDisposable
    {

        protected const int WIDTH = 640;
        protected const int HEIGHT = 480;
        protected const int FPS = 30;
        protected const int BYTE_PER_PIXEL = 4;

        [Input("Apply", IsSingle = true, DefaultValue = 1)]
        protected ISpread<bool> FApply;

        [Input("Enabled", IsSingle = true, DefaultValue = 0)]
        protected ISpread<bool> FInEnabled;

        [Output("Texture Out")]
        protected Pin<DX11Resource<DX11DynamicTexture2D>> FTextureOutput;

        protected bool initialized = false;
        protected bool FInvalidate = false;

        protected PXCMSession session;
        protected PXCMSenseManager senseManager;
        protected PXCMCapture.Device device;

        protected PXCMImage image;

        [Import()]
        protected ILogger FLogger;

        public void Evaluate(int SpreadMax)
        {
            if (this.initialized && !FInEnabled[0])
            {
                this.Uninitialize();
            }

            if (!FInEnabled[0]) { return; }

            if (this.FApply[0]) { this.FInvalidate = true; }

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
                try
                {
                    this.Initialize();
                }
                catch (Exception e)
                {
                    FLogger.Log(LogType.Error, e.Message + e.StackTrace);

                    this.Uninitialize();
                }
            }
            else
            {
                try
                {
                    this.UpdateFrame();
                }
                catch (Exception e)
                {
                    FLogger.Log(LogType.Error, "UpdateFrame: " + e.Message + e.StackTrace);
                    this.Uninitialize();
                }
            }
        }

        protected abstract void Initialize();

        protected virtual void Uninitialize()
        {
            if (this.image != null)
            {
                this.image.Dispose();
            }

            if (this.device != null)
            {
                this.device.Dispose();
                this.device = null;
            }

            if (this.senseManager != null)
            {
                this.senseManager.Close();
                this.senseManager.Dispose();
                this.senseManager = null;
            }

            if (this.session != null)
            {
                this.session.Dispose();
                this.session = null;
            }

            this.initialized = false;
        }

        protected abstract void UpdateFrame();

        protected abstract byte[] GetImageBuffer();

        protected void GetSessionAndSenseManager()
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
        }

        protected void GetDevice()
        {
            this.device = this.senseManager.QueryCaptureManager().QueryDevice();
            if (this.device == null)
            {
                throw new Exception("デバイスの取得に失敗しました");
            }
        }

        protected void EnableColorStream()
        {
            pxcmStatus sts = this.senseManager.EnableStream(PXCMCapture.StreamType.STREAM_TYPE_COLOR, WIDTH, HEIGHT, FPS);
            if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                throw new Exception("カラーストリームの有効化に失敗しました");
            }
        }

        protected void EnableDepthStream()
        {
            pxcmStatus sts = this.senseManager.EnableStream(PXCMCapture.StreamType.STREAM_TYPE_DEPTH, WIDTH, HEIGHT, FPS);
            if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                throw new Exception("Depthストリームの有効化に失敗しました");
            }
        }

        protected void SenseManagerInit()
        {
            pxcmStatus sts = this.senseManager.Init();
            if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                throw new Exception("初期化に失敗しました: " + sts.ToString());
            }
        }

        protected void SetMirrorMode()
        {
            pxcmStatus sts = this.device.SetMirrorMode(PXCMCapture.Device.MirrorMode.MIRROR_MODE_HORIZONTAL);
            if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                throw new Exception("ミラー表示の設定に失敗しました");
            }
        }

        public void Update(IPluginIO pin, DX11RenderContext context)
        {
            if (!this.initialized) { return; }

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

                var buffer = this.GetImageBuffer();
                if (buffer != null)
                {
                    t.WriteData(buffer);
                    this.FInvalidate = false;
                }
            }
        }

        public void Dispose()
        {
            FLogger.Log(LogType.Debug, "Dispose");
            if (this.FTextureOutput.SliceCount > 0)
            {
                if (this.FTextureOutput[0] != null)
                {
                    this.FTextureOutput[0].Dispose();
                }
            }

            this.Uninitialize();

        }

        public void Destroy(IPluginIO pin, DX11RenderContext context, bool force)
        {
            FLogger.Log(LogType.Debug, "Destory");
            this.FTextureOutput[0].Dispose(context);
        }
    }
}
