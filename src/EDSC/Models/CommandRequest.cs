using System;
using System.Text.Json.Serialization;

namespace EDSC.Models
{
    /// <summary>
    /// Command request sent from mobile to PC
    /// </summary>
    public class CommandRequest
    {
        [JsonPropertyName("buttonId")]
        public string ButtonId { get; set; }

        [JsonPropertyName("key")]
        public string Key { get; set; }

        [JsonPropertyName("timestamp")]
        public long Timestamp { get; set; }

        /// <summary>
        /// Hold the key for this long before releasing (0 = normal tap).
        /// </summary>
        [JsonPropertyName("holdMs")]
        public int HoldMs { get; set; }

        public CommandRequest()
        {
            ButtonId = string.Empty;
            Key = string.Empty;
            Timestamp = 0;
            HoldMs = 0;
        }

        public override string ToString()
        {
            return HoldMs > 0
                ? $"CommandRequest [ButtonId={ButtonId}, Key={Key}, Hold={HoldMs}ms]"
                : $"CommandRequest [ButtonId={ButtonId}, Key={Key}]";
        }
    }

    /// <summary>
    /// Response sent back to mobile
    /// </summary>
    public class CommandResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; }

        [JsonPropertyName("timestamp")]
        public long Timestamp { get; set; }

        public CommandResponse()
        {
            Success = false;
            Message = string.Empty;
            Timestamp = 0;
        }

        public CommandResponse(bool success, string message)
        {
            Success = success;
            Message = message ?? string.Empty;
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        public override string ToString()
        {
            return $"CommandResponse [Success={Success}, Message={Message}]";
        }
    }
}
