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
        public int Port { get; set; } = 9000;

        /// <summary>
        /// Whether to start the server automatically when the app starts
        /// </summary>
        [JsonPropertyName("autoStart")]
        public bool AutoStart { get; set; } = true;

        /// <summary>
        /// The local IP address chosen in the desktop app, so the same one is used after a restart.
        /// Null or an address the PC no longer has falls back to the first address found.
        /// </summary>
        [JsonPropertyName("bindAddress")]
        public string? BindAddress { get; set; }

        public ServerConfig()
        {
            // Explicit parameterless constructor with default values already set above
        }

        public override string ToString()
        {
            return $"ServerConfig [Port={Port}, AutoStart={AutoStart}, BindAddress={BindAddress ?? "(first)"}]";
        }
    }
}
