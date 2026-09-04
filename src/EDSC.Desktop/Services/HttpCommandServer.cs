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
        /// Event fired when the phone sends a low-rate preview image (with its overlay baked in) alongside poses
        /// </summary>
        public event EventHandler<byte[]>? PreviewFrameReceived;

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
                                            version = "1.2.0"
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

            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(configToReturn));

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
    body.fit-all {
      overflow: hidden;
    }
    body.fit-all header,
    body.fit-all .status,
    body.fit-all .tracking-mode,
    body.fit-all #voiceFeedback,
    body.fit-all #reload,
    body.fit-all #voiceBtn {
      display: none !important;
    }
    body.fit-all main {
      max-width: none;
      padding: 4px;
    }
    body.fit-all .toolbar {
      justify-content: center;
      margin-bottom: 4px;
    }
    body.fit-all .toolbar button {
      padding: 5px 9px;
    }
    #blades,
    #bladeNav {
      display: none;
    }
    body.fit-all #blades {
      display: block;
    }
    body.fit-all.blades-mode #bladeNav {
      display: grid;
      grid-template-columns: repeat(var(--blade-count, 1), minmax(0, 1fr));
      gap: 2px;
      margin-bottom: 4px;
    }
    #bladeNav button {
      min-width: 0;
      overflow: hidden;
      padding: 7px 3px 5px;
      border: 1px solid rgba(255, 255, 255, 0.2);
      clip-path: polygon(7px 0, 100% 0, calc(100% - 7px) 100%, 0 100%);
      color: #fff;
      font-size: clamp(7px, 2.4vw, 10px);
      font-weight: 600;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
    #bladeNav button[aria-selected=""true""] {
      border-bottom-color: #fff;
      filter: brightness(1.3);
      transform: translateY(2px);
    }
    body.fit-all #trackingPanel {
      width: min(180px, 48vw);
      padding: 4px;
      margin: 0 auto 4px auto;
      border-radius: 6px;
    }
    body.fit-all .tracking-status {
      margin-bottom: 3px;
      font-size: 10px;
    }
    body.fit-all #videoCanvas,
    body.fit-all #videoPreview {
      border-radius: 4px;
    }
    body.fit-all .pose-readout {
      display: none !important;
    }
    body.fit-all #grid {
      display: grid;
      grid-template-columns: repeat(var(--fit-columns, 4), minmax(0, 1fr));
      gap: var(--fit-gap, 4px);
    }
    body.fit-all #grid > .category,
    body.fit-all #grid > .category > .grid {
      display: contents;
    }
    body.fit-all #grid > .category > h2 {
      display: none;
    }
    body.fit-all.blades-mode #grid {
      display: block;
    }
    body.fit-all.blades-mode #grid > .category {
      display: none;
    }
    body.fit-all.blades-mode #grid > .category.active-blade {
      display: block;
      margin: 0;
      padding: 8px;
      border-radius: 4px 4px 10px 10px;
    }
    body.fit-all.blades-mode #grid > .category.active-blade > h2 {
      display: block;
      margin: 0 0 6px;
      font-size: 13px;
    }
    body.fit-all.blades-mode #grid > .category.active-blade > .grid {
      display: grid;
      grid-template-columns: repeat(var(--fit-columns, 3), minmax(0, var(--fit-cell-size, 120px)));
      gap: var(--fit-gap, 4px);
      justify-content: center;
    }
    body.fit-all .btn {
      width: 100% !important;
      height: auto !important;
      aspect-ratio: 1;
      min-width: 0;
      padding: 2px;
      border-radius: 7px;
      font-size: var(--fit-label-size, 9px);
      line-height: 1.05;
      overflow: hidden;
    }
    body.fit-all .btn .icon {
      width: var(--fit-icon-size, 24px);
      height: var(--fit-icon-size, 24px);
      margin: 0 auto 2px auto;
    }
    body.fit-all .btn .icon svg {
      width: var(--fit-icon-size, 24px);
      height: var(--fit-icon-size, 24px);
    }
    body.fit-all .btn small {
      margin-top: 2px;
      font-size: var(--fit-key-size, 8px);
      line-height: 1;
    }
  </style>
</head>
<body>
  <header>
    <h1>EDSC Web Control</h1>
    <p>Connected to your PC server</p>
  </header>
  <main>
    <div class=""status"" id=""status"">Loading buttons...</div>
    <div class=""toolbar"">
      <button id=""reload"">Reload config</button>
      <button id=""fullscreen"">Fullscreen</button>
      <button id=""fitAll"" aria-pressed=""false"">Fit all</button>
      <button id=""blades"" aria-pressed=""false"">Blades</button>
      <button id=""voiceBtn"" class=""voice-btn"">🎤 Voice</button>
      <button id=""trackingBtn"" class=""tracking-btn"">📹 Face Tracking</button>
      <button id=""previewBtn"" aria-pressed=""true"" style=""display:none;"">⏹ Stop Preview</button>
    </div>
    <label class=""tracking-mode"">
      <input type=""checkbox"" id=""phoneModeToggle"">
      <span>Track on phone (MediaPipe) - sends pose only, no video</span>
    </label>
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
      <div id=""poseReadout"" class=""pose-readout"" style=""display:none;""></div>
    </div>
    <div id=""bladeNav"" role=""tablist"" aria-label=""Control categories""></div>
    <div id=""voiceFeedback"" class=""voice-feedback"" style=""display:none;"">
      <div class=""voice-status"">
        <span id=""voiceStatusIcon"">⚪</span>
        <span id=""voiceStatusText"">Ready</span>
      </div>
      <div class=""voice-transcript"" id=""voiceTranscript""></div>
      <div class=""voice-match"" id=""voiceMatch""></div>
    </div>
    <div id=""grid""></div>
  </main>
  <script>
    const statusEl = document.getElementById('status');
    const gridEl = document.getElementById('grid');
    const reloadBtn = document.getElementById('reload');
    const fullscreenBtn = document.getElementById('fullscreen');
    const fitAllBtn = document.getElementById('fitAll');
    const bladesBtn = document.getElementById('blades');
    const bladeNavEl = document.getElementById('bladeNav');
    const iconCache = new Map();
    const FIT_ALL_STORAGE_KEY = 'edsc-fit-all';
    const BLADES_STORAGE_KEY = 'edsc-blades';
    let bladesEnabled = false;
    let activeBladeIndex = 0;

    function setActiveBlade(index) {
      const sections = Array.from(gridEl.querySelectorAll(':scope > .category'));
      if (!sections.length) return;

      activeBladeIndex = (index + sections.length) % sections.length;
      sections.forEach((section, sectionIndex) => {
        section.classList.toggle('active-blade', sectionIndex === activeBladeIndex);
      });
      Array.from(bladeNavEl.children).forEach((tab, tabIndex) => {
        const selected = tabIndex === activeBladeIndex;
        tab.setAttribute('aria-selected', selected ? 'true' : 'false');
        tab.tabIndex = selected ? 0 : -1;
      });
      requestAnimationFrame(updateFitLayout);
    }

    function rebuildBladeNav(categories) {
      bladeNavEl.innerHTML = '';
      bladeNavEl.style.setProperty('--blade-count', String(Math.max(1, categories.length)));
      const sections = Array.from(gridEl.querySelectorAll(':scope > .category'));
      categories.forEach((category, index) => {
        const tab = document.createElement('button');
        tab.type = 'button';
        tab.setAttribute('role', 'tab');
        tab.textContent = category;
        tab.title = category;
        const firstButton = sections[index] && sections[index].querySelector('.btn');
        tab.style.background = firstButton ? firstButton.style.background : '#374151';
        tab.addEventListener('click', () => setActiveBlade(index));
        bladeNavEl.appendChild(tab);
      });
      setActiveBlade(Math.min(activeBladeIndex, Math.max(0, categories.length - 1)));
    }

    function setBlades(enabled, persist) {
      bladesEnabled = enabled;
      const active = enabled && document.body.classList.contains('fit-all');
      document.body.classList.toggle('blades-mode', active);
      bladesBtn.setAttribute('aria-pressed', enabled ? 'true' : 'false');
      bladesBtn.textContent = enabled ? 'All icons' : 'Blades';
      if (persist) {
        try {
          localStorage.setItem(BLADES_STORAGE_KEY, enabled ? '1' : '0');
        } catch (err) {
          // Storage is optional; the mode still works for this page load.
        }
      }
      setActiveBlade(activeBladeIndex);
      requestAnimationFrame(updateFitLayout);
    }

    function updateFitLayout() {
      if (!document.body.classList.contains('fit-all')) {
        gridEl.style.removeProperty('--fit-columns');
        gridEl.style.removeProperty('--fit-icon-size');
        gridEl.style.removeProperty('--fit-label-size');
        gridEl.style.removeProperty('--fit-key-size');
        gridEl.style.removeProperty('--fit-cell-size');
        return;
      }

      const isBlades = document.body.classList.contains('blades-mode');
      const buttonScope = isBlades ? gridEl.querySelector('.category.active-blade') : gridEl;
      const buttonCount = buttonScope ? buttonScope.querySelectorAll('.btn').length : 0;
      if (!buttonCount) return;

      const gap = 4;
      const availableWidth = Math.max(240, window.innerWidth - 8);
      const gridTop = gridEl.getBoundingClientRect().top;
      const availableHeight = Math.max(160, window.innerHeight - gridTop - 4);
      let columns = Math.max(2, Math.ceil(Math.sqrt(buttonCount * availableWidth / availableHeight)));
      if (isBlades) {
        columns = Math.max(columns, Math.ceil(availableWidth / 120));
      }
      columns = Math.min(columns, buttonCount);

      function dimensionsFor(columnCount) {
        const rows = Math.ceil(buttonCount / columnCount);
        const cellSize = (availableWidth - gap * (columnCount - 1)) / columnCount;
        return { rows, cellSize, height: rows * cellSize + gap * (rows - 1) };
      }

      let dimensions = dimensionsFor(columns);
      while (columns < buttonCount && dimensions.height > availableHeight) {
        columns += 1;
        dimensions = dimensionsFor(columns);
      }

      const iconSize = Math.max(14, Math.min(32, Math.floor(dimensions.cellSize * 0.42)));
      const labelSize = Math.max(7, Math.min(11, dimensions.cellSize * 0.13));
      const keySize = Math.max(7, Math.min(9, dimensions.cellSize * 0.11));

      gridEl.style.setProperty('--fit-columns', String(columns));
      gridEl.style.setProperty('--fit-icon-size', iconSize + 'px');
      gridEl.style.setProperty('--fit-label-size', labelSize.toFixed(1) + 'px');
      gridEl.style.setProperty('--fit-key-size', keySize.toFixed(1) + 'px');
      gridEl.style.setProperty('--fit-cell-size', Math.min(120, dimensions.cellSize).toFixed(1) + 'px');
    }

    function setFitAll(enabled, persist) {
      document.body.classList.toggle('fit-all', enabled);
      document.body.classList.toggle('blades-mode', enabled && bladesEnabled);
      fitAllBtn.setAttribute('aria-pressed', enabled ? 'true' : 'false');
      fitAllBtn.textContent = enabled ? 'Exit fit' : 'Fit all';
      if (persist) {
        try {
          localStorage.setItem(FIT_ALL_STORAGE_KEY, enabled ? '1' : '0');
        } catch (err) {
          // Storage is optional; the mode still works for this page load.
        }
      }
      requestAnimationFrame(updateFitLayout);
    }

    try {
      bladesEnabled = localStorage.getItem(BLADES_STORAGE_KEY) === '1';
      setFitAll(localStorage.getItem(FIT_ALL_STORAGE_KEY) === '1', false);
      setBlades(bladesEnabled, false);
    } catch (err) {
      setFitAll(false, false);
      setBlades(false, false);
    }

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
    function findBestMatch(commandText) {
      if (!commandText || voiceControl.buttons.length === 0) {
        return null;
      }

      const normalized = commandText.toLowerCase().trim();
      let bestMatch = null;
      let bestScore = 0;

      for (const button of voiceControl.buttons) {
        if (!button) {
          continue;
        }

        const label = (button.label || '').toLowerCase();
        const id = (button.id || '').toLowerCase();

        if (label === normalized || id === normalized) {
          return button;
        }

        let score = 0;

        if (label.includes(normalized)) {
          score += 100;
        } else if (normalized.includes(label)) {
          score += 90;
        }

        const commandWords = normalized.split(/\s+/);
        const labelWords = label.split(/\s+/);

        for (const cmdWord of commandWords) {
          for (const labelWord of labelWords) {
            if (labelWord.includes(cmdWord) || cmdWord.includes(labelWord)) {
              score += 30;
            }

            if (cmdWord.length >= 4 && labelWord.length >= 4) {
              const similarity = calculateSimilarity(cmdWord, labelWord);
              if (similarity > 0.7) {
                score += 20 * similarity;
              }
            }
          }
        }

        if (label.startsWith(normalized)) {
          score += 80;
        }

        if (score > bestScore && score > 30) {
          bestScore = score;
          bestMatch = button;
        }
      }

      console.log('Best match:', bestMatch ? bestMatch.label : 'none', 'Score:', bestScore);
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
          console.log('[Config] PC app restarted with a new page, reloading');
          reloading = true;
          try {
            if (tracking.isActive) {
              sessionStorage.setItem(RESUME_KEY, '1');
            }
          } catch (e) {
            // Storage unavailable; tracking just will not auto-resume
          }
          location.reload();
          return;
        }

        if (typeof v.preview === 'boolean') {
          previewWanted = v.preview;
        }

        const stamp = String(v.version) + ':' + String(v.updatedUtc);
        if (configStamp !== null && stamp !== configStamp) {
          console.log('[Config] Layout changed on the PC, reloading');
          await loadConfig();
        }
      } catch (err) {
        // Server unreachable for the moment; try again next tick
      }
    }
    setInterval(checkConfigVersion, 3000);

    async function loadConfig() {
      statusEl.textContent = 'Loading buttons...';
      gridEl.innerHTML = '';
      try {
        const res = await fetch('/config', { cache: 'no-store' });
        const config = await res.json();
        configStamp = String(config.configVersion) + ':' + String(config.lastUpdatedUtc);
        const buttons = (config && config.buttons) ? config.buttons : [];
        if (!buttons.length) {
          statusEl.textContent = 'No buttons configured.';
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
            const buttonSize = (button.size || 80) * 1.6;
            btn.style.width = buttonSize + 'px';
            btn.style.height = buttonSize + 'px';
            if (!button.key) {
              btn.classList.add('unbound');
              btn.title = 'No keyboard key bound for this action in Elite Dangerous';
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
        rebuildBladeNav(order);
        requestAnimationFrame(updateFitLayout);
      } catch (err) {
        statusEl.textContent = 'Failed to load config.';
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
    fitAllBtn.addEventListener('click', () => {
      setFitAll(!document.body.classList.contains('fit-all'), true);
    });
    bladesBtn.addEventListener('click', () => {
      setBlades(!bladesEnabled, true);
    });
    bladeNavEl.addEventListener('keydown', event => {
      if (event.key === 'ArrowLeft') {
        event.preventDefault();
        setActiveBlade(activeBladeIndex - 1);
        bladeNavEl.children[activeBladeIndex]?.focus();
      } else if (event.key === 'ArrowRight') {
        event.preventDefault();
        setActiveBlade(activeBladeIndex + 1);
        bladeNavEl.children[activeBladeIndex]?.focus();
      }
    });
    let bladeSwipeStartX = null;
    gridEl.addEventListener('touchstart', event => {
      if (document.body.classList.contains('blades-mode') && event.touches.length === 1) {
        bladeSwipeStartX = event.touches[0].clientX;
      }
    }, { passive: true });
    gridEl.addEventListener('touchend', event => {
      if (bladeSwipeStartX === null || !document.body.classList.contains('blades-mode')) return;
      const endX = event.changedTouches[0]?.clientX;
      if (typeof endX === 'number' && Math.abs(endX - bladeSwipeStartX) > 45) {
        setActiveBlade(activeBladeIndex + (endX < bladeSwipeStartX ? 1 : -1));
      }
      bladeSwipeStartX = null;
    }, { passive: true });
    window.addEventListener('resize', () => requestAnimationFrame(updateFitLayout));
    document.addEventListener('fullscreenchange', () => requestAnimationFrame(updateFitLayout));
    fullscreenBtn.addEventListener('click', async () => {
      try {
        if (!document.fullscreenElement) {
          await document.documentElement.requestFullscreen();
          fullscreenBtn.textContent = 'Exit Fullscreen';
        } else {
          await document.exitFullscreen();
          fullscreenBtn.textContent = 'Fullscreen';
        }
      } catch (err) {
        statusEl.textContent = 'Fullscreen not available';
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
    const phoneTracker = {
      vision: null,
      landmarker: null,
      delegate: '',
      loading: null,
      raf: null,
      vfc: null,
      lastVideoTime: -1,
      lostSent: false,
      inferMs: 0
    };

    function setTrackingStatus(text) {
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
      tracking.overlayCtx = tracking.overlay.getContext('2d');

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
    function poseFromMatrix(d) {
      const RAD2DEG = 180 / Math.PI;
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

    function drawFaceMesh(landmarks) {
      const ctx = tracking.overlayCtx;
      const vision = phoneTracker.vision;
      if (!ctx || !vision) {
        return;
      }

      const w = tracking.overlay.width;
      const h = tracking.overlay.height;
      const FL = vision.FaceLandmarker;

      ctx.clearRect(0, 0, w, h);
      strokeConnections(ctx, landmarks, FL.FACE_LANDMARKS_TESSELATION, 'rgba(76, 175, 80, 0.35)', 0.6, w, h);
      strokeConnections(ctx, landmarks, FL.FACE_LANDMARKS_FACE_OVAL, '#4caf50', 1.5, w, h);
      strokeConnections(ctx, landmarks, FL.FACE_LANDMARKS_LEFT_EYE, '#60a5fa', 1.2, w, h);
      strokeConnections(ctx, landmarks, FL.FACE_LANDMARKS_RIGHT_EYE, '#60a5fa', 1.2, w, h);
      strokeConnections(ctx, landmarks, FL.FACE_LANDMARKS_LIPS, '#f87171', 1.2, w, h);
    }

    function sendPhoneMessage(obj) {
      if (tracking.ws && tracking.ws.readyState === WebSocket.OPEN) {
        tracking.ws.send(JSON.stringify(obj));
      }
    }

    // Low-rate preview for the desktop panel: video plus the mesh overlay, small and cheap.
    const PREVIEW_INTERVAL_MS = 150;
    const PREVIEW_WIDTH = 320;
    const previewCanvas = document.createElement('canvas');
    let previewLastSent = 0;
    let previewBusy = false;
    let previewWanted = true;   // the PC can switch this off via the version poll

    function sendPhonePreview(video, w, h) {
      if (!previewWanted) {
        return;
      }
      const now = Date.now();
      if (previewBusy || now - previewLastSent < PREVIEW_INTERVAL_MS) {
        return;
      }
      if (!tracking.ws || tracking.ws.readyState !== WebSocket.OPEN) {
        return;
      }
      previewLastSent = now;

      const pw = PREVIEW_WIDTH;
      const ph = Math.max(1, Math.round(PREVIEW_WIDTH * h / w));
      if (previewCanvas.width !== pw || previewCanvas.height !== ph) {
        previewCanvas.width = pw;
        previewCanvas.height = ph;
      }

      const ctx = previewCanvas.getContext('2d');
      ctx.drawImage(video, 0, 0, pw, ph);
      ctx.drawImage(tracking.overlay, 0, 0, pw, ph);

      previewBusy = true;
      previewCanvas.toBlob((blob) => {
        previewBusy = false;
        if (!blob || !tracking.ws || tracking.ws.readyState !== WebSocket.OPEN) {
          return;
        }
        blob.arrayBuffer().then((buffer) => {
          if (tracking.ws && tracking.ws.readyState === WebSocket.OPEN) {
            tracking.ws.send(buffer);
          }
        });
      }, 'image/jpeg', 0.5);
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

      if (tracking.overlay.width !== w || tracking.overlay.height !== h) {
        tracking.overlay.width = w;
        tracking.overlay.height = h;
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
      phoneTracker.inferMs = phoneTracker.inferMs * 0.9 + (performance.now() - t0) * 0.1;

      const readout = document.getElementById('poseReadout');
      const faces = result && result.faceLandmarks ? result.faceLandmarks : [];
      const matrices = result && result.facialTransformationMatrixes ? result.facialTransformationMatrixes : [];

      if (faces.length > 0 && matrices.length > 0) {
        const pose = poseFromMatrix(matrices[0].data);
        sendPhoneMessage({ t: 'pose', yaw: pose.yaw, pitch: pose.pitch, roll: pose.roll, x: pose.x, y: pose.y, z: pose.z });
        phoneTracker.lostSent = false;
        updatePhoneFps();
        drawFaceMesh(faces[0]);
        sendPhonePreview(video, w, h);

        if (readout) {
          // Nose tip position in the frame: if this stays near 50% while you move sideways,
          // the phone camera is auto-framing (Samsung 'Video call effects') and hiding the movement.
          const nose = faces[0][1];
          const noseText = nose ? ('nose in frame ' + Math.round(nose.x * 100) + '%, ' + Math.round(nose.y * 100) + '%') : '';
          readout.textContent =
            'yaw ' + pose.yaw.toFixed(1).padStart(6) + '°   pitch ' + pose.pitch.toFixed(1).padStart(6) + '°   roll ' + pose.roll.toFixed(1).padStart(6) + '°\n' +
            'x   ' + pose.x.toFixed(1).padStart(6) + 'cm  y     ' + pose.y.toFixed(1).padStart(6) + 'cm  z    ' + pose.z.toFixed(1).padStart(6) + 'cm\n' +
            'infer ' + phoneTracker.inferMs.toFixed(1) + ' ms on ' + phoneTracker.delegate + '  ' + w + 'x' + h + '   ' + noseText;
        }
        setTrackingStatus('Tracking on phone');
      } else {
        tracking.overlayCtx.clearRect(0, 0, tracking.overlay.width, tracking.overlay.height);
        sendPhonePreview(video, w, h);
        if (!phoneTracker.lostSent) {
          sendPhoneMessage({ t: 'lost' });
          phoneTracker.lostSent = true;
        }
        if (readout) {
          readout.textContent = 'No face detected\ninfer ' + phoneTracker.inferMs.toFixed(1) + ' ms on ' + phoneTracker.delegate;
        }
        setTrackingStatus('Tracking on phone - no face');
      }
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
        const constraints = {
          video: {
            width: { ideal: tracking.phoneMode ? 640 : 480 },
            height: { ideal: tracking.phoneMode ? 480 : 360 },
            frameRate: { ideal: 30 },
            facingMode: 'user'
          }
        };

        setTrackingStatus('Starting camera...');
        tracking.stream = await navigator.mediaDevices.getUserMedia(constraints);
        tracking.video.srcObject = tracking.stream;

        await tracking.video.play();

        console.log('[Tracking] Camera started');

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
        /// Receives poses computed on the phone by MediaPipe as small JSON text messages:
        /// {"t":"pose","yaw":..,"pitch":..,"roll":..,"x":..,"y":..,"z":..} or {"t":"lost"}.
        /// Angles in degrees, translation in centimetres, same conventions as the PC tracker.
        /// </summary>
        private async Task HandlePoseWebSocket(HttpContext context)
        {
            if (context == null)
            {
                return;
            }

            WebSocket? webSocket = null;
            const int MaxPoseMessageBytes = 512 * 1024;   // poses are tiny; preview JPEGs are a few tens of KB

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

                    // Binary on this socket is a preview image for the desktop panel
                    if (result.MessageType == WebSocketMessageType.Binary)
                    {
                        if (!PreviewEnabled)
                        {
                            continue;
                        }

                        var preview = message.ToArray();
                        lock (_frameLock)
                        {
                            _latestFrame = preview;
                        }
                        PreviewFrameReceived?.Invoke(this, preview);
                        continue;
                    }

                    if (result.MessageType != WebSocketMessageType.Text)
                    {
                        continue;
                    }

                    var pose = ParsePhonePose(message.GetBuffer(), (int)message.Length, out bool lost);

                    if (lost)
                    {
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
                        await _poseOutput.SendPoseAsync(pose);
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
                PhonePoseReceived?.Invoke(this, null);
                PoseLost?.Invoke(this, EventArgs.Empty);
            }
        }

        private static HeadPose? ParsePhonePose(byte[] buffer, int length, out bool lost)
        {
            lost = false;

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
