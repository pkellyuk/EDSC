using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EDSC.Models;
using EDSC.Models.Discovery;

namespace EDSC.Services.Discovery
{
    /// <summary>
    /// PC implementation of discovery service that listens for discovery requests
    /// </summary>
    public class UdpDiscoveryServicePC : IDiscoveryService
    {
        private UdpClient _udpListener;
        private CancellationTokenSource _listenerCts;
        private readonly ServerConfig _serverConfig;
        private const int DISCOVERY_PORT = 5001;

        public bool IsRunning { get; private set; }

        public UdpDiscoveryServicePC(ServerConfig serverConfig)
        {
            if (serverConfig == null)
            {
                throw new ArgumentNullException(nameof(serverConfig));
            }

            _serverConfig = serverConfig;
        }

        public async Task StartListeningAsync(int udpPort = 5001, CancellationToken cancellationToken = default)
        {
            Debug.WriteLine($"[Discovery] Entry: StartListeningAsync(udpPort={udpPort})");

            if (IsRunning)
            {
                Debug.WriteLine("[Discovery] Already running, returning");
                return;
            }

            try
            {
                Debug.WriteLine($"[Discovery] Starting UDP listener on port {udpPort}");

                _udpListener = new UdpClient(udpPort);
                _listenerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                IsRunning = true;

                // Start listening loop in background
                _ = Task.Run(() => ListenForDiscoveryRequests(_listenerCts.Token), _listenerCts.Token);

                Debug.WriteLine("[Discovery] UDP listener started successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Discovery] Error starting listener: {ex.Message}");
                throw;
            }

            Debug.WriteLine("[Discovery] Exit: StartListeningAsync");
        }

        private async Task ListenForDiscoveryRequests(CancellationToken ct)
        {
            Debug.WriteLine("[Discovery] Entry: ListenForDiscoveryRequests");

            if (_udpListener == null)
            {
                Debug.WriteLine("[Discovery] UDP listener is null, exiting");
                return;
            }

            try
            {
                while (!ct.IsCancellationRequested && IsRunning)
                {
                    try
                    {
                        Debug.WriteLine("[Discovery] Waiting for discovery request...");
                        var result = await _udpListener.ReceiveAsync(ct);

                        Debug.WriteLine($"[Discovery] Received {result.Buffer.Length} bytes from {result.RemoteEndPoint}");

                        var json = Encoding.UTF8.GetString(result.Buffer);
                        Debug.WriteLine($"[Discovery] JSON: {json}");

                        var request = JsonSerializer.Deserialize<DiscoveryRequest>(json);

                        if (request == null)
                        {
                            Debug.WriteLine("[Discovery] Failed to deserialize request");
                            continue;
                        }

                        if (request.Type == "discover")
                        {
                            Debug.WriteLine($"[Discovery] Valid discovery request from {result.RemoteEndPoint}, RequestId={request.RequestId}");
                            await SendDiscoveryResponse(result.RemoteEndPoint, request.RequestId);
                        }
                        else
                        {
                            Debug.WriteLine($"[Discovery] Invalid request type: {request.Type}");
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        Debug.WriteLine("[Discovery] Operation cancelled");
                        break;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[Discovery] Error in listener loop: {ex.Message}");
                        // Continue listening despite errors
                    }
                }
            }
            finally
            {
                Debug.WriteLine("[Discovery] Exit: ListenForDiscoveryRequests");
            }
        }

        private async Task SendDiscoveryResponse(IPEndPoint remoteEndPoint, string requestId)
        {
            Debug.WriteLine($"[Discovery] Entry: SendDiscoveryResponse(remoteEndPoint={remoteEndPoint}, requestId={requestId})");

            if (remoteEndPoint == null)
            {
                Debug.WriteLine("[Discovery] Remote endpoint is null, returning");
                return;
            }

            if (string.IsNullOrEmpty(requestId))
            {
                Debug.WriteLine("[Discovery] Request ID is null or empty, returning");
                return;
            }

            try
            {
                var localIp = GetLocalIpAddress();
                Debug.WriteLine($"[Discovery] Local IP address: {localIp}");

                var response = new DiscoveryResponse
                {
                    RequestId = requestId,
                    ServerName = $"EDSC-{Environment.MachineName}",
                    IpAddress = localIp,
                    HttpPort = _serverConfig.Port,
                    Version = "1.0.0"
                };

                var json = JsonSerializer.Serialize(response);
                var bytes = Encoding.UTF8.GetBytes(json);

                Debug.WriteLine($"[Discovery] Sending response: {json}");

                await _udpListener.SendAsync(bytes, bytes.Length, remoteEndPoint);

                Debug.WriteLine($"[Discovery] Sent {bytes.Length} bytes to {remoteEndPoint}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Discovery] Error sending response: {ex.Message}");
            }

            Debug.WriteLine("[Discovery] Exit: SendDiscoveryResponse");
        }

        private string GetLocalIpAddress()
        {
            Debug.WriteLine("[Discovery] Entry: GetLocalIpAddress");

            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());

                if (host == null || host.AddressList == null)
                {
                    Debug.WriteLine("[Discovery] Host or address list is null");
                    return "127.0.0.1";
                }

                foreach (var ip in host.AddressList)
                {
                    if (ip == null)
                    {
                        continue;
                    }

                    if (ip.AddressFamily == AddressFamily.InterNetwork)
                    {
                        Debug.WriteLine($"[Discovery] Found IPv4 address: {ip}");
                        return ip.ToString();
                    }
                }

                Debug.WriteLine("[Discovery] No IPv4 address found, returning loopback");
                return "127.0.0.1";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Discovery] Error getting local IP: {ex.Message}");
                return "127.0.0.1";
            }
            finally
            {
                Debug.WriteLine("[Discovery] Exit: GetLocalIpAddress");
            }
        }

        public async Task StopListeningAsync()
        {
            Debug.WriteLine("[Discovery] Entry: StopListeningAsync");

            if (!IsRunning)
            {
                Debug.WriteLine("[Discovery] Not running, returning");
                return;
            }

            try
            {
                Debug.WriteLine("[Discovery] Stopping UDP listener");

                IsRunning = false;

                if (_listenerCts != null)
                {
                    _listenerCts.Cancel();
                    _listenerCts.Dispose();
                    _listenerCts = null;
                }

                if (_udpListener != null)
                {
                    _udpListener.Close();
                    _udpListener.Dispose();
                    _udpListener = null;
                }

                Debug.WriteLine("[Discovery] UDP listener stopped");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Discovery] Error stopping listener: {ex.Message}");
            }

            Debug.WriteLine("[Discovery] Exit: StopListeningAsync");
            await Task.CompletedTask;
        }

        public Task<List<DiscoveredServer>> DiscoverServersAsync(int timeoutSeconds = 3, CancellationToken cancellationToken = default)
        {
            Debug.WriteLine("[Discovery] DiscoverServersAsync called on PC - not implemented");
            throw new NotImplementedException("PC does not discover servers - it listens for discovery requests");
        }
    }
}
