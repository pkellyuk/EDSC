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
    /// Represents 6DOF head pose data
    /// </summary>
    public class HeadPose
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double Yaw { get; set; }
        public double Pitch { get; set; }
        public double Roll { get; set; }

        public override string ToString()
        {
            return $"X={X:F2}, Y={Y:F2}, Z={Z:F2}, Yaw={Yaw:F2}, Pitch={Pitch:F2}, Roll={Roll:F2}";
        }
    }
}
