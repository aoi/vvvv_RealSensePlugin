#region usings;
using System.ComponentModel.Composition;

using VVVV.PluginInterfaces.V1;
using VVVV.PluginInterfaces.V2;

using VVVV.Core.Logging;
#endregion

namespace RealSense.Nodes
{
    [PluginInfo(Name = "RealSense", Category = "Devices", Version = "Intel", Help = "RealSense Device.", Tags = "", Author = "aoi")]
    public class RealSenseNode : IPluginEvaluate
    {
        private const int COLOR_WIDTH = 640;
        private const int COLOR_HEIGHT = 480;
        private const int COLOR_FPS = 30;

        [Import()]
        public ILogger FLogger;

        private PXCMSession session;

        [Input("Enabled", IsSingle = true, DefaultValue = 0)]
        IDiffSpread<bool> FInEnabled;

        [Output("Manager", IsSingle = true)]
        ISpread<PXCMSenseManager> FOutManager;

        private bool initialized = false;

        public void Evaluate(int SpreadMax)
        {
            if (!FInEnabled[0])
            {
                this.initialized = false;
                if (FOutManager[0] != null)
                {
                    FOutManager[0].Dispose();
                }
                if (session != null)
                {
                    session.Dispose();
                }
                FOutManager[0] = null;
                session = null;
                return;
            }

            if (!this.initialized)
            {
                this.Initialize();
            }

            if (FOutManager[0] == null)
            {
                FLogger.Log(LogType.Debug, "manager is null.");
            }
        }

        public void Initialize()
        {
            PXCMSession session = PXCMSession.CreateInstance();
            FOutManager[0] = session.CreateSenseManager();

            this.initialized = true;

            FLogger.Log(LogType.Debug, "Get PXCMSenseManager.");
        }
    }
}
