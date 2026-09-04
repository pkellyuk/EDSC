using System.Text.Json.Serialization;

namespace EDSC.Models
{
    /// <summary>
    /// Configuration for face tracking sensitivity.
    /// </summary>
    public class TrackingConfig
    {
        [JsonPropertyName("translationScale")]
        public double TranslationScale { get; set; } = 1.0;

        [JsonPropertyName("yawScale")]
        public double YawScale { get; set; } = 1.0;

        [JsonPropertyName("pitchScale")]
        public double PitchScale { get; set; } = 1.0;

        [JsonPropertyName("rollScale")]
        public double RollScale { get; set; } = 1.0;

        [JsonPropertyName("smoothingStrength")]
        public double SmoothingStrength { get; set; } = 0.5;

        /// <summary>
        /// True to write pose straight into the game's TrackIR/FreeTrack interface instead of sending to Opentrack.
        /// </summary>
        [JsonPropertyName("directOutput")]
        public bool DirectOutput { get; set; } = false;

        /// <summary>
        /// Show the camera preview in the desktop app. Off saves CPU on the PC and, in phone-tracking mode, on the phone.
        /// </summary>
        [JsonPropertyName("showPreview")]
        public bool ShowPreview { get; set; } = true;
    }
}
