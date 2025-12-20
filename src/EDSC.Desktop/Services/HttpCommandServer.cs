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

        public bool IsRunning { get; private set; }

        public HttpCommandServer(IKeyboardService keyboardService, IConfigurationService configService)
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
                            if (!string.IsNullOrEmpty(bindAddress) && System.Net.IPAddress.TryParse(bindAddress, out var ip))
                            {
                                options.Listen(ip, port);
                                if (!System.Net.IPAddress.IsLoopback(ip))
                                {
                                    options.Listen(System.Net.IPAddress.Loopback, port);
                                }
                            }
                            else
                            {
                                options.ListenAnyIP(port);
                            }
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
    </div>
    <div id=""grid""></div>
  </main>
  <script>
    const statusEl = document.getElementById('status');
    const gridEl = document.getElementById('grid');
    const reloadBtn = document.getElementById('reload');
    const fullscreenBtn = document.getElementById('fullscreen');
    const iconCache = new Map();

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
