namespace EDSC.Models
{
    /// <summary>
    /// What the desktop face-tracking preview panel shows. The panel now only ever draws the face mesh,
    /// so the two camera values exist purely so older config files still parse; they behave as LandmarksOnly.
    /// </summary>
    public enum PreviewMode
    {
        /// <summary>No preview. Saves CPU on the PC and, in phone-tracking mode, stops the phone sending mesh frames.</summary>
        Off,

        /// <summary>Legacy: camera image only. Treated as <see cref="LandmarksOnly"/>.</summary>
        Camera,

        /// <summary>Legacy: camera image with the mesh drawn over it. Treated as <see cref="LandmarksOnly"/>.</summary>
        CameraWithLandmarks,

        /// <summary>The face mesh on a black background. The only live preview mode.</summary>
        LandmarksOnly
    }
}
