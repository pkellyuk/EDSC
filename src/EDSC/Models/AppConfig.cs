using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace EDSC.Models
{
    /// <summary>
    /// The games EDSC has a button layout and a bindings importer for.
    /// </summary>
    public static class GameIds
    {
        public const string EliteDangerous = "elite";
        public const string StarCitizen = "starcitizen";

        public static string Normalize(string? id)
        {
            return string.Equals(id, StarCitizen, StringComparison.OrdinalIgnoreCase) ? StarCitizen : EliteDangerous;
        }

        public static string DisplayName(string? id)
        {
            return Normalize(id) == StarCitizen ? "Star Citizen" : "Elite Dangerous";
        }
    }

    /// <summary>
    /// Complete application configuration
    /// </summary>
    public class AppConfig
    {
        [JsonPropertyName("server")]
        public ServerConfig Server { get; set; }

        /// <summary>
        /// Elite Dangerous button layout. Kept under its historical name so older config files load unchanged.
        /// </summary>
        [JsonPropertyName("buttons")]
        public List<ButtonConfig> Buttons { get; set; }

        /// <summary>
        /// Star Citizen button layout.
        /// </summary>
        [JsonPropertyName("starCitizenButtons")]
        public List<ButtonConfig> StarCitizenButtons { get; set; }

        /// <summary>
        /// Which game's layout the phone shows and the editor edits: "elite" or "starcitizen".
        /// </summary>
        [JsonPropertyName("activeGame")]
        public string ActiveGame { get; set; }

        [JsonPropertyName("tracking")]
        public TrackingConfig Tracking { get; set; }

        [JsonPropertyName("configVersion")]
        public long ConfigVersion { get; set; }

        [JsonPropertyName("lastUpdatedUtc")]
        public long LastUpdatedUtc { get; set; }

        [JsonPropertyName("lastUpdatedBy")]
        public string LastUpdatedBy { get; set; }

        /// <summary>
        /// Optional path to the game's ControlSchemes folder, for when auto-detection cannot find the install.
        /// </summary>
        [JsonPropertyName("eliteControlSchemesPath")]
        public string? EliteControlSchemesPath { get; set; }

        /// <summary>
        /// Optional path to a Star Citizen channel folder (the one containing Bin64 and user), for when
        /// auto-detection cannot find the install.
        /// </summary>
        [JsonPropertyName("starCitizenPath")]
        public string? StarCitizenPath { get; set; }

        public AppConfig()
        {
            Server = new ServerConfig();
            Buttons = new List<ButtonConfig>();
            StarCitizenButtons = new List<ButtonConfig>();
            ActiveGame = GameIds.EliteDangerous;
            Tracking = new TrackingConfig();
            ConfigVersion = 1;
            LastUpdatedUtc = 0;
            LastUpdatedBy = string.Empty;
        }

        /// <summary>
        /// The button list for a game, never null.
        /// </summary>
        public List<ButtonConfig> GetButtons(string? gameId)
        {
            if (GameIds.Normalize(gameId) == GameIds.StarCitizen)
            {
                return StarCitizenButtons ??= new List<ButtonConfig>();
            }

            return Buttons ??= new List<ButtonConfig>();
        }

        public void SetButtons(string? gameId, List<ButtonConfig> buttons)
        {
            if (GameIds.Normalize(gameId) == GameIds.StarCitizen)
            {
                StarCitizenButtons = buttons ?? new List<ButtonConfig>();
            }
            else
            {
                Buttons = buttons ?? new List<ButtonConfig>();
            }
        }

        /// <summary>
        /// The button list the phone should show right now.
        /// </summary>
        public List<ButtonConfig> ActiveButtons
        {
            get { return GetButtons(ActiveGame); }
        }

        public override string ToString()
        {
            return $"AppConfig [Server={Server}, Game={GameIds.Normalize(ActiveGame)}, Buttons={Buttons?.Count ?? 0}, SC={StarCitizenButtons?.Count ?? 0}, Version={ConfigVersion}]";
        }
    }
}
