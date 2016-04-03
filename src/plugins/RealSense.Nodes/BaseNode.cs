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
using System.Threading.Tasks;
using System.Net.Http;
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

        [Output("Texture Out", IsSingle = true, Order = 1)]
        protected Pin<DX11Resource<DX11DynamicTexture2D>> FTextureOutput;

        [Output("Status", IsSingle = true, DefaultString = "", Order = 99)]
        protected ISpread<string> FOutStatus;

        protected bool initialized = false;
        protected bool initializing = false;

        protected PXCMSession session;
        protected PXCMSenseManager senseManager;
        protected PXCMCapture.Device device;
        protected PXCMImage image;

        protected bool invalidate = true;
        protected object lockObj = new object();
        protected bool isResized = false;

        [Import()]
        protected ILogger FLogger;

        public BaseNode()
        {
            string[] resolution = new string[9] { "320x240", "480x360", "628x468", "640x240", "640x360", "640x480", "960x540", "1280x720", "1920x1080" };
            EnumManager.UpdateEnum("Resolution", resolution[5], resolution);
        }

        public void Evaluate(int SpreadMax)
        {
            if ((this.initialized || this.initializing) && !FInEnabled[0])
            {
                this.Uninitialize();

                return;
            }

            if (!this.FInEnabled[0]) { return; }

            if (this.image == null)
            {
                if (this.FTextureOutput[0] != null)
                {
                    this.FTextureOutput[0].Dispose();
                }
            }
            else
            {
                if (this.FTextureOutput[0] == null)
                {
                    this.FTextureOutput[0] = new DX11Resource<DX11DynamicTexture2D>();
                }
            }

            if (!this.initialized)
            {
                try
                {
                    string[] r = FInResolution[0].Name.ToString().Split('x');
                    this.width = int.Parse(r[0]);
                    this.height = int.Parse(r[1]);

                    //this.Initialize();
                    if (!this.initializing)
                    {
                        this.InitializeAsync();
                    }
                }
                catch (Exception e)
                {
                    FLogger.Log(LogType.Error, e.Message + e.StackTrace);
                    this.initializing = false;
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

        protected async void InitializeAsync()
        {
            this.initializing = true;
            FOutStatus.SliceCount = 1;
            FOutStatus[0] = "Initializing";

            try
            {
                await Task.Run(() => this.Initialize());
            }
            catch (Exception e)
            {
                FLogger.Log(LogType.Error, e.Message, e);
                this.initializing = false;
                this.Uninitialize();
            }
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
            pxcmStatus sts = this.senseManager.EnableStream(PXCMCapture.StreamType.STREAM_TYPE_COLOR, this.width, this.height, FPS);
            if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                throw new Exception("カラーストリームの有効化に失敗しました");
            }
        }

        protected void EnableDepthStream()
        {
            pxcmStatus sts = this.senseManager.EnableStream(PXCMCapture.StreamType.STREAM_TYPE_DEPTH, this.width, this.height, FPS);
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
                throw new Exception("Initialization failed: " + sts.ToString());
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
            if (!this.FInEnabled[0] || !this.initialized || this.image == null) { return; }

            if (this.FTextureOutput[0] == null) { return; }

            SlimDX.DXGI.Format fmt = SlimDX.DXGI.Format.B8G8R8A8_UNorm;

            if (!this.FTextureOutput[0].Contains(context))
            {
                this.FTextureOutput[0][context] = new DX11DynamicTexture2D(context, this.width, this.height, fmt);
            }
            
            if (this.invalidate)
            {
                lock (this.lockObj)
                {
                    
                    var buffer = this.GetImageBuffer();

                    Texture2DDescription desc = this.FTextureOutput[0][context].Resource.Description;

                    if (this.isResized || desc.Width != this.width || desc.Height != this.height || desc.Format != fmt) // resized
                    {
                        this.FTextureOutput[0].Dispose(context);
                        this.FTextureOutput[0][context] = new DX11DynamicTexture2D(context, this.width, this.height, fmt);

                    }

                    if (buffer != null)
                    {
                        var t = this.FTextureOutput[0][context];
                        t.WriteData(buffer);
                        this.invalidate = false;
                    }
                }
            }
        }

        protected virtual void Uninitialize()
        {
            if (this.initializing) { return; }

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

            this.initializing = false;
            this.initialized = false;
            FOutStatus[0] = "";
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
            if (this.FTextureOutput[0] != null)
            {
                this.FTextureOutput[0].Dispose(context);
            }

            if (this.FTextureOutput != null)
            {
                this.FTextureOutput.Dispose();
            }

        }
    }
}
