using System;

namespace EDSC.Models.Discovery
{
    /// <summary>
    /// Represents a discovered EDSC server on the network
    /// </summary>
    public class DiscoveredServer
    {
        public string Name { get; set; }
        public string IpAddress { get; set; }
        public int Port { get; set; }
        public DateTime DiscoveredAt { get; set; }
        public bool IsManualEntry { get; set; }

        public DiscoveredServer()
        {
            Name = string.Empty;
            IpAddress = string.Empty;
            Port = 5000;
            DiscoveredAt = DateTime.UtcNow;
            IsManualEntry = false;
        }

        public override string ToString()
        {
            return $"{Name} ({IpAddress}:{Port})";
        }

        public override bool Equals(object obj)
        {
            if (obj == null)
            {
                return false;
            }

            if (!(obj is DiscoveredServer other))
            {
                return false;
            }

            return IpAddress == other.IpAddress && Port == other.Port;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(IpAddress, Port);
        }
    }
}
