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
        /// Legacy single gaze nudge from 2.0.5 config files. Read as the fallback for both axes; no longer written.
        /// </summary>
        [JsonPropertyName("gazeNudge")]
        public double? GazeNudge { get; set; }

        /// <summary>
        /// How much of the sideways eye gaze angle is added to head yaw, 0 (off) to 2 (twice the eye angle).
        /// Only phone-side tracking measures gaze. Applied after a small dead zone so a resting glance does nothing.
        /// </summary>
        [JsonPropertyName("gazeNudgeYaw")]
        public double? GazeNudgeYaw { get; set; }

        /// <summary>
        /// How much of the up/down eye gaze angle is added to head pitch, 0 (off) to 2. Eyes move less
        /// vertically than sideways, so this wants a higher setting than yaw for the same feel.
        /// </summary>
        [JsonPropertyName("gazeNudgePitch")]
        public double? GazeNudgePitch { get; set; }

        public const double DefaultGazeNudgeYaw = 0.2;
        public const double DefaultGazeNudgePitch = 0.6;

        [JsonIgnore]
        public double EffectiveGazeNudgeYaw
        {
            get
            {
                return GazeNudgeYaw ?? GazeNudge ?? DefaultGazeNudgeYaw;
            }
        }

        [JsonIgnore]
        public double EffectiveGazeNudgePitch
        {
            get
            {
                if (GazeNudgePitch.HasValue)
                {
                    return GazeNudgePitch.Value;
                }

                // An old single value was tuned by feel for yaw; vertical needs roughly three times as much
                return GazeNudge.HasValue ? GazeNudge.Value * 3.0 : DefaultGazeNudgePitch;
            }
        }

        /// <summary>
        /// True to write pose straight into the game's TrackIR/FreeTrack interface instead of sending to Opentrack.
        /// </summary>
        [JsonPropertyName("directOutput")]
        public bool DirectOutput { get; set; } = false;

        /// <summary>
        /// Draw the face mesh preview in the desktop app. Off saves CPU on the PC and, in phone-tracking mode, on the phone.
        /// Kept for older config files; <see cref="PreviewMode"/> takes precedence when present.
        /// </summary>
        [JsonPropertyName("showPreview")]
        public bool ShowPreview { get; set; } = true;

        /// <summary>
        /// What the desktop preview panel shows. Null in older config files, in which case <see cref="ShowPreview"/> decides.
        /// Only Off and LandmarksOnly are written now; the legacy camera modes are read as LandmarksOnly.
        /// </summary>
        [JsonPropertyName("previewMode")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public PreviewMode? PreviewMode { get; set; }

        /// <summary>
        /// Whether the preview panel is on, resolving the legacy <see cref="ShowPreview"/> flag when
        /// <see cref="PreviewMode"/> is absent. Every mode other than Off means "show the mesh".
        /// </summary>
        [JsonIgnore]
        public bool EffectiveShowPreview
        {
            get
            {
                if (PreviewMode.HasValue)
                {
                    return PreviewMode.Value != Models.PreviewMode.Off;
                }

                return ShowPreview;
            }
        }

        /// <summary>
        /// Windows virtual key name for the re-centre hotkey (e.g. OEM_PLUS for "=", F12, NUMPAD0).
        /// </summary>
        [JsonPropertyName("centerHotkey")]
        public string CenterHotkey { get; set; } = "OEM_PLUS";
    }
}
