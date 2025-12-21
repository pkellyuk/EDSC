using EDSC.Services;
using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace EDSC.Desktop.Services
{
    /// <summary>
    /// UDP sender for Opentrack integration
    /// Sends 6DOF head pose data over UDP
    /// </summary>
    public class OpentrackUdpSender : IDisposable
    {
        private UdpClient? _udpClient;
        private IPEndPoint? _endpoint;
        private bool _isConnected;

        public bool IsConnected
        {
            get
            {
                return _isConnected;
            }
        }

        /// <summary>
        /// Connect to Opentrack UDP endpoint
        /// </summary>
        /// <param name="ipAddress">IP address (default: 127.0.0.1)</param>
        /// <param name="port">UDP port (default: 4242)</param>
        public void Connect(string ipAddress = "127.0.0.1", int port = 4242)
        {
            Debug.WriteLine($"[OpentrackUdpSender] Entry: Connect(ip={ipAddress}, port={port})");

            if (string.IsNullOrEmpty(ipAddress))
            {
                Debug.WriteLine("[OpentrackUdpSender] IP address is null or empty, using default");
                ipAddress = "127.0.0.1";
            }

            if (port <= 0 || port > 65535)
            {
                Debug.WriteLine($"[OpentrackUdpSender] Invalid port {port}, using default 4242");
                port = 4242;
            }

            try
            {
                Disconnect();

                _udpClient = new UdpClient();
                _endpoint = new IPEndPoint(IPAddress.Parse(ipAddress), port);
                _isConnected = true;

                Debug.WriteLine($"[OpentrackUdpSender] Connected to {ipAddress}:{port}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OpentrackUdpSender] Error connecting: {ex.Message}");
                _isConnected = false;
                throw;
            }

            Debug.WriteLine("[OpentrackUdpSender] Exit: Connect");
        }

        /// <summary>
        /// Send head pose data to Opentrack
        /// </summary>
        /// <param name="pose">Head pose data</param>
        public async Task SendPoseAsync(HeadPose pose)
        {
            if (pose == null)
            {
                return;
            }

            if (!_isConnected || _udpClient == null || _endpoint == null)
            {
                Debug.WriteLine("[OpentrackUdpSender] Not connected, cannot send pose");
                return;
            }

            try
            {
                // Pack 6 doubles into byte array (48 bytes total)
                // Order: X, Y, Z, Yaw, Pitch, Roll
                byte[] buffer = new byte[48];

                BitConverter.GetBytes(pose.X).CopyTo(buffer, 0);
                BitConverter.GetBytes(pose.Y).CopyTo(buffer, 8);
                BitConverter.GetBytes(pose.Z).CopyTo(buffer, 16);
                BitConverter.GetBytes(pose.Yaw).CopyTo(buffer, 24);
                BitConverter.GetBytes(pose.Pitch).CopyTo(buffer, 32);
                BitConverter.GetBytes(pose.Roll).CopyTo(buffer, 40);

                await _udpClient.SendAsync(buffer, buffer.Length, _endpoint);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OpentrackUdpSender] Error sending pose: {ex.Message}");
            }
        }

        /// <summary>
        /// Disconnect from Opentrack
        /// </summary>
        public void Disconnect()
        {
            Debug.WriteLine("[OpentrackUdpSender] Entry: Disconnect");

            if (_udpClient != null)
            {
                _udpClient.Close();
                _udpClient.Dispose();
                _udpClient = null;
            }

            _endpoint = null;
            _isConnected = false;

            Debug.WriteLine("[OpentrackUdpSender] Exit: Disconnect");
        }

        public void Dispose()
        {
            Disconnect();
        }
    }
}
