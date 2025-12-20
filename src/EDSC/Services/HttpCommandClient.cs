using EDSC.Models;
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace EDSC.Services
{
    /// <summary>
    /// HTTP client for sending commands to PC server
    /// </summary>
    public class HttpCommandClient : ICommandClient
    {
        private readonly HttpClient _httpClient;
        private string _serverUrl;

        public HttpCommandClient()
        {
            Debug.WriteLine("[HttpCommandClient] Entry: Constructor");

            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(5)
            };
            _serverUrl = string.Empty;

            Debug.WriteLine("[HttpCommandClient] Exit: Constructor");
        }

        public void SetServerAddress(string ipAddress, int port)
        {
            Debug.WriteLine($"[HttpCommandClient] Entry: SetServerAddress(ipAddress={ipAddress}, port={port})");

            if (string.IsNullOrEmpty(ipAddress))
            {
                Debug.WriteLine("[HttpCommandClient] IP address is null or empty");
                return;
            }

            if (port <= 0 || port > 65535)
            {
                Debug.WriteLine($"[HttpCommandClient] Invalid port: {port}");
                return;
            }

            _serverUrl = $"http://{ipAddress}:{port}";

            Debug.WriteLine($"[HttpCommandClient] Server URL set to: {_serverUrl}");
            Debug.WriteLine("[HttpCommandClient] Exit: SetServerAddress");
        }

        public async Task<bool> TestConnectionAsync()
        {
            Debug.WriteLine("[HttpCommandClient] Entry: TestConnectionAsync");

            if (string.IsNullOrEmpty(_serverUrl))
            {
                Debug.WriteLine("[HttpCommandClient] Server URL is not set");
                return false;
            }

            try
            {
                Debug.WriteLine($"[HttpCommandClient] Testing connection to {_serverUrl}");

                var response = await _httpClient.GetAsync(_serverUrl);

                if (response == null)
                {
                    Debug.WriteLine("[HttpCommandClient] Response is null");
                    return false;
                }

                var isSuccess = response.IsSuccessStatusCode;

                Debug.WriteLine($"[HttpCommandClient] Connection test result: {isSuccess}");
                return isSuccess;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HttpCommandClient] Connection test failed: {ex.Message}");
                return false;
            }
            finally
            {
                Debug.WriteLine("[HttpCommandClient] Exit: TestConnectionAsync");
            }
        }

        public async Task<CommandResponse> SendCommandAsync(string buttonId, string key)
        {
            Debug.WriteLine($"[HttpCommandClient] Entry: SendCommandAsync(buttonId={buttonId}, key={key})");

            if (string.IsNullOrEmpty(_serverUrl))
            {
                Debug.WriteLine("[HttpCommandClient] Server URL is not set");
                return new CommandResponse(false, "Server URL not set");
            }

            if (string.IsNullOrEmpty(key))
            {
                Debug.WriteLine("[HttpCommandClient] Key is null or empty");
                return new CommandResponse(false, "Key is required");
            }

            try
            {
                var request = new CommandRequest
                {
                    ButtonId = buttonId ?? string.Empty,
                    Key = key,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                };

                Debug.WriteLine($"[HttpCommandClient] Sending command: {request}");

                var json = JsonSerializer.Serialize(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var url = $"{_serverUrl}/command";
                Debug.WriteLine($"[HttpCommandClient] Posting to {url}");

                var response = await _httpClient.PostAsync(url, content);

                if (response == null)
                {
                    Debug.WriteLine("[HttpCommandClient] Response is null");
                    return new CommandResponse(false, "No response from server");
                }

                Debug.WriteLine($"[HttpCommandClient] Response status: {response.StatusCode}");

                var responseJson = await response.Content.ReadAsStringAsync();

                if (string.IsNullOrEmpty(responseJson))
                {
                    Debug.WriteLine("[HttpCommandClient] Response body is empty");
                    return new CommandResponse(false, "Empty response from server");
                }

                Debug.WriteLine($"[HttpCommandClient] Response JSON: {responseJson}");

                var commandResponse = JsonSerializer.Deserialize<CommandResponse>(responseJson);

                if (commandResponse == null)
                {
                    Debug.WriteLine("[HttpCommandClient] Failed to deserialize response");
                    return new CommandResponse(false, "Invalid response from server");
                }

                Debug.WriteLine($"[HttpCommandClient] Command response: {commandResponse}");
                return commandResponse;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HttpCommandClient] Error sending command: {ex.Message}");
                return new CommandResponse(false, $"Error: {ex.Message}");
            }
            finally
            {
                Debug.WriteLine("[HttpCommandClient] Exit: SendCommandAsync");
            }
        }
    }
}
