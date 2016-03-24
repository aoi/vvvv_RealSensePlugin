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

        protected int width = 640;
        protected int height = 480;
        protected const int FPS = 30;
        protected const int BYTE_PER_PIXEL = 4;

        [Input("Resolution", EnumName = "Resolution", IsSingle = true, DefaultEnumEntry = "640x480")]
        private ISpread<EnumEntry> FInResolution;

        [Input("Enabled", IsSingle = true, DefaultValue = 0)]
        protected ISpread<bool> FInEnabled;

        [Output("Texture Out")]
        protected Pin<DX11Resource<DX11DynamicTexture2D>> FTextureOutput;

        protected bool initialized = false;

        protected PXCMSession session;
        protected PXCMSenseManager senseManager;
        protected PXCMCapture.Device device;
        protected PXCMImage image;

        [Import()]
        protected ILogger FLogger;

        public BaseNode()
        {
            string[] resolution = new string[9] { "320x240", "480x360", "628x468", "640x240", "640x360", "640x480", "960x540", "1280x720", "1920x1080" };
            EnumManager.UpdateEnum("Resolution", resolution[5], resolution);
        }

        public void Evaluate(int SpreadMax)
        {

            if (this.initialized && !FInEnabled[0])
            {
                this.Uninitialize();
            }

            if (!FInEnabled[0]) { return; }

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

            if (!this.initialized)
            {
                try
                {
                    string[] r = FInResolution[0].Name.ToString().Split('x');
                    this.width = int.Parse(r[0]);
                    this.height = int.Parse(r[1]);

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
                this.image = null;
            }

            if (this.device != null)
            {
                this.device.Dispose();
                this.device = null;
            }

            if (this.session != null)
            {
                this.session.Dispose();
                this.session = null;
            }

            if (this.senseManager != null)
            {
                this.senseManager.Close();
                this.senseManager.Dispose();
                this.senseManager = null;
            }

            this.initialized = false;
        }

        protected abstract void UpdateFrame();

        protected abstract byte[] GetImageBuffer();

        protected void GetSessionAndSenseManager()
        {
            this.senseManager = PXCMSenseManager.CreateInstance();
            if (this.senseManager == null)
            {
                throw new Exception("マネージャを作成できませんでした");
            }

            this.session = this.senseManager.session;
        }

        protected void GetDevice()
        {
            this.device = this.senseManager.QueryCaptureManager().QueryDevice();
            PXCMCapture.DeviceInfo dinfo;
            this.device.QueryDeviceInfo(out dinfo);

            if (this.device == null)
            {
                throw new Exception("デバイスの取得に失敗しました");
            }
        }

        protected void EnableColorStream()
        {
            pxcmStatus sts = this.senseManager.EnableStream(PXCMCapture.StreamType.STREAM_TYPE_COLOR, width, height, FPS);
            if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                throw new Exception("カラーストリームの有効化に失敗しました");
            }
        }

        protected void EnableDepthStream()
        {
            pxcmStatus sts = this.senseManager.EnableStream(PXCMCapture.StreamType.STREAM_TYPE_DEPTH, width, height, FPS);
            if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                throw new Exception("Depthストリームの有効化に失敗しました");
            }
        }

        protected void InitSenseManager()
        {
            if (this.senseManager == null) { return; }

            if (!this.senseManager.IsConnected()) { return; }

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
            if (!this.FInEnabled[0] || !this.initialized) { return; }

            if (this.FTextureOutput.SliceCount == 0) { return; }

            SlimDX.DXGI.Format fmt = SlimDX.DXGI.Format.B8G8R8A8_UNorm;

            Texture2DDescription desc;

            if (this.FTextureOutput[0].Contains(context))
            {
                desc = this.FTextureOutput[0][context].Resource.Description;

                if (desc.Width != width || desc.Height != height || desc.Format != fmt)
                {
                    this.FTextureOutput[0].Dispose(context);
                    DX11DynamicTexture2D t2D = new DX11DynamicTexture2D(context, width, height, fmt);

                    this.FTextureOutput[0][context] = t2D;
                }
            }
            else
            {
                this.FTextureOutput[0][context] = new DX11DynamicTexture2D(context, width, height, fmt);
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
            }
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

            this.Uninitialize();

        }

        public void Destroy(IPluginIO pin, DX11RenderContext context, bool force)
        {
            this.FTextureOutput[0].Dispose(context);
        }
    }
}
