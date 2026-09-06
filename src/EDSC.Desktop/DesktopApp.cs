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
using System.Threading;
using System.Threading.Tasks;
using WindowsInput.Native;
using Avalonia.Media.Imaging;
using System.Collections.Generic;
using Path = System.IO.Path;

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
        private CertificateService? _certificateService;
        private FaceTrackingService? _faceTrackingService;
        private OpentrackUdpSender? _opentrackSender;
        private PoseOutputRouter? _poseRouter;
        private GlobalHotkeyService? _centerHotkey;
        private ConnectionViewModel? _connectionViewModel;
        private DateTime _lastPhoneStatusUpdate = DateTime.MinValue;
        private HeadPose? _lastPose;
        private FaceMeshFrame? _pendingMesh;
        private int _meshUpdateQueued;

        public override async void OnFrameworkInitializationCompleted()
        {
            Debug.WriteLine("[DesktopApp] Entry: OnFrameworkInitializationCompleted");

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // Check for command-line arguments for elevated certificate installation
                var args = desktop.Args;
                if (args != null && args.Length > 0 && args[0] == "--install-certificate")
                {
                    Debug.WriteLine("[DesktopApp] Running in elevated mode for certificate installation");
                    await HandleElevatedCertificateInstallationAsync(args);
                    Environment.Exit(0);
                    return;
                }

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

                    // Initialize certificate service
                    _certificateService = new CertificateService();
                    var certStatus = _certificateService.GetCertificateStatus();
                    connectionViewModel.CertificateStatus = GetCertificateStatusText(certStatus);
                    Debug.WriteLine($"[DesktopApp] Certificate service initialized - Status: {certStatus}");

                    // Wire up certificate installation command
                    connectionViewModel.InstallCertificateCommand = new RelayCommand(
                        async () => await InstallCertificateAsync(connectionViewModel, localIps.ToArray()),
                        () => true
                    );

                    // Wire up URL open command
                    connectionViewModel.OpenUrlCommand = new RelayCommand(
                        async () => await Task.Run(() => OpenUrlInBrowser(connectionViewModel.QrCodeUrl)),
                        () => true
                    );

                    // Initialize face tracking service
                    try
                    {
                        _faceTrackingService = new FaceTrackingService();
                        var modelsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Models");
                        await _faceTrackingService.InitializeAsync(modelsPath);

                        // Initialize Opentrack UDP sender
                        _opentrackSender = new OpentrackUdpSender();
                        _opentrackSender.Connect("127.0.0.1", 4242);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[DesktopApp] Failed to initialize face tracking: {ex.Message}");
                        Console.WriteLine($"[DesktopApp] Stack trace: {ex.StackTrace}");
                        _faceTrackingService = null;
                        _opentrackSender = null;
                    }

                    // Pose output: Opentrack over UDP by default, or straight into the game's TrackIR interface
                    _poseRouter = new PoseOutputRouter(_opentrackSender);
                    connectionViewModel.CenterCommand = new RelayCommand(
                        () =>
                        {
                            _poseRouter?.Center();
                            return Task.CompletedTask;
                        },
                        () => true
                    );

                    // Re-centre hotkey ("=" by default, changeable) works from anywhere, including while the
                    // game has focus. Installed here because this runs on the UI thread, which pumps messages
                    // for the hook. Created before the config binding so the saved key can be applied to it.
                    _centerHotkey = new GlobalHotkeyService();
                    if (!_centerHotkey.Start(GlobalHotkeyService.VkOemPlus, () => _poseRouter?.Center()))
                    {
                        Debug.WriteLine("[DesktopApp] Centre hotkey could not be installed");
                        _centerHotkey = null;
                    }

                    connectionViewModel.ChangeCenterHotkeyCommand = new RelayCommand(
                        () =>
                        {
                            BeginHotkeyCapture(connectionViewModel);
                            return Task.CompletedTask;
                        },
                        () => true
                    );

                    connectionViewModel.ResetTrackingCommand = new RelayCommand(
                        () =>
                        {
                            // Each property change flows through the normal apply-and-save path
                            ApplyTrackingConfigToViewModel(new TrackingConfig(), connectionViewModel);
                            connectionViewModel.StatusMessage = "Tracking settings reset to defaults";
                            return Task.CompletedTask;
                        },
                        () => true
                    );

                    if (_faceTrackingService != null)
                    {
                        await BindTrackingSensitivityAsync(connectionViewModel, _faceTrackingService, _poseRouter, configService);
                    }

                    connectionViewModel.DirectOutputStatus = _poseRouter.Status;

                    // Check for existing certificate
                    string? certPath = null;
                    string? certPassword = null;

                    if (_certificateService != null)
                    {
                        var existingCertStatus = _certificateService.GetCertificateStatus();
                        if (existingCertStatus == CertificateStatus.InstalledAndValid ||
                            existingCertStatus == CertificateStatus.GeneratedNotInstalled)
                        {
                            certPath = _certificateService.GetCertificatePath();
                            certPassword = "edsc-local-cert";
                            Debug.WriteLine($"[DesktopApp] Using existing certificate: {certPath}");
                        }
                    }

                    // Initialize HTTP command server with certificate
                    _commandServer = new HttpCommandServer(
                        _keyboardService,
                        configService,
                        _faceTrackingService,
                        _poseRouter,
                        certPath,
                        certPassword
                    );
                    Debug.WriteLine("[DesktopApp] Command server initialized");

                    _connectionViewModel = connectionViewModel;
                    WireServerEvents(_commandServer);

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

                    var shellViewModel = new DesktopShellViewModel(connectionViewModel, configService);
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

                // Cleanup (releases FreeTrack shared memory and stops the dummy TrackIR process)
                _centerHotkey?.Dispose();
                _centerHotkey = null;
                _poseRouter?.Dispose();
                _poseRouter = null;
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

        private const double TrackingScaleMin = 0.1;
        private const double TrackingScaleMax = 5.0;
        // Head movement of a few cm is a small fraction of the TrackIR axis range, so position needs far more gain
        private const double TranslationScaleMax = 25.0;
        private const double TrackingSmoothingMin = 0.0;
        private const double TrackingSmoothingMax = 0.95;

        private async Task BindTrackingSensitivityAsync(
            ConnectionViewModel viewModel,
            FaceTrackingService faceTrackingService,
            PoseOutputRouter poseRouter,
            IConfigurationService configService)
        {
            if (viewModel == null || faceTrackingService == null || poseRouter == null || configService == null)
            {
                return;
            }

            var config = await configService.LoadConfigurationAsync() ?? new AppConfig();
            if (config.Tracking == null)
            {
                config.Tracking = new TrackingConfig();
            }

            ApplyTrackingConfigToViewModel(config.Tracking, viewModel);
            ApplyTrackingConfigToService(viewModel, faceTrackingService, poseRouter);

            CancellationTokenSource? saveCts = null;

            viewModel.PropertyChanged += async (_, args) =>
            {
                if (args == null || string.IsNullOrEmpty(args.PropertyName))
                {
                    return;
                }

                if (!IsTrackingProperty(args.PropertyName))
                {
                    return;
                }

                ApplyTrackingConfigToService(viewModel, faceTrackingService, poseRouter);
                viewModel.DirectOutputStatus = poseRouter.Status;

                saveCts?.Cancel();
                var currentCts = new CancellationTokenSource();
                saveCts = currentCts;

                try
                {
                    await Task.Delay(250, currentCts.Token);
                }
                catch (TaskCanceledException)
                {
                    return;
                }

                if (currentCts.IsCancellationRequested)
                {
                    return;
                }

                // Merge into whatever is on disk now, so button edits saved elsewhere are not overwritten
                var latest = await configService.LoadConfigurationAsync() ?? config;
                UpdateTrackingConfigFromViewModel(latest, viewModel);
                latest.LastUpdatedUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                latest.LastUpdatedBy = "desktop-tracking";
                await configService.SaveConfigurationAsync(latest);
            };
        }

        private static bool IsTrackingProperty(string propertyName)
        {
            return propertyName == nameof(ConnectionViewModel.TranslationScale)
                || propertyName == nameof(ConnectionViewModel.YawScale)
                || propertyName == nameof(ConnectionViewModel.RotationScale)
                || propertyName == nameof(ConnectionViewModel.RollScale)
                || propertyName == nameof(ConnectionViewModel.SmoothingStrength)
                || propertyName == nameof(ConnectionViewModel.DirectOutputEnabled)
                || propertyName == nameof(ConnectionViewModel.ShowPcPreview)
                || propertyName == nameof(ConnectionViewModel.GazeNudge)
                || propertyName == nameof(ConnectionViewModel.CenterHotkey);
        }

        /// <summary>
        /// Turn a stored key name (OEM_PLUS, F12, NUMPAD0, or a VK_ prefixed name) into a virtual key code.
        /// </summary>
        private static int ParseHotkey(string? name)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                if (Enum.TryParse<VirtualKeyCode>(name.Trim(), true, out var direct))
                {
                    return (int)direct;
                }

                if (Enum.TryParse<VirtualKeyCode>("VK_" + name.Trim().ToUpperInvariant(), true, out var prefixed))
                {
                    return (int)prefixed;
                }
            }

            return GlobalHotkeyService.VkOemPlus;
        }

        private void BeginHotkeyCapture(ConnectionViewModel viewModel)
        {
            if (viewModel == null)
            {
                return;
            }

            if (_centerHotkey == null)
            {
                viewModel.StatusMessage = "The keyboard hook is not available, so the hotkey cannot be changed.";
                return;
            }

            if (viewModel.IsCapturingHotkey)
            {
                _centerHotkey.CancelCapture();
                viewModel.IsCapturingHotkey = false;
                return;
            }

            viewModel.IsCapturingHotkey = true;
            viewModel.StatusMessage = "Press the key you want to use for re-centring. Escape cancels.";

            _centerHotkey.CaptureNextKey(captured =>
            {
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    viewModel.IsCapturingHotkey = false;

                    if (!captured.HasValue)
                    {
                        viewModel.StatusMessage = "Hotkey change cancelled.";
                        return;
                    }

                    // Saved through the normal tracking-settings path
                    viewModel.CenterHotkey = ((VirtualKeyCode)captured.Value).ToString();
                    viewModel.StatusMessage = $"Re-centre hotkey is now '{GlobalHotkeyService.DescribeKey(captured.Value)}'.";
                });
            });
        }

        private static void ApplyTrackingConfigToViewModel(TrackingConfig config, ConnectionViewModel viewModel)
        {
            viewModel.TranslationScale = ClampTranslationScale(config.TranslationScale);
            viewModel.YawScale = ClampTrackingScale(config.YawScale);
            viewModel.RotationScale = ClampTrackingScale(config.PitchScale);
            viewModel.RollScale = ClampTrackingScale(config.RollScale);
            viewModel.SmoothingStrength = ClampTrackingSmoothing(config.SmoothingStrength);
            viewModel.GazeNudge = ClampGazeNudge(config.GazeNudge);
            viewModel.DirectOutputEnabled = config.DirectOutput;
            viewModel.ShowPcPreview = config.EffectiveShowPreview;
            viewModel.CenterHotkey = string.IsNullOrWhiteSpace(config.CenterHotkey) ? "OEM_PLUS" : config.CenterHotkey;
        }

        private void ApplyTrackingConfigToService(
            ConnectionViewModel viewModel,
            FaceTrackingService faceTrackingService,
            PoseOutputRouter poseRouter)
        {
            if (_commandServer is HttpCommandServer httpServer)
            {
                httpServer.PreviewEnabled = viewModel.ShowPcPreview;
                Debug.WriteLine($"[DesktopApp] Preview enabled applied to server: {viewModel.ShowPcPreview}");
            }

            var hotkey = ParseHotkey(viewModel.CenterHotkey);
            if (_centerHotkey != null)
            {
                _centerHotkey.VirtualKey = hotkey;
            }
            viewModel.CenterHotkeyDisplay = GlobalHotkeyService.DescribeKey(hotkey);

            if (!viewModel.ShowPcPreview && viewModel.HasMeshFrame)
            {
                viewModel.UpdateMesh(null, 0, null, preserveStatus: true);
            }

            // The tracker emits unscaled poses; all gain is applied in the router after centring
            faceTrackingService.TranslationScale = 1f;
            faceTrackingService.YawScale = 1f;
            faceTrackingService.RotationScale = 1f;
            faceTrackingService.RollScale = 1f;
            faceTrackingService.SmoothingStrength = (float)viewModel.SmoothingStrength;
            poseRouter.TranslationScale = viewModel.TranslationScale;
            poseRouter.YawScale = viewModel.YawScale;
            poseRouter.PitchScale = viewModel.RotationScale;
            poseRouter.RollScale = viewModel.RollScale;
            poseRouter.SmoothingStrength = viewModel.SmoothingStrength;
            poseRouter.GazeNudge = viewModel.GazeNudge;
            poseRouter.DirectOutputEnabled = viewModel.DirectOutputEnabled;
        }

        private static void UpdateTrackingConfigFromViewModel(AppConfig config, ConnectionViewModel viewModel)
        {
            if (config.Tracking == null)
            {
                config.Tracking = new TrackingConfig();
            }

            config.Tracking.TranslationScale = viewModel.TranslationScale;
            config.Tracking.YawScale = viewModel.YawScale;
            config.Tracking.PitchScale = viewModel.RotationScale;
            config.Tracking.RollScale = viewModel.RollScale;
            config.Tracking.SmoothingStrength = viewModel.SmoothingStrength;
            config.Tracking.GazeNudge = viewModel.GazeNudge;
            config.Tracking.DirectOutput = viewModel.DirectOutputEnabled;
            config.Tracking.ShowPreview = viewModel.ShowPcPreview;
            config.Tracking.PreviewMode = viewModel.ShowPcPreview ? PreviewMode.LandmarksOnly : PreviewMode.Off;
            config.Tracking.CenterHotkey = viewModel.CenterHotkey;
        }

        private static double ClampTranslationScale(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
            {
                return 1.0;
            }

            return Math.Clamp(value, TrackingScaleMin, TranslationScaleMax);
        }

        private static double ClampTrackingScale(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
            {
                return 1.0;
            }

            return Math.Clamp(value, TrackingScaleMin, TrackingScaleMax);
        }

        private static double ClampGazeNudge(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
            {
                return 0.0;
            }

            return Math.Clamp(value, 0.0, 1.0);
        }

        /// <summary>
        /// Snapshot of the router's latest directions for the preview inset, or null before any output.
        /// </summary>
        private GazeIndicator? BuildGazeIndicator()
        {
            var router = _poseRouter;
            if (router == null)
            {
                return null;
            }

            var output = router.LastOutput;
            if (!output.Valid)
            {
                return null;
            }

            return new GazeIndicator
            {
                HeadYaw = output.Yaw,
                HeadPitch = output.Pitch,
                HasGaze = output.HasGaze,
                GazeYaw = output.GazeYaw,
                GazePitch = output.GazePitch,
                NudgeYaw = output.NudgeYaw,
                NudgePitch = output.NudgePitch
            };
        }

        private static double ClampTrackingSmoothing(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
            {
                return 0.0;
            }

            return Math.Clamp(value, TrackingSmoothingMin, TrackingSmoothingMax);
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

        // Outline topology for the 66-point landmark model: the iBUG 68-point layout minus the two
        // inner mouth corners, so points 0-59 are the standard ones and the inner lip is 60-65.
        private static readonly (FaceMeshStyle Style, int[] Path, bool Closed)[] LandmarkPaths = new[]
        {
            (FaceMeshStyle.Outline, Enumerable.Range(0, 17).ToArray(), false),   // jaw
            (FaceMeshStyle.Eyes, Enumerable.Range(17, 5).ToArray(), false),     // right brow
            (FaceMeshStyle.Eyes, Enumerable.Range(22, 5).ToArray(), false),     // left brow
            (FaceMeshStyle.Nose, Enumerable.Range(27, 4).ToArray(), false),     // nose bridge
            (FaceMeshStyle.Nose, Enumerable.Range(31, 5).ToArray(), false),     // nose base
            (FaceMeshStyle.Eyes, Enumerable.Range(36, 6).ToArray(), true),      // right eye
            (FaceMeshStyle.Eyes, Enumerable.Range(42, 6).ToArray(), true),      // left eye
            (FaceMeshStyle.Lips, Enumerable.Range(48, 12).ToArray(), true),     // outer lip
            (FaceMeshStyle.Lips, Enumerable.Range(60, 6).ToArray(), true)       // inner lip
        };

        private const int OutlineLandmarkCount = 66;

        /// <summary>
        /// Build the preview mesh for one PC-tracked camera frame: the face box plus the landmark
        /// outline, normalised to the frame size. Only the JPEG header is read for the size; the
        /// pixels are never decoded here, the tracker already did that on its own thread.
        /// </summary>
        private static FaceMeshFrame? BuildMeshFromPose(byte[] frameData, HeadPose? pose)
        {
            if (frameData == null || frameData.Length == 0)
            {
                return null;
            }

            if (pose == null)
            {
                return null;
            }

            int width = 640;
            int height = 480;
            try
            {
                var info = SixLabors.ImageSharp.Image.Identify(frameData);
                if (info != null && info.Width > 0 && info.Height > 0)
                {
                    width = info.Width;
                    height = info.Height;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DesktopApp] BuildMeshFromPose: could not read frame size, assuming {width}x{height}: {ex.Message}");
            }

            var groups = new List<FaceMeshGroup>();
            var sx = 1f / width;
            var sy = 1f / height;

            var box = pose.FaceBox;
            if (box != null)
            {
                var x0 = box.X * sx;
                var y0 = box.Y * sy;
                var x1 = (box.X + box.Width) * sx;
                var y1 = (box.Y + box.Height) * sy;
                groups.Add(new FaceMeshGroup(FaceMeshStyle.FaceBox, 2f, new[]
                {
                    x0, y0, x1, y0,
                    x1, y0, x1, y1,
                    x1, y1, x0, y1,
                    x0, y1, x0, y0
                }));
            }

            var landmarks = pose.Landmarks;
            if (landmarks != null && landmarks.Length >= OutlineLandmarkCount)
            {
                foreach (var (style, path, closed) in LandmarkPaths)
                {
                    var segmentCount = closed ? path.Length : path.Length - 1;
                    var segments = new float[segmentCount * 4];
                    for (int i = 0; i < segmentCount; i++)
                    {
                        var a = landmarks[path[i]];
                        var b = landmarks[path[(i + 1) % path.Length]];
                        segments[i * 4] = a.X * sx;
                        segments[i * 4 + 1] = a.Y * sy;
                        segments[i * 4 + 2] = b.X * sx;
                        segments[i * 4 + 3] = b.Y * sy;
                    }
                    groups.Add(new FaceMeshGroup(style, 1.5f, segments));
                }
            }
            else if (landmarks != null && landmarks.Length > 0)
            {
                // Unknown layout: tiny crosses at each point so something still shows
                Debug.WriteLine($"[DesktopApp] BuildMeshFromPose: {landmarks.Length} landmarks, drawing points only");
                var segments = new float[landmarks.Length * 8];
                const float Half = 0.004f;
                for (int i = 0; i < landmarks.Length; i++)
                {
                    var x = landmarks[i].X * sx;
                    var y = landmarks[i].Y * sy;
                    segments[i * 8] = x - Half;
                    segments[i * 8 + 1] = y;
                    segments[i * 8 + 2] = x + Half;
                    segments[i * 8 + 3] = y;
                    segments[i * 8 + 4] = x;
                    segments[i * 8 + 5] = y - Half;
                    segments[i * 8 + 6] = x;
                    segments[i * 8 + 7] = y + Half;
                }
                groups.Add(new FaceMeshGroup(FaceMeshStyle.Lips, 1.5f, segments));
            }

            if (groups.Count == 0)
            {
                return null;
            }

            return new FaceMeshFrame(width, height, groups.ToArray());
        }

        private async Task<ServerConfig?> LoadConfigurationAsync()
        {
            Debug.WriteLine("[DesktopApp] Entry: LoadConfigurationAsync");

            try
            {
                // Same location the configuration service uses (AppData, migrated from the install folder)
                var configPath = new JsonConfigurationService().GetConfigurationPath();

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

                // The file is the full AppConfig; the server settings live under its "server" section
                var config = JsonSerializer.Deserialize<AppConfig>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (config?.Server == null)
                {
                    Debug.WriteLine("[DesktopApp] Failed to deserialize config");
                    return new ServerConfig();
                }

                Debug.WriteLine($"[DesktopApp] Configuration loaded successfully (port {config.Server.Port})");
                return config.Server;
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

        private async Task InstallCertificateAsync(ConnectionViewModel viewModel, string[] localIpAddresses)
        {
            Debug.WriteLine("[DesktopApp] Entry: InstallCertificateAsync");

            if (viewModel == null)
            {
                Debug.WriteLine("[DesktopApp] ViewModel is null");
                Debug.WriteLine("[DesktopApp] Exit: InstallCertificateAsync");
                return;
            }

            if (_certificateService == null)
            {
                Debug.WriteLine("[DesktopApp] CertificateService is null");
                viewModel.StatusMessage = "Certificate service not initialized";
                Debug.WriteLine("[DesktopApp] Exit: InstallCertificateAsync");
                return;
            }

            if (localIpAddresses == null || localIpAddresses.Length == 0)
            {
                Debug.WriteLine("[DesktopApp] localIpAddresses is null or empty");
                localIpAddresses = new[] { "127.0.0.1", "localhost" };
            }

            try
            {
                // Check if we're running as admin
                var isAdmin = _certificateService.IsRunningAsAdmin();
                Debug.WriteLine($"[DesktopApp] Running as admin: {isAdmin}");

                if (!isAdmin)
                {
                    Debug.WriteLine("[DesktopApp] Not running as admin, requesting elevation");

                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        viewModel.StatusMessage = "Requesting administrator privileges...";
                        viewModel.CertificateStatus = "Requesting elevation...";
                    });

                    // Request elevation - this will show UAC prompt
                    var elevated = await Task.Run(() => _certificateService.RequestElevatedInstallation(localIpAddresses));

                    if (!elevated)
                    {
                        Debug.WriteLine("[DesktopApp] Elevation request failed or cancelled");
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            viewModel.StatusMessage = "Administrator privileges required. Please accept UAC prompt.";
                            viewModel.CertificateStatus = "Not installed - elevation required";
                        });
                        Debug.WriteLine("[DesktopApp] Exit: InstallCertificateAsync");
                        return;
                    }

                    Debug.WriteLine("[DesktopApp] Elevated process completed, checking status");

                    // Check if certificate was installed
                    var status = _certificateService.GetCertificateStatus();
                    var statusText = GetCertificateStatusText(status);

                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        viewModel.CertificateStatus = statusText;

                        if (status == CertificateStatus.InstalledAndValid)
                        {
                            viewModel.StatusMessage = "SSL certificate installed successfully! Restarting server...";
                        }
                        else
                        {
                            viewModel.StatusMessage = "Certificate installation may have failed. Check status.";
                        }
                    });

                    // Restart server with new certificate if successful
                    if (status == CertificateStatus.InstalledAndValid || status == CertificateStatus.GeneratedNotInstalled)
                    {
                        var certPath = _certificateService.GetCertificatePath();
                        await RestartServerWithCertificateAsync(certPath);

                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            viewModel.StatusMessage = "SSL certificate active - HTTPS connections secured";
                        });
                    }

                    Debug.WriteLine("[DesktopApp] Exit: InstallCertificateAsync");
                    return;
                }

                // If we're already running as admin, install directly
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    viewModel.StatusMessage = "Generating SSL certificate...";
                    viewModel.CertificateStatus = "Generating...";
                });

                Debug.WriteLine("[DesktopApp] Generating certificate (already elevated)");

                var result = await _certificateService.GenerateAndInstallCertificateAsync(localIpAddresses);

                if (result == null)
                {
                    Debug.WriteLine("[DesktopApp] Certificate generation returned null result");
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        viewModel.StatusMessage = "Certificate generation failed";
                        viewModel.CertificateStatus = "Error";
                    });
                    Debug.WriteLine("[DesktopApp] Exit: InstallCertificateAsync");
                    return;
                }

                if (!result.Success)
                {
                    Debug.WriteLine($"[DesktopApp] Certificate installation failed: {result.Message}");

                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        viewModel.StatusMessage = $"Certificate installation failed: {result.Message}";
                        viewModel.CertificateStatus = "Error";
                    });

                    Debug.WriteLine("[DesktopApp] Exit: InstallCertificateAsync");
                    return;
                }

                Debug.WriteLine("[DesktopApp] Certificate installed successfully");

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    viewModel.StatusMessage = "SSL certificate installed successfully! Restarting server...";
                    viewModel.CertificateStatus = "Installed and Active";
                });

                // Restart server with new certificate
                await RestartServerWithCertificateAsync(result.CertificatePath);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    viewModel.StatusMessage = "SSL certificate active - HTTPS connections secured";
                });

                Debug.WriteLine("[DesktopApp] Certificate installation complete");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DesktopApp] Error installing certificate: {ex.Message}");
                Debug.WriteLine($"[DesktopApp] Stack trace: {ex.StackTrace}");

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    viewModel.StatusMessage = $"Error: {ex.Message}";
                    viewModel.CertificateStatus = "Error";
                });
            }

            Debug.WriteLine("[DesktopApp] Exit: InstallCertificateAsync");
        }

        private async Task RestartServerWithCertificateAsync(string certPath)
        {
            Debug.WriteLine("[DesktopApp] Entry: RestartServerWithCertificateAsync");

            if (string.IsNullOrEmpty(certPath))
            {
                Debug.WriteLine("[DesktopApp] Certificate path is null or empty");
                Debug.WriteLine("[DesktopApp] Exit: RestartServerWithCertificateAsync");
                return;
            }

            if (_commandServer == null)
            {
                Debug.WriteLine("[DesktopApp] Command server is null");
                Debug.WriteLine("[DesktopApp] Exit: RestartServerWithCertificateAsync");
                return;
            }

            if (_keyboardService == null)
            {
                Debug.WriteLine("[DesktopApp] Keyboard service is null");
                Debug.WriteLine("[DesktopApp] Exit: RestartServerWithCertificateAsync");
                return;
            }

            if (_serverConfig == null)
            {
                Debug.WriteLine("[DesktopApp] Server config is null");
                Debug.WriteLine("[DesktopApp] Exit: RestartServerWithCertificateAsync");
                return;
            }

            try
            {
                Debug.WriteLine("[DesktopApp] Stopping existing server");

                // Stop existing server
                if (_commandServer.IsRunning)
                {
                    await _commandServer.StopAsync();
                }

                Debug.WriteLine("[DesktopApp] Creating new server with certificate");

                // Create new server with certificate
                var configService = new JsonConfigurationService();
                _commandServer = new HttpCommandServer(
                    _keyboardService,
                    configService,
                    _faceTrackingService,
                    _poseRouter,
                    certPath,
                    "edsc-local-cert"
                );

                // The new instance has no subscribers yet; without this the preview goes dark after a cert install
                WireServerEvents(_commandServer);

                // Bind to the IP shown in the QR code, not merely the first one found
                var selectedIp = _connectionViewModel?.SelectedLocalIpAddress;
                if (string.IsNullOrEmpty(selectedIp))
                {
                    selectedIp = GetLocalIpAddresses().FirstOrDefault() ?? "127.0.0.1";
                }

                Debug.WriteLine($"[DesktopApp] Starting server on {selectedIp}:{_serverConfig.Port}");
                await _commandServer.StartAsync(_serverConfig.Port, selectedIp);

                Debug.WriteLine("[DesktopApp] Server restarted successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DesktopApp] Error restarting server: {ex.Message}");
                Debug.WriteLine($"[DesktopApp] Stack trace: {ex.StackTrace}");
            }

            Debug.WriteLine("[DesktopApp] Exit: RestartServerWithCertificateAsync");
        }

        /// <summary>
        /// Subscribe the desktop preview to a command server's frame and pose events.
        /// Must be called for every server instance, including ones created on restart.
        /// </summary>
        private void WireServerEvents(ICommandServer? server)
        {
            if (server is not HttpCommandServer httpServer)
            {
                Debug.WriteLine("[DesktopApp] WireServerEvents: server is not an HttpCommandServer, nothing to wire");
                return;
            }

            httpServer.PreviewEnabled = _connectionViewModel?.ShowPcPreview ?? true;

            httpServer.PoseDetected += (sender, pose) =>
            {
                _lastPose = pose;
            };

            // Clear the overlay when tracking drops out so stale landmarks are not drawn over live video
            httpServer.PoseLost += (sender, args) =>
            {
                _lastPose = null;
            };

            // Phone-side tracking sends its mesh as line segments; hand the newest one to the panel.
            // Frames arrive at camera rate, so keep only the latest and post one UI update at a time:
            // if the UI thread is busy the queue never grows, it just skips to the freshest mesh.
            httpServer.PhoneMeshReceived += (sender, frame) =>
            {
                var viewModel = _connectionViewModel;
                if (viewModel == null || !viewModel.ShowPcPreview || frame == null)
                {
                    return;
                }

                Interlocked.Exchange(ref _pendingMesh, frame);
                if (Interlocked.Exchange(ref _meshUpdateQueued, 1) == 1)
                {
                    return;
                }

                Dispatcher.UIThread.Post(() =>
                {
                    Interlocked.Exchange(ref _meshUpdateQueued, 0);
                    var latest = Interlocked.Exchange(ref _pendingMesh, null);
                    if (latest == null)
                    {
                        return;
                    }

                    try
                    {
                        var vm = _connectionViewModel;
                        if (vm == null || !vm.ShowPcPreview)
                        {
                            return;
                        }

                        vm.UpdateMesh(latest, 0, null, preserveStatus: true);
                        vm.GazeIndicator = BuildGazeIndicator();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[DesktopApp] Error showing phone mesh: {ex.Message}");
                    }
                });
            };

            // Phone-side tracking sends poses; show the numbers on the status line
            httpServer.PhonePoseReceived += (sender, pose) =>
            {
                var now = DateTime.UtcNow;
                if (pose != null && (now - _lastPhoneStatusUpdate).TotalMilliseconds < 100)
                {
                    return;
                }
                _lastPhoneStatusUpdate = now;

                var rate = httpServer.GetPhonePoseRate();
                var text = pose == null
                    ? "Phone tracking: no face"
                    : $"Phone tracking  yaw {pose.Yaw,6:F1}°  pitch {pose.Pitch,6:F1}°  roll {pose.Roll,6:F1}°\n" +
                      $"x {pose.X,6:F1} cm  y {pose.Y,6:F1} cm  z {pose.Z,6:F1} cm";

                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    try
                    {
                        var viewModel = _connectionViewModel;
                        if (viewModel == null)
                        {
                            return;
                        }

                        viewModel.UpdatePhoneTracking(text, rate);

                        if (pose == null && viewModel.HasMeshFrame)
                        {
                            // Face lost: the phone stops sending meshes, so clear the stale one
                            viewModel.UpdateMesh(null, 0, null, preserveStatus: true);
                            viewModel.GazeIndicator = null;
                        }

                        if (_poseRouter != null)
                        {
                            viewModel.DirectOutputStatus = _poseRouter.Status;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[DesktopApp] Error updating phone tracking status: {ex.Message}");
                    }
                });
            };

            httpServer.FrameReceived += (sender, frameData) =>
            {
                try
                {
                    // Convert byte array to Bitmap on UI thread with overlays
                    Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        try
                        {
                            var viewModel = _connectionViewModel;
                            if (viewModel == null)
                            {
                                return;
                            }

                            if (!viewModel.ShowPcPreview)
                            {
                                // Keep the status line alive without touching the frame
                                viewModel.UpdateMesh(null, 0, null, preserveStatus: true);
                                viewModel.ShowVideoPreview = true;
                                viewModel.VideoStatusText = _faceTrackingService?.LastStatus ?? "Tracking (preview off)";
                                viewModel.VideoFps = httpServer.GetCurrentFps().ToString("F1");
                                return;
                            }

                            var mesh = BuildMeshFromPose(frameData, _lastPose);
                            var fps = httpServer.GetCurrentFps();
                            if (mesh == null)
                            {
                                // No face this frame: show an empty panel but keep the status and rate ticking
                                viewModel.UpdateMesh(null, 0, null, preserveStatus: true);
                                viewModel.ShowVideoPreview = true;
                                viewModel.VideoStatusText = _faceTrackingService?.LastStatus ?? "Tracking";
                                viewModel.VideoFps = fps.ToString("F1");
                                return;
                            }

                            viewModel.UpdateMesh(mesh, fps, _faceTrackingService?.LastStatus);
                            viewModel.GazeIndicator = BuildGazeIndicator();

                            if (_poseRouter != null)
                            {
                                viewModel.DirectOutputStatus = _poseRouter.Status;
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

        private static string GetCertificateStatusText(CertificateStatus status)
        {
            Debug.WriteLine("[DesktopApp] Entry: GetCertificateStatusText");

            string result;

            switch (status)
            {
                case CertificateStatus.NotGenerated:
                    result = "Not installed - click to install";
                    break;
                case CertificateStatus.GeneratedNotInstalled:
                    result = "Generated but not installed in system";
                    break;
                case CertificateStatus.InstalledAndValid:
                    result = "Installed and active";
                    break;
                case CertificateStatus.InstalledButExpired:
                    result = "Expired - needs renewal";
                    break;
                case CertificateStatus.Error:
                    result = "Error checking status";
                    break;
                default:
                    result = "Unknown status";
                    break;
            }

            Debug.WriteLine($"[DesktopApp] Status text: {result}");
            Debug.WriteLine("[DesktopApp] Exit: GetCertificateStatusText");

            return result;
        }

        private async Task HandleElevatedCertificateInstallationAsync(string[] args)
        {
            Debug.WriteLine("[DesktopApp] Entry: HandleElevatedCertificateInstallationAsync");

            if (args == null || args.Length < 2)
            {
                Debug.WriteLine("[DesktopApp] No IP addresses provided in arguments");
                Debug.WriteLine("[DesktopApp] Exit: HandleElevatedCertificateInstallationAsync");
                return;
            }

            try
            {
                // Extract IP addresses from arguments (skip the first argument which is "--install-certificate")
                var localIpAddresses = args.Skip(1).ToArray();
                Debug.WriteLine($"[DesktopApp] IP addresses for certificate: {string.Join(", ", localIpAddresses)}");

                _certificateService = new CertificateService();

                Debug.WriteLine("[DesktopApp] Generating and installing certificate");
                var result = await _certificateService.GenerateAndInstallCertificateAsync(localIpAddresses);

                if (result == null)
                {
                    Debug.WriteLine("[DesktopApp] Certificate generation returned null");
                    Debug.WriteLine("[DesktopApp] Exit: HandleElevatedCertificateInstallationAsync");
                    return;
                }

                if (result.Success)
                {
                    Debug.WriteLine($"[DesktopApp] Certificate installed successfully: {result.Message}");
                }
                else
                {
                    Debug.WriteLine($"[DesktopApp] Certificate installation failed: {result.Message}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DesktopApp] Error in elevated certificate installation: {ex.Message}");
                Debug.WriteLine($"[DesktopApp] Stack trace: {ex.StackTrace}");
            }

            Debug.WriteLine("[DesktopApp] Exit: HandleElevatedCertificateInstallationAsync");
        }

        private static void OpenUrlInBrowser(string url)
        {
            Debug.WriteLine("[DesktopApp] Entry: OpenUrlInBrowser");

            if (string.IsNullOrEmpty(url))
            {
                Debug.WriteLine("[DesktopApp] URL is null or empty");
                Debug.WriteLine("[DesktopApp] Exit: OpenUrlInBrowser");
                return;
            }

            try
            {
                Debug.WriteLine($"[DesktopApp] Opening URL: {url}");

                var startInfo = new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                };

                Process.Start(startInfo);

                Debug.WriteLine("[DesktopApp] URL opened successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DesktopApp] Error opening URL: {ex.Message}");
                Debug.WriteLine($"[DesktopApp] Stack trace: {ex.StackTrace}");
            }

            Debug.WriteLine("[DesktopApp] Exit: OpenUrlInBrowser");
        }
    }

    /// <summary>
    /// Simple relay command for parameterless async actions
    /// </summary>
    public class RelayCommand : System.Windows.Input.ICommand
    {
        private readonly Func<Task> _executeAsync;
        private readonly Func<bool> _canExecute;

        public event EventHandler? CanExecuteChanged;

        public RelayCommand(Func<Task> executeAsync, Func<bool> canExecute)
        {
            if (executeAsync == null)
            {
                throw new ArgumentNullException(nameof(executeAsync));
            }

            _executeAsync = executeAsync;
            _canExecute = canExecute ?? (() => true);
        }

        public bool CanExecute(object? parameter)
        {
            if (_canExecute == null)
            {
                return true;
            }

            return _canExecute();
        }

        public async void Execute(object? parameter)
        {
            if (_executeAsync == null)
            {
                return;
            }

            await _executeAsync();
        }

        public void RaiseCanExecuteChanged()
        {
            if (CanExecuteChanged == null)
            {
                return;
            }

            CanExecuteChanged.Invoke(this, EventArgs.Empty);
        }
    }
}
