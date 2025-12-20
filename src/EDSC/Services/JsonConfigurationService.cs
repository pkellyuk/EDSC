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
                    Port = 5000,
                    DiscoveryPort = 5001,
                    AutoStart = true,
                    EnableDiscovery = true
                },
                Buttons = new System.Collections.Generic.List<ButtonConfig>
                {
                    new ButtonConfig
                    {
                        Id = "shieldboost",
                        Key = "F1",
                        Icon = "shield",
                        Color = "#4CAF50",
                        Label = "Shield Boost",
                        Size = 80
                    },
                    new ButtonConfig
                    {
                        Id = "ecm",
                        Key = "F2",
                        Icon = "flash",
                        Color = "#2196F3",
                        Label = "ECM",
                        Size = 80
                    },
                    new ButtonConfig
                    {
                        Id = "chaff",
                        Key = "F3",
                        Icon = "smoke",
                        Color = "#FF9800",
                        Label = "Chaff",
                        Size = 80
                    },
                    new ButtonConfig
                    {
                        Id = "heatsink",
                        Key = "F4",
                        Icon = "ac_unit",
                        Color = "#00BCD4",
                        Label = "Heat Sink",
                        Size = 80
                    }
                }
            };

            Debug.WriteLine($"[JsonConfigurationService] Created default configuration with {config.Buttons.Count} buttons");
            Debug.WriteLine("[JsonConfigurationService] Exit: CreateDefaultConfiguration");

            return config;
        }
    }
}
