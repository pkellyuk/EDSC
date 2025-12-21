using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using EDSC.Desktop.Services;
using EDSC.Models;
using EDSC.Services;
using EDSC.ViewModels;
using QRCoder;
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace EDSC.Desktop
{
    /// <summary>
    /// Desktop-specific application class with server functionality
    /// </summary>
    public class DesktopApp : App
    {
        private ICommandServer? _commandServer;
        private IKeyboardService? _keyboardService;
        private ServerConfig? _serverConfig;

        public override async void OnFrameworkInitializationCompleted()
        {
            Debug.WriteLine("[DesktopApp] Entry: OnFrameworkInitializationCompleted");

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                try
                {
                    // Load configuration
                    _serverConfig = await LoadConfigurationAsync();

                    if (_serverConfig == null)
                    {
                        Debug.WriteLine("[DesktopApp] Configuration is null, using defaults");
                        _serverConfig = new ServerConfig();
                    }

                    Debug.WriteLine($"[DesktopApp] Server config loaded: Port={_serverConfig.Port}");

                    var configService = new JsonConfigurationService();

                    var localIps = GetLocalIpAddresses();
                    var selectedIp = localIps.FirstOrDefault() ?? "127.0.0.1";

                    // Initialize keyboard service
                    _keyboardService = new WindowsKeyboardService();
                    Debug.WriteLine("[DesktopApp] Keyboard service initialized");

                    var connectionViewModel = new ConnectionViewModel();

                    // Initialize HTTP command server
                    _commandServer = new HttpCommandServer(_keyboardService, configService);
                    Debug.WriteLine("[DesktopApp] Command server initialized");

                    // Wire up video frame handler
                    if (_commandServer is HttpCommandServer httpServer)
                    {
                        httpServer.FrameReceived += (sender, frameData) =>
                        {
                            try
                            {
                                // Convert byte array to Bitmap on UI thread
                                Dispatcher.UIThread.InvokeAsync(() =>
                                {
                                    try
                                    {
                                        using (var ms = new MemoryStream(frameData))
                                        {
                                            var bitmap = new Bitmap(ms);
                                            var fps = httpServer.GetCurrentFps();
                                            connectionViewModel.UpdateVideoFrame(bitmap, fps);
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Debug.WriteLine($"[DesktopApp] Error updating video frame: {ex.Message}");
                                    }
                                });
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"[DesktopApp] Error in FrameReceived handler: {ex.Message}");
                            }
                        };
                        Debug.WriteLine("[DesktopApp] Video frame handler wired up");
                    }

                    // Start HTTP server
                    if (_serverConfig.AutoStart)
                    {
                        Debug.WriteLine($"[DesktopApp] Starting HTTP server on port {_serverConfig.Port}");
                        await _commandServer.StartAsync(_serverConfig.Port, selectedIp);
                        Debug.WriteLine("[DesktopApp] HTTP server started successfully");
                    }
                    else
                    {
                        Debug.WriteLine("[DesktopApp] HTTP server auto-start disabled");
                    }

                    var shellViewModel = new DesktopShellViewModel(connectionViewModel, configService, _serverConfig.Port);
                    desktop.MainWindow = new MainWindow
                    {
                        DataContext = shellViewModel
                    };
                    desktop.MainWindow.Show();
                    desktop.MainWindow.Activate();

                    connectionViewModel.SetLocalIpAddresses(localIps);
                    connectionViewModel.LocalIpAddressChanged += (_, ip) =>
                    {
                        UpdateQrCode(connectionViewModel, ip, _serverConfig.Port);
                        _ = RebindCommandServerAsync(ip, _serverConfig.Port);
                    };
                    UpdateQrCode(connectionViewModel, selectedIp, _serverConfig.Port);

                    // Register shutdown handler
                    desktop.Exit += OnApplicationExit;

                    Debug.WriteLine("[DesktopApp] Desktop application initialized successfully");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[DesktopApp] Error during initialization: {ex.Message}");
                    Debug.WriteLine($"[DesktopApp] Stack trace: {ex.StackTrace}");
                }
            }

            base.OnFrameworkInitializationCompleted();
            Debug.WriteLine("[DesktopApp] Exit: OnFrameworkInitializationCompleted");
        }

        private async void OnApplicationExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
        {
            Debug.WriteLine("[DesktopApp] Entry: OnApplicationExit");

            try
            {
                // Stop HTTP command server
                if (_commandServer != null && _commandServer.IsRunning)
                {
                    Debug.WriteLine("[DesktopApp] Stopping HTTP command server");
                    await _commandServer.StopAsync();
                    Debug.WriteLine("[DesktopApp] HTTP command server stopped");
                }

                // Cleanup
                _keyboardService = null;
                _commandServer = null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DesktopApp] Error during shutdown: {ex.Message}");
            }

            Debug.WriteLine("[DesktopApp] Exit: OnApplicationExit");
        }

        private static void UpdateQrCode(ConnectionViewModel viewModel, string ipAddress, int port)
        {
            if (viewModel == null || string.IsNullOrEmpty(ipAddress))
            {
                return;
            }

            var url = BuildWebUiUrl(port, ipAddress);
            if (string.IsNullOrEmpty(url))
            {
                return;
            }

            var qrBitmap = GenerateQrCode(url);
            if (qrBitmap != null)
            {
                viewModel.SetQrCode(qrBitmap, url);
            }
        }

        private async Task RebindCommandServerAsync(string ipAddress, int port)
        {
            try
            {
                if (_commandServer == null)
                {
                    return;
                }

                if (_commandServer.IsRunning)
                {
                    await _commandServer.StopAsync();
                }

                await _commandServer.StartAsync(port, ipAddress);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DesktopApp] Error rebinding HTTP server: {ex.Message}");
            }
        }

        private static Bitmap? GenerateQrCode(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return null;
            }

            try
            {
                using var generator = new QRCodeGenerator();
                using var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
                var pngQr = new PngByteQRCode(data);
                var bytes = pngQr.GetGraphic(6);
                return new Bitmap(new MemoryStream(bytes));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DesktopApp] Error generating QR code: {ex.Message}");
                return null;
            }
        }

        private static string BuildWebUiUrl(int port, string ipAddress)
        {
            if (string.IsNullOrEmpty(ipAddress))
            {
                return string.Empty;
            }

            return $"http://{ipAddress}:{port}/web";
        }

        private static string[] GetLocalIpAddresses()
        {
            Debug.WriteLine("[DesktopApp] Entry: GetLocalIpAddresses()");

            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());

                if (host?.AddressList == null)
                {
                    Debug.WriteLine("[DesktopApp] Exit: GetLocalIpAddresses() - host or AddressList is null, returning 127.0.0.1");
                    return new[] { "127.0.0.1" };
                }

                var addresses = host.AddressList
                    .Where(ip => ip != null && ip.AddressFamily == AddressFamily.InterNetwork)
                    .Select(ip => ip.ToString())
                    .Where(ip => !string.IsNullOrEmpty(ip))
                    .Distinct()
                    .OrderBy(ip => GetIpPriority(ip))
                    .ToArray();

                if (addresses.Length > 0)
                {
                    Debug.WriteLine($"[DesktopApp] Exit: GetLocalIpAddresses() - returning {addresses.Length} addresses: {string.Join(", ", addresses)}");
                    return addresses;
                }

                Debug.WriteLine("[DesktopApp] Exit: GetLocalIpAddresses() - no addresses found, returning 127.0.0.1");
                return new[] { "127.0.0.1" };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DesktopApp] Exit: GetLocalIpAddresses() - exception: {ex.Message}");
                return new[] { "127.0.0.1" };
            }
        }

        private static int GetIpPriority(string ip)
        {
            if (string.IsNullOrEmpty(ip))
            {
                return 99;
            }

            // 192.168.x.x - most common home/small office networks
            if (ip.StartsWith("192.168."))
            {
                return 0;
            }

            // 10.x.x.x - common in larger networks
            if (ip.StartsWith("10."))
            {
                return 1;
            }

            // 172.16.x.x - 172.31.x.x - private range
            if (ip.StartsWith("172."))
            {
                var parts = ip.Split('.');
                if (parts.Length >= 2 && int.TryParse(parts[1], out int second))
                {
                    if (second >= 16 && second <= 31)
                    {
                        return 2;
                    }
                }
            }

            // Other IPs (public, etc.)
            return 10;
        }

        private static string GetLocalIpAddress()
        {
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());

                if (host?.AddressList == null)
                {
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
                        return ip.ToString();
                    }
                }

                return "127.0.0.1";
            }
            catch
            {
                return "127.0.0.1";
            }
        }

        private async Task<ServerConfig?> LoadConfigurationAsync()
        {
            Debug.WriteLine("[DesktopApp] Entry: LoadConfigurationAsync");

            try
            {
                var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");

                if (!File.Exists(configPath))
                {
                    Debug.WriteLine($"[DesktopApp] Config file not found at {configPath}, using defaults");
                    return new ServerConfig();
                }

                Debug.WriteLine($"[DesktopApp] Loading config from {configPath}");

                var json = await File.ReadAllTextAsync(configPath);

                if (string.IsNullOrEmpty(json))
                {
                    Debug.WriteLine("[DesktopApp] Config file is empty");
                    return new ServerConfig();
                }

                var config = JsonSerializer.Deserialize<ServerConfig>(json);

                if (config == null)
                {
                    Debug.WriteLine("[DesktopApp] Failed to deserialize config");
                    return new ServerConfig();
                }

                Debug.WriteLine("[DesktopApp] Configuration loaded successfully");
                return config;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DesktopApp] Error loading configuration: {ex.Message}");
                return new ServerConfig();
            }
            finally
            {
                Debug.WriteLine("[DesktopApp] Exit: LoadConfigurationAsync");
            }
        }
    }
}
