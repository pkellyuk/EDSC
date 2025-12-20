using Avalonia.Controls.ApplicationLifetimes;
using EDSC.Desktop.Services;
using EDSC.Models;
using EDSC.Services;
using EDSC.Services.Discovery;
using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace EDSC.Desktop
{
    /// <summary>
    /// Desktop-specific application class with server functionality
    /// </summary>
    public class DesktopApp : App
    {
        private IDiscoveryService? _discoveryService;
        private ICommandServer? _commandServer;
        private IKeyboardService? _keyboardService;
        private ServerConfig? _serverConfig;

        public override async void OnFrameworkInitializationCompleted()
        {
            Debug.WriteLine("[DesktopApp] Entry: OnFrameworkInitializationCompleted");

            base.OnFrameworkInitializationCompleted();

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

                    Debug.WriteLine($"[DesktopApp] Server config loaded: Port={_serverConfig.Port}, DiscoveryPort={_serverConfig.DiscoveryPort}");

                    // Initialize keyboard service
                    _keyboardService = new WindowsKeyboardService();
                    Debug.WriteLine("[DesktopApp] Keyboard service initialized");

                    // Initialize HTTP command server
                    _commandServer = new HttpCommandServer(_keyboardService);
                    Debug.WriteLine("[DesktopApp] Command server initialized");

                    // Start HTTP server
                    if (_serverConfig.AutoStart)
                    {
                        Debug.WriteLine($"[DesktopApp] Starting HTTP server on port {_serverConfig.Port}");
                        await _commandServer.StartAsync(_serverConfig.Port);
                        Debug.WriteLine("[DesktopApp] HTTP server started successfully");
                    }
                    else
                    {
                        Debug.WriteLine("[DesktopApp] HTTP server auto-start disabled");
                    }

                    // Initialize and start discovery service
                    if (_serverConfig.EnableDiscovery)
                    {
                        _discoveryService = new UdpDiscoveryServicePC(_serverConfig);
                        Debug.WriteLine($"[DesktopApp] Starting discovery service on port {_serverConfig.DiscoveryPort}");
                        await _discoveryService.StartListeningAsync(_serverConfig.DiscoveryPort);
                        Debug.WriteLine("[DesktopApp] Discovery service started successfully");
                    }
                    else
                    {
                        Debug.WriteLine("[DesktopApp] Discovery service disabled in configuration");
                    }

                    // TODO: Create and show main window (optional - can run as headless server)

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

                // Stop discovery service
                if (_discoveryService != null && _discoveryService.IsRunning)
                {
                    Debug.WriteLine("[DesktopApp] Stopping discovery service");
                    await _discoveryService.StopListeningAsync();
                    Debug.WriteLine("[DesktopApp] Discovery service stopped");
                }

                // Cleanup
                _keyboardService = null;
                _commandServer = null;
                _discoveryService = null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DesktopApp] Error during shutdown: {ex.Message}");
            }

            Debug.WriteLine("[DesktopApp] Exit: OnApplicationExit");
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
