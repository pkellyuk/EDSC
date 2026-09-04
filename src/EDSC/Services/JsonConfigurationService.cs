using EDSC.Models;
using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace EDSC.Services
{
    /// <summary>
    /// JSON-based configuration service
    /// </summary>
    public class JsonConfigurationService : IConfigurationService
    {
        private const string CONFIG_FILENAME = "config.json";
        private readonly string _configPath;

        public JsonConfigurationService()
        {
            Debug.WriteLine("[JsonConfigurationService] Entry: Constructor");

            // Per-user AppData: the install folder is read-only under Program Files.
            // A config.json left next to the executable by an older version is migrated once.
            var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EDSC");
            _configPath = Path.Combine(appData, CONFIG_FILENAME);

            try
            {
                Directory.CreateDirectory(appData);

                var legacyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CONFIG_FILENAME);
                if (!File.Exists(_configPath) && File.Exists(legacyPath))
                {
                    File.Copy(legacyPath, _configPath);
                    Debug.WriteLine($"[JsonConfigurationService] Migrated config from {legacyPath}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[JsonConfigurationService] Could not prepare config folder: {ex.Message}");
            }

            Debug.WriteLine($"[JsonConfigurationService] Config path: {_configPath}");
            Debug.WriteLine("[JsonConfigurationService] Exit: Constructor");
        }

        public string GetConfigurationPath()
        {
            Debug.WriteLine("[JsonConfigurationService] GetConfigurationPath called");
            return _configPath;
        }

        public async Task<AppConfig?> LoadConfigurationAsync()
        {
            Debug.WriteLine("[JsonConfigurationService] Entry: LoadConfigurationAsync");

            try
            {
                if (!File.Exists(_configPath))
                {
                    Debug.WriteLine($"[JsonConfigurationService] Config file not found at {_configPath}");
                    Debug.WriteLine("[JsonConfigurationService] Creating default configuration");

                    var defaultConfig = CreateDefaultConfiguration();
                    await SaveConfigurationAsync(defaultConfig);

                    return defaultConfig;
                }

                Debug.WriteLine($"[JsonConfigurationService] Loading config from {_configPath}");

                var json = await File.ReadAllTextAsync(_configPath);

                if (string.IsNullOrEmpty(json))
                {
                    Debug.WriteLine("[JsonConfigurationService] Config file is empty");
                    return CreateDefaultConfiguration();
                }

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    WriteIndented = true
                };

                var config = JsonSerializer.Deserialize<AppConfig>(json, options);

                if (config == null)
                {
                    Debug.WriteLine("[JsonConfigurationService] Failed to deserialize config");
                    return CreateDefaultConfiguration();
                }

                Debug.WriteLine($"[JsonConfigurationService] Configuration loaded successfully: {config}");
                return config;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[JsonConfigurationService] Error loading configuration: {ex.Message}");
                return CreateDefaultConfiguration();
            }
            finally
            {
                Debug.WriteLine("[JsonConfigurationService] Exit: LoadConfigurationAsync");
            }
        }

        public async Task SaveConfigurationAsync(AppConfig config)
        {
            Debug.WriteLine("[JsonConfigurationService] Entry: SaveConfigurationAsync");

            if (config == null)
            {
                Debug.WriteLine("[JsonConfigurationService] Config is null, cannot save");
                return;
            }

            try
            {
                Debug.WriteLine($"[JsonConfigurationService] Saving config to {_configPath}");

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };

                var json = JsonSerializer.Serialize(config, options);

                if (string.IsNullOrEmpty(json))
                {
                    Debug.WriteLine("[JsonConfigurationService] Serialized JSON is empty");
                    return;
                }

                await File.WriteAllTextAsync(_configPath, json);

                Debug.WriteLine("[JsonConfigurationService] Configuration saved successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[JsonConfigurationService] Error saving configuration: {ex.Message}");
            }
            finally
            {
                Debug.WriteLine("[JsonConfigurationService] Exit: SaveConfigurationAsync");
            }
        }

        private AppConfig CreateDefaultConfiguration()
        {
            Debug.WriteLine("[JsonConfigurationService] Entry: CreateDefaultConfiguration");

            var config = new AppConfig
            {
                Server = new ServerConfig
                {
                    Port = 9000,
                    AutoStart = true
                },
                Buttons = new System.Collections.Generic.List<ButtonConfig>
                {
                    new ButtonConfig
                    {
                        Id = "hardpoints",
                        Key = "U",
                        Icon = "build",
                        IconSvg = "hardpoints.svg",
                        Color = "#6B7280",
                        Label = "Hardpoints",
                        Category = "Combat",
                        Size = 80
                    },
                    new ButtonConfig
                    {
                        Id = "landinggear",
                        Key = "L",
                        Icon = "flight_land",
                        IconSvg = "landinggear.svg",
                        Color = "#4B5563",
                        Label = "Landing Gear",
                        Category = "Ship Control",
                        Size = 80
                    },
                    new ButtonConfig
                    {
                        Id = "cargoscoop",
                        Key = "HOME",
                        Icon = "inbox",
                        IconSvg = "cargoscoop.svg",
                        Color = "#92400E",
                        Label = "Cargo Scoop",
                        Category = "Ship Control",
                        Size = 80
                    },
                    new ButtonConfig
                    {
                        Id = "silentrunning",
                        Key = "DELETE",
                        Icon = "visibility_off",
                        IconSvg = "silentrunning.svg",
                        Color = "#7C3AED",
                        Label = "Silent Running",
                        Category = "Combat",
                        Size = 80
                    },
                    new ButtonConfig
                    {
                        Id = "chaff",
                        Key = "C",
                        Icon = "burst",
                        IconSvg = "chaff.svg",
                        Color = "#DC2626",
                        Label = "Chaff",
                        Category = "Combat",
                        Size = 80
                    },
                    new ButtonConfig
                    {
                        Id = "shieldbooster",
                        Key = "B",
                        Icon = "shield",
                        IconSvg = "shieldbooster.svg",
                        Color = "#2563EB",
                        Label = "Shield Booster",
                        Category = "Combat",
                        Size = 80
                    },
                    new ButtonConfig
                    {
                        Id = "fsd",
                        Key = "J",
                        Icon = "travel_explore",
                        IconSvg = "fsd.svg",
                        Color = "#2563EB",
                        Label = "FSD Jump",
                        Category = "Navigation",
                        Size = 80
                    },
                    new ButtonConfig
                    {
                        Id = "orbitlines",
                        Key = "OEM_PLUS",
                        Icon = "timeline",
                        IconSvg = "orbitlines.svg",
                        Color = "#0EA5E9",
                        Label = "Orbit Lines",
                        Category = "Navigation",
                        Size = 80
                    },
                    new ButtonConfig
                    {
                        Id = "targetahead",
                        Key = "T",
                        Icon = "gps_fixed",
                        IconSvg = "targetahead.svg",
                        Color = "#10B981",
                        Label = "Target Ahead",
                        Category = "Targeting",
                        Size = 80
                    },
                    new ButtonConfig
                    {
                        Id = "nexttarget",
                        Key = "G",
                        Icon = "my_location",
                        IconSvg = "nexttarget.svg",
                        Color = "#059669",
                        Label = "Next Target",
                        Category = "Targeting",
                        Size = 80
                    },
                    new ButtonConfig
                    {
                        Id = "highestthreat",
                        Key = "H",
                        Icon = "priority_high",
                        IconSvg = "highestthreat.svg",
                        Color = "#DC2626",
                        Label = "Highest Threat",
                        Category = "Targeting",
                        Size = 80
                    },
                    new ButtonConfig
                    {
                        Id = "nextsubsystem",
                        Key = "Y",
                        Icon = "swap_horiz",
                        IconSvg = "nextsubsystem.svg",
                        Color = "#F59E0B",
                        Label = "Next Subsystem",
                        Category = "Combat",
                        Size = 80
                    },
                    new ButtonConfig
                    {
                        Id = "nextfiregroup",
                        Key = "N",
                        Icon = "layers",
                        IconSvg = "nextfiregroup.svg",
                        Color = "#F97316",
                        Label = "Next Fire Group",
                        Category = "Combat",
                        Size = 80
                    },
                    new ButtonConfig
                    {
                        Id = "flightassist",
                        Key = "Z",
                        Icon = "alt_route",
                        IconSvg = "flightassist.svg",
                        Color = "#14B8A6",
                        Label = "Flight Assist",
                        Category = "Ship Control",
                        Size = 80
                    },
                    new ButtonConfig
                    {
                        Id = "setzero",
                        Key = "X",
                        Icon = "exposure_zero",
                        IconSvg = "speed0.svg",
                        Color = "#0F766E",
                        Label = "Speed 0%",
                        Category = "Ship Control",
                        Size = 80
                    },
                    new ButtonConfig
                    {
                        Id = "setfifty",
                        Key = "NUMPAD5",
                        Icon = "speed_50",
                        IconSvg = "speed50.svg",
                        Color = "#0F766E",
                        Label = "Speed 50%",
                        Category = "Ship Control",
                        Size = 80
                    },
                    new ButtonConfig
                    {
                        Id = "sethundred",
                        Key = "NUMPAD0",
                        Icon = "speed_100",
                        IconSvg = "speed100.svg",
                        Color = "#0F766E",
                        Label = "Speed 100%",
                        Category = "Ship Control",
                        Size = 80
                    },
                    new ButtonConfig
                    {
                        Id = "leftpanel",
                        Key = "1",
                        Icon = "filter_1",
                        IconSvg = "leftpanel.svg",
                        Color = "#1D4ED8",
                        Label = "Left Panel",
                        Category = "Panels",
                        Size = 80
                    },
                    new ButtonConfig
                    {
                        Id = "commspanel",
                        Key = "2",
                        Icon = "filter_2",
                        IconSvg = "commspanel.svg",
                        Color = "#1E40AF",
                        Label = "Comms Panel",
                        Category = "Panels",
                        Size = 80
                    },
                    new ButtonConfig
                    {
                        Id = "radarpanel",
                        Key = "3",
                        Icon = "filter_3",
                        IconSvg = "radarpanel.svg",
                        Color = "#1E3A8A",
                        Label = "Radar Panel",
                        Category = "Panels",
                        Size = 80
                    },
                    new ButtonConfig
                    {
                        Id = "rightpanel",
                        Key = "4",
                        Icon = "filter_4",
                        IconSvg = "rightpanel.svg",
                        Color = "#1E3A8A",
                        Label = "Right Panel",
                        Category = "Panels",
                        Size = 80
                    },
                    new ButtonConfig
                    {
                        Id = "return",
                        Key = "BACK",
                        Icon = "keyboard_return",
                        IconSvg = "return.svg",
                        Color = "#374151",
                        Label = "Return",
                        Category = "Panels",
                        Size = 80
                    },
                    new ButtonConfig
                    {
                        Id = "escape",
                        Key = "ESCAPE",
                        Icon = "close",
                        IconSvg = "escape.svg",
                        Color = "#374151",
                        Label = "Escape",
                        Category = "Panels",
                        Size = 80
                    },
                    new ButtonConfig
                    {
                        Id = "quickcomms",
                        Key = "ENTER",
                        Icon = "sms",
                        IconSvg = "quickcomms.svg",
                        Color = "#374151",
                        Label = "Quick Comms",
                        Category = "Panels",
                        Size = 80
                    },
                    new ButtonConfig
                    {
                        Id = "hudmode",
                        Key = "M",
                        Icon = "visibility",
                        IconSvg = "hudmode.svg",
                        Color = "#6D28D9",
                        Label = "HUD Mode",
                        Category = "Display",
                        Size = 80
                    },
                    new ButtonConfig
                    {
                        Id = "wingman1",
                        Key = "7",
                        Icon = "group",
                        IconSvg = "wingman.svg",
                        Color = "#2563EB",
                        Label = "Wingman 1",
                        Category = "Wing",
                        Size = 80
                    },
                    new ButtonConfig
                    {
                        Id = "wingman2",
                        Key = "8",
                        Icon = "group",
                        IconSvg = "wingman.svg",
                        Color = "#1D4ED8",
                        Label = "Wingman 2",
                        Category = "Wing",
                        Size = 80
                    },
                    new ButtonConfig
                    {
                        Id = "wingman3",
                        Key = "9",
                        Icon = "group",
                        IconSvg = "wingman.svg",
                        Color = "#1E40AF",
                        Label = "Wingman 3",
                        Category = "Wing",
                        Size = 80
                    },
                    new ButtonConfig
                    {
                        Id = "wingmantarget",
                        Key = "0",
                        Icon = "assistant",
                        IconSvg = "wingmantarget.svg",
                        Color = "#312E81",
                        Label = "Wingman Target",
                        Category = "Wing",
                        Size = 80
                    },
                    new ButtonConfig
                    {
                        Id = "wingnavlock",
                        Key = "OEM_MINUS",
                        Icon = "link",
                        IconSvg = "wingnavlock.svg",
                        Color = "#4B5563",
                        Label = "Wing Nav Lock",
                        Category = "Wing",
                        Size = 80
                    }
                },
                ConfigVersion = 1,
                LastUpdatedUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                LastUpdatedBy = "local"
            };

            Debug.WriteLine($"[JsonConfigurationService] Created default configuration with {config.Buttons.Count} buttons");
            Debug.WriteLine("[JsonConfigurationService] Exit: CreateDefaultConfiguration");

            return config;
        }
    }
}
