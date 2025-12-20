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

            _configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CONFIG_FILENAME);

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
                        Color = "#6B7280",
                        Label = "Hardpoints",
                        Size = 80
                    },
                    new ButtonConfig
                    {
                        Id = "landinggear",
                        Key = "L",
                        Icon = "flight_land",
                        Color = "#4B5563",
                        Label = "Landing Gear",
                        Size = 80
                    },
                    new ButtonConfig
                    {
                        Id = "cargoscoop",
                        Key = "HOME",
                        Icon = "inbox",
                        Color = "#92400E",
                        Label = "Cargo Scoop",
                        Size = 80
                    },
                    new ButtonConfig
                    {
                        Id = "silentrunning",
                        Key = "DELETE",
                        Icon = "visibility_off",
                        Color = "#7C3AED",
                        Label = "Silent Running",
                        Size = 80
                    },
                    new ButtonConfig
                    {
                        Id = "fsd",
                        Key = "J",
                        Icon = "travel_explore",
                        Color = "#2563EB",
                        Label = "FSD Jump",
                        Size = 80
                    },
                    new ButtonConfig
                    {
                        Id = "orbitlines",
                        Key = "OEM_PLUS",
                        Icon = "timeline",
                        Color = "#0EA5E9",
                        Label = "Orbit Lines",
                        Size = 80
                    },
                    new ButtonConfig
                    {
                        Id = "targetahead",
                        Key = "T",
                        Icon = "gps_fixed",
                        Color = "#10B981",
                        Label = "Target Ahead",
                        Size = 80
                    },
                    new ButtonConfig
                    {
                        Id = "nexttarget",
                        Key = "G",
                        Icon = "my_location",
                        Color = "#059669",
                        Label = "Next Target",
                        Size = 80
                    },
                    new ButtonConfig
                    {
                        Id = "highestthreat",
                        Key = "H",
                        Icon = "priority_high",
                        Color = "#DC2626",
                        Label = "Highest Threat",
                        Size = 80
                    },
                    new ButtonConfig
                    {
                        Id = "nextsubsystem",
                        Key = "Y",
                        Icon = "swap_horiz",
                        Color = "#F59E0B",
                        Label = "Next Subsystem",
                        Size = 80
                    },
                    new ButtonConfig
                    {
                        Id = "nextfiregroup",
                        Key = "N",
                        Icon = "layers",
                        Color = "#F97316",
                        Label = "Next Fire Group",
                        Size = 80
                    },
                    new ButtonConfig
                    {
                        Id = "flightassist",
                        Key = "Z",
                        Icon = "alt_route",
                        Color = "#14B8A6",
                        Label = "Flight Assist",
                        Size = 80
                    },
                    new ButtonConfig
                    {
                        Id = "setzero",
                        Key = "X",
                        Icon = "exposure_zero",
                        Color = "#0F766E",
                        Label = "Speed 0%",
                        Size = 80
                    },
                    new ButtonConfig
                    {
                        Id = "leftpanel",
                        Key = "1",
                        Icon = "filter_1",
                        Color = "#1D4ED8",
                        Label = "Left Panel",
                        Size = 80
                    },
                    new ButtonConfig
                    {
                        Id = "commspanel",
                        Key = "2",
                        Icon = "filter_2",
                        Color = "#1E40AF",
                        Label = "Comms Panel",
                        Size = 80
                    },
                    new ButtonConfig
                    {
                        Id = "radarpanel",
                        Key = "3",
                        Icon = "filter_3",
                        Color = "#1E3A8A",
                        Label = "Radar Panel",
                        Size = 80
                    },
                    new ButtonConfig
                    {
                        Id = "rightpanel",
                        Key = "4",
                        Icon = "filter_4",
                        Color = "#1E3A8A",
                        Label = "Right Panel",
                        Size = 80
                    },
                    new ButtonConfig
                    {
                        Id = "quickcomms",
                        Key = "ENTER",
                        Icon = "sms",
                        Color = "#374151",
                        Label = "Quick Comms",
                        Size = 80
                    },
                    new ButtonConfig
                    {
                        Id = "hudmode",
                        Key = "M",
                        Icon = "visibility",
                        Color = "#6D28D9",
                        Label = "HUD Mode",
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
