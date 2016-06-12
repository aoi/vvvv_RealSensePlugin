#region usings
using System;
using System.ComponentModel.Composition;

using VVVV.PluginInterfaces.V1;
using VVVV.PluginInterfaces.V2;
using VVVV.Utils.VMath;
using VVVV.Core.Logging;
#endregion

namespace RealSense.Nodes
{
    [PluginInfo(Name = "TouchlessController", Category = "RealSense", Version = "Intel(R)", Help = "RealSense Touchless Controller.", Tags = "RealSense", Author = "aoi", AutoEvaluate = true)]
    public class TouchlessControllerNode : IPluginEvaluate, IDisposable
    {

        [Input("Configuration", DefaultEnumEntry = "Configuration_None")]
        private ISpread<PXCMTouchlessController.ProfileInfo.Configuration> FInConfiguration;

        [Input("PointerSensitivity", IsSingle = true, DefaultEnumEntry = "PointerSensitivity_Smoothed")]
        private IDiffSpread<PXCMTouchlessController.PointerSensitivity> FInPointerSensitivity;
        
        [Input("ScrollSensitivity", IsSingle = true, DefaultValue = 1.0f, MaxValue = 4.0f, MinValue = 0.25f)]
        private IDiffSpread<float> FInScrollSensitivity;

        [Input("Enabled", IsSingle = true, DefaultValue = 0)]
        protected ISpread<bool> FInEnabled;

        [Output("UXEvent", IsSingle = true)]
        private ISpread<string> FOutUXEvent;

        [Output("StartScrollPosition")]
        private ISpread<Vector3D> FOutStartScrollPosition;
        [Output("ScrollPosition")]
        private ISpread<Vector3D> FOutScrollPosition;
        [Output("EndScrollPosition")]
        private ISpread<Vector3D> FOutEndScrollPosition;

        [Output("CursorVisiblePosition")]
        private ISpread<Vector3D> FOutCursorVisiblePosition;
        [Output("CursorMovePosition")]
        public ISpread<Vector3D> FOutCursorMovePosition;
        [Output("CursorNotVisiblePosition")]
        private ISpread<Vector3D> FOutCursorNotVisiblePosition;

        [Output("StartZoomPosition")]
        private ISpread<Vector3D> FOutStartZoomPosition;
        [Output("ZoomPosition")]
        private ISpread<Vector3D> FOutZoomPosition;
        [Output("EndZoomPosition")]
        private ISpread<Vector3D> FOutEndZoomPosition;

        protected int width = 640;
        protected int height = 480;
        protected const int FPS = 30;

        private bool initialized = false;
        protected PXCMSession session;
        protected PXCMSenseManager senseManager;
        private PXCMTouchlessController controller;

        [Import()]
        protected ILogger FLogger;

        public void Evaluate(int SpreadMax)
        {
            if (this.initialized && !FInEnabled[0])
            {
                this.Uninitialize();
            }

            if (!FInEnabled[0]) { return; }

            try
            {
                if (!this.initialized)
                {
                    this.InitTouchlessController();
                }
                else
                {
                    this.UpdateSensivity();
                }
            }
            catch(Exception e)
            {
                FLogger.Log(LogType.Error, e.Message);
                this.Uninitialize();
            }
        }

        private void InitTouchlessController()
        {
            this.senseManager = PXCMSenseManager.CreateInstance();
            if (this.senseManager == null)
            {
                throw new Exception("Could not create Sense Manager.");
            }

            this.session = this.senseManager.session;

            pxcmStatus sts = this.senseManager.EnableTouchlessController(null);
            if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                throw new Exception("Can't get Touchless Controller");
            }

            sts = this.senseManager.EnableStream(PXCMCapture.StreamType.STREAM_TYPE_COLOR, this.width, this.height, FPS);
            if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                throw new Exception("Could not enable Color Stream.");
            }

            sts = this.senseManager.EnableStream(PXCMCapture.StreamType.STREAM_TYPE_DEPTH, this.width, this.height, FPS);
            if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                throw new Exception("Could not enable Depth Stream.");
            }


            if (!this.senseManager.IsConnected()) { return; }


            var handler = new PXCMSenseManager.Handler();
            sts = this.senseManager.Init(handler);
            if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                throw new Exception("Initialization failed. " + sts.ToString());
            }

            this.controller = this.senseManager.QueryTouchlessController();
            if (this.controller == null)
            {
                throw new Exception("Can't get Touchless Controller");
            }

            this.controller.SubscribeEvent(new PXCMTouchlessController.OnFiredUXEventDelegate(OnTouchlessControllerUXEvent));

            this.controller.AddGestureActionMapping("swipeLeft", PXCMTouchlessController.Action.Action_LeftKeyPress, OnFiredAction);


            PXCMTouchlessController.ProfileInfo pinfo;
            //this.controller.ClearAllGestureActionMappings();
            sts = this.controller.QueryProfile(out pinfo);
            if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                throw new Exception("Can't get Touchless Controller profile");
            }

            IntPtr pointer = this.controller.QueryNativePointer();

            for (int i=0; i<FInConfiguration.SliceCount; i++)
            {
                pinfo.config |= FInConfiguration[i];
            }

            sts = this.controller.SetProfile(pinfo);
            if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                throw new Exception("Can't set Touchless Controller profile");
            }

            this.senseManager.StreamFrames(false);

            this.initialized = true;
        }

        private void OnFiredAction(PXCMTouchlessController.Action action)
        {
            FLogger.Log(LogType.Debug, "action: " + action.ToString());
        }

        private void OnTouchlessControllerUXEvent(PXCMTouchlessController.UXEventData data)
        {
            try
            {
                FOutUXEvent.SliceCount = 1;
                FOutUXEvent[0] = data.type.ToString();

                switch (data.type)
                {
                    case PXCMTouchlessController.UXEventData.UXEventType.UXEvent_StartScroll:
                        {
                            FOutStartScrollPosition.SliceCount = 1;
                            FOutStartScrollPosition[0] = new Vector3D(data.position.x, data.position.y, data.position.z);
                            break;
                        }
                    case PXCMTouchlessController.UXEventData.UXEventType.UXEvent_Scroll:
                        {
                            FOutScrollPosition.SliceCount = 1;
                            FOutScrollPosition[0] = new Vector3D(data.position.x, data.position.y, data.position.z);

                            break;
                        }
                    case PXCMTouchlessController.UXEventData.UXEventType.UXEvent_EndScroll:
                        {
                            FOutEndScrollPosition.SliceCount = 1;
                            FOutEndScrollPosition[0] = new Vector3D(data.position.x, data.position.y, data.position.z);

                            break;
                        }
                    case PXCMTouchlessController.UXEventData.UXEventType.UXEvent_CursorVisible:
                        {
                            FOutCursorVisiblePosition.SliceCount = 1;
                            FOutCursorVisiblePosition[0] = new Vector3D(data.position.x, data.position.y, data.position.z);

                            break;
                        }

                    case PXCMTouchlessController.UXEventData.UXEventType.UXEvent_CursorMove:
                        {
                            FOutCursorMovePosition.SliceCount = 1;
                            FOutCursorMovePosition[0] = new Vector3D(data.position.x, data.position.y, data.position.z);

                            break;
                        }
                    case PXCMTouchlessController.UXEventData.UXEventType.UXEvent_CursorNotVisible:
                        {
                            FOutCursorNotVisiblePosition.SliceCount = 1;
                            FOutCursorNotVisiblePosition[0] = new Vector3D(data.position.x, data.position.y, data.position.z);

                            break;
                        }
                    case PXCMTouchlessController.UXEventData.UXEventType.UXEvent_StartZoom:
                        {
                            FOutStartZoomPosition.SliceCount = 1;
                            FOutStartZoomPosition[0] = new Vector3D(data.position.x, data.position.y, data.position.z);
                            
                            break;
                        }
                    case PXCMTouchlessController.UXEventData.UXEventType.UXEvent_Zoom:
                        {
                            FOutZoomPosition.SliceCount = 1;
                            FOutZoomPosition[0] = new Vector3D(data.position.x, data.position.y, data.position.z);

                            break;
                        }
                    case PXCMTouchlessController.UXEventData.UXEventType.UXEvent_EndZoom:
                        {
                            FOutEndZoomPosition.SliceCount = 1;
                            FOutEndZoomPosition[0] = new Vector3D(data.position.x, data.position.y, data.position.z);

                            break;
                        }
                }
                
            }
            catch (Exception e)
            {
                FLogger.Log(LogType.Error, e.Message, e);
            }
        }

        private void UpdateSensivity()
        {

            if (FInPointerSensitivity.IsChanged)
            {
                this.controller.SetPointerSensitivity(FInPointerSensitivity[0]);
            }

            if (FInScrollSensitivity.IsChanged)
            {
                this.controller.SetScrollSensitivity(FInScrollSensitivity[0]);
            }
        }

        private void Uninitialize()
        {
            if (this.controller != null)
            {
                this.controller.UnsubscribeEvent(OnTouchlessControllerUXEvent);

                this.controller.Dispose();
                this.controller = null;
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

        public void Dispose()
        {
            this.Uninitialize();
        }
    }
}
