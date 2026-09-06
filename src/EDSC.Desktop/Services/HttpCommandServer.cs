using EDSC.Models;
using EDSC.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
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
        private readonly IConfigurationService _configService;
        private readonly object _configLock = new object();
        private AppConfig? _currentConfig;

        // Changes on every server start; the page compares it and reloads itself to pick up new scripts
        private static readonly string PageStamp = Guid.NewGuid().ToString("N");

        // Video streaming state
        private const int MaxFrameBytes = 4 * 1024 * 1024;
        private byte[]? _latestFrame;
        private readonly object _frameLock = new object();
        private DateTime _lastFrameTime = DateTime.MinValue;
        private int _frameCount = 0;
        private double _currentFps = 0;

        // Face tracking
        private readonly IFaceTrackingService? _faceTrackingService;
        private readonly PoseOutputRouter? _poseOutput;

        // SSL Certificate
        private readonly string? _certificatePath;
        private readonly string? _certificatePassword;

        public bool IsRunning { get; private set; }

        /// <summary>
        /// False to drop phone preview images and tell the phone to stop sending them
        /// </summary>
        public bool PreviewEnabled { get; set; } = true;


        /// <summary>
        /// Event fired when a new video frame is received
        /// </summary>
        public event EventHandler<byte[]>? FrameReceived;

        /// <summary>
        /// Event fired when head pose is detected
        /// </summary>
        public event EventHandler<HeadPose>? PoseDetected;

        /// <summary>
        /// Event fired when a frame was processed but no pose could be produced
        /// </summary>
        public event EventHandler? PoseLost;

        /// <summary>
        /// Event fired for each pose computed on the phone (null when the phone reports the face lost)
        /// </summary>
        public event EventHandler<HeadPose?>? PhonePoseReceived;

        /// <summary>
        /// Event fired when the phone sends a face mesh frame (line segments only, no camera pixels) alongside poses
        /// </summary>
        public event EventHandler<FaceMeshFrame>? PhoneMeshReceived;

        // Phone pose rate
        private int _phonePoseCount;
        private DateTime _phonePoseWindowStart = DateTime.MinValue;
        private double _phonePoseRate;

        public HttpCommandServer(
            IKeyboardService keyboardService,
            IConfigurationService configService,
            IFaceTrackingService? faceTrackingService = null,
            PoseOutputRouter? poseOutput = null,
            string? certificatePath = null,
            string? certificatePassword = null)
        {
            Debug.WriteLine("[HttpCommandServer] Entry: Constructor");

            if (keyboardService == null)
            {
                throw new ArgumentNullException(nameof(keyboardService));
            }

            if (configService == null)
            {
                throw new ArgumentNullException(nameof(configService));
            }

            _keyboardService = keyboardService;
            _configService = configService;
            _faceTrackingService = faceTrackingService;
            _poseOutput = poseOutput;
            _certificatePath = certificatePath;
            _certificatePassword = certificatePassword;

            Debug.WriteLine($"[HttpCommandServer] Face tracking enabled: {_faceTrackingService != null}");
            Debug.WriteLine($"[HttpCommandServer] Pose output enabled: {_poseOutput != null}");
            Debug.WriteLine($"[HttpCommandServer] Custom certificate: {!string.IsNullOrEmpty(_certificatePath)}");
            Debug.WriteLine("[HttpCommandServer] Exit: Constructor");
        }

        public async Task StartAsync(int port, string? bindAddress = null, CancellationToken cancellationToken = default)
        {
            Debug.WriteLine($"[HttpCommandServer] Entry: StartAsync(port={port}, bindAddress={bindAddress})");

            if (IsRunning)
            {
                Debug.WriteLine("[HttpCommandServer] Server already running");
                return;
            }

            try
            {
                _currentConfig = await LoadConfigAsync();

                Debug.WriteLine($"[HttpCommandServer] Building web host on port {port}");

                _host = Host.CreateDefaultBuilder()
                    .ConfigureWebHostDefaults(webBuilder =>
                    {
                        webBuilder.UseKestrel(options =>
                        {
                            var httpsPort = port + 1;

                            if (!string.IsNullOrEmpty(bindAddress) && System.Net.IPAddress.TryParse(bindAddress, out var ip))
                            {
                                options.Listen(ip, port);

                                try
                                {
                                    options.Listen(ip, httpsPort, listenOptions =>
                                    {
                                        if (!string.IsNullOrEmpty(_certificatePath) && File.Exists(_certificatePath))
                                        {
                                            Debug.WriteLine($"[HttpCommandServer] Using custom certificate: {_certificatePath}");
                                            listenOptions.UseHttps(_certificatePath, _certificatePassword);
                                        }
                                        else
                                        {
                                            Debug.WriteLine("[HttpCommandServer] Using default dev certificate");
                                            listenOptions.UseHttps();
                                        }
                                    });
                                    Debug.WriteLine($"[HttpCommandServer] HTTPS enabled on port {httpsPort}");
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"[HttpCommandServer] HTTPS not available: {ex.Message}");
                                }

                                if (!System.Net.IPAddress.IsLoopback(ip))
                                {
                                    options.Listen(System.Net.IPAddress.Loopback, port);

                                    try
                                    {
                                        options.Listen(System.Net.IPAddress.Loopback, httpsPort, listenOptions =>
                                        {
                                            if (!string.IsNullOrEmpty(_certificatePath) && File.Exists(_certificatePath))
                                            {
                                                listenOptions.UseHttps(_certificatePath, _certificatePassword);
                                            }
                                            else
                                            {
                                                listenOptions.UseHttps();
                                            }
                                        });
                                    }
                                    catch
                                    {
                                    }
                                }
                            }
                            else
                            {
                                options.ListenAnyIP(port);

                                try
                                {
                                    options.ListenAnyIP(httpsPort, listenOptions =>
                                    {
                                        if (!string.IsNullOrEmpty(_certificatePath) && File.Exists(_certificatePath))
                                        {
                                            Debug.WriteLine($"[HttpCommandServer] Using custom certificate: {_certificatePath}");
                                            listenOptions.UseHttps(_certificatePath, _certificatePassword);
                                        }
                                        else
                                        {
                                            Debug.WriteLine("[HttpCommandServer] Using default dev certificate");
                                            listenOptions.UseHttps();
                                        }
                                    });
                                    Debug.WriteLine($"[HttpCommandServer] HTTPS enabled on port {httpsPort}");
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"[HttpCommandServer] HTTPS not available: {ex.Message}");
                                }
                            }
                        });

                        webBuilder.Configure(app =>
                        {
                            // Enable WebSocket support
                            app.UseWebSockets();

                            app.Use(async (context, next) =>
                            {
                                if (!context.Request.IsHttps && context.Request.Host.Host != "localhost" && context.Request.Host.Host != "127.0.0.1")
                                {
                                    var httpsUrl = $"https://{context.Request.Host.Host}:{port + 1}{context.Request.Path}{context.Request.QueryString}";
                                    Debug.WriteLine($"[HttpCommandServer] Redirecting HTTP to HTTPS: {httpsUrl}");
                                    context.Response.StatusCode = 301;
                                    context.Response.Headers["Location"] = httpsUrl;
                                    return;
                                }
                                await next();
                            });

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
                                            version = typeof(HttpCommandServer).Assembly.GetName().Version?.ToString(3) ?? "0.0.0"
                                        });
                                    await context.Response.WriteAsync(healthJson);
                                }
                                else if (context.Request.Path == "/config" && context.Request.Method == "GET")
                                {
                                    await HandleConfigGet(context);
                                }
                                else if (context.Request.Path == "/config/version" && context.Request.Method == "GET")
                                {
                                    await HandleConfigVersionGet(context);
                                }
                                else if (context.Request.Path == "/web" && context.Request.Method == "GET")
                                {
                                    await HandleWebUiRequest(context);
                                }
                                else if (context.Request.Path.StartsWithSegments("/assets/icons", out var remaining) && context.Request.Method == "GET")
                                {
                                    await HandleIconRequest(context, remaining.Value);
                                }
                                else if (context.Request.Path == "/command" && context.Request.Method == "POST")
                                {
                                    await HandleCommandRequest(context);
                                }
                                else if (context.Request.Path == "/tracking/center" && context.Request.Method == "POST")
                                {
                                    if (_poseOutput == null || !_poseOutput.DirectOutputEnabled)
                                    {
                                        await SendResponse(context, false, "Enable 'Send directly to game' on the PC to recentre here. Opentrack uses its own centring control.");
                                    }
                                    else
                                    {
                                        _poseOutput.Center();
                                        await SendResponse(context, true, "Recentre requested. Look straight ahead and hold still for 4 seconds.");
                                    }
                                }
                                else if (context.Request.Path == "/video" && context.WebSockets.IsWebSocketRequest)
                                {
                                    await HandleVideoWebSocket(context);
                                }
                                else if (context.Request.Path == "/pose" && context.WebSockets.IsWebSocketRequest)
                                {
                                    await HandlePoseWebSocket(context);
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

                Debug.WriteLine($"[HttpCommandServer] Server started - HTTP: {port}, HTTPS: {port + 1}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HttpCommandServer] Error starting server: {ex.Message}");
                throw;
            }

            Debug.WriteLine("[HttpCommandServer] Exit: StartAsync");
        }

        private async Task<AppConfig> LoadConfigAsync()
        {
            Debug.WriteLine("[HttpCommandServer] Entry: LoadConfigAsync");

            var config = await _configService.LoadConfigurationAsync();

            if (config == null)
            {
                Debug.WriteLine("[HttpCommandServer] Config service returned null, using defaults");
                config = new AppConfig();
            }

            EnsureConfigMetadata(config, "server");

            Debug.WriteLine("[HttpCommandServer] Exit: LoadConfigAsync");
            return config;
        }

        private static void EnsureConfigMetadata(AppConfig config, string updatedBy)
        {
            if (config.ConfigVersion <= 0)
            {
                config.ConfigVersion = 1;
            }

            if (config.LastUpdatedUtc <= 0)
            {
                config.LastUpdatedUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            }

            if (string.IsNullOrEmpty(config.LastUpdatedBy))
            {
                config.LastUpdatedBy = updatedBy ?? string.Empty;
            }
        }

        private async Task HandleConfigGet(HttpContext context)
        {
            Debug.WriteLine("[HttpCommandServer] Entry: HandleConfigGet");

            if (context == null)
            {
                Debug.WriteLine("[HttpCommandServer] Context is null");
                return;
            }

            // Re-read from disk so edits made in the desktop editor are served on the next reload
            AppConfig configToReturn;
            try
            {
                var fresh = await _configService.LoadConfigurationAsync();
                lock (_configLock)
                {
                    if (fresh != null)
                    {
                        _currentConfig = fresh;
                    }
                    configToReturn = _currentConfig ?? new AppConfig();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HttpCommandServer] Config reload failed, serving cached copy: {ex.Message}");
                lock (_configLock)
                {
                    configToReturn = _currentConfig ?? new AppConfig();
                }
            }

            // The page only ever sees the active game's layout, plus enough to brand itself
            var payload = new
            {
                game = GameIds.Normalize(configToReturn.ActiveGame),
                gameName = GameIds.DisplayName(configToReturn.ActiveGame),
                buttons = configToReturn.ActiveButtons,
                configVersion = configToReturn.ConfigVersion,
                lastUpdatedUtc = configToReturn.LastUpdatedUtc,
                lastUpdatedBy = configToReturn.LastUpdatedBy
            };

            context.Response.ContentType = "application/json";
            context.Response.Headers["Cache-Control"] = "no-store";
            await context.Response.WriteAsync(JsonSerializer.Serialize(payload));

            Debug.WriteLine("[HttpCommandServer] Exit: HandleConfigGet");
        }

        /// <summary>
        /// Cheap stamp the phone polls to learn that the layout changed
        /// </summary>
        private async Task HandleConfigVersionGet(HttpContext context)
        {
            if (context == null)
            {
                return;
            }

            AppConfig config;
            try
            {
                config = await _configService.LoadConfigurationAsync() ?? new AppConfig();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HttpCommandServer] Config version read failed: {ex.Message}");
                lock (_configLock)
                {
                    config = _currentConfig ?? new AppConfig();
                }
            }

            context.Response.ContentType = "application/json";
            context.Response.Headers["Cache-Control"] = "no-store";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                version = config.ConfigVersion,
                updatedUtc = config.LastUpdatedUtc,
                game = GameIds.Normalize(config.ActiveGame),
                page = PageStamp,
                preview = PreviewEnabled
            }));
        }

        private async Task HandleWebUiRequest(HttpContext context)
        {
            Debug.WriteLine("[HttpCommandServer] Entry: HandleWebUiRequest");

            if (context == null)
            {
                Debug.WriteLine("[HttpCommandServer] Context is null");
                return;
            }

            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.Headers["Cache-Control"] = "no-store";
            await context.Response.WriteAsync(GetWebUiHtml().Replace("__EDSC_PAGE_STAMP__", PageStamp));

            Debug.WriteLine("[HttpCommandServer] Exit: HandleWebUiRequest");
        }

        private static string GetWebUiHtml()
        {
            return @"<!doctype html>
<html lang=""en"">
<head>
  <meta charset=""utf-8"">
  <meta name=""viewport"" content=""width=device-width, initial-scale=1"">
  <title>EDSC Web Control</title>
  <style>
    :root {
      --bg: #0f1115;
      --card: #1b1f26;
      --text: #f2f4f8;
      --muted: #9aa4b2;
      --accent: #4caf50;
    }
    body {
      margin: 0;
      font-family: ""Segoe UI"", Arial, sans-serif;
      background: var(--bg);
      color: var(--text);
    }
    header {
      padding: 16px;
      text-align: center;
      background: #121621;
      border-bottom: 1px solid #252b36;
    }
    header h1 {
      margin: 0 0 6px 0;
      font-size: 18px;
    }
    header p {
      margin: 0;
      font-size: 12px;
      color: var(--muted);
    }
    main {
      max-width: 720px;
      margin: 0 auto;
      padding: 16px;
    }
    .status {
      background: var(--card);
      padding: 12px;
      border-radius: 8px;
      margin-bottom: 16px;
      font-size: 13px;
    }
    .category {
      background: var(--card);
      border: 1px solid #2a313d;
      border-radius: 12px;
      padding: 12px;
      margin-bottom: 14px;
    }
    .category h2 {
      margin: 0 0 10px 0;
      font-size: 15px;
      color: #e6e9ef;
    }
    .grid {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(120px, 1fr));
      gap: 12px;
    }
    .btn {
      border: none;
      border-radius: 12px;
      padding: 14px;
      color: #fff;
      font-weight: 600;
      cursor: pointer;
      text-align: center;
      background: var(--accent);
    }
    .btn .icon {
      display: block;
      height: 56px;
      margin: 0 auto 6px auto;
    }
    .btn .icon svg {
      width: 56px;
      height: 56px;
      display: block;
    }
    .btn small {
      display: block;
      font-weight: 400;
      opacity: 0.8;
      margin-top: 4px;
    }
    .toolbar {
      display: flex;
      gap: 8px;
      margin-bottom: 12px;
    }
    .toolbar button {
      border: 1px solid #2f3746;
      background: #1d2430;
      color: #e6e9ef;
      padding: 8px 12px;
      border-radius: 6px;
      cursor: pointer;
      font-size: 12px;
      transition: all 0.3s ease;
    }
    .voice-btn.listening {
      background: #DC2626;
      border-color: #EF4444;
      animation: pulse 1.5s infinite;
    }
    .voice-btn.processing {
      background: #F59E0B;
      border-color: #FBBF24;
    }
    @keyframes pulse {
      0%, 100% { opacity: 1; }
      50% { opacity: 0.7; }
    }
    .voice-feedback {
      background: var(--card);
      border: 1px solid #2a313d;
      border-radius: 8px;
      padding: 12px;
      margin-bottom: 16px;
      font-size: 13px;
    }
    .voice-status {
      display: flex;
      align-items: center;
      gap: 8px;
      margin-bottom: 8px;
      font-weight: 600;
    }
    .voice-transcript {
      background: #0f1115;
      padding: 8px;
      border-radius: 4px;
      margin-bottom: 8px;
      min-height: 24px;
      color: var(--muted);
      font-style: italic;
    }
    .voice-match {
      padding: 6px 10px;
      border-radius: 4px;
      font-size: 12px;
    }
    .voice-match.success {
      background: #065F46;
      color: #D1FAE5;
    }
    .voice-match.error {
      background: #7F1D1D;
      color: #FEE2E2;
    }
    .voice-match.info {
      background: #1E3A8A;
      color: #DBEAFE;
    }
    .tracking-panel {
      background: var(--card);
      border: 1px solid #2a313d;
      border-radius: 8px;
      padding: 12px;
      margin-bottom: 16px;
    }
    .tracking-status {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: 10px;
      font-size: 13px;
      font-weight: 600;
    }
    .video-wrap {
      position: relative;
      width: 100%;
      line-height: 0;
    }
    #videoCanvas {
      width: 100%;
      height: auto;
      border-radius: 8px;
      background: #000;
    }
    #overlayCanvas {
      position: absolute;
      left: 0;
      top: 0;
      width: 100%;
      height: 100%;
      pointer-events: none;
    }
    #videoPreview {
      display: none;
      width: 100%;
      height: auto;
      border-radius: 8px;
      background: #000;
    }
    .video-wrap.phone #videoPreview {
      display: block;
    }
    .video-wrap.phone #videoCanvas {
      display: none;
    }
    .tracking-mode {
      display: flex;
      align-items: center;
      gap: 8px;
      margin-bottom: 10px;
      font-size: 13px;
    }
    .tracking-mode input {
      width: 18px;
      height: 18px;
    }
    .tracking-options {
      display: flex;
      flex-wrap: wrap;
      gap: 14px;
      margin-bottom: 10px;
      font-size: 13px;
      color: var(--muted);
    }
    .tracking-options select {
      margin-left: 6px;
      background: var(--card);
      color: var(--text);
      border: 1px solid #2a313d;
      border-radius: 6px;
      padding: 4px 6px;
      font-size: 13px;
    }
    .game-badge {
      display: inline-block;
      margin-left: 8px;
      padding: 2px 9px;
      border-radius: 999px;
      font-size: 12px;
      font-weight: 600;
      vertical-align: middle;
      background: #374151;
      color: #f9fafb;
    }
    body.game-elite .game-badge {
      background: #b45309;
    }
    body.game-elite header {
      border-bottom-color: #b45309;
    }
    body.game-starcitizen .game-badge {
      background: #1d4ed8;
    }
    body.game-starcitizen header {
      border-bottom-color: #1d4ed8;
    }
    .pose-readout {
      margin-top: 8px;
      font-family: Consolas, monospace;
      font-size: 12px;
      color: var(--muted);
      white-space: pre;
      line-height: 1.5;
    }
    .tracking-btn.active {
      background: #DC2626;
      border-color: #EF4444;
    }
    .btn.unbound {
      opacity: 0.4;
    }
    /* Fit mode is a multifunction display with physical-style perimeter keys. */
    .cockpit-only { display: none; }
    #recenterTracking { display: none; }
    body.fit-all {
      --hud: #8ee9dd;
      --hud-dim: #537d80;
      --bezel: #101b22;
      overflow: hidden;
      background: #080e13;
    }
    body.fit-all header,
    body.fit-all .status,
    body.fit-all #voiceFeedback,
    body.fit-all #previewBtn { display: none !important; }
    /* Settings drawer under the toolbar; closed by default so the display keeps the room */
    .settings-panel { display: none; }
    body.settings-open .settings-panel {
      display: flex;
      flex: 0 0 auto;
      flex-wrap: wrap;
      align-items: center;
      gap: 6px 18px;
      padding: 8px 12px;
      border: 1px solid #35454f;
      border-radius: 8px;
      background: #0d171f;
      color: #b7cdd2;
      font-size: 12px;
    }
    .settings-panel .tracking-mode,
    .settings-panel .tracking-options { margin: 0; font-size: 12px; }
    .settings-panel .tracking-options { gap: 12px; align-items: center; }
    .settings-panel #reload {
      min-height: 30px;
      padding: 4px 10px;
      border: 1px solid #33434d;
      border-radius: 3px;
      background: #131f28;
      color: #b7cdd2;
      font-size: 11px;
    }
    .settings-readout { flex-basis: 100%; margin: 0; font-size: 11px; line-height: 1.4; white-space: pre-wrap; }
    .settings-readout[hidden] { display: none; }
    body.fit-all main {
      box-sizing: border-box;
      max-width: none;
      height: 100vh;
      height: 100dvh;
      padding: max(8px, env(safe-area-inset-top)) max(8px, env(safe-area-inset-right)) max(8px, env(safe-area-inset-bottom)) max(8px, env(safe-area-inset-left));
      display: flex;
      flex-direction: column;
      gap: 8px;
    }
    body.fit-all .toolbar {
      flex: 0 0 auto;
      gap: 6px;
      margin: 0;
      align-items: stretch;
    }
    body.fit-all .toolbar::before {
      content: 'EDSC / FLIGHT DECK';
      flex: 1;
      align-self: center;
      color: var(--hud-dim);
      font: 11px Consolas, monospace;
      letter-spacing: 2px;
    }
    body.fit-all .toolbar button {
      min-height: 36px;
      padding: 5px 10px;
      border: 1px solid #33434d;
      border-radius: 3px;
      background: #131f28;
      font-size: 11px;
      color: #b7cdd2;
    }
    body.fit-all .toolbar button[aria-pressed=""true""] { border-color: var(--hud-dim); color: var(--hud); }
    body.fit-all .toolbar .active, body.fit-all .toolbar .listening { border-color: #ff9a6c; color: #ffb08c; }
    body.fit-all #cockpit {
      flex: 1;
      min-height: 0;
      display: grid;
      grid-template-columns: clamp(70px, 11vw, 132px) minmax(0, 1fr) clamp(70px, 11vw, 132px);
      grid-template-rows: 58px minmax(0, 1fr) 100px;
      grid-template-areas: 'top top top' 'left screen right' 'bottom bottom bottom';
      gap: 10px;
      padding: 12px;
      border: 1px solid #35454f;
      border-radius: 18px 18px 28px 28px;
      background: linear-gradient(135deg, #24313a 0, #121e27 22%, #0d171f 80%, #202d36 100%);
      box-shadow: inset 0 0 0 4px #0c141b, inset 0 0 0 5px #293640, 0 8px 32px #0006;
    }
    body.fit-all .cockpit-only { display: flex; }
    .edge-rail { min-width: 0; min-height: 0; gap: 7px; }
    body.fit-all .edge-rail { display: grid; grid-auto-columns: minmax(0, 1fr); grid-auto-flow: column; }
    #navTop { grid-area: top; padding: 0 clamp(20px, 10vw, 120px); }
    #navLeft { grid-area: left; }
    #navRight { grid-area: right; }
    body.fit-all #navLeft, body.fit-all #navRight { grid-auto-flow: row; grid-auto-rows: minmax(0, 1fr); }
    .edge-key {
      position: relative;
      min-width: 0;
      min-height: 44px;
      padding: 8px 5px;
      border: 1px solid #3a4b56;
      border-radius: 4px;
      box-shadow: inset 0 1px #ffffff12, 0 3px 0 #060c11;
      background: linear-gradient(#24323c, #17232c);
      color: #a5bdc5;
      cursor: pointer;
      font: 600 11px/1.3 'Segoe UI', sans-serif;
      text-transform: uppercase;
      letter-spacing: .6px;
      overflow-wrap: anywhere;
    }
    .edge-key::before { content: attr(data-channel); display: block; color: #617e8b; font: 9px Consolas, monospace; margin-bottom: 5px; }
    .edge-key::after { content: ''; position: absolute; bottom: 4px; left: 35%; right: 35%; height: 2px; background: #49616e; }
    .edge-key[aria-pressed=""true""] { color: #c6fff2; border-color: #77c9bc; background: linear-gradient(#1c4142, #172e34); box-shadow: inset 0 0 18px #71fbd914, 0 3px 0 #060c11; }
    .edge-key[aria-pressed=""true""]::after { background: var(--hud); box-shadow: 0 0 8px #82f3d8; }
    .edge-key[aria-pressed=""true""]::before { color: var(--hud); }
    body.fit-all button:focus-visible { outline: 2px solid #fff1ad; outline-offset: 2px; }
    body.fit-all button:active { filter: brightness(1.3); }
    body.fit-all #controlDisplay {
      grid-area: screen;
      min-width: 0;
      min-height: 0;
      display: flex;
      flex-direction: column;
      padding: 12px;
      border: 1px solid #436064;
      border-radius: 8px;
      background: radial-gradient(ellipse at 50% 20%, #13303688, transparent 80%), repeating-linear-gradient(0deg, transparent, transparent 31px, #6cc9bb05 32px), #081419;
      box-shadow: 0 0 0 3px #080f15, inset 0 0 35px #0008;
      overflow: hidden;
    }
    .display-heading { flex: 0 0 auto; justify-content: space-between; align-items: center; gap: 10px; padding-bottom: 10px; border-bottom: 1px solid #28484c; }
    .display-heading small { display: block; color: var(--hud-dim); font: 9px Consolas, monospace; letter-spacing: 2px; margin-bottom: 4px; }
    #activeCategory { margin: 0; font: 500 clamp(13px, 2.2vw, 22px) 'Segoe UI', sans-serif; letter-spacing: 1px; text-transform: uppercase; color: #b9f6ea; overflow-wrap: anywhere; }
    #categoryCount { flex-shrink: 0; font: 10px Consolas, monospace; color: var(--hud-dim); }
    body.fit-all #grid { flex: 1; min-height: 0; margin: 10px 0; }
    body.fit-all #grid > .category { display: none; }
    body.fit-all #grid > .category.active-category { display: flex; flex-direction: column; height: 100%; box-sizing: border-box; border: 0; padding: 0; margin: 0; background: transparent; }
    body.fit-all #grid > .category > h2 { display: none; }
    body.fit-all #grid > .category > .grid {
      flex: 1;
      min-height: 0;
      display: grid;
      grid-template-columns: repeat(var(--fit-columns, 3), minmax(0, 1fr));
      grid-auto-rows: var(--fit-cell-size, 80px);
      align-content: center;
      gap: 6px;
    }
    body.fit-all .btn {
      width: 100% !important;
      height: 100% !important;
      min-width: 0;
      min-height: 0;
      padding: 5px;
      border: 1px solid #385358;
      border-top: 2px solid var(--button-accent, #83c7bd);
      border-radius: 3px;
      background: linear-gradient(145deg, #1b3037, #101f27) !important;
      color: #d2e9e7;
      font-size: var(--fit-label-size, 11px);
      line-height: 1.15;
      overflow: hidden;
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      gap: 3px;
    }
    body.fit-all .btn .icon { flex-shrink: 0; width: var(--fit-icon-size, 28px); height: var(--fit-icon-size, 28px); margin: 0; }
    body.fit-all .btn .icon svg { width: 100%; height: 100%; }
    body.fit-all .btn .command-label { width: 100%; display: -webkit-box; -webkit-line-clamp: 2; -webkit-box-orient: vertical; overflow: hidden; overflow-wrap: anywhere; }
    body.fit-all .btn small { max-width: 100%; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; margin: 0; font: 9px Consolas, monospace; color: #789b9f; }
    body.fit-all .btn.unbound { opacity: .45; }
    body.fit-all .btn[hidden] { display: none; }
    .display-footer { flex: 0 0 auto; min-height: 32px; align-items: center; justify-content: space-between; gap: 8px; border-top: 1px solid #28484c; padding-top: 6px; font: 10px Consolas, monospace; color: var(--hud-dim); }
    #deckStatus { overflow: hidden; white-space: nowrap; text-overflow: ellipsis; }
    .page-controls { display: flex; align-items: center; gap: 8px; flex-shrink: 0; }
    .page-controls[hidden] { display: none; }
    #commandPages[hidden] { display: flex; visibility: hidden; }
    .page-controls button { min-width: 36px; min-height: 32px; border: 1px solid #3d6267; border-radius: 3px; color: var(--hud); background: #152a32; cursor: pointer; }
    .page-controls button:disabled { opacity: .25; cursor: default; }
    body.fit-all #cockpitBottom { grid-area: bottom; display: grid; grid-template-columns: minmax(0, 1fr) clamp(132px, 23vw, 210px); grid-template-rows: minmax(0, 1fr) auto; gap: 5px 12px; min-height: 0; }
    #navBottom { grid-column: 1; grid-row: 1; }
    #navBank { grid-column: 1; grid-row: 2; align-self: center; display: flex; align-items: center; justify-content: space-between; gap: 4px; color: var(--hud-dim); font: 9px Consolas, monospace; }
    #navBank[hidden] { display: none; }
    #trackingDock { grid-column: 2; grid-row: 1 / span 2; min-height: 0; }
    body.fit-all #trackingPanel {
      position: relative;
      box-sizing: border-box;
      display: flex !important;
      flex-direction: column;
      height: 100%;
      padding: 5px;
      margin: 0;
      border-radius: 4px;
      border: 1px solid #49656b;
      background: #0a171e;
      overflow: hidden;
    }
    body.fit-all .tracking-status { margin: 0; font: 8px Consolas, monospace; color: #92babf; gap: 4px; }
    body.fit-all #trackingStatusText { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    body.fit-all #trackingFps { flex-shrink: 0; }
    body.fit-all .video-wrap { flex: 1; min-height: 0; width: 50%; overflow: hidden; }
    body.fit-all:not(.tracking-live) .video-wrap { visibility: hidden; }
    body.fit-all #videoCanvas, body.fit-all #videoPreview { height: 100%; width: 100%; object-fit: contain; border-radius: 0; background: #0a171e; }
    body.fit-all #overlayCanvas { object-fit: contain; }
    body.fit-all .pose-readout { display: none !important; }
    body.fit-all #recenterTracking {
      position: absolute;
      inset: 0;
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: flex-end;
      width: 100%;
      padding: 5px;
      border: 0;
      background: transparent;
      color: var(--hud);
      cursor: pointer;
      font: 600 10px Consolas, monospace;
      letter-spacing: 1px;
    }
    #recenterLabel { position: absolute; left: 53%; right: 4px; top: 45%; line-height: 1.4; font-size: 9px; letter-spacing: .4px; }
    #trackingPlaceholder { position: absolute; inset: 18px 50% 8px 0; display: flex; justify-content: center; align-items: center; font-size: 24px; color: #587f88; }
    body.tracking-live #trackingPlaceholder { display: none; }
    #recenterTracking[aria-busy=""true""] { color: #ffe2a0; background: #20352b88; }
    body.fit-all #edscToast { background: #183239 !important; color: #d2f7ed !important; border: 1px solid #719e9c; }
    .empty-controls { color: #b2cecf; font-size: 13px; line-height: 1.5; }
    @media (max-width: 600px) {
      body.fit-all main { gap: 5px; padding: max(5px, env(safe-area-inset-top)) max(5px, env(safe-area-inset-right)) max(5px, env(safe-area-inset-bottom)) max(5px, env(safe-area-inset-left)); }
      body.fit-all .toolbar::before { display: none; }
      body.fit-all .toolbar button { flex: 1; padding: 4px; font-size: 10px; }
      body.fit-all #cockpit { grid-template-columns: 62px minmax(0, 1fr) 62px; grid-template-rows: 62px minmax(0, 1fr) 90px; padding: 8px; gap: 7px; }
      #navTop { padding: 0; }
      .edge-rail { gap: 5px; }
      .edge-key { font-size: 8px; letter-spacing: 0; padding: 6px 3px; }
      body.fit-all #controlDisplay { padding: 7px; }
      .display-heading { gap: 4px; padding-bottom: 7px; }
      .display-heading small { font-size: 8px; letter-spacing: 1px; }
      #categoryCount { font-size: 9px; }
      body.fit-all #cockpitBottom { column-gap: 7px; grid-template-columns: minmax(0, 1fr) 132px; }
      .display-footer { flex-wrap: wrap; gap: 4px; }
      #deckStatus { width: 100%; font-size: 9px; }
      .page-controls { margin-left: auto; }
    }
    @media (max-height: 500px) and (min-width: 601px) {
      body.fit-all main { gap: 5px; padding-top: max(5px, env(safe-area-inset-top)); padding-bottom: max(5px, env(safe-area-inset-bottom)); }
      body.fit-all #cockpit { grid-template-rows: 46px minmax(0, 1fr) 70px; gap: 7px; padding: 8px; }
      .edge-key { font-size: 9px; padding: 4px; }
      .edge-key::before { margin-bottom: 1px; font-size: 8px; }
      body.fit-all #controlDisplay { padding: 6px 10px; }
      .display-heading { padding-bottom: 5px; }
      .display-heading small { display: none; }
      #activeCategory { font-size: 13px; }
      body.fit-all #grid { margin: 5px 0; }
      .display-footer { padding-top: 3px; }
    }

  </style>
</head>
<body>
  <header>
    <h1>EDSC Web Control <span class=""game-badge"" id=""gameBadge"">Elite Dangerous</span></h1>
    <p id=""pageSubtitle"">Connected to your PC server</p>
  </header>
  <main>
    <div class=""status"" id=""status"">Loading buttons...</div>
    <div class=""toolbar"">
      <button id=""fullscreen"">Fullscreen</button>
      <button id=""settingsBtn"" aria-pressed=""false"" aria-controls=""settingsPanel"">⚙ Settings</button>
      <button id=""voiceBtn"" class=""voice-btn"">🎤 Voice</button>
      <button id=""trackingBtn"" class=""tracking-btn"">📹 Face Tracking</button>
      <button id=""previewBtn"" aria-pressed=""true"" style=""display:none;"">⏹ Stop Preview</button>
    </div>
    <!-- Settings drawer: the cockpit layout is the only layout, so the tracking options live here -->
    <div id=""settingsPanel"" class=""settings-panel"">
      <label class=""tracking-mode"">
        <input type=""checkbox"" id=""phoneModeToggle"">
        <span>Track on phone (MediaPipe) - sends pose only, no video</span>
      </label>
      <div class=""tracking-options"" id=""trackingOptions"">
        <label>Camera
          <select id=""camResSelect"">
            <option value=""640x480"">640x480</option>
            <option value=""480x360"">480x360 (faster)</option>
            <option value=""320x240"">320x240 (fastest)</option>
          </select>
        </label>
        <label>Mesh
          <select id=""meshSelect"">
            <option value=""outline"">Outline (faster)</option>
            <option value=""full"">Full mesh</option>
            <option value=""off"">Off</option>
          </select>
        </label>
        <button id=""reload"" type=""button"">Reload config</button>
      </div>
      <div id=""settingsReadout"" class=""pose-readout settings-readout"" hidden></div>
    </div>
    <div id=""trackingPanel"" class=""tracking-panel"" style=""display:none;"">
      <div class=""tracking-status"">
        <span id=""trackingStatusText"">Ready</span>
        <span id=""trackingFps"">0 FPS</span>
      </div>
      <div class=""video-wrap"" id=""videoWrap"">
        <canvas id=""videoCanvas"" width=""480"" height=""360""></canvas>
        <video id=""videoPreview"" autoplay playsinline muted></video>
        <canvas id=""overlayCanvas"" width=""480"" height=""360""></canvas>
      </div>
      <button id=""recenterTracking"" type=""button"" aria-label=""Recentre in-game head tracking"" aria-busy=""false"" title=""Look straight ahead, then tap to recentre"">
        <span id=""trackingPlaceholder"" aria-hidden=""true"">⌖</span>
        <span id=""recenterLabel"">⌖ RECENTRE</span>
      </button>
      <div id=""poseReadout"" class=""pose-readout"" style=""display:none;""></div>
    </div>
    <div id=""voiceFeedback"" class=""voice-feedback"" style=""display:none;"">
      <div class=""voice-status"">
        <span id=""voiceStatusIcon"">⚪</span>
        <span id=""voiceStatusText"">Ready</span>
      </div>
      <div class=""voice-transcript"" id=""voiceTranscript""></div>
      <div class=""voice-match"" id=""voiceMatch""></div>
    </div>
    <div id=""cockpit"">
      <nav id=""navTop"" class=""edge-rail cockpit-only"" aria-label=""Top control categories""></nav>
      <nav id=""navLeft"" class=""edge-rail cockpit-only"" aria-label=""Left control categories""></nav>
      <section id=""controlDisplay"" aria-label=""Flight controls"">
        <div class=""display-heading cockpit-only"">
          <div><small id=""deckGame"">ELITE DANGEROUS / CONTROLS</small><h2 id=""activeCategory"">Standby</h2></div>
          <span id=""categoryCount"">00 / 00</span>
        </div>
        <div id=""grid""></div>
        <div class=""display-footer cockpit-only"">
          <span id=""deckStatus"" role=""status"">Loading controls…</span>
          <div id=""commandPages"" class=""page-controls"" hidden>
            <button id=""prevCommands"" type=""button"" aria-label=""Previous controls"">‹</button>
            <span id=""commandPageLabel"">1 / 1</span>
            <button id=""nextCommands"" type=""button"" aria-label=""Next controls"">›</button>
          </div>
        </div>
      </section>
      <nav id=""navRight"" class=""edge-rail cockpit-only"" aria-label=""Right control categories""></nav>
      <div id=""cockpitBottom"" class=""cockpit-only"">
        <nav id=""navBottom"" class=""edge-rail"" aria-label=""Bottom control categories""></nav>
        <div id=""navBank"" hidden>
          <span id=""navBankLabel"">BANK 1/1</span>
          <div class=""page-controls"">
            <button id=""prevBank"" type=""button"" aria-label=""Previous category bank"">‹</button>
            <button id=""nextBank"" type=""button"" aria-label=""Next category bank"">›</button>
          </div>
        </div>
        <div id=""trackingDock""></div>
      </div>
    </div>
  </main>
  <script>
    const statusEl = document.getElementById('status');
    const gridEl = document.getElementById('grid');
    new MutationObserver(() => { document.getElementById('deckStatus').textContent = statusEl.textContent; }).observe(statusEl, { childList: true, characterData: true, subtree: true });
    const reloadBtn = document.getElementById('reload');
    const fullscreenBtn = document.getElementById('fullscreen');
    const settingsBtn = document.getElementById('settingsBtn');
    const iconCache = new Map();
    const navRails = ['navTop', 'navLeft', 'navRight', 'navBottom'].map(id => document.getElementById(id));
    const NAV_BANK_SIZE = 12;
    let categories = [];
    let activeCategoryIndex = 0;
    let navBank = 0;
    let commandPage = 0;
    let commandPageCount = 1;

    function setActiveCategory(index) {
      const sections = Array.from(gridEl.querySelectorAll(':scope > .category'));
      if (!sections.length) return;
      activeCategoryIndex = (index + sections.length) % sections.length;
      commandPage = 0;
      sections.forEach((section, sectionIndex) => {
        section.classList.toggle('active-category', sectionIndex === activeCategoryIndex);
      });
      document.getElementById('activeCategory').textContent = categories[activeCategoryIndex];
      document.getElementById('categoryCount').textContent = String(activeCategoryIndex + 1).padStart(2, '0') + ' / ' + String(categories.length).padStart(2, '0');
      navBank = Math.floor(activeCategoryIndex / NAV_BANK_SIZE);
      renderEdgeNavigation();
      requestAnimationFrame(updateFitLayout);
    }

    function renderEdgeNavigation() {
      navRails.forEach(rail => rail.replaceChildren());
      // Populate all four edges before adding another key to any edge.
      const slots = [0, 1, 2, 3, 0, 1, 2, 3, 0, 1, 2, 0];
      categories.slice(navBank * NAV_BANK_SIZE, (navBank + 1) * NAV_BANK_SIZE).forEach((category, slot) => {
        const index = navBank * NAV_BANK_SIZE + slot;
        const key = document.createElement('button');
        key.type = 'button';
        key.className = 'edge-key';
        key.textContent = category;
        key.title = category;
        key.dataset.channel = 'SYS ' + String(index + 1).padStart(2, '0');
        key.dataset.index = String(index);
        key.setAttribute('aria-pressed', String(index === activeCategoryIndex));
        key.setAttribute('aria-controls', 'grid');
        key.addEventListener('click', () => {
          setActiveCategory(index);
          document.querySelector('.edge-key[data-index=""' + index + '""]')?.focus({ preventScroll: true });
        });
        navRails[slots[slot]].appendChild(key);
      });
      const bankCount = Math.max(1, Math.ceil(categories.length / NAV_BANK_SIZE));
      document.getElementById('navBank').hidden = bankCount <= 1;
      document.getElementById('navBankLabel').textContent = 'BANK ' + (navBank + 1) + '/' + bankCount;
      document.getElementById('prevBank').disabled = navBank === 0;
      document.getElementById('nextBank').disabled = navBank >= bankCount - 1;
    }

    function rebuildEdgeNavigation(order) {
      const previousCategory = categories[activeCategoryIndex];
      categories = order;
      navBank = 0;
      renderEdgeNavigation();
      if (order.length) {
        setActiveCategory(Math.max(0, order.indexOf(previousCategory)));
      } else {
        activeCategoryIndex = 0;
        document.getElementById('activeCategory').textContent = 'Standby';
        document.getElementById('categoryCount').textContent = '00 / 00';
        document.getElementById('commandPages').hidden = true;
      }
    }

    function updateFitLayout() {
      const isFit = document.body.classList.contains('fit-all');
      gridEl.querySelectorAll('.btn').forEach(button => { button.hidden = false; });
      if (!isFit) return;
      const scope = gridEl.querySelector('.category.active-category');
      const buttons = scope ? Array.from(scope.querySelectorAll('.btn')) : [];
      if (!buttons.length) return;

      // Page large categories so touch targets stay readable in small landscape viewports.
      const rect = gridEl.getBoundingClientRect();
      const width = Math.max(1, rect.width);
      const height = Math.max(1, rect.height);
      const gap = 6;
      const minSize = 64;
      const capacity = Math.max(1, Math.floor((width + gap) / (minSize + gap))) * Math.max(1, Math.floor((height + gap) / (minSize + gap)));
      commandPageCount = Math.max(1, Math.ceil(buttons.length / capacity));
      commandPage = Math.min(commandPage, commandPageCount - 1);
      const count = Math.min(capacity, buttons.length - commandPage * capacity);
      let columns = 1;
      let size = 0;
      for (let candidate = 1; candidate <= count; candidate++) {
        const rows = Math.ceil(count / candidate);
        const candidateSize = Math.min((width - gap * (candidate - 1)) / candidate, (height - gap * (rows - 1)) / rows);
        if (candidateSize > size) { columns = candidate; size = candidateSize; }
      }
      size = Math.max(1, Math.min(150, size));
      gridEl.style.setProperty('--fit-columns', String(columns));
      gridEl.style.setProperty('--fit-cell-size', Math.floor(size) + 'px');
      gridEl.style.setProperty('--fit-icon-size', Math.max(16, Math.min(44, size * .34)) + 'px');
      gridEl.style.setProperty('--fit-label-size', Math.max(9, Math.min(13, size * .14)) + 'px');
      buttons.forEach((button, index) => { button.hidden = index < commandPage * capacity || index >= (commandPage + 1) * capacity; });
      document.getElementById('commandPages').hidden = commandPageCount <= 1;
      document.getElementById('commandPageLabel').textContent = (commandPage + 1) + ' / ' + commandPageCount;
      document.getElementById('prevCommands').disabled = commandPage === 0;
      document.getElementById('nextCommands').disabled = commandPage >= commandPageCount - 1;
    }

    // The cockpit layout is the only layout: the fit-all class is always on and the tracking
    // panel always lives in its dock. The plain stacked layout's CSS is kept only as the base
    // the cockpit rules build on.
    function enterCockpitLayout() {
      document.body.classList.add('fit-all');
      document.getElementById('trackingDock').appendChild(document.getElementById('trackingPanel'));
      requestAnimationFrame(updateFitLayout);
    }
    enterCockpitLayout();

    function setSettingsOpen(open) {
      document.body.classList.toggle('settings-open', open);
      settingsBtn.setAttribute('aria-pressed', String(open));
      // The readout is hidden in the dock; mirror it into the drawer while it is open
      document.getElementById('settingsReadout').hidden = !open || !tracking.isActive || !tracking.phoneMode;
      requestAnimationFrame(updateFitLayout);
    }
    settingsBtn.addEventListener('click', () => setSettingsOpen(!document.body.classList.contains('settings-open')));
    new MutationObserver(() => {
      const mirror = document.getElementById('settingsReadout');
      mirror.textContent = document.getElementById('poseReadout').textContent;
      mirror.hidden = !document.body.classList.contains('settings-open') || !tracking.isActive || !tracking.phoneMode;
    }).observe(document.getElementById('poseReadout'), { childList: true, characterData: true, subtree: true });

    document.getElementById('prevBank').addEventListener('click', () => { if (navBank > 0) { navBank--; renderEdgeNavigation(); } });
    document.getElementById('nextBank').addEventListener('click', () => { if ((navBank + 1) * NAV_BANK_SIZE < categories.length) { navBank++; renderEdgeNavigation(); } });
    document.getElementById('prevCommands').addEventListener('click', () => { commandPage = Math.max(0, commandPage - 1); updateFitLayout(); });
    document.getElementById('nextCommands').addEventListener('click', () => { commandPage = Math.min(commandPageCount - 1, commandPage + 1); updateFitLayout(); });
    // The display can change size when paging, tracking or the browser chrome changes.
    if (typeof ResizeObserver !== 'undefined') new ResizeObserver(() => requestAnimationFrame(updateFitLayout)).observe(gridEl);
    if (window.visualViewport) window.visualViewport.addEventListener('resize', () => requestAnimationFrame(updateFitLayout));


    // Voice control state and configuration
    const voiceControl = {
      recognition: null,
      isListening: false,
      buttons: [],
      lastProcessedButtonId: '',
      lastProcessedTime: 0,
      commandCooldownMs: 2000,
      interimResults: true,
      language: 'en-GB'
    };

    // UI Update Helper Functions
    function updateVoiceStatus(state, text) {
      const statusIcon = document.getElementById('voiceStatusIcon');
      const statusText = document.getElementById('voiceStatusText');

      if (statusIcon && statusText) {
        const icons = {
          'idle': '⚪',
          'listening': '🔴',
          'processing': '🟡'
        };
        statusIcon.textContent = icons[state] || '⚪';
        statusText.textContent = text;
      }
    }

    function updateVoiceButtonState(state) {
      const btn = document.getElementById('voiceBtn');
      if (!btn) return;

      btn.classList.remove('listening', 'processing');

      if (state === 'listening') {
        btn.classList.add('listening');
        btn.textContent = '🔴 Listening';
      } else if (state === 'processing') {
        btn.classList.add('processing');
        btn.textContent = '🟡 Processing';
      } else {
        btn.textContent = '🎤 Voice';
      }
    }

    function updateVoiceTranscript(text) {
      const el = document.getElementById('voiceTranscript');
      if (el) {
        el.textContent = text || '...';
      }
    }

    function updateVoiceMatch(type, message) {
      const el = document.getElementById('voiceMatch');
      if (el) {
        el.textContent = message;
        el.className = 'voice-match ' + type;
      }
    }

    // Voice Recognition Initialization
    function initVoiceRecognition() {
      console.log('[Voice] Initializing speech recognition...');
      const SpeechRecognition = window.SpeechRecognition || window.webkitSpeechRecognition;

      if (!SpeechRecognition) {
        console.error('[Voice] Speech Recognition API not available');
        console.log('[Voice] window.SpeechRecognition:', typeof window.SpeechRecognition);
        console.log('[Voice] window.webkitSpeechRecognition:', typeof window.webkitSpeechRecognition);
        return false;
      }

      console.log('[Voice] SpeechRecognition API found');

      try {
        voiceControl.recognition = new SpeechRecognition();
        voiceControl.recognition.continuous = true;
        voiceControl.recognition.interimResults = voiceControl.interimResults;
        voiceControl.recognition.lang = voiceControl.language;
        voiceControl.recognition.maxAlternatives = 1;

        voiceControl.recognition.onstart = handleVoiceStart;
        voiceControl.recognition.onresult = handleVoiceResult;
        voiceControl.recognition.onerror = handleVoiceError;
        voiceControl.recognition.onend = handleVoiceEnd;

        console.log('[Voice] Recognition initialized successfully');
        return true;
      } catch (e) {
        console.error('[Voice] Error creating SpeechRecognition:', e);
        updateVoiceMatch('error', 'Failed to initialize: ' + e.message);
        return false;
      }
    }

    // Voice Event Handlers
    function handleVoiceStart() {
      console.log('Voice recognition started');
      updateVoiceStatus('listening', 'Listening...');
      updateVoiceButtonState('listening');
    }

    function handleVoiceResult(event) {
      if (!event || !event.results) {
        return;
      }

      let interimTranscript = '';
      let finalTranscript = '';

      for (let i = event.resultIndex; i < event.results.length; i++) {
        const transcript = event.results[i][0].transcript;
        if (event.results[i].isFinal) {
          finalTranscript += transcript;
        } else {
          interimTranscript += transcript;
        }
      }

      const currentTranscript = finalTranscript || interimTranscript;
      updateVoiceTranscript(currentTranscript);

      if (finalTranscript) {
        processVoiceCommand(finalTranscript.trim().toLowerCase());
      }
    }

    function handleVoiceError(event) {
      if (!event) {
        console.error('[Voice] handleVoiceError called with no event');
        return;
      }

      console.error('[Voice] Speech recognition error:', event.error);
      console.error('[Voice] Error event details:', event);

      let errorMessage = 'Voice error: ';
      switch(event.error) {
        case 'no-speech':
          errorMessage += 'No speech detected';
          break;
        case 'audio-capture':
          errorMessage += 'No microphone found';
          break;
        case 'not-allowed':
          errorMessage += 'Microphone permission denied';
          break;
        case 'network':
          errorMessage += 'Network error (requires internet connection)';
          break;
        case 'service-not-allowed':
          errorMessage += 'Speech service not allowed (check browser settings)';
          break;
        case 'aborted':
          errorMessage += 'Recognition aborted';
          break;
        default:
          errorMessage += event.error;
      }

      console.log('[Voice] Error message:', errorMessage);
      updateVoiceMatch('error', errorMessage);

      if (['not-allowed', 'audio-capture', 'service-not-allowed'].includes(event.error)) {
        console.log('[Voice] Critical error, stopping recognition');
        stopVoiceRecognition();
      }
    }

    function handleVoiceEnd() {
      console.log('Voice recognition ended');
      if (voiceControl.isListening) {
        try {
          voiceControl.recognition.start();
        } catch(e) {
          console.error('Failed to restart recognition:', e);
          stopVoiceRecognition();
        }
      } else {
        updateVoiceStatus('idle', 'Ready');
        updateVoiceButtonState('idle');
      }
    }

    // Clear the voice transcript UI after command execution
    function clearTranscriptUI() {
      console.log('[Voice] Clearing transcript UI');
      updateVoiceTranscript('');
    }

    // Process voice command - continuous listening, immediate execution
    function processVoiceCommand(transcript) {
      if (!transcript) {
        return;
      }

      console.log('[Voice] Processing transcript:', transcript);

      const commandText = transcript.trim();
      const matchedButton = findBestMatch(commandText);

      if (!matchedButton) {
        // No match - just keep listening
        return;
      }

      // Deduplication check - skip if same BUTTON was pressed recently
      // This prevents double-firing even if transcript text varies slightly
      const now = Date.now();
      const timeSinceLastCommand = now - voiceControl.lastProcessedTime;
      if (matchedButton.id === voiceControl.lastProcessedButtonId && timeSinceLastCommand < voiceControl.commandCooldownMs) {
        console.log('[Voice] Duplicate button ignored. Button:', matchedButton.id, 'Time since last:', timeSinceLastCommand, 'ms');
        clearTranscriptUI();
        return;
      }

      // Update deduplication tracking with button ID
      voiceControl.lastProcessedButtonId = matchedButton.id;
      voiceControl.lastProcessedTime = now;

      console.log('[Voice] Executing command:', matchedButton.label, 'Button ID:', matchedButton.id);
      updateVoiceMatch('success', `Executing: ${matchedButton.label}`);

      // Send the command
      sendCommand(matchedButton);

      // Clear the transcript UI
      clearTranscriptUI();
    }

    // Fuzzy Matching Algorithm
    // Letters and digits only, so 'hard points', 'hard-points' and 'Hardpoints' compare equal
    function squashPhrase(text) {
      return (text || '').toLowerCase().replace(/[^a-z0-9]+/g, '');
    }

    // Every phrase that can trigger a button: its label, its id, and any voice aliases from the config
    function phrasesFor(button) {
      const phrases = [button.label, button.id];
      if (Array.isArray(button.voiceAliases)) {
        for (const alias of button.voiceAliases) {
          phrases.push(alias);
        }
      }
      return phrases.filter((p) => typeof p === 'string' && p.trim().length > 0);
    }

    function scorePhrase(normalized, squashedCommand, commandWords, phrase) {
      const text = phrase.toLowerCase().trim();
      const squashed = squashPhrase(text);
      if (!squashed) {
        return 0;
      }

      if (squashed === squashedCommand) {
        return 1000;
      }

      let score = 0;

      if (squashedCommand.length >= 3 && squashed.includes(squashedCommand)) {
        score += 100;
      } else if (squashed.length >= 3 && squashedCommand.includes(squashed)) {
        score += 90;
      }

      const phraseWords = text.split(/\s+/);
      for (const cmdWord of commandWords) {
        for (const phraseWord of phraseWords) {
          if (phraseWord.includes(cmdWord) || cmdWord.includes(phraseWord)) {
            score += 30;
          }

          if (cmdWord.length >= 4 && phraseWord.length >= 4) {
            const similarity = calculateSimilarity(cmdWord, phraseWord);
            if (similarity > 0.7) {
              score += 20 * similarity;
            }
          }
        }
      }

      if (squashed.startsWith(squashedCommand)) {
        score += 80;
      }

      return score;
    }

    function findBestMatch(commandText) {
      if (!commandText || voiceControl.buttons.length === 0) {
        return null;
      }

      const normalized = commandText.toLowerCase().trim();
      const squashedCommand = squashPhrase(normalized);
      const commandWords = normalized.split(/\s+/).filter((w) => w.length > 0);
      let bestMatch = null;
      let bestScore = 0;
      let bestPhrase = '';

      for (const button of voiceControl.buttons) {
        if (!button) {
          continue;
        }

        for (const phrase of phrasesFor(button)) {
          const score = scorePhrase(normalized, squashedCommand, commandWords, phrase);

          if (score >= 1000) {
            console.log('[Voice] Exact match:', phrase, '->', button.label);
            return button;
          }

          if (score > bestScore) {
            bestScore = score;
            bestMatch = button;
            bestPhrase = phrase;
          }
        }
      }

      if (bestScore <= 30) {
        console.log('[Voice] No match for:', normalized, 'best score', bestScore);
        return null;
      }

      console.log('[Voice] Best match:', bestMatch ? bestMatch.label : 'none', 'via', bestPhrase, 'score', bestScore);
      return bestMatch;
    }

    function calculateSimilarity(str1, str2) {
      if (!str1 || !str2) {
        return 0;
      }

      const len1 = str1.length;
      const len2 = str2.length;
      const maxLen = Math.max(len1, len2);

      if (maxLen === 0) return 1.0;

      let matches = 0;
      const minLen = Math.min(len1, len2);

      for (let i = 0; i < minLen; i++) {
        if (str1[i] === str2[i]) {
          matches++;
        }
      }

      return matches / maxLen;
    }

    // Voice Control Toggle Functions
    function toggleVoiceRecognition() {
      if (!voiceControl.recognition && !initVoiceRecognition()) {
        updateVoiceMatch('error', 'Speech recognition not supported in this browser');
        return;
      }

      if (voiceControl.isListening) {
        stopVoiceRecognition();
      } else {
        startVoiceRecognition();
      }
    }

    function startVoiceRecognition() {
      console.log('[Voice] startVoiceRecognition called');

      if (!voiceControl.recognition) {
        console.log('[Voice] Recognition not initialized, initializing now...');
        if (!initVoiceRecognition()) {
          console.error('[Voice] Failed to initialize recognition');
          return;
        }
      }

      try {
        const feedbackEl = document.getElementById('voiceFeedback');
        if (feedbackEl) {
          feedbackEl.style.display = 'block';
        }
        voiceControl.isListening = true;
        console.log('[Voice] Calling recognition.start()...');
        voiceControl.recognition.start();
        updateVoiceMatch('info', `Say ""${voiceControl.wakeWord}"" followed by a command...`);
        console.log('[Voice] recognition.start() called successfully');
      } catch(e) {
        console.error('[Voice] Failed to start voice recognition:', e);
        console.error('[Voice] Error details:', e.name, e.message);
        updateVoiceMatch('error', 'Failed to start: ' + e.message);
        voiceControl.isListening = false;
      }
    }

    function stopVoiceRecognition() {
      if (voiceControl.recognition) {
        voiceControl.isListening = false;
        try {
          voiceControl.recognition.stop();
        } catch(e) {
          console.error('Error stopping recognition:', e);
        }
      }
      updateVoiceStatus('idle', 'Stopped');
      updateVoiceButtonState('idle');

      setTimeout(() => {
        if (!voiceControl.isListening) {
          const feedbackEl = document.getElementById('voiceFeedback');
          if (feedbackEl) {
            feedbackEl.style.display = 'none';
          }
        }
      }, 2000);
    }

    async function getIconMarkup(name) {
      if (!name) {
        return '';
      }
      if (iconCache.has(name)) {
        return iconCache.get(name);
      }
      try {
        const res = await fetch(`/assets/icons/${encodeURIComponent(name)}`);
        if (!res.ok) {
          return '';
        }
        const svg = await res.text();
        iconCache.set(name, svg);
        return svg;
      } catch (err) {
        return '';
      }
    }

    // The desktop editor bumps the config version on save; poll it so the layout follows without a manual reload.
    // The same poll carries a stamp that changes when the PC app restarts, so a new page script is picked up
    // automatically and tracking resumes by itself.
    const PAGE_STAMP = '__EDSC_PAGE_STAMP__';
    const RESUME_KEY = 'edsc.resumeTracking';
    let configStamp = null;
    let reloading = false;
    let reloadPending = false;

    // A brief message that stays visible even when Fit all hides the status line
    let toastTimer = null;
    function showToast(text) {
      let el = document.getElementById('edscToast');
      if (!el) {
        el = document.createElement('div');
        el.id = 'edscToast';
        el.style.cssText = 'position:fixed;left:50%;bottom:14px;transform:translateX(-50%);background:#7F1D1D;color:#FEE2E2;padding:8px 14px;border-radius:8px;font-size:13px;z-index:9999;max-width:90%;text-align:center;box-shadow:0 2px 8px rgba(0,0,0,.5)';
        document.body.appendChild(el);
      }
      el.textContent = text;
      el.style.display = 'block';
      if (toastTimer) {
        clearTimeout(toastTimer);
      }
      toastTimer = setTimeout(() => { el.style.display = 'none'; }, 4000);
    }

    function reloadForNewPage() {
      reloading = true;
      try {
        if (tracking.isActive) {
          sessionStorage.setItem(RESUME_KEY, '1');
        }
      } catch (e) {
        // Storage unavailable; tracking just will not auto-resume
      }
      location.reload();
    }

    async function checkConfigVersion() {
      if (reloading) {
        return;
      }
      try {
        const res = await fetch('/config/version', { cache: 'no-store' });
        if (!res.ok) {
          return;
        }
        const v = await res.json();

        if (v.page && v.page !== PAGE_STAMP) {
          // A reload drops fullscreen, so wait until the user leaves it
          if (document.fullscreenElement) {
            if (!reloadPending) {
              console.log('[Config] PC app restarted; reload deferred until fullscreen ends');
              reloadPending = true;
            }
            return;
          }
          console.log('[Config] PC app restarted with a new page, reloading');
          reloadForNewPage();
          return;
        }

        if (typeof v.preview === 'boolean') {
          previewWanted = v.preview;
        }

        const stamp = String(v.version) + ':' + String(v.updatedUtc) + ':' + String(v.game || '');
        if (configStamp !== null && stamp !== configStamp) {
          console.log('[Config] Layout changed on the PC, reloading');
          await loadConfig();
        }
      } catch (err) {
        // Server unreachable for the moment; try again next tick
      }
    }
    setInterval(checkConfigVersion, 3000);

    // Per-game branding: title, badge and accent follow whichever game the PC has active
    let currentGame = 'elite';
    let currentGameName = 'Elite Dangerous';
    function applyGameBranding(game, gameName) {
      currentGame = game === 'starcitizen' ? 'starcitizen' : 'elite';
      currentGameName = gameName || (currentGame === 'starcitizen' ? 'Star Citizen' : 'Elite Dangerous');
      document.body.classList.toggle('game-starcitizen', currentGame === 'starcitizen');
      document.body.classList.toggle('game-elite', currentGame !== 'starcitizen');
      const badge = document.getElementById('gameBadge');
      if (badge) {
        badge.textContent = currentGameName;
      }
      const subtitle = document.getElementById('pageSubtitle');
      if (subtitle) {
        subtitle.textContent = currentGame === 'starcitizen'
          ? 'Ship controls and head tracking for Star Citizen'
          : 'Ship controls and head tracking for Elite Dangerous';
      }
      document.title = 'EDSC - ' + currentGameName;
      document.getElementById('deckGame').textContent = currentGameName.toUpperCase() + ' / CONTROLS';
    }

    async function loadConfig() {
      statusEl.textContent = 'Loading buttons...';
      gridEl.innerHTML = '';
      try {
        const res = await fetch('/config', { cache: 'no-store' });
        const config = await res.json();
        configStamp = String(config.configVersion) + ':' + String(config.lastUpdatedUtc) + ':' + String(config.game || '');
        applyGameBranding(config.game, config.gameName);
        const buttons = (config && config.buttons) ? config.buttons : [];
        if (!buttons.length) {
          voiceControl.buttons = [];
          rebuildEdgeNavigation([]);
          statusEl.textContent = 'No buttons configured for ' + currentGameName + '. Use Import on the PC.';
          const empty = document.createElement('p');
          empty.className = 'empty-controls';
          empty.textContent = statusEl.textContent;
          gridEl.appendChild(empty);
          return;
        }
        statusEl.textContent = `Loaded ${buttons.length} buttons`;
        voiceControl.buttons = buttons;
        const groups = new Map();
        const order = [];
        for (const button of buttons) {
          const category = (button.category && button.category.trim()) ? button.category.trim() : 'General';
          if (!groups.has(category)) {
            groups.set(category, []);
            order.push(category);
          }
          groups.get(category).push(button);
        }
        for (const category of order) {
          const section = document.createElement('section');
          section.className = 'category';

          const title = document.createElement('h2');
          title.textContent = category;
          section.appendChild(title);

          const grid = document.createElement('div');
          grid.className = 'grid';
          for (const button of groups.get(category)) {
            const btn = document.createElement('button');
            btn.className = 'btn';
            btn.style.background = button.color || '#4caf50';
            btn.style.setProperty('--button-accent', button.color || '#83c7bd');
            btn.title = (button.label || button.id) + (button.key ? ' · ' + button.key : ' · not bound');
            const buttonSize = (button.size || 80) * 1.6;
            btn.style.width = buttonSize + 'px';
            btn.style.height = buttonSize + 'px';
            if (!button.key) {
              btn.classList.add('unbound');
              btn.title = 'No keyboard key bound for this action in ' + currentGameName;
            }
            const iconWrap = document.createElement('div');
            iconWrap.className = 'icon';
            if (button.iconSvg) {
              getIconMarkup(button.iconSvg).then(svg => {
                if (svg) {
                  iconWrap.innerHTML = svg;
                }
              });
            }

            const label = document.createElement('div');
            label.className = 'command-label';
            label.textContent = button.label || button.id;

            const key = document.createElement('small');
            key.textContent = button.key || 'not bound';

            btn.appendChild(iconWrap);
            btn.appendChild(label);
            btn.appendChild(key);
            btn.addEventListener('click', () => sendCommand(button));
            grid.appendChild(btn);
          }
          section.appendChild(grid);
          gridEl.appendChild(section);
        }
        rebuildEdgeNavigation(order);
        requestAnimationFrame(updateFitLayout);
      } catch (err) {
        rebuildEdgeNavigation([]);
        statusEl.textContent = 'Failed to load config. Reconnect to the PC, then reload.';
        const retry = document.createElement('button');
        retry.className = 'edge-key';
        retry.textContent = 'Retry connection';
        retry.addEventListener('click', loadConfig);
        gridEl.replaceChildren(retry);
      }
    }

    async function sendCommand(button) {
      if (!button || !button.key) {
        return;
      }
      statusEl.textContent = `Sending ${button.label || button.id}...`;
      try {
        const res = await fetch('/command', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            buttonId: button.id || '',
            key: button.key,
            holdMs: button.holdMs > 0 ? button.holdMs : 0,
            timestamp: Date.now()
          })
        });
        const data = await res.json();
        if (data && data.success) {
          statusEl.textContent = data.message || 'Command sent';
        } else {
          statusEl.textContent = data && data.message ? data.message : 'Command failed';
        }
      } catch (err) {
        statusEl.textContent = 'Command failed';
      }
    }

    reloadBtn.addEventListener('click', loadConfig);
    window.addEventListener('resize', () => requestAnimationFrame(updateFitLayout));
    document.addEventListener('fullscreenchange', () => {
      requestAnimationFrame(updateFitLayout);
      fullscreenBtn.textContent = document.fullscreenElement ? 'Exit Fullscreen' : 'Fullscreen';
      if (!document.fullscreenElement && reloadPending) {
        reloadPending = false;
        reloadForNewPage();
      }
    });
    fullscreenBtn.addEventListener('click', async () => {
      try {
        if (!document.fullscreenElement) {
          const root = document.documentElement;
          if (typeof root.requestFullscreen === 'function') {
            await root.requestFullscreen({ navigationUI: 'hide' });
          } else if (typeof root.webkitRequestFullscreen === 'function') {
            root.webkitRequestFullscreen();
          } else {
            throw new Error('not supported by this browser');
          }
          fullscreenBtn.textContent = 'Exit Fullscreen';
        } else {
          await document.exitFullscreen();
          fullscreenBtn.textContent = 'Fullscreen';
        }
      } catch (err) {
        const reason = err && err.message ? err.message : String(err);
        statusEl.textContent = 'Fullscreen not available';
        showToast('Fullscreen failed: ' + reason);
        console.error('[Fullscreen] failed:', err);
      }
    });

    const voiceBtn = document.getElementById('voiceBtn');
    if (voiceBtn) {
      voiceBtn.addEventListener('click', toggleVoiceRecognition);
    }

    window.addEventListener('beforeunload', () => {
      if (voiceControl.isListening) {
        stopVoiceRecognition();
      }
      if (tracking.isActive) {
        stopTracking();
      }
    });

    // Face Tracking functionality
    const tracking = {
      isActive: false,
      stream: null,
      ws: null,
      video: null,
      canvas: null,
      ctx: null,
      overlay: null,
      overlayCtx: null,
      frameInterval: null,
      fps: 0,
      frameCount: 0,
      lastFpsUpdate: Date.now(),
      phoneMode: false,
      previewVisible: true
    };

    // On-phone tracking with MediaPipe Face Landmarker: the browser runs the model and
    // sends only a pose to the PC, so no video crosses the network.
    const MEDIAPIPE_BASE = 'https://cdn.jsdelivr.net/npm/@mediapipe/tasks-vision@0.10.14';
    const MEDIAPIPE_MODEL = 'https://storage.googleapis.com/mediapipe-models/face_landmarker/face_landmarker/float16/1/face_landmarker.task';
    const PHONE_MODE_KEY = 'edsc.trackOnPhone';
    const CAM_RES_KEY = 'edsc.camRes';
    const MESH_KEY = 'edsc.mesh';
    const phoneTracker = {
      vision: null,
      landmarker: null,
      delegate: '',
      loading: null,
      raf: null,
      vfc: null,
      lastVideoTime: -1,
      lostSent: false,
      inferMs: 0,
      stepMs: 0,
      camInfo: '',
      lastReadoutAt: 0,
      overlayClear: true,
      pendingGaze: null
    };

    // Phone-side performance choices, remembered per device. The camera size feeds MediaPipe's
    // texture upload and face detector; the mesh setting is pure drawing cost.
    const trackingPrefs = { camRes: '640x480', mesh: 'outline' };
    const MAX_OVERLAY_WIDTH = 480;
    const READOUT_INTERVAL_MS = 125;

    function readPref(key, fallback, allowed) {
      try {
        const value = localStorage.getItem(key);
        return allowed.indexOf(value) >= 0 ? value : fallback;
      } catch (e) {
        return fallback;
      }
    }

    function writePref(key, value) {
      try {
        localStorage.setItem(key, value);
      } catch (e) {
        // Storage unavailable; the choice just will not persist
      }
    }

    function initTrackingOptions() {
      trackingPrefs.camRes = readPref(CAM_RES_KEY, '640x480', ['640x480', '480x360', '320x240']);
      trackingPrefs.mesh = readPref(MESH_KEY, 'outline', ['full', 'outline', 'off']);

      const camSelect = document.getElementById('camResSelect');
      if (camSelect) {
        camSelect.value = trackingPrefs.camRes;
        camSelect.addEventListener('change', () => {
          trackingPrefs.camRes = camSelect.value;
          writePref(CAM_RES_KEY, camSelect.value);
          if (tracking.isActive) {
            stopTracking(true);
            startTracking();
          }
        });
      }

      const meshSelect = document.getElementById('meshSelect');
      if (meshSelect) {
        meshSelect.value = trackingPrefs.mesh;
        meshSelect.addEventListener('change', () => {
          trackingPrefs.mesh = meshSelect.value;
          writePref(MESH_KEY, meshSelect.value);
          if (tracking.overlayCtx && tracking.overlay) {
            tracking.overlayCtx.clearRect(0, 0, tracking.overlay.width, tracking.overlay.height);
            phoneTracker.overlayClear = true;
          }
        });
      }
    }

    let lastTrackingStatus = null;
    function setTrackingStatus(text) {
      // Same text every frame would still invalidate layout; only touch the DOM on change
      if (text === lastTrackingStatus) {
        return;
      }
      lastTrackingStatus = text;
      document.getElementById('trackingStatusText').textContent = text;
    }

    function isPhoneMode() {
      const toggle = document.getElementById('phoneModeToggle');
      return !!(toggle && toggle.checked);
    }

    function initTracking() {
      tracking.video = document.getElementById('videoPreview');
      tracking.canvas = document.getElementById('videoCanvas');
      tracking.ctx = tracking.canvas.getContext('2d', { willReadFrequently: true });
      tracking.overlay = document.getElementById('overlayCanvas');
      // Plain 2D context on purpose. The 'desynchronized' (low-latency) hint routes the canvas
      // round the page compositor, and on Android Chrome that path intermittently stops
      // presenting new frames when the canvas sits over a <video> element: the mesh freezes
      // while inference and pose sending carry on. The compositor copy it saves is a single
      // 480x360 RGBA blit per frame, far below the inference cost, so nothing measurable is lost.
      tracking.overlayCtx = tracking.overlay.getContext('2d');

      initTrackingOptions();

      const toggle = document.getElementById('phoneModeToggle');
      if (toggle) {
        // On-phone tracking is the default; only an explicit opt-out turns it off
        try {
          toggle.checked = localStorage.getItem(PHONE_MODE_KEY) !== '0';
        } catch (e) {
          toggle.checked = true;
        }
        toggle.addEventListener('change', () => {
          try {
            localStorage.setItem(PHONE_MODE_KEY, toggle.checked ? '1' : '0');
          } catch (e) {
            // Storage unavailable; the choice just will not persist
          }
          if (tracking.isActive) {
            stopTracking(true);
            startTracking();
          }
        });
      }
    }

    async function loadMediaPipe() {
      if (phoneTracker.landmarker) {
        return phoneTracker.landmarker;
      }

      if (!phoneTracker.loading) {
        phoneTracker.loading = (async () => {
          setTrackingStatus('Loading MediaPipe...');
          const vision = await import(MEDIAPIPE_BASE + '/vision_bundle.mjs');
          const fileset = await vision.FilesetResolver.forVisionTasks(MEDIAPIPE_BASE + '/wasm');

          const makeOptions = (delegate) => ({
            baseOptions: { modelAssetPath: MEDIAPIPE_MODEL, delegate: delegate },
            runningMode: 'VIDEO',
            numFaces: 1,
            outputFaceBlendshapes: false,
            outputFacialTransformationMatrixes: true
          });

          let landmarker;
          try {
            landmarker = await vision.FaceLandmarker.createFromOptions(fileset, makeOptions('GPU'));
            phoneTracker.delegate = 'GPU';
            console.log('[Phone tracking] MediaPipe running on GPU');
          } catch (gpuError) {
            console.warn('[Phone tracking] GPU delegate failed, falling back to CPU:', gpuError);
            landmarker = await vision.FaceLandmarker.createFromOptions(fileset, makeOptions('CPU'));
            phoneTracker.delegate = 'CPU';
          }

          phoneTracker.vision = vision;
          phoneTracker.landmarker = landmarker;
          return landmarker;
        })().catch((err) => {
          phoneTracker.loading = null;
          throw err;
        });
      }

      return phoneTracker.loading;
    }

    // MediaPipe gives a column-major 4x4 transform from canonical face space to camera space,
    // x right, y up, z toward the viewer, translation in centimetres. Convert to the same
    // convention the PC tracker uses: yaw + toward image-right, pitch + up, roll + when the
    // image-right side of the face drops, z + away from the camera.
    const RAD2DEG = 180 / Math.PI;

    // Eye gaze from the iris landmarks (468-472 and 473-477 of the 478-point model), which the
    // landmarker already produces every frame, so this costs nothing extra to measure. Each iris
    // centre is placed against its own eye: sideways along the corner-to-corner axis, up and down
    // against the eyelid midpoint, both as a fraction of the eye width so distance from the camera
    // drops out. The fraction becomes an angle through the eyeball (about 12 mm radius against a
    // 30 mm eye opening). Positive yaw = looking to your own left, positive pitch = up, matching
    // the head pose.
    //
    // Blinks. Both measurements are taken against the eye corners, which do not move when the
    // lids do, so a closing lid cannot drag the reference around. The lid gap is compared with a
    // slowly learned open-eye baseline per eye: once it drops below 70% of that, the iris estimate
    // is already being pulled by the lid, so the eye is left out, and it stays out for a short
    // settle time after it reopens. With both eyes out no gaze is reported and the PC holds the
    // last value, so a blink neither flicks the view nor drops it to zero.
    const EYE_DEFS = [
      { cornerA: 33, cornerB: 133, lidTop: 159, lidBottom: 145 },
      { cornerA: 362, cornerB: 263, lidTop: 386, lidBottom: 374 }
    ];
    const IRIS_CENTRES = [468, 473];
    const EYE_ANGLE_GAIN = 2.4;
    const EYE_CLOSED_RATIO = 0.12;          // lid gap / width: definitely shut, before a baseline exists
    const EYE_BLINK_FRACTION = 0.75;        // of the learned open baseline; below this the eye is blinking
    const EYE_BASELINE_RATE = 0.02;         // per frame; the baseline follows slow changes only
    const EYE_SETTLE_MS = 150;              // ignore an eye for this long after a blink ends
    const GAZE_RAY_PX_PER_DEG = 1.6;        // drawn ray length per degree, at 480 px frame width
    const eyeState = [
      { baseline: 0, blockedUntil: 0 },
      { baseline: 0, blockedUntil: 0 }
    ];

    function resetEyeState() {
      for (let i = 0; i < eyeState.length; i++) {
        eyeState[i].baseline = 0;
        eyeState[i].blockedUntil = 0;
      }
      phoneTracker.pendingGaze = null;
    }

    // Gaze is reported one frame late: a frame only counts once the frame after it is also clean.
    // The last frame before a blink is detected already has the lid pulling on the iris a little,
    // and it would otherwise be the value the PC holds for the whole blink. 33 ms on the nudge
    // alone is not noticeable; the head pose is not delayed.
    function delayedGaze(current) {
      if (!current) {
        phoneTracker.pendingGaze = null;
        return null;
      }
      const ready = phoneTracker.pendingGaze;
      phoneTracker.pendingGaze = current;
      return ready;
    }

    function computeGaze(landmarks, w, h) {
      if (!landmarks || landmarks.length < 478 || !w || !h) {
        return null;
      }
      const clampUnit = (v) => Math.max(-1, Math.min(1, v));
      const now = performance.now();
      let yawSum = 0;
      let pitchSum = 0;
      let count = 0;
      const irisUsed = [];

      for (let e = 0; e < EYE_DEFS.length; e++) {
        const def = EYE_DEFS[e];
        const a = landmarks[def.cornerA];
        const b = landmarks[def.cornerB];
        const top = landmarks[def.lidTop];
        const bottom = landmarks[def.lidBottom];
        if (!a || !b || !top || !bottom) {
          continue;
        }

        // Pixel space, corners ordered image-left to image-right so +u is your own left
        let ax = a.x * w, ay = a.y * h, bx = b.x * w, by = b.y * h;
        if (bx < ax) {
          const tx = ax, ty = ay;
          ax = bx; ay = by; bx = tx; by = ty;
        }
        const width = Math.hypot(bx - ax, by - ay);
        if (width < 4) {
          continue;
        }
        const ux = (bx - ax) / width, uy = (by - ay) / width;
        const vx = -uy, vy = ux;   // perpendicular, pointing down the image
        const cornerMidX = (ax + bx) / 2, cornerMidY = (ay + by) / 2;

        // The iris nearest this eye, so the pairing does not depend on landmark naming
        let iris = null;
        let bestDist = Infinity;
        for (let i = 0; i < IRIS_CENTRES.length; i++) {
          const c = landmarks[IRIS_CENTRES[i]];
          if (!c) {
            continue;
          }
          const cx = c.x * w, cy = c.y * h;
          const dist = Math.hypot(cx - cornerMidX, cy - cornerMidY);
          if (dist < bestDist) {
            bestDist = dist;
            iris = { x: cx, y: cy };
          }
        }
        if (!iris) {
          continue;
        }

        const topX = top.x * w, topY = top.y * h, botX = bottom.x * w, botY = bottom.y * h;
        const openness = ((botX - topX) * vx + (botY - topY) * vy) / width;
        const state = eyeState[e];
        const blinking = openness < EYE_CLOSED_RATIO || (state.baseline > 0 && openness < state.baseline * EYE_BLINK_FRACTION);
        if (blinking) {
          state.blockedUntil = now + EYE_SETTLE_MS;
          continue;
        }
        if (now < state.blockedUntil) {
          continue;
        }
        // Learn how open this eye normally is; a first reading seeds it, then it drifts slowly
        state.baseline = state.baseline > 0 ? state.baseline + (openness - state.baseline) * EYE_BASELINE_RATE : openness;

        // Both axes against the corner midpoint: the corners hold still through a blink
        const hFrac = ((iris.x - cornerMidX) * ux + (iris.y - cornerMidY) * uy) / width;
        const vFrac = ((iris.x - cornerMidX) * vx + (iris.y - cornerMidY) * vy) / width;
        yawSum += Math.asin(clampUnit(hFrac * EYE_ANGLE_GAIN)) * RAD2DEG;
        pitchSum -= Math.asin(clampUnit(vFrac * EYE_ANGLE_GAIN)) * RAD2DEG;
        count++;
        irisUsed.push(iris);
      }

      if (count === 0) {
        return null;
      }

      const yaw = yawSum / count;
      const pitch = pitchSum / count;

      // One ray per open eye from the iris centre in the direction of gaze, normalised coordinates
      const rayScale = GAZE_RAY_PX_PER_DEG * (w / 480);
      const rays = [];
      for (let i = 0; i < irisUsed.length; i++) {
        const c = irisUsed[i];
        rays.push(c.x / w, c.y / h, (c.x + yaw * rayScale) / w, (c.y - pitch * rayScale) / h);
      }

      return { yaw: yaw, pitch: pitch, rays: rays };
    }

    function poseFromMatrix(d) {
      const fx = d[8], fy = d[9], fz = d[10];   // third column: face forward
      const rx = d[0], ry = d[1];               // first column: face right
      const clampUnit = (v) => Math.max(-1, Math.min(1, v));
      return {
        yaw: Math.atan2(fx, fz) * RAD2DEG,
        pitch: Math.asin(clampUnit(fy)) * RAD2DEG,
        roll: Math.atan2(-ry, rx) * RAD2DEG,
        x: d[12],
        y: d[13],
        z: -d[14]
      };
    }

    // One path per group and a single stroke: thousands of individual strokes per frame
    // is what makes the stock drawing helper slow on phones.
    function strokeConnections(ctx, landmarks, connections, color, lineWidth, w, h) {
      ctx.beginPath();
      for (let i = 0; i < connections.length; i++) {
        const a = landmarks[connections[i].start];
        const b = landmarks[connections[i].end];
        if (!a || !b) {
          continue;
        }
        ctx.moveTo(a.x * w, a.y * h);
        ctx.lineTo(b.x * w, b.y * h);
      }
      ctx.strokeStyle = color;
      ctx.lineWidth = lineWidth;
      ctx.stroke();
    }

    // The line groups that make up the mesh, shared by the on-screen overlay and the frames sent
    // to the desktop panel so both show the same thing. style is the FaceMeshStyle value the PC
    // uses to pick a colour. Built once the MediaPipe module is loaded.
    let meshGroupTable = null;

    function getMeshGroups() {
      if (meshGroupTable) {
        return meshGroupTable;
      }
      const vision = phoneTracker.vision;
      if (!vision) {
        return null;
      }
      const FL = vision.FaceLandmarker;
      meshGroupTable = {
        // ~2500 segments; the single biggest drawing cost, so it is opt-in
        tessellation: { conn: FL.FACE_LANDMARKS_TESSELATION, color: 'rgba(76, 175, 80, 0.35)', width: 0.6, style: 3 },
        outline: [
          { conn: FL.FACE_LANDMARKS_FACE_OVAL, color: '#4caf50', width: 1.5, style: 0 },
          { conn: FL.FACE_LANDMARKS_LEFT_EYE, color: '#60a5fa', width: 1.2, style: 1 },
          { conn: FL.FACE_LANDMARKS_RIGHT_EYE, color: '#60a5fa', width: 1.2, style: 1 },
          { conn: FL.FACE_LANDMARKS_LIPS, color: '#f87171', width: 1.2, style: 2 },
          { conn: irisRing(469), color: '#facc15', width: 1.2, style: 6 },
          { conn: irisRing(474), color: '#facc15', width: 1.2, style: 6 }
        ],
        // Gaze rays are computed segments rather than landmark connections; segs is filled per frame
        gaze: { segs: [], color: '#fbbf24', width: 2, style: 7 }
      };
      return meshGroupTable;
    }

    // The four ring points of an iris as a closed loop of connections
    function irisRing(first) {
      return [
        { start: first, end: first + 1 },
        { start: first + 1, end: first + 2 },
        { start: first + 2, end: first + 3 },
        { start: first + 3, end: first }
      ];
    }

    // Explicit normalised segments (x1, y1, x2, y2, ...) as one stroke
    function strokeSegments(ctx, segs, color, lineWidth, w, h) {
      if (!segs || segs.length < 4) {
        return;
      }
      ctx.beginPath();
      for (let i = 0; i + 3 < segs.length; i += 4) {
        ctx.moveTo(segs[i] * w, segs[i + 1] * h);
        ctx.lineTo(segs[i + 2] * w, segs[i + 3] * h);
      }
      ctx.strokeStyle = color;
      ctx.lineWidth = lineWidth;
      ctx.stroke();
    }

    function drawFaceMesh(landmarks, gaze) {
      const ctx = tracking.overlayCtx;
      const groups = getMeshGroups();
      if (!ctx || !groups) {
        return;
      }

      const w = tracking.overlay.width;
      const h = tracking.overlay.height;

      if (trackingPrefs.mesh === 'off') {
        if (!phoneTracker.overlayClear) {
          ctx.clearRect(0, 0, w, h);
          phoneTracker.overlayClear = true;
        }
        return;
      }

      ctx.clearRect(0, 0, w, h);
      phoneTracker.overlayClear = false;
      if (trackingPrefs.mesh === 'full') {
        const t = groups.tessellation;
        strokeConnections(ctx, landmarks, t.conn, t.color, t.width, w, h);
      }
      for (let i = 0; i < groups.outline.length; i++) {
        const g = groups.outline[i];
        strokeConnections(ctx, landmarks, g.conn, g.color, g.width, w, h);
      }
      if (gaze && gaze.rays.length) {
        strokeSegments(ctx, gaze.rays, groups.gaze.color, groups.gaze.width, w, h);
      }
    }

    function sendPhoneMessage(obj) {
      if (tracking.ws && tracking.ws.readyState === WebSocket.OPEN) {
        tracking.ws.send(JSON.stringify(obj));
      }
    }

    // Face mesh for the desktop panel: the same line segments the overlay draws, packed as 16-bit
    // normalised coordinates. The outline is about 1 KB a frame so it goes at camera rate; the full
    // tessellation is ~20 KB so it is halved. The camera image itself never leaves the phone.
    const MESH_SEND_INTERVAL_MS = 33;
    const MESH_SEND_INTERVAL_FULL_MS = 66;
    let meshLastSent = 0;
    let previewWanted = true;   // the PC can switch this off via the version poll

    function toU16(v) {
      return Math.max(0, Math.min(65535, Math.round(v * 65535)));
    }

    function sendPhoneMesh(landmarks, w, h, gaze) {
      if (!previewWanted || !landmarks) {
        return;
      }
      if (!tracking.ws || tracking.ws.readyState !== WebSocket.OPEN) {
        return;
      }
      const groups = getMeshGroups();
      if (!groups) {
        return;
      }

      const full = trackingPrefs.mesh === 'full';
      const now = Date.now();
      if (now - meshLastSent < (full ? MESH_SEND_INTERVAL_FULL_MS : MESH_SEND_INTERVAL_MS)) {
        return;
      }
      meshLastSent = now;

      // Mesh 'off' on the phone still sends the outline: the PC panel is the check that tracking works
      let list = full ? [groups.tessellation].concat(groups.outline) : groups.outline;
      if (gaze && gaze.rays.length) {
        groups.gaze.segs = gaze.rays;
        list = list.concat([groups.gaze]);
      }
      let bytes = 7;
      for (let i = 0; i < list.length; i++) {
        const g = list[i];
        bytes += 4 + (g.conn ? g.conn.length * 8 : g.segs.length * 2);
      }

      const buf = new ArrayBuffer(bytes);
      const dv = new DataView(buf);
      let o = 0;
      dv.setUint8(o++, 0x4d);   // 'M'
      dv.setUint8(o++, 1);      // format version
      dv.setUint16(o, Math.min(65535, w), true); o += 2;
      dv.setUint16(o, Math.min(65535, h), true); o += 2;
      dv.setUint8(o++, list.length);

      for (let gi = 0; gi < list.length; gi++) {
        const g = list[gi];
        dv.setUint8(o++, g.style);
        dv.setUint8(o++, Math.round(g.width * 10));
        if (g.conn) {
          const conn = g.conn;
          dv.setUint16(o, conn.length, true); o += 2;
          for (let i = 0; i < conn.length; i++) {
            const a = landmarks[conn[i].start];
            const b = landmarks[conn[i].end];
            dv.setUint16(o, toU16(a ? a.x : 0), true); o += 2;
            dv.setUint16(o, toU16(a ? a.y : 0), true); o += 2;
            dv.setUint16(o, toU16(b ? b.x : 0), true); o += 2;
            dv.setUint16(o, toU16(b ? b.y : 0), true); o += 2;
          }
        } else {
          const segs = g.segs;
          dv.setUint16(o, segs.length / 4, true); o += 2;
          for (let i = 0; i < segs.length; i++) {
            dv.setUint16(o, toU16(segs[i]), true); o += 2;
          }
        }
      }

      tracking.ws.send(buf);
    }

    function updatePhoneFps() {
      tracking.frameCount++;
      const now = Date.now();
      const elapsed = (now - tracking.lastFpsUpdate) / 1000;
      if (elapsed >= 1.0) {
        tracking.fps = tracking.frameCount / elapsed;
        document.getElementById('trackingFps').textContent = tracking.fps.toFixed(1) + ' FPS';
        tracking.frameCount = 0;
        tracking.lastFpsUpdate = now;
      }
    }

    // Schedule the next phone-tracking step. requestVideoFrameCallback fires once per camera
    // frame where supported; otherwise fall back to requestAnimationFrame with a frame check.
    function schedulePhoneFrame() {
      const video = tracking.video;
      if (video && typeof video.requestVideoFrameCallback === 'function') {
        phoneTracker.vfc = video.requestVideoFrameCallback(() => {
          phoneTracker.vfc = null;
          phoneTrackingStep(true);
        });
      } else {
        phoneTracker.raf = requestAnimationFrame(() => {
          phoneTracker.raf = null;
          phoneTrackingStep(false);
        });
      }
    }

    function phoneTrackingStep(isNewFrame) {
      if (!tracking.isActive || !tracking.phoneMode) {
        return;
      }

      schedulePhoneFrame();

      const video = tracking.video;
      const landmarker = phoneTracker.landmarker;
      if (!video || !landmarker || video.readyState < 2) {
        return;
      }

      if (!isNewFrame) {
        // rAF path: only run inference when the camera has produced a new frame
        if (video.currentTime === phoneTracker.lastVideoTime) {
          return;
        }
        phoneTracker.lastVideoTime = video.currentTime;
      }

      const w = video.videoWidth;
      const h = video.videoHeight;
      if (w === 0 || h === 0) {
        return;
      }

      // The overlay is drawn at a capped size and stretched by CSS: landmarks are normalised,
      // so a smaller canvas is only fewer pixels to rasterise, not a worse mesh.
      const ow = Math.min(w, MAX_OVERLAY_WIDTH);
      const oh = Math.max(1, Math.round(h * ow / w));
      if (tracking.overlay.width !== ow || tracking.overlay.height !== oh) {
        tracking.overlay.width = ow;
        tracking.overlay.height = oh;
        phoneTracker.overlayClear = true;
      }

      // Hand the video element straight to MediaPipe: it uploads the frame as a GPU texture,
      // with no intermediate 2D canvas copy.
      let result;
      const t0 = performance.now();
      try {
        result = landmarker.detectForVideo(video, t0);
      } catch (err) {
        console.error('[Phone tracking] detect failed:', err);
        setTrackingStatus('Detect error: ' + err.message);
        return;
      }
      const t1 = performance.now();
      phoneTracker.inferMs = phoneTracker.inferMs * 0.9 + (t1 - t0) * 0.1;

      const faces = result && result.faceLandmarks ? result.faceLandmarks : [];
      const matrices = result && result.facialTransformationMatrixes ? result.facialTransformationMatrixes : [];
      const showReadout = t1 - phoneTracker.lastReadoutAt >= READOUT_INTERVAL_MS;
      const readout = showReadout ? document.getElementById('poseReadout') : null;
      if (showReadout) {
        phoneTracker.lastReadoutAt = t1;
      }

      if (faces.length > 0 && matrices.length > 0) {
        const pose = poseFromMatrix(matrices[0].data);
        const gaze = delayedGaze(computeGaze(faces[0], w, h));
        // ts is the phone's own clock at capture; the PC uses it to place the sample on a
        // jitter-free timeline before resampling to the game's rate. gy/gp are eye gaze relative
        // to the head, left out while the eyes are shut so the PC holds the last value.
        const msg = { t: 'pose', yaw: pose.yaw, pitch: pose.pitch, roll: pose.roll, x: pose.x, y: pose.y, z: pose.z, ts: t0 };
        if (gaze) {
          msg.gy = Math.round(gaze.yaw * 10) / 10;
          msg.gp = Math.round(gaze.pitch * 10) / 10;
        }
        sendPhoneMessage(msg);
        phoneTracker.lostSent = false;
        updatePhoneFps();
        drawFaceMesh(faces[0], gaze);
        sendPhoneMesh(faces[0], w, h, gaze);

        if (readout) {
          // Nose tip position in the frame: if this stays near 50% while you move sideways,
          // the phone camera is auto-framing (Samsung 'Video call effects') and hiding the movement.
          const nose = faces[0][1];
          const noseText = nose ? ('nose in frame ' + Math.round(nose.x * 100) + '%, ' + Math.round(nose.y * 100) + '%') : '';
          const gazeText = gaze
            ? ('eyes yaw ' + gaze.yaw.toFixed(1).padStart(6) + '°   pitch ' + gaze.pitch.toFixed(1).padStart(6) + '°')
            : 'eyes closed';
          readout.textContent =
            'yaw ' + pose.yaw.toFixed(1).padStart(6) + '°   pitch ' + pose.pitch.toFixed(1).padStart(6) + '°   roll ' + pose.roll.toFixed(1).padStart(6) + '°\n' +
            'x   ' + pose.x.toFixed(1).padStart(6) + 'cm  y     ' + pose.y.toFixed(1).padStart(6) + 'cm  z    ' + pose.z.toFixed(1).padStart(6) + 'cm\n' +
            gazeText + '\n' +
            'infer ' + phoneTracker.inferMs.toFixed(1) + ' ms on ' + phoneTracker.delegate + ', frame ' + phoneTracker.stepMs.toFixed(1) + ' ms   cam ' + phoneTracker.camInfo + '\n' +
            noseText;
        }
        setTrackingStatus('Tracking on phone');
      } else {
        if (!phoneTracker.overlayClear) {
          tracking.overlayCtx.clearRect(0, 0, tracking.overlay.width, tracking.overlay.height);
          phoneTracker.overlayClear = true;
        }
        if (!phoneTracker.lostSent) {
          sendPhoneMessage({ t: 'lost' });
          phoneTracker.lostSent = true;
        }
        if (readout) {
          readout.textContent = 'No face detected\ninfer ' + phoneTracker.inferMs.toFixed(1) + ' ms on ' + phoneTracker.delegate + ', frame ' + phoneTracker.stepMs.toFixed(1) + ' ms   cam ' + phoneTracker.camInfo;
        }
        setTrackingStatus('Tracking on phone - no face');
      }

      // Whole-step cost (inference plus drawing, sending and DOM), which is what sets the frame rate
      phoneTracker.stepMs = phoneTracker.stepMs * 0.9 + (performance.now() - t0) * 0.1;
    }

    async function startTracking() {
      console.log('[Tracking] Starting face tracking...');
      tracking.phoneMode = isPhoneMode();

      document.getElementById('trackingPanel').style.display = 'block';
      requestAnimationFrame(updateFitLayout);
      document.getElementById('trackingBtn').classList.add('active');
      document.getElementById('trackingBtn').textContent = '⏹ Stop Tracking';
      document.getElementById('poseReadout').style.display = tracking.phoneMode ? 'block' : 'none';
      document.getElementById('videoWrap').classList.toggle('phone', tracking.phoneMode);
      tracking.overlayCtx.clearRect(0, 0, tracking.overlay.width, tracking.overlay.height);

      try {
        if (tracking.phoneMode) {
          await loadMediaPipe();
        }

        // Get camera access
        const camParts = String(trackingPrefs.camRes).split('x');
        const camWidth = tracking.phoneMode ? (parseInt(camParts[0], 10) || 640) : 480;
        const camHeight = tracking.phoneMode ? (parseInt(camParts[1], 10) || 480) : 360;
        const constraints = {
          video: {
            width: { ideal: camWidth },
            height: { ideal: camHeight },
            frameRate: { ideal: 30 },
            facingMode: 'user'
          }
        };

        setTrackingStatus('Starting camera...');
        tracking.stream = await navigator.mediaDevices.getUserMedia(constraints);
        tracking.video.srcObject = tracking.stream;

        await tracking.video.play();

        // What the camera actually agreed to: if this says 15 fps the room is too dark for
        // the camera, and no amount of processing speed will help
        try {
          const settings = tracking.stream.getVideoTracks()[0].getSettings();
          phoneTracker.camInfo = (settings.width || '?') + 'x' + (settings.height || '?') + '@' +
            (settings.frameRate ? Math.round(settings.frameRate) : '?') + 'fps';
        } catch (e) {
          phoneTracker.camInfo = camWidth + 'x' + camHeight;
        }
        phoneTracker.stepMs = 0;
        phoneTracker.lastReadoutAt = 0;
        lastTrackingStatus = null;

        console.log('[Tracking] Camera started', phoneTracker.camInfo);

        // Connect WebSocket: JPEG frames to /video, or poses to /pose
        const protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
        const wsUrl = `${protocol}//${window.location.host}/` + (tracking.phoneMode ? 'pose' : 'video');

        tracking.ws = new WebSocket(wsUrl);
        tracking.ws.binaryType = 'arraybuffer';

        tracking.ws.onopen = () => {
          console.log('[Tracking] WebSocket connected');
          tracking.frameCount = 0;
          tracking.lastFpsUpdate = Date.now();

          if (tracking.phoneMode) {
            setTrackingStatus('Tracking on phone');
            phoneTracker.lastVideoTime = -1;
            phoneTracker.lostSent = false;
            phoneTracker.inferMs = 0;
            resetEyeState();
            schedulePhoneFrame();
          } else {
            setTrackingStatus('Streaming');
            tracking.frameInterval = setInterval(captureAndSendFrame, 33); // ~30 FPS
          }
        };

        tracking.ws.onerror = (error) => {
          console.error('[Tracking] WebSocket error:', error);
          setTrackingStatus('Connection error');
        };

        tracking.ws.onclose = () => {
          console.log('[Tracking] WebSocket closed');
          stopTracking();
        };

        tracking.isActive = true;
        document.body.classList.add('tracking-live');
        tracking.previewVisible = true;
        const previewBtn = document.getElementById('previewBtn');
        previewBtn.style.display = 'block';
        previewBtn.setAttribute('aria-pressed', 'true');
        previewBtn.textContent = '⏹ Stop Preview';

      } catch (error) {
        console.error('[Tracking] Error starting:', error);
        const message = error && error.message ? error.message : String(error);
        stopTracking(true);
        document.getElementById('trackingPanel').style.display = 'block';
        requestAnimationFrame(updateFitLayout);
        setTrackingStatus('Error: ' + message);
      }
    }

    function captureAndSendFrame() {
      if (!tracking.video || !tracking.canvas || !tracking.ctx || !tracking.ws) {
        return;
      }

      if (tracking.ws.readyState !== WebSocket.OPEN) {
        return;
      }

      // Draw video to canvas
      tracking.ctx.drawImage(tracking.video, 0, 0, tracking.canvas.width, tracking.canvas.height);

      // Get image data and convert to grayscale
      const imageData = tracking.ctx.getImageData(0, 0, tracking.canvas.width, tracking.canvas.height);
      const data = imageData.data;

      for (let i = 0; i < data.length; i += 4) {
        const gray = 0.299 * data[i] + 0.587 * data[i + 1] + 0.114 * data[i + 2];
        data[i] = data[i + 1] = data[i + 2] = gray;
      }

      tracking.ctx.putImageData(imageData, 0, 0);

      // Convert to JPEG and send via WebSocket
      tracking.canvas.toBlob((blob) => {
        if (blob && tracking.ws && tracking.ws.readyState === WebSocket.OPEN) {
          blob.arrayBuffer().then(buffer => {
            tracking.ws.send(buffer);

            // Update FPS counter
            tracking.frameCount++;
            const now = Date.now();
            const elapsed = (now - tracking.lastFpsUpdate) / 1000;
            if (elapsed >= 1.0) {
              tracking.fps = tracking.frameCount / elapsed;
              document.getElementById('trackingFps').textContent = tracking.fps.toFixed(1) + ' FPS';
              tracking.frameCount = 0;
              tracking.lastFpsUpdate = now;
            }
          });
        }
      }, 'image/jpeg', 0.6);
    }

    function stopTracking(keepPanelOpen) {
      console.log('[Tracking] Stopping...');

      tracking.isActive = false;
      document.body.classList.remove('tracking-live');

      if (tracking.frameInterval) {
        clearInterval(tracking.frameInterval);
        tracking.frameInterval = null;
      }

      if (phoneTracker.raf) {
        cancelAnimationFrame(phoneTracker.raf);
        phoneTracker.raf = null;
      }

      if (phoneTracker.vfc && tracking.video && typeof tracking.video.cancelVideoFrameCallback === 'function') {
        tracking.video.cancelVideoFrameCallback(phoneTracker.vfc);
      }
      phoneTracker.vfc = null;

      const wrap = document.getElementById('videoWrap');
      if (wrap) {
        wrap.classList.remove('phone');
      }

      if (tracking.overlayCtx && tracking.overlay) {
        tracking.overlayCtx.clearRect(0, 0, tracking.overlay.width, tracking.overlay.height);
        phoneTracker.overlayClear = true;
      }

      if (tracking.ws) {
        const ws = tracking.ws;
        tracking.ws = null;
        ws.onclose = null;
        ws.close();
      }

      if (tracking.stream) {
        tracking.stream.getTracks().forEach(track => track.stop());
        tracking.stream = null;
      }

      if (tracking.video) {
        tracking.video.srcObject = null;
      }

      if (!keepPanelOpen) {
        document.getElementById('trackingPanel').style.display = 'none';
      }
      requestAnimationFrame(updateFitLayout);
      document.getElementById('trackingBtn').classList.remove('active');
      document.getElementById('trackingBtn').textContent = '📹 Face Tracking';
      tracking.previewVisible = true;
      const previewBtn = document.getElementById('previewBtn');
      previewBtn.style.display = 'none';
      previewBtn.setAttribute('aria-pressed', 'true');
      previewBtn.textContent = '⏹ Stop Preview';
      lastTrackingStatus = null;
      document.getElementById('trackingStatusText').textContent = 'Ready';
      document.getElementById('trackingFps').textContent = '0 FPS';
      document.getElementById('poseReadout').style.display = 'none';
    }

    function toggleTracking() {
      if (tracking.isActive) {
        stopTracking();
      } else {
        startTracking();
      }
    }

    function togglePreview() {
      if (!tracking.isActive) return;

      tracking.previewVisible = !tracking.previewVisible;
      document.getElementById('trackingPanel').style.display = tracking.previewVisible ? 'block' : 'none';
      const previewBtn = document.getElementById('previewBtn');
      previewBtn.setAttribute('aria-pressed', tracking.previewVisible ? 'true' : 'false');
      previewBtn.textContent = tracking.previewVisible ? '⏹ Stop Preview' : '▶ Show Preview';
      requestAnimationFrame(updateFitLayout);
    }

    // Use the same centring operation as the desktop's Center view control.
    // A request starts a settling/averaging window; it is not an immediate completion.
    document.getElementById('recenterTracking').addEventListener('click', async () => {
      const button = document.getElementById('recenterTracking');
      const label = document.getElementById('recenterLabel');
      if (button.getAttribute('aria-busy') === 'true') return;
      if (!tracking.isActive) { showToast('Start Face Tracking, then tap the preview to recentre.'); return; }
      button.setAttribute('aria-busy', 'true');
      label.textContent = 'SENDING…';
      try {
        const controller = new AbortController();
        const timeout = setTimeout(() => controller.abort(), 5000);
        let res;
        let data;
        try {
          res = await fetch('/tracking/center', { method: 'POST', signal: controller.signal });
          data = await res.json();
        } finally { clearTimeout(timeout); }
        if (!res.ok || !data.success) throw new Error(data.message || 'Recentre request failed.');
        label.textContent = 'LOOK STRAIGHT AHEAD';
        statusEl.textContent = data.message;
        showToast(data.message);
        await new Promise(resolve => setTimeout(resolve, 4500));
      } catch (err) {
        showToast(err.name === 'AbortError' ? 'Recentre timed out. Check the PC connection.' : (err.message || 'Could not recentre.'));
      } finally {
        button.setAttribute('aria-busy', 'false');
        label.textContent = '⌖ RECENTRE';
      }
    });

    // Initialize tracking
    initTracking();

    // Tracking button handler
    const trackingBtn = document.getElementById('trackingBtn');
    if (trackingBtn) {
      trackingBtn.addEventListener('click', toggleTracking);
    }
    const previewBtn = document.getElementById('previewBtn');
    if (previewBtn) {
      previewBtn.addEventListener('click', togglePreview);
    }

    loadConfig();

    // Resume tracking after a self-triggered reload; camera permission is already granted on this origin
    try {
      if (sessionStorage.getItem(RESUME_KEY) === '1') {
        sessionStorage.removeItem(RESUME_KEY);
        console.log('[Tracking] Resuming after page reload');
        setTimeout(startTracking, 500);
      }
    } catch (e) {
      // Storage unavailable
    }
  </script>
</body>
</html>";
        }

        private static async Task HandleIconRequest(HttpContext context, string? remainingPath)
        {
            if (context == null)
            {
                return;
            }

            var fileName = Path.GetFileName(remainingPath ?? string.Empty);
            if (string.IsNullOrWhiteSpace(fileName) || !fileName.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = 404;
                return;
            }

            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", fileName);
            if (!File.Exists(iconPath))
            {
                context.Response.StatusCode = 404;
                return;
            }

            context.Response.ContentType = "image/svg+xml";
            var svg = await File.ReadAllTextAsync(iconPath);
            await context.Response.WriteAsync(svg);
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
                await _keyboardService.SendKeyPressAsync(request.Key, request.HoldMs);

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

        private async Task HandleVideoWebSocket(HttpContext context)
        {
            Console.WriteLine("[HttpCommandServer] Entry: HandleVideoWebSocket");

            if (context == null)
            {
                Console.WriteLine("[HttpCommandServer] Context is null");
                return;
            }

            WebSocket? webSocket = null;

            // One-slot queue: the worker always sees the newest frame and stale frames are dropped,
            // so inference never falls behind the camera and poses are produced in order.
            var frameQueue = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(1)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = true
            });

            var worker = Task.Run(() => ProcessFrameQueueAsync(frameQueue.Reader));

            try
            {
                webSocket = await context.WebSockets.AcceptWebSocketAsync();
                Console.WriteLine("[HttpCommandServer] WebSocket connection established");

                var chunk = new byte[64 * 1024];
                var message = new MemoryStream();
                var ackMessage = Encoding.UTF8.GetBytes("OK");
                var frameStartTime = DateTime.UtcNow;

                while (webSocket.State == WebSocketState.Open)
                {
                    message.SetLength(0);
                    WebSocketReceiveResult result;

                    // Accumulate fragments until the full message has arrived
                    do
                    {
                        result = await webSocket.ReceiveAsync(new ArraySegment<byte>(chunk), CancellationToken.None);

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            break;
                        }

                        if (message.Length + result.Count > MaxFrameBytes)
                        {
                            throw new InvalidOperationException($"Video frame exceeds {MaxFrameBytes} bytes");
                        }

                        message.Write(chunk, 0, result.Count);
                    }
                    while (!result.EndOfMessage);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        Console.WriteLine("[HttpCommandServer] WebSocket close requested");
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                        break;
                    }

                    if (result.MessageType != WebSocketMessageType.Binary || message.Length == 0)
                    {
                        continue;
                    }

                    var frameData = message.ToArray();

                    lock (_frameLock)
                    {
                        _latestFrame = frameData;
                        _frameCount++;

                        var elapsed = (DateTime.UtcNow - frameStartTime).TotalSeconds;
                        if (elapsed > 0)
                        {
                            _currentFps = _frameCount / elapsed;
                        }

                        _lastFrameTime = DateTime.UtcNow;
                    }

                    FrameReceived?.Invoke(this, frameData);

                    // Hand to the tracking worker; replaces any frame it has not picked up yet
                    frameQueue.Writer.TryWrite(frameData);

                    await webSocket.SendAsync(new ArraySegment<byte>(ackMessage), WebSocketMessageType.Text, true, CancellationToken.None);
                }

                Console.WriteLine("[HttpCommandServer] WebSocket connection closed");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HttpCommandServer] WebSocket error: {ex.Message}");

                if (webSocket != null && webSocket.State == WebSocketState.Open)
                {
                    try
                    {
                        await webSocket.CloseAsync(WebSocketCloseStatus.InternalServerError, ex.Message, CancellationToken.None);
                    }
                    catch
                    {
                        // Ignore close errors
                    }
                }
            }
            finally
            {
                frameQueue.Writer.TryComplete();

                try
                {
                    await worker;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[HttpCommandServer] Frame worker ended with error: {ex.Message}");
                }
            }

            Debug.WriteLine("[HttpCommandServer] Exit: HandleVideoWebSocket");
        }

        /// <summary>
        /// Decode a face mesh frame sent by the page script (see sendPhoneMesh there). Little-endian layout:
        /// u8 magic 'M', u8 version 1, u16 width, u16 height, u8 groupCount, then per group
        /// u8 style, u8 lineWidth*10, u16 segmentCount and segmentCount * 4 * u16 coordinates scaled 0..65535.
        /// Returns null for anything that does not fit, so a bad frame is dropped rather than drawn.
        /// </summary>
        internal static FaceMeshFrame? ParseMeshFrame(byte[] data, int length)
        {
            if (data == null || length < 7 || length > data.Length)
            {
                return null;
            }

            if (data[0] != 0x4D || data[1] != 1)
            {
                return null;
            }

            var span = new ReadOnlySpan<byte>(data, 0, length);
            var offset = 2;
            int width = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(offset));
            offset += 2;
            int height = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(offset));
            offset += 2;
            int groupCount = data[offset++];

            const float Scale = 1f / 65535f;
            var groups = new FaceMeshGroup[groupCount];
            for (int g = 0; g < groupCount; g++)
            {
                if (offset + 4 > length)
                {
                    return null;
                }

                var style = (FaceMeshStyle)data[offset++];
                var lineWidth = data[offset++] / 10f;
                int segmentCount = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(offset));
                offset += 2;

                var bytesNeeded = segmentCount * 8;
                if (offset + bytesNeeded > length)
                {
                    return null;
                }

                var segments = new float[segmentCount * 4];
                for (int i = 0; i < segments.Length; i++)
                {
                    segments[i] = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(offset)) * Scale;
                    offset += 2;
                }

                groups[g] = new FaceMeshGroup(style, lineWidth, segments);
            }

            if (offset != length)
            {
                Debug.WriteLine($"[HttpCommandServer] Mesh frame has {length - offset} trailing bytes");
            }

            return new FaceMeshFrame(width, height, groups);
        }

        /// <summary>
        /// Receives poses computed on the phone by MediaPipe as small JSON text messages:
        /// {"t":"pose","yaw":..,"pitch":..,"roll":..,"x":..,"y":..,"z":..,"gy":..,"gp":..} or {"t":"lost"}.
        /// Angles in degrees, translation in centimetres, same conventions as the PC tracker.
        /// gy and gp are eye gaze yaw and pitch relative to the head and are absent while the eyes are shut.
        /// </summary>
        private async Task HandlePoseWebSocket(HttpContext context)
        {
            if (context == null)
            {
                return;
            }

            WebSocket? webSocket = null;
            const int MaxPoseMessageBytes = 512 * 1024;   // poses are tiny; a full-tessellation mesh frame is ~20 KB

            try
            {
                webSocket = await context.WebSockets.AcceptWebSocketAsync();
                Console.WriteLine("[HttpCommandServer] Pose WebSocket connection established");

                var chunk = new byte[16 * 1024];
                var message = new MemoryStream();

                lock (_frameLock)
                {
                    _phonePoseCount = 0;
                    _phonePoseWindowStart = DateTime.UtcNow;
                    _phonePoseRate = 0;
                }

                while (webSocket.State == WebSocketState.Open)
                {
                    message.SetLength(0);
                    WebSocketReceiveResult result;

                    do
                    {
                        result = await webSocket.ReceiveAsync(new ArraySegment<byte>(chunk), CancellationToken.None);

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            break;
                        }

                        if (message.Length + result.Count > MaxPoseMessageBytes)
                        {
                            throw new InvalidOperationException($"Pose message exceeds {MaxPoseMessageBytes} bytes");
                        }

                        message.Write(chunk, 0, result.Count);
                    }
                    while (!result.EndOfMessage);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        Console.WriteLine("[HttpCommandServer] Pose WebSocket close requested");
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                        break;
                    }

                    if (message.Length == 0)
                    {
                        continue;
                    }

                    // Binary on this socket is a face mesh frame for the desktop panel
                    if (result.MessageType == WebSocketMessageType.Binary)
                    {
                        if (!PreviewEnabled)
                        {
                            continue;
                        }

                        var mesh = ParseMeshFrame(message.GetBuffer(), (int)message.Length);
                        if (mesh == null)
                        {
                            Debug.WriteLine($"[HttpCommandServer] Ignoring malformed mesh frame of {message.Length} bytes");
                            continue;
                        }

                        PhoneMeshReceived?.Invoke(this, mesh);
                        continue;
                    }

                    if (result.MessageType != WebSocketMessageType.Text)
                    {
                        continue;
                    }

                    var pose = ParsePhonePose(message.GetBuffer(), (int)message.Length, out bool lost, out double? sourceTimestampMs);

                    if (lost)
                    {
                        _poseOutput?.NotifyLost();
                        PhonePoseReceived?.Invoke(this, null);
                        PoseLost?.Invoke(this, EventArgs.Empty);
                        continue;
                    }

                    if (pose == null)
                    {
                        continue;
                    }

                    lock (_frameLock)
                    {
                        _phonePoseCount++;
                        var elapsed = (DateTime.UtcNow - _phonePoseWindowStart).TotalSeconds;
                        if (elapsed >= 1.0)
                        {
                            _phonePoseRate = _phonePoseCount / elapsed;
                            _phonePoseCount = 0;
                            _phonePoseWindowStart = DateTime.UtcNow;
                        }
                    }

                    PhonePoseReceived?.Invoke(this, pose);

                    if (_poseOutput != null)
                    {
                        await _poseOutput.SendPoseAsync(pose, sourceTimestampMs);
                    }
                }

                Console.WriteLine("[HttpCommandServer] Pose WebSocket connection closed");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HttpCommandServer] Pose WebSocket error: {ex.Message}");

                if (webSocket != null && webSocket.State == WebSocketState.Open)
                {
                    try
                    {
                        await webSocket.CloseAsync(WebSocketCloseStatus.InternalServerError, ex.Message, CancellationToken.None);
                    }
                    catch
                    {
                        // Ignore close errors
                    }
                }
            }
            finally
            {
                _poseOutput?.NotifyLost();
                PhonePoseReceived?.Invoke(this, null);
                PoseLost?.Invoke(this, EventArgs.Empty);
            }
        }

        private static HeadPose? ParsePhonePose(byte[] buffer, int length, out bool lost, out double? sourceTimestampMs)
        {
            lost = false;
            sourceTimestampMs = null;

            try
            {
                using var document = JsonDocument.Parse(new ReadOnlyMemory<byte>(buffer, 0, length));
                var root = document.RootElement;

                if (root.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                if (root.TryGetProperty("t", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() == "lost")
                {
                    lost = true;
                    return null;
                }

                double Read(string name)
                {
                    if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var d))
                    {
                        return d;
                    }

                    return double.NaN;
                }

                var pose = new HeadPose
                {
                    Yaw = Read("yaw"),
                    Pitch = Read("pitch"),
                    Roll = Read("roll"),
                    X = Read("x"),
                    Y = Read("y"),
                    Z = Read("z")
                };

                if (double.IsNaN(pose.Yaw) || double.IsNaN(pose.Pitch) || double.IsNaN(pose.Roll)
                    || double.IsNaN(pose.X) || double.IsNaN(pose.Y) || double.IsNaN(pose.Z))
                {
                    return null;
                }

                // Eye gaze relative to the head; the phone leaves it out while the eyes are closed
                var gazeYaw = Read("gy");
                var gazePitch = Read("gp");
                if (!double.IsNaN(gazeYaw) && !double.IsNaN(gazePitch))
                {
                    pose.HasGaze = true;
                    pose.GazeYaw = gazeYaw;
                    pose.GazePitch = gazePitch;
                }

                // Capture time on the phone's clock (performance.now), used to place the sample on
                // a jitter-free timeline for resampling
                var ts = Read("ts");
                if (!double.IsNaN(ts) && ts >= 0)
                {
                    sourceTimestampMs = ts;
                }

                return pose;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// Poses per second arriving from the phone
        /// </summary>
        public double GetPhonePoseRate()
        {
            lock (_frameLock)
            {
                return _phonePoseRate;
            }
        }

        /// <summary>
        /// Single consumer for the video frame queue. Runs inference serially so poses
        /// are emitted in frame order and smoothing sees a monotonic sequence.
        /// </summary>
        private async Task ProcessFrameQueueAsync(ChannelReader<byte[]> reader)
        {
            if (reader == null)
            {
                return;
            }

            await foreach (var frameData in reader.ReadAllAsync())
            {
                if (_faceTrackingService == null || !_faceTrackingService.IsInitialized)
                {
                    continue;
                }

                try
                {
                    var pose = await _faceTrackingService.ProcessFrameAsync(frameData);
                    if (pose == null)
                    {
                        _poseOutput?.NotifyLost();
                        PoseLost?.Invoke(this, EventArgs.Empty);
                        continue;
                    }

                    PoseDetected?.Invoke(this, pose);

                    if (_poseOutput != null)
                    {
                        await _poseOutput.SendPoseAsync(pose);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[HttpCommandServer] Error processing frame: {ex.Message}");
                    Console.WriteLine($"[HttpCommandServer] Stack trace: {ex.StackTrace}");
                }
            }
        }

        /// <summary>
        /// Get the latest received frame
        /// </summary>
        public byte[]? GetLatestFrame()
        {
            lock (_frameLock)
            {
                return _latestFrame;
            }
        }

        /// <summary>
        /// Get current FPS
        /// </summary>
        public double GetCurrentFps()
        {
            lock (_frameLock)
            {
                return _currentFps;
            }
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
