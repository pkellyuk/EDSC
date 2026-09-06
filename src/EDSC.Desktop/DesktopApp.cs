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
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing;
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
                || propertyName == nameof(ConnectionViewModel.PreviewMode)
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
            viewModel.DirectOutputEnabled = config.DirectOutput;
            viewModel.PreviewMode = config.EffectivePreviewMode;
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
                httpServer.PreviewMode = ToPhonePreviewMode(viewModel.PreviewMode);
                Debug.WriteLine($"[DesktopApp] Preview mode applied to server: {viewModel.PreviewMode}");
            }

            var hotkey = ParseHotkey(viewModel.CenterHotkey);
            if (_centerHotkey != null)
            {
                _centerHotkey.VirtualKey = hotkey;
            }
            viewModel.CenterHotkeyDisplay = GlobalHotkeyService.DescribeKey(hotkey);

            if (!viewModel.ShowPcPreview && viewModel.HasVideoFrame)
            {
                viewModel.UpdateVideoFrame(null, 0, null, preserveStatus: true);
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
            config.Tracking.DirectOutput = viewModel.DirectOutputEnabled;
            config.Tracking.ShowPreview = viewModel.ShowPcPreview;
            config.Tracking.PreviewMode = viewModel.PreviewMode;
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

        /// <summary>
        /// The preview mode name the phone script understands (see the version poll in HttpCommandServer).
        /// </summary>
        private static string ToPhonePreviewMode(PreviewMode mode)
        {
            switch (mode)
            {
                case PreviewMode.Camera:
                    return "camera";
                case PreviewMode.LandmarksOnly:
                    return "landmarksOnly";
                default:
                    return "cameraWithLandmarks";
            }
        }

        // Outline topology for the 66-point landmark model: the iBUG 68-point layout minus the two
        // inner mouth corners, so points 0-59 are the standard ones and the inner lip is 60-65.
        private static readonly int[][] OpenLandmarkPaths = new[]
        {
            Enumerable.Range(0, 17).ToArray(),   // jaw
            Enumerable.Range(17, 5).ToArray(),   // right brow
            Enumerable.Range(22, 5).ToArray(),   // left brow
            Enumerable.Range(27, 4).ToArray(),   // nose bridge
            Enumerable.Range(31, 5).ToArray()    // nose base
        };

        private static readonly int[][] ClosedLandmarkPaths = new[]
        {
            Enumerable.Range(36, 6).ToArray(),   // right eye
            Enumerable.Range(42, 6).ToArray(),   // left eye
            Enumerable.Range(48, 12).ToArray(),  // outer lip
            Enumerable.Range(60, 6).ToArray()    // inner lip
        };

        private const int OutlineLandmarkCount = 66;

        /// <summary>
        /// Build the preview panel image for one camera frame according to the selected preview mode.
        /// Camera: the frame as-is. CameraWithLandmarks: the frame with the face box and mesh drawn over it.
        /// LandmarksOnly: the face box and mesh on a black canvas the same size as the frame.
        /// </summary>
        private static Bitmap BuildPreviewBitmap(byte[] frameData, HeadPose? pose, PreviewMode mode)
        {
            if (frameData == null || frameData.Length == 0)
            {
                throw new ArgumentException("Frame data is empty", nameof(frameData));
            }

            var haveFace = pose != null && pose.FaceBox != null;

            if (mode == PreviewMode.LandmarksOnly)
            {
                return DrawLandmarksOnly(frameData, haveFace ? pose : null);
            }

            if (mode == PreviewMode.CameraWithLandmarks && haveFace)
            {
                return DrawOverlays(frameData, pose!);
            }

            using (var ms = new MemoryStream(frameData))
            {
                return new Bitmap(ms);
            }
        }

        private static Bitmap DrawOverlays(byte[] frameData, HeadPose pose)
        {
            if (frameData == null || pose == null)
            {
                throw new ArgumentNullException(frameData == null ? nameof(frameData) : nameof(pose));
            }

            try
            {
                using (var image = SixLabors.ImageSharp.Image.Load<Rgb24>(frameData))
                {
                    image.Mutate(ctx => DrawFaceOverlay(ctx, pose));
                    return ToAvaloniaBitmap(image);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DesktopApp] Error drawing overlays: {ex.Message}");
                // Return original frame if overlay fails
                using (var ms = new MemoryStream(frameData))
                {
                    return new Bitmap(ms);
                }
            }
        }

        /// <summary>
        /// Draw the face box and mesh on a black canvas sized like the frame, without decoding the frame's pixels.
        /// </summary>
        private static Bitmap DrawLandmarksOnly(byte[] frameData, HeadPose? pose)
        {
            if (frameData == null)
            {
                throw new ArgumentNullException(nameof(frameData));
            }

            // Identify reads only the header, so this is far cheaper than decoding the JPEG
            var info = SixLabors.ImageSharp.Image.Identify(frameData);
            var width = info?.Width ?? 640;
            var height = info?.Height ?? 480;

            using (var image = new Image<Rgb24>(width, height, new Rgb24(0, 0, 0)))
            {
                if (pose != null)
                {
                    image.Mutate(ctx => DrawFaceOverlay(ctx, pose));
                }

                return ToAvaloniaBitmap(image);
            }
        }

        private static Bitmap ToAvaloniaBitmap(Image<Rgb24> image)
        {
            if (image == null)
            {
                throw new ArgumentNullException(nameof(image));
            }

            using (var ms = new MemoryStream())
            {
                image.SaveAsJpeg(ms);
                ms.Position = 0;
                return new Bitmap(ms);
            }
        }

        /// <summary>
        /// Draw the face box (green) and the landmark outline (cyan lines, red points).
        /// Falls back to plain dots when the landmark set is not the 66-point layout.
        /// </summary>
        private static void DrawFaceOverlay(IImageProcessingContext ctx, HeadPose pose)
        {
            if (ctx == null || pose == null)
            {
                return;
            }

            if (pose.FaceBox != null)
            {
                var faceBox = pose.FaceBox;
                var rect = new RectangleF(faceBox.X, faceBox.Y, faceBox.Width, faceBox.Height);
                ctx.Draw(SixLabors.ImageSharp.Color.Lime, 2f, rect);
            }

            var landmarks = pose.Landmarks;
            if (landmarks == null || landmarks.Length == 0)
            {
                return;
            }

            if (landmarks.Length < OutlineLandmarkCount)
            {
                Debug.WriteLine($"[DesktopApp] DrawFaceOverlay: {landmarks.Length} landmarks, drawing dots only");
                foreach (var landmark in landmarks)
                {
                    ctx.Fill(SixLabors.ImageSharp.Color.Red, new EllipsePolygon(new PointF(landmark.X, landmark.Y), 3f));
                }
                return;
            }

            var outline = SixLabors.ImageSharp.Color.Cyan;
            foreach (var path in OpenLandmarkPaths)
            {
                ctx.DrawLine(outline, 1.5f, ToPoints(landmarks, path));
            }

            foreach (var path in ClosedLandmarkPaths)
            {
                ctx.DrawPolygon(outline, 1.5f, ToPoints(landmarks, path));
            }

            foreach (var landmark in landmarks)
            {
                ctx.Fill(SixLabors.ImageSharp.Color.Red, new EllipsePolygon(new PointF(landmark.X, landmark.Y), 2f));
            }
        }

        private static PointF[] ToPoints(LandmarkPoint[] landmarks, int[] indices)
        {
            var points = new PointF[indices.Length];
            for (int i = 0; i < indices.Length; i++)
            {
                var lm = landmarks[indices[i]];
                points[i] = new PointF(lm.X, lm.Y);
            }
            return points;
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
            httpServer.PreviewMode = ToPhonePreviewMode(_connectionViewModel?.PreviewMode ?? PreviewMode.CameraWithLandmarks);

            httpServer.PoseDetected += (sender, pose) =>
            {
                _lastPose = pose;
            };

            // Clear the overlay when tracking drops out so stale landmarks are not drawn over live video
            httpServer.PoseLost += (sender, args) =>
            {
                _lastPose = null;
            };

            // Phone-side tracking sends a small preview with its mesh already drawn; show it as-is
            httpServer.PreviewFrameReceived += (sender, frameData) =>
            {
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    try
                    {
                        var viewModel = _connectionViewModel;
                        if (viewModel == null || !viewModel.ShowPcPreview)
                        {
                            return;
                        }

                        Bitmap bitmap;
                        using (var ms = new MemoryStream(frameData))
                        {
                            bitmap = new Bitmap(ms);
                        }

                        viewModel.UpdateVideoFrame(bitmap, 0, null, preserveStatus: true);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[DesktopApp] Error showing phone preview: {ex.Message}");
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
                                // Keep the status line alive without decoding the frame
                                viewModel.UpdateVideoFrame(null, 0, null, preserveStatus: true);
                                viewModel.ShowVideoPreview = true;
                                viewModel.VideoStatusText = _faceTrackingService?.LastStatus ?? "Tracking (preview off)";
                                viewModel.VideoFps = httpServer.GetCurrentFps().ToString("F1");
                                return;
                            }

                            var bitmap = BuildPreviewBitmap(frameData, _lastPose, viewModel.PreviewMode);

                            var fps = httpServer.GetCurrentFps();
                            viewModel.UpdateVideoFrame(bitmap, fps, _faceTrackingService?.LastStatus);

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
