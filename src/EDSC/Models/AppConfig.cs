using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace EDSC.Models
{
    /// <summary>
    /// Complete application configuration
    /// </summary>
    public class AppConfig
    {
        [JsonPropertyName("server")]
        public ServerConfig Server { get; set; }

        [JsonPropertyName("buttons")]
        public List<ButtonConfig> Buttons { get; set; }

        [JsonPropertyName("configVersion")]
        public long ConfigVersion { get; set; }

        [JsonPropertyName("lastUpdatedUtc")]
        public long LastUpdatedUtc { get; set; }

        [JsonPropertyName("lastUpdatedBy")]
        public string LastUpdatedBy { get; set; }

        public AppConfig()
        {
            Server = new ServerConfig();
            Buttons = new List<ButtonConfig>();
            ConfigVersion = 1;
            LastUpdatedUtc = 0;
            LastUpdatedBy = string.Empty;
        }

        public override string ToString()
        {
            return $"AppConfig [Server={Server}, Buttons={Buttons?.Count ?? 0}, Version={ConfigVersion}]";
        }
    }
}
