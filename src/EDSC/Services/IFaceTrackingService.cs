using System;
using System.Threading.Tasks;

namespace EDSC.Services
{
    /// <summary>
    /// Face tracking service for head pose estimation
    /// </summary>
    public interface IFaceTrackingService
    {
        /// <summary>
        /// Initialize the face tracking service with ONNX models
        /// </summary>
        Task InitializeAsync(string modelsPath);

        /// <summary>
        /// Process a video frame and return head pose
        /// </summary>
        /// <param name="frameData">JPEG image data</param>
        /// <returns>Head pose data (X, Y, Z, Yaw, Pitch, Roll) or null if no face detected</returns>
        Task<HeadPose?> ProcessFrameAsync(byte[] frameData);

        /// <summary>
        /// Gets whether the service is initialized
        /// </summary>
        bool IsInitialized { get; }
    }

    /// <summary>
    /// Represents 6DOF head pose data with detection info
    /// </summary>
    public class HeadPose
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double Yaw { get; set; }
        public double Pitch { get; set; }
        public double Roll { get; set; }

        // Detection visualization data
        public FaceBox? FaceBox { get; set; }
        public LandmarkPoint[]? Landmarks { get; set; }

        /// <summary>True when the tracker measured eye gaze for this pose (phone-side tracking with the iris landmarks).</summary>
        public bool HasGaze { get; set; }

        /// <summary>Eye gaze relative to the head in degrees, positive when looking to your own left. Valid when <see cref="HasGaze"/>.</summary>
        public double GazeYaw { get; set; }

        /// <summary>Eye gaze relative to the head in degrees, positive when looking up. Valid when <see cref="HasGaze"/>.</summary>
        public double GazePitch { get; set; }

        /// <summary>True while the tracker sees an eye closing, closed or just reopened. Head pitch is unreliable then.</summary>
        public bool Blinking { get; set; }

        public override string ToString()
        {
            return $"X={X:F2}, Y={Y:F2}, Z={Z:F2}, Yaw={Yaw:F2}, Pitch={Pitch:F2}, Roll={Roll:F2}";
        }
    }

    /// <summary>
    /// Represents a detected face bounding box
    /// </summary>
    public class FaceBox
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
    }

    /// <summary>
    /// Represents a facial landmark point
    /// </summary>
    public class LandmarkPoint
    {
        public float X { get; set; }
        public float Y { get; set; }
    }
}
