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

        [JsonPropertyName("color")]
        public string Color { get; set; }

        [JsonPropertyName("label")]
        public string Label { get; set; }

        [JsonPropertyName("category")]
        public string Category { get; set; }

        [JsonPropertyName("size")]
        public int Size { get; set; }

        public ButtonConfig()
        {
            Id = string.Empty;
            Key = string.Empty;
            Icon = string.Empty;
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
