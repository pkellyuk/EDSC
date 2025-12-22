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
        public double SmoothingStrength { get; set; } = 0.35;
    }
}
