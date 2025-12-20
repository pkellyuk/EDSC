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
using EDSC.Models.Discovery;

namespace EDSC.Services.Discovery
{
    /// <summary>
    /// Android/Mobile implementation of discovery service that broadcasts discovery requests
    /// </summary>
    public class UdpDiscoveryServiceAndroid : IDiscoveryService
    {
        private const int DISCOVERY_PORT = 5001;
        private const int RETRY_COUNT = 3;
        private const int TIMEOUT_MS = 1000;

        public bool IsRunning { get; private set; }

        public async Task<List<DiscoveredServer>> DiscoverServersAsync(int timeoutSeconds = 3, CancellationToken cancellationToken = default)
        {
            Debug.WriteLine("[Discovery] Entry: DiscoverServersAsync(timeoutSeconds={0})", timeoutSeconds);

            if (IsRunning)
            {
                Debug.WriteLine("[Discovery] Discovery already in progress");
                return new List<DiscoveredServer>();
            }

            IsRunning = true;
            var discoveredServers = new List<DiscoveredServer>();
            var requestId = Guid.NewGuid().ToString();

            Debug.WriteLine($"[Discovery] Starting server discovery with RequestId={requestId}");

            try
            {
                using var udpClient = new UdpClient();

                if (udpClient == null)
                {
                    Debug.WriteLine("[Discovery] Failed to create UDP client");
                    return discoveredServers;
                }

                udpClient.EnableBroadcast = true;
                udpClient.Client.ReceiveTimeout = TIMEOUT_MS;

                var request = new DiscoveryRequest
                {
                    RequestId = requestId,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                };

                var requestJson = JsonSerializer.Serialize(request);
                var requestBytes = Encoding.UTF8.GetBytes(requestJson);

                Debug.WriteLine($"[Discovery] Request JSON: {requestJson}");
                Debug.WriteLine($"[Discovery] Will attempt {RETRY_COUNT} times with {TIMEOUT_MS}ms timeout");

                // Try multiple times
                for (int attempt = 0; attempt < RETRY_COUNT; attempt++)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        Debug.WriteLine("[Discovery] Cancellation requested");
                        break;
                    }

                    Debug.WriteLine($"[Discovery] Attempt {attempt + 1}/{RETRY_COUNT}");

                    try
                    {
                        // Send broadcast
                        var broadcastEndpoint = new IPEndPoint(IPAddress.Broadcast, DISCOVERY_PORT);
                        Debug.WriteLine($"[Discovery] Broadcasting to {broadcastEndpoint}");

                        await udpClient.SendAsync(requestBytes, requestBytes.Length, broadcastEndpoint);
                        Debug.WriteLine($"[Discovery] Sent {requestBytes.Length} bytes");

                        // Listen for responses
                        var startTime = DateTime.UtcNow;
                        while ((DateTime.UtcNow - startTime).TotalMilliseconds < TIMEOUT_MS)
                        {
                            try
                            {
                                Debug.WriteLine("[Discovery] Waiting for response...");
                                var result = await udpClient.ReceiveAsync();

                                if (result.Buffer == null)
                                {
                                    Debug.WriteLine("[Discovery] Received null buffer");
                                    continue;
                                }

                                Debug.WriteLine($"[Discovery] Received {result.Buffer.Length} bytes from {result.RemoteEndPoint}");

                                var responseJson = Encoding.UTF8.GetString(result.Buffer);
                                Debug.WriteLine($"[Discovery] Response JSON: {responseJson}");

                                var response = JsonSerializer.Deserialize<DiscoveryResponse>(responseJson);

                                if (response == null)
                                {
                                    Debug.WriteLine("[Discovery] Failed to deserialize response");
                                    continue;
                                }

                                if (response.RequestId != requestId)
                                {
                                    Debug.WriteLine($"[Discovery] Request ID mismatch: {response.RequestId} != {requestId}");
                                    continue;
                                }

                                if (response.Type != "response")
                                {
                                    Debug.WriteLine($"[Discovery] Invalid response type: {response.Type}");
                                    continue;
                                }

                                Debug.WriteLine($"[Discovery] Found server: {response.ServerName} at {response.IpAddress}:{response.HttpPort}");

                                var server = new DiscoveredServer
                                {
                                    Name = response.ServerName,
                                    IpAddress = response.IpAddress,
                                    Port = response.HttpPort,
                                    DiscoveredAt = DateTime.UtcNow,
                                    IsManualEntry = false
                                };

                                // Avoid duplicates
                                if (!discoveredServers.Any(s => s.IpAddress == server.IpAddress && s.Port == server.Port))
                                {
                                    discoveredServers.Add(server);
                                    Debug.WriteLine($"[Discovery] Added server to list. Total servers: {discoveredServers.Count}");
                                }
                                else
                                {
                                    Debug.WriteLine("[Discovery] Server already in list, skipping");
                                }
                            }
                            catch (SocketException socketEx)
                            {
                                Debug.WriteLine($"[Discovery] Socket timeout/error: {socketEx.Message}");
                                // Timeout, continue to next attempt
                                break;
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"[Discovery] Error receiving response: {ex.Message}");
                                break;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[Discovery] Error in attempt {attempt + 1}: {ex.Message}");
                    }

                    // Small delay between attempts
                    if (attempt < RETRY_COUNT - 1)
                    {
                        Debug.WriteLine("[Discovery] Waiting 200ms before next attempt");
                        await Task.Delay(200, cancellationToken);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Discovery] Error during discovery: {ex.Message}");
            }
            finally
            {
                IsRunning = false;
            }

            Debug.WriteLine($"[Discovery] Discovery complete. Found {discoveredServers.Count} server(s)");
            Debug.WriteLine("[Discovery] Exit: DiscoverServersAsync");
            return discoveredServers;
        }

        public Task StartListeningAsync(int udpPort = 5001, CancellationToken cancellationToken = default)
        {
            Debug.WriteLine("[Discovery] StartListeningAsync called on Android - not implemented");
            throw new NotImplementedException("Mobile does not listen for discovery - it broadcasts discovery requests");
        }

        public Task StopListeningAsync()
        {
            Debug.WriteLine("[Discovery] StopListeningAsync called on Android - not implemented");
            throw new NotImplementedException("Mobile does not listen for discovery - it broadcasts discovery requests");
        }
    }
}
