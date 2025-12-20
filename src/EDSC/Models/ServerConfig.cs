using System.Text.Json.Serialization;

namespace EDSC.Models
{
    /// <summary>
    /// Configuration for the EDSC server (PC)
    /// </summary>
    public class ServerConfig
    {
        /// <summary>
        /// HTTP port for command server
        /// </summary>
        [JsonPropertyName("port")]
        public int Port { get; set; } = 5000;

        /// <summary>
        /// UDP port for discovery service
        /// </summary>
        [JsonPropertyName("discoveryPort")]
        public int DiscoveryPort { get; set; } = 5001;

        /// <summary>
        /// Whether to start the server automatically when the app starts
        /// </summary>
        [JsonPropertyName("autoStart")]
        public bool AutoStart { get; set; } = true;

        /// <summary>
        /// Whether to enable network discovery
        /// </summary>
        [JsonPropertyName("enableDiscovery")]
        public bool EnableDiscovery { get; set; } = true;

        public ServerConfig()
        {
            // Explicit parameterless constructor with default values already set above
        }

        public override string ToString()
        {
            return $"ServerConfig [Port={Port}, DiscoveryPort={DiscoveryPort}, AutoStart={AutoStart}, EnableDiscovery={EnableDiscovery}]";
        }
    }
}
