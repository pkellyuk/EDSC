namespace EDSC.Models
{
    /// <summary>
    /// What the desktop face-tracking preview panel shows.
    /// </summary>
    public enum PreviewMode
    {
        /// <summary>No preview. Saves CPU on the PC and, in phone-tracking mode, stops the phone sending preview images.</summary>
        Off,

        /// <summary>The camera image only, no overlay.</summary>
        Camera,

        /// <summary>The camera image with the face box and landmark outline drawn over it.</summary>
        CameraWithLandmarks,

        /// <summary>The face box and landmark outline on a plain background, without the camera image.</summary>
        LandmarksOnly
    }
}
