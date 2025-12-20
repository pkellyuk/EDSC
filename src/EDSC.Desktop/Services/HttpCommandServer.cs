using EDSC.Models;
using EDSC.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace EDSC.Desktop.Services
{
    /// <summary>
    /// HTTP server for receiving commands from mobile clients (PC implementation)
    /// </summary>
    public class HttpCommandServer : ICommandServer
    {
        private IHost? _host;
        private readonly IKeyboardService _keyboardService;

        public bool IsRunning { get; private set; }

        public HttpCommandServer(IKeyboardService keyboardService)
        {
            Debug.WriteLine("[HttpCommandServer] Entry: Constructor");

            if (keyboardService == null)
            {
                throw new ArgumentNullException(nameof(keyboardService));
            }

            _keyboardService = keyboardService;

            Debug.WriteLine("[HttpCommandServer] Exit: Constructor");
        }

        public async Task StartAsync(int port, CancellationToken cancellationToken = default)
        {
            Debug.WriteLine($"[HttpCommandServer] Entry: StartAsync(port={port})");

            if (IsRunning)
            {
                Debug.WriteLine("[HttpCommandServer] Server already running");
                return;
            }

            try
            {
                Debug.WriteLine($"[HttpCommandServer] Building web host on port {port}");

                _host = Host.CreateDefaultBuilder()
                    .ConfigureWebHostDefaults(webBuilder =>
                    {
                        webBuilder.UseKestrel(options =>
                        {
                            options.ListenAnyIP(port);
                        });

                        webBuilder.Configure(app =>
                        {
                            app.Run(async context =>
                            {
                                if (context.Request.Path == "/" && context.Request.Method == "GET")
                                {
                                    Debug.WriteLine("[HttpCommandServer] Health check requested");
                                    context.Response.ContentType = "application/json";
                                    var healthJson = JsonSerializer.Serialize(new
                                    {
                                        service = "EDSC",
                                        status = "running",
                                        version = "1.0.0"
                                    });
                                    await context.Response.WriteAsync(healthJson);
                                }
                                else if (context.Request.Path == "/command" && context.Request.Method == "POST")
                                {
                                    await HandleCommandRequest(context);
                                }
                                else
                                {
                                    context.Response.StatusCode = 404;
                                }
                            });
                        });
                    })
                    .Build();

                await _host.StartAsync(cancellationToken);
                IsRunning = true;

                Debug.WriteLine($"[HttpCommandServer] HTTP server started on port {port}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HttpCommandServer] Error starting server: {ex.Message}");
                throw;
            }

            Debug.WriteLine("[HttpCommandServer] Exit: StartAsync");
        }

        private async Task HandleCommandRequest(HttpContext context)
        {
            Debug.WriteLine("[HttpCommandServer] Entry: HandleCommandRequest");

            if (context == null)
            {
                Debug.WriteLine("[HttpCommandServer] Context is null");
                return;
            }

            try
            {
                // Read and deserialize request
                var request = await JsonSerializer.DeserializeAsync<CommandRequest>(context.Request.Body);

                if (request == null)
                {
                    Debug.WriteLine("[HttpCommandServer] Failed to deserialize request");
                    await SendResponse(context, false, "Invalid request");
                    return;
                }

                Debug.WriteLine($"[HttpCommandServer] Received command: {request}");

                // Validate request
                if (string.IsNullOrEmpty(request.Key))
                {
                    Debug.WriteLine("[HttpCommandServer] Key is empty");
                    await SendResponse(context, false, "Key is required");
                    return;
                }

                // Execute keyboard command
                Debug.WriteLine($"[HttpCommandServer] Simulating key press: {request.Key}");
                await _keyboardService.SendKeyPressAsync(request.Key);

                Debug.WriteLine("[HttpCommandServer] Key press simulated successfully");
                await SendResponse(context, true, $"Key '{request.Key}' pressed");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HttpCommandServer] Error handling command: {ex.Message}");
                await SendResponse(context, false, $"Error: {ex.Message}");
            }

            Debug.WriteLine("[HttpCommandServer] Exit: HandleCommandRequest");
        }

        private async Task SendResponse(HttpContext context, bool success, string message)
        {
            Debug.WriteLine($"[HttpCommandServer] Entry: SendResponse(success={success}, message={message})");

            if (context == null)
            {
                Debug.WriteLine("[HttpCommandServer] Context is null");
                return;
            }

            try
            {
                var response = new CommandResponse(success, message);

                context.Response.ContentType = "application/json";
                context.Response.StatusCode = success ? 200 : 400;

                var responseJson = JsonSerializer.Serialize(response);
                await context.Response.WriteAsync(responseJson);

                Debug.WriteLine($"[HttpCommandServer] Response sent: {response}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HttpCommandServer] Error sending response: {ex.Message}");
            }

            Debug.WriteLine("[HttpCommandServer] Exit: SendResponse");
        }

        public async Task StopAsync()
        {
            Debug.WriteLine("[HttpCommandServer] Entry: StopAsync");

            if (!IsRunning)
            {
                Debug.WriteLine("[HttpCommandServer] Server not running");
                return;
            }

            try
            {
                if (_host != null)
                {
                    Debug.WriteLine("[HttpCommandServer] Stopping web host");
                    await _host.StopAsync();
                    _host.Dispose();
                    _host = null;
                }

                IsRunning = false;
                Debug.WriteLine("[HttpCommandServer] HTTP server stopped");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HttpCommandServer] Error stopping server: {ex.Message}");
            }

            Debug.WriteLine("[HttpCommandServer] Exit: StopAsync");
        }
    }
}
