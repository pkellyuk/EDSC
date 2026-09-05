using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace EDSC.Models
{
    /// <summary>
    /// Configuration for a single button
    /// </summary>
    public class ButtonConfig
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("key")]
        public string Key { get; set; }

        [JsonPropertyName("icon")]
        public string Icon { get; set; }

        [JsonPropertyName("iconSvg")]
        public string IconSvg { get; set; }

        [JsonPropertyName("color")]
        public string Color { get; set; }

        [JsonPropertyName("label")]
        public string Label { get; set; }

        [JsonPropertyName("category")]
        public string Category { get; set; }

        [JsonPropertyName("size")]
        public int Size { get; set; }

        /// <summary>
        /// Extra spoken phrases that trigger this button, in addition to its label ("frame shift", "jump").
        /// </summary>
        [JsonPropertyName("voiceAliases")]
        public List<string> VoiceAliases { get; set; }

        /// <summary>
        /// How long to hold the key down, in milliseconds. 0 means a normal tap. Games such as
        /// Star Citizen give some keys a different action on a long press.
        /// </summary>
        [JsonPropertyName("holdMs")]
        public int HoldMs { get; set; }

        public ButtonConfig()
        {
            VoiceAliases = new List<string>();
            HoldMs = 0;
            Id = string.Empty;
            Key = string.Empty;
            Icon = string.Empty;
            IconSvg = string.Empty;
            Color = "#4CAF50"; // Default green
            Label = string.Empty;
            Category = "General";
            Size = 80; // Default size
        }

        public override string ToString()
        {
            return $"Button [{Id}] Key={Key} Label={Label}";
        }
    }
}
