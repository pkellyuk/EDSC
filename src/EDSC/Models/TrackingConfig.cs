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
        /// Kept for older config files; <see cref="PreviewMode"/> takes precedence when present.
        /// </summary>
        [JsonPropertyName("showPreview")]
        public bool ShowPreview { get; set; } = true;

        /// <summary>
        /// What the desktop preview panel shows. Null in older config files, in which case <see cref="ShowPreview"/> decides.
        /// </summary>
        [JsonPropertyName("previewMode")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public PreviewMode? PreviewMode { get; set; }

        /// <summary>
        /// The preview mode to use, resolving the legacy <see cref="ShowPreview"/> flag when <see cref="PreviewMode"/> is absent.
        /// </summary>
        [JsonIgnore]
        public PreviewMode EffectivePreviewMode
        {
            get
            {
                if (PreviewMode.HasValue)
                {
                    return PreviewMode.Value;
                }

                return ShowPreview ? Models.PreviewMode.CameraWithLandmarks : Models.PreviewMode.Off;
            }
        }

        /// <summary>
        /// Windows virtual key name for the re-centre hotkey (e.g. OEM_PLUS for "=", F12, NUMPAD0).
        /// </summary>
        [JsonPropertyName("centerHotkey")]
        public string CenterHotkey { get; set; } = "OEM_PLUS";
    }
}
