using System;
using System.Text.Json.Serialization;

namespace EDSC.Models.Discovery
{
    /// <summary>
    /// Discovery request sent by mobile clients to find PC servers on the network
    /// </summary>
    public class DiscoveryRequest
    {
        [JsonPropertyName("type")]
        public string Type => "discover";

        [JsonPropertyName("requestId")]
        public string RequestId { get; set; }

        [JsonPropertyName("timestamp")]
        public long Timestamp { get; set; }

        public DiscoveryRequest()
        {
            RequestId = Guid.NewGuid().ToString();
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }
    }

    /// <summary>
    /// Discovery response sent by PC server when it receives a discovery request
    /// </summary>
    public class DiscoveryResponse
    {
        [JsonPropertyName("type")]
        public string Type => "response";

        [JsonPropertyName("requestId")]
        public string RequestId { get; set; }

        [JsonPropertyName("serverName")]
        public string ServerName { get; set; }

        [JsonPropertyName("ipAddress")]
        public string IpAddress { get; set; }

        [JsonPropertyName("httpPort")]
        public int HttpPort { get; set; }

        [JsonPropertyName("version")]
        public string Version { get; set; }

        public DiscoveryResponse()
        {
            RequestId = string.Empty;
            ServerName = string.Empty;
            IpAddress = string.Empty;
            HttpPort = 5000;
            Version = "1.0.0";
        }
    }
}
