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

        // Video streaming state
        private byte[]? _latestFrame;
        private readonly object _frameLock = new object();
        private DateTime _lastFrameTime = DateTime.MinValue;
        private int _frameCount = 0;
        private double _currentFps = 0;

        // Face tracking
        private readonly IFaceTrackingService? _faceTrackingService;
        private readonly OpentrackUdpSender? _opentrackSender;

        public bool IsRunning { get; private set; }

        /// <summary>
        /// Event fired when a new video frame is received
        /// </summary>
        public event EventHandler<byte[]>? FrameReceived;

        /// <summary>
        /// Event fired when head pose is detected
        /// </summary>
        public event EventHandler<HeadPose>? PoseDetected;

        public HttpCommandServer(
            IKeyboardService keyboardService,
            IConfigurationService configService,
            IFaceTrackingService? faceTrackingService = null,
            OpentrackUdpSender? opentrackSender = null)
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
            _opentrackSender = opentrackSender;

            Debug.WriteLine($"[HttpCommandServer] Face tracking enabled: {_faceTrackingService != null}");
            Debug.WriteLine($"[HttpCommandServer] Opentrack enabled: {_opentrackSender != null}");
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
                                        listenOptions.UseHttps();
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
                                            listenOptions.UseHttps();
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
                                        listenOptions.UseHttps();
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
                                        version = "1.0.0"
                                    });
                                    await context.Response.WriteAsync(healthJson);
                                }
                                else if (context.Request.Path == "/config" && context.Request.Method == "GET")
                                {
                                    await HandleConfigGet(context);
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

            AppConfig configToReturn;

            lock (_configLock)
            {
                configToReturn = _currentConfig ?? new AppConfig();
            }

            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(configToReturn));

            Debug.WriteLine("[HttpCommandServer] Exit: HandleConfigGet");
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
            await context.Response.WriteAsync(GetWebUiHtml());

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
    #videoCanvas {
      width: 100%;
      height: auto;
      border-radius: 8px;
      background: #000;
    }
    .tracking-btn.active {
      background: #DC2626;
      border-color: #EF4444;
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
      <button id=""voiceBtn"" class=""voice-btn"">🎤 Voice</button>
      <button id=""trackingBtn"" class=""tracking-btn"">📹 Face Tracking</button>
    </div>
    <div id=""trackingPanel"" class=""tracking-panel"" style=""display:none;"">
      <div class=""tracking-status"">
        <span id=""trackingStatusText"">Ready</span>
        <span id=""trackingFps"">0 FPS</span>
      </div>
      <canvas id=""videoCanvas"" width=""480"" height=""360""></canvas>
      <video id=""videoPreview"" autoplay playsinline style=""display:none;""></video>
    </div>
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
    const iconCache = new Map();

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

    async function loadConfig() {
      statusEl.textContent = 'Loading buttons...';
      gridEl.innerHTML = '';
      try {
        const res = await fetch('/config');
        const config = await res.json();
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
            key.textContent = button.key || '';

            btn.appendChild(iconWrap);
            btn.appendChild(label);
            btn.appendChild(key);
            btn.addEventListener('click', () => sendCommand(button));
            grid.appendChild(btn);
          }
          section.appendChild(grid);
          gridEl.appendChild(section);
        }
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
      frameInterval: null,
      fps: 0,
      frameCount: 0,
      lastFpsUpdate: Date.now()
    };

    function initTracking() {
      tracking.video = document.getElementById('videoPreview');
      tracking.canvas = document.getElementById('videoCanvas');
      tracking.ctx = tracking.canvas.getContext('2d');
    }

    async function startTracking() {
      console.log('[Tracking] Starting face tracking...');

      try {
        // Get camera access
        const constraints = {
          video: {
            width: { ideal: 480 },
            height: { ideal: 360 },
            frameRate: { ideal: 30 },
            facingMode: 'user'
          }
        };

        tracking.stream = await navigator.mediaDevices.getUserMedia(constraints);
        tracking.video.srcObject = tracking.stream;

        await tracking.video.play();

        console.log('[Tracking] Camera started');

        // Connect WebSocket
        const protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
        const wsUrl = `${protocol}//${window.location.host}/video`;

        tracking.ws = new WebSocket(wsUrl);
        tracking.ws.binaryType = 'arraybuffer';

        tracking.ws.onopen = () => {
          console.log('[Tracking] WebSocket connected');
          document.getElementById('trackingStatusText').textContent = 'Streaming';

          // Start capturing and sending frames
          tracking.frameInterval = setInterval(captureAndSendFrame, 33); // ~30 FPS
        };

        tracking.ws.onerror = (error) => {
          console.error('[Tracking] WebSocket error:', error);
          document.getElementById('trackingStatusText').textContent = 'Connection error';
        };

        tracking.ws.onclose = () => {
          console.log('[Tracking] WebSocket closed');
          stopTracking();
        };

        tracking.isActive = true;
        document.getElementById('trackingPanel').style.display = 'block';
        document.getElementById('trackingBtn').classList.add('active');
        document.getElementById('trackingBtn').textContent = '⏹ Stop Tracking';

      } catch (error) {
        console.error('[Tracking] Error starting:', error);
        document.getElementById('trackingStatusText').textContent = 'Error: ' + error.message;
        stopTracking();
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

    function stopTracking() {
      console.log('[Tracking] Stopping...');

      tracking.isActive = false;

      if (tracking.frameInterval) {
        clearInterval(tracking.frameInterval);
        tracking.frameInterval = null;
      }

      if (tracking.ws) {
        tracking.ws.close();
        tracking.ws = null;
      }

      if (tracking.stream) {
        tracking.stream.getTracks().forEach(track => track.stop());
        tracking.stream = null;
      }

      if (tracking.video) {
        tracking.video.srcObject = null;
      }

      document.getElementById('trackingPanel').style.display = 'none';
      document.getElementById('trackingBtn').classList.remove('active');
      document.getElementById('trackingBtn').textContent = '📹 Face Tracking';
      document.getElementById('trackingStatusText').textContent = 'Ready';
      document.getElementById('trackingFps').textContent = '0 FPS';
    }

    function toggleTracking() {
      if (tracking.isActive) {
        stopTracking();
      } else {
        startTracking();
      }
    }

    // Initialize tracking
    initTracking();

    // Tracking button handler
    const trackingBtn = document.getElementById('trackingBtn');
    if (trackingBtn) {
      trackingBtn.addEventListener('click', toggleTracking);
    }

    loadConfig();
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

            try
            {
                webSocket = await context.WebSockets.AcceptWebSocketAsync();
                Console.WriteLine("[HttpCommandServer] WebSocket connection established");

                var buffer = new byte[1024 * 1024]; // 1MB buffer for frame data
                var frameStartTime = DateTime.UtcNow;

                while (webSocket.State == WebSocketState.Open)
                {
                    var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        Console.WriteLine("[HttpCommandServer] WebSocket close requested");
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                        break;
                    }

                    if (result.MessageType == WebSocketMessageType.Binary)
                    {
                        Console.WriteLine($"[HttpCommandServer] Received frame: {result.Count} bytes");

                        // Store the frame
                        var frameData = new byte[result.Count];
                        Array.Copy(buffer, 0, frameData, 0, result.Count);

                        lock (_frameLock)
                        {
                            _latestFrame = frameData;
                            _frameCount++;

                            // Calculate FPS
                            var elapsed = (DateTime.UtcNow - frameStartTime).TotalSeconds;
                            if (elapsed > 0)
                            {
                                _currentFps = _frameCount / elapsed;
                            }

                            _lastFrameTime = DateTime.UtcNow;
                        }

                        // Fire event for UI update
                        FrameReceived?.Invoke(this, frameData);

                        // Process frame with face tracking service (async, don't wait)
                        if (_faceTrackingService == null)
                        {
                            Console.WriteLine("[HttpCommandServer] Face tracking service is null - skipping processing");
                        }
                        else if (!_faceTrackingService.IsInitialized)
                        {
                            Console.WriteLine("[HttpCommandServer] Face tracking service not initialized - skipping processing");
                        }
                        else
                        {
                            Console.WriteLine("[HttpCommandServer] Processing frame with face tracking service");
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    Console.WriteLine($"[HttpCommandServer] Task.Run started, frame size: {frameData.Length}");
                                    var pose = await _faceTrackingService.ProcessFrameAsync(frameData);
                                    Console.WriteLine($"[HttpCommandServer] ProcessFrameAsync completed, pose is null: {pose == null}");
                                    if (pose != null)
                                    {
                                        Console.WriteLine($"[HttpCommandServer] Pose detected: X={pose.X:F2}, Y={pose.Y:F2}, Z={pose.Z:F2}");
                                        // Fire event for UI update
                                        PoseDetected?.Invoke(this, pose);

                                        // Send to Opentrack
                                        if (_opentrackSender != null && _opentrackSender.IsConnected)
                                        {
                                            await _opentrackSender.SendPoseAsync(pose);
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"[HttpCommandServer] Error processing frame: {ex.Message}");
                                    Console.WriteLine($"[HttpCommandServer] Stack trace: {ex.StackTrace}");
                                }
                            });
                        }

                        // Send acknowledgment
                        var ackMessage = Encoding.UTF8.GetBytes("OK");
                        await webSocket.SendAsync(new ArraySegment<byte>(ackMessage), WebSocketMessageType.Text, true, CancellationToken.None);
                    }
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

            Debug.WriteLine("[HttpCommandServer] Exit: HandleVideoWebSocket");
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
