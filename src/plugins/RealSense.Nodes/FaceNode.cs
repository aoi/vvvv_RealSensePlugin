#region usings
using System;
using System.ComponentModel.Composition;

using VVVV.PluginInterfaces.V1;
using VVVV.PluginInterfaces.V2;
using VVVV.Utils.VMath;

using VVVV.DX11;

using SlimDX.Direct3D11;

using FeralTic.DX11.Resources;
using FeralTic.DX11;

using VVVV.Core.Logging;
#endregion

namespace RealSense.Nodes
{
    public enum FaceExpressions
    {
        BROW_RAISER_LEFT,
        BROW_RAISER_RIGHT,
        BROW_LOWERER_LEFT,
        BROW_LOWERER_RIGHT,
        SMILE,
        KISS,
        MOUTH_OPEN,
        TONGUE_OUT,
        HEAD_TURN_LEFT,
        HEAD_TURN_RIGHT,
        HEAD_UP,
        HEAD_DOWN,
        HEAD_TILT_LEFT,
        HEAD_TILT_RIGHT,
        EYES_CLOSED_LEFT,
        EYES_CLOSED_RIGHT,
        EYES_TURN_LEFT,
        EYES_TURN_RIGHT,
        EYES_UP,
        EYES_DOWN,
    }

    [PluginInfo(Name = "Face", Category = "RealSense", Version = "Intel(R)", Help = "RealSense Face.", Tags = "RealSense, DX11, texture", Author = "aoi")]
    public class FaceNode : BaseNode
    {
        private const int MAX_FACES = 2;

        [Import()]
        public ILogger FLogger;

        [Input("Face Expressions", DefaultEnumEntry = "EXPRESSION_BROW_RAISER_LEFT")]
        protected ISpread<PXCMFaceData.ExpressionsData.FaceExpression> FInExpressions;

        [Output("Face Position")]
        protected ISpread<Vector2D> FOutFacePosition;

        [Output("Face Width")]
        protected ISpread<int> FOutFaceWidth;
        [Output("Face Height")]
        protected ISpread<int> FOutFaceHeight;

        [Output("Face Pose")]
        protected ISpread<Vector3D> FOutFacePose;

        [Output("Face Landmark Bin Size")]
        protected ISpread<int> FOutFaceLandmarkBinSize;
        [Output("Face Landmark Points")]
        protected ISpread<Vector2D> FOutFaceLandmarkPoints;
        [Output("Face Expressions Result")]
        protected ISpread<int> FOutFaceExpressionsResult;

        [Output("Pulse")]
        protected ISpread<Single> FOutPulse;

        private PXCMFaceConfiguration config;
        private PXCMFaceModule faceModule;
        private PXCMFaceData faceData;


        protected override bool Initialize()
        {
            this.GetSessionAndSenseManager();

            this.EnableColorStream();

            this.InitializeFace();

            this.initialized = true;

            return true;
        }

        private void InitializeFace()
        {
            pxcmStatus sts = this.senseManager.EnableFace();
            if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                throw new Exception("Could not enable face.");
            }

            this.faceModule = this.senseManager.QueryFace();
            if (this.faceModule == null)
            {
                throw new Exception("Could not get face module.");
            }

            // get properties for face module
            this.config = this.faceModule.CreateActiveConfiguration();
            this.config.SetTrackingMode(PXCMFaceConfiguration.TrackingModeType.FACE_MODE_COLOR);
            this.config.ApplyChanges();
            this.config.Update();

            // init pipeline
            sts = this.senseManager.Init();
            if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR &&
                sts != pxcmStatus.PXCM_STATUS_CAPTURE_CONFIG_ALREADY_SET)
            {
                throw new Exception("Initialization failed. " + sts.ToString());
            }

            this.device = this.senseManager.QueryCaptureManager().QueryDevice();
            if (this.device == null)
            {
                throw new Exception("Could not get device.");
            }

            // set mirror mode
            sts = this.device.SetMirrorMode(PXCMCapture.Device.MirrorMode.MIRROR_MODE_HORIZONTAL);
            if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                throw new Exception("Could not set mirror mode.");
            }

            PXCMCapture.DeviceInfo deviceInfo;
            this.device.QueryDeviceInfo(out deviceInfo);
            if (deviceInfo.model == PXCMCapture.DeviceModel.DEVICE_MODEL_IVCAM)
            {
                this.device.SetDepthConfidenceThreshold(1);
                this.device.SetIVCAMFilterOption(6);
            }

            // detection
            this.config.detection.isEnabled = true;
            this.config.detection.maxTrackedFaces = MAX_FACES;
            // pose
            this.config.pose.isEnabled = true;
            this.config.pose.maxTrackedFaces = MAX_FACES;
            // landmarks
            this.config.landmarks.isEnabled = true;
            this.config.landmarks.maxTrackedFaces = MAX_FACES;
            // expressions
            PXCMFaceConfiguration.ExpressionsConfiguration expressionConfig =  this.config.QueryExpressions();
            if (expressionConfig == null)
            {
                throw new Exception("Could not configure expressions module.");
            }
            expressionConfig.Enable();
            expressionConfig.EnableAllExpressions();
            expressionConfig.properties.maxTrackedFaces = MAX_FACES;
            // pulse
            PXCMFaceConfiguration.PulseConfiguration pulseConfig = this.config.QueryPulse();
            if (pulseConfig == null)
            {
                throw new Exception("Could not configure pulse module.");
            }
            pulseConfig.Enable();
            pulseConfig.properties.maxTrackedFaces = MAX_FACES;
            this.config.ApplyChanges();
            this.config.Update();

            this.faceData = faceModule.CreateOutput();
        }


        protected override void UpdateFrame()
        {
            pxcmStatus sts = this.senseManager.AcquireFrame(true);
            if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                FLogger.Log(LogType.Debug, "Could not acquire frame. " + sts.ToString());
                return;
            }

            this.updateFaceFrame();

            this.senseManager.ReleaseFrame();
        }

        private void updateFaceFrame()
        {
            PXCMCapture.Sample sample = this.senseManager.QuerySample();
            this.image = sample.color;

            if (this.image != null)
            {
                this.invalidate = true;
            }

            this.faceData.Update();

            // get number of faces
            FOutFaceLandmarkPoints.SliceCount = 0;
            FOutFaceExpressionsResult.SliceCount = 0;
            int numFaces = this.faceData.QueryNumberOfDetectedFaces();
            for (int i=0; i<numFaces; ++i)
            {
                // get faces info
                PXCMFaceData.Face face = this.faceData.QueryFaceByIndex(i);
                
                // get face position by Depth
                var detection = face.QueryDetection();
                if (detection != null)
                {
                    // detection
                    PXCMRectI32 faceRect;
                    detection.QueryBoundingRect(out faceRect);
                    int sliceCount = i + 1;
                    FOutFacePosition.SliceCount = sliceCount;
                    FOutFacePosition[i] = new Vector2D(faceRect.x, faceRect.y);
                    FOutFaceWidth.SliceCount = sliceCount;
                    FOutFaceWidth[i] = faceRect.w;
                    FOutFaceHeight.SliceCount = sliceCount;
                    FOutFaceHeight[i] = faceRect.h;

                    // pose(only use Depth mode)
                    PXCMFaceData.PoseData pose = face.QueryPose();
                    if (pose != null)
                    {
                        // faces angle
                        PXCMFaceData.PoseEulerAngles poseAngle = new PXCMFaceData.PoseEulerAngles();
                        pose.QueryPoseAngles(out poseAngle);
                        FOutFacePose.SliceCount = sliceCount;
                        FOutFacePose[i] = new Vector3D(poseAngle.pitch, poseAngle.yaw, poseAngle.roll);
                    }

                    // landmarks
                    PXCMFaceData.LandmarksData landmarks = face.QueryLandmarks();
                    FOutFaceLandmarkBinSize.SliceCount = sliceCount;
                    if (landmarks != null)
                    {
                        // number of feature points from landmarks
                        int numPoints = landmarks.QueryNumPoints();
                        FOutFaceLandmarkBinSize[i] = numPoints;

                        PXCMFaceData.LandmarkPoint[] landmarkPoints = new PXCMFaceData.LandmarkPoint[numPoints];
                        int prevSliceCount = FOutFaceLandmarkPoints.SliceCount;
                        FOutFaceLandmarkPoints.SliceCount = prevSliceCount + numPoints;

                        if (landmarks.QueryPoints(out landmarkPoints))
                        {
                            for (int j = 0; j < numPoints; j++)
                            {
                                int index = prevSliceCount + j;
                                FOutFaceLandmarkPoints[index] = new Vector2D(landmarkPoints[j].image.x, landmarkPoints[j].image.y);
                            }
                        }
                    }
                    else
                    {
                        FOutFaceLandmarkBinSize[i] = 0;
                        FOutFaceLandmarkPoints.SliceCount = 0;
                    }

                    PXCMFaceData.ExpressionsData expressionData = face.QueryExpressions();
                    if (expressionData != null)
                    {
                        for (int j = 0; j<FInExpressions.SliceCount; j++)
                        {
                            PXCMFaceData.ExpressionsData.FaceExpressionResult expressionResult;
                            if (expressionData.QueryExpression(FInExpressions[j], out expressionResult))
                            {
                                FOutFaceExpressionsResult.SliceCount++;
                                FOutFaceExpressionsResult[j] = expressionResult.intensity;
                            }
                            else
                            {
                                // do nothing
                            }
                        }

                    }
                    else
                    {
                        FOutFaceExpressionsResult.SliceCount = 0;
                    }

                    PXCMFaceData.PulseData pulseData = face.QueryPulse();
                    if (pulseData != null)
                    {
                        FOutPulse.SliceCount = sliceCount;
                        FOutPulse[i] = pulseData.QueryHeartRate();
                    }
                }
            }
        }

        protected override byte[] GetImageBuffer()
        {
            if (this.image == null)
            {
                return null;
            }

            PXCMImage.ImageData data;
            pxcmStatus ret = this.image.AcquireAccess(
                PXCMImage.Access.ACCESS_READ,
                PXCMImage.PixelFormat.PIXEL_FORMAT_RGB32, out data
            );

            if (ret < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                throw new Exception("Could not acquire Color image.");
            }

            var info = this.image.QueryInfo();
            var length = data.pitches[0] * info.height;

            var buffer = data.ToByteArray(0, length);

            this.image.ReleaseAccess(data);

            return buffer;
        }

        protected override void Uninitialize()
        {

            if (this.faceData != null)
            {
                this.faceData.Dispose();
                this.faceData = null;
            }
            if (this.config != null)
            {
                this.config.Dispose();
                this.config = null;
            }
            if (this.faceModule != null)
            {
                this.faceModule.Dispose();
                this.faceModule = null;
            }

            base.Uninitialize();
        }
    }
}
