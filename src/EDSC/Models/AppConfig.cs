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

        public AppConfig()
        {
            Server = new ServerConfig();
            Buttons = new List<ButtonConfig>();
        }

        public override string ToString()
        {
            return $"AppConfig [Server={Server}, Buttons={Buttons?.Count ?? 0}]";
        }
    }
}
