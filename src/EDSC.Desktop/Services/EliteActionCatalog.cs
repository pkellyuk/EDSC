using System;
using System.Collections.Generic;

namespace EDSC.Desktop.Services
{
    /// <summary>
    /// Which Elite Dangerous preset an action lives in. The game keeps four presets
    /// (general, ship, SRV, on foot) and only the matching one is consulted in-game.
    /// </summary>
    public enum EliteScope
    {
        General = 0,
        Ship = 1,
        Srv = 2,
        OnFoot = 3
    }

    /// <summary>
    /// One Elite Dangerous binding name and how EDSC should present it.
    /// </summary>
    public sealed class EliteAction
    {
        public string Name { get; }
        public string Id { get; }
        public string Label { get; }
        public string Category { get; }
        public EliteScope Scope { get; }
        public string IconSvg { get; }
        public string Color { get; }

        /// <summary>Spoken phrases that should trigger the action, beyond its label.</summary>
        public IReadOnlyList<string> VoiceAliases { get; }

        public EliteAction(string name, string id, string label, string category, EliteScope scope, string iconSvg, string color, params string[] voiceAliases)
        {
            Name = name;
            Id = id;
            Label = label;
            Category = category;
            Scope = scope;
            IconSvg = iconSvg;
            Color = color;
            VoiceAliases = voiceAliases ?? Array.Empty<string>();
        }
    }

    /// <summary>
    /// Curated map from Elite Dangerous binding names to EDSC buttons, in display order.
    /// Names are the element names used in the game's .binds files.
    /// </summary>
    public static class EliteActionCatalog
    {
        private const string Combat = "Combat";
        private const string ShipControl = "Ship Control";
        private const string Navigation = "Navigation";
        private const string Targeting = "Targeting";
        private const string Panels = "Panels";
        private const string Display = "Display";
        private const string Wing = "Wing";
        private const string Fighter = "Fighter Orders";
        private const string Exploration = "Exploration";
        private const string General = "General";

        public static readonly IReadOnlyList<EliteAction> Actions = new List<EliteAction>
        {
            // Combat
            new EliteAction("DeployHardpointToggle", "hardpoints", "Hardpoints", Combat, EliteScope.Ship, "hardpoints.svg", "#6B7280", "hard points", "weapons", "deploy weapons", "retract weapons"),
            new EliteAction("ToggleButtonUpInput", "silentrunning", "Silent Running", Combat, EliteScope.Ship, "silentrunning.svg", "#7C3AED", "silent", "go silent"),
            new EliteAction("FireChaffLauncher", "chaff", "Chaff", Combat, EliteScope.Ship, "chaff.svg", "#DC2626", "fire chaff", "launch chaff"),
            new EliteAction("UseShieldCell", "shieldcell", "Shield Cell", Combat, EliteScope.Ship, "shieldbooster.svg", "#2563EB", "shield cell bank", "shields", "cell bank"),
            new EliteAction("DeployHeatSink", "heatsink", "Heat Sink", Combat, EliteScope.Ship, "", "#F59E0B", "heat sink", "deploy heat sink", "heatsink"),
            new EliteAction("ChargeECM", "ecm", "ECM", Combat, EliteScope.Ship, "", "#9333EA", "e c m", "countermeasure"),
            new EliteAction("CycleNextSubsystem", "nextsubsystem", "Next Subsystem", Combat, EliteScope.Ship, "nextsubsystem.svg", "#F59E0B", "subsystem", "next module"),
            new EliteAction("CyclePreviousSubsystem", "prevsubsystem", "Prev Subsystem", Combat, EliteScope.Ship, "nextsubsystem.svg", "#D97706", "previous subsystem", "previous module"),
            new EliteAction("CycleFireGroupNext", "nextfiregroup", "Next Fire Group", Combat, EliteScope.Ship, "nextfiregroup.svg", "#F97316", "fire group", "next group"),
            new EliteAction("CycleFireGroupPrevious", "prevfiregroup", "Prev Fire Group", Combat, EliteScope.Ship, "nextfiregroup.svg", "#EA580C", "previous fire group", "previous group"),

            // Ship control
            new EliteAction("LandingGearToggle", "landinggear", "Landing Gear", ShipControl, EliteScope.Ship, "landinggear.svg", "#4B5563", "gear", "gear up", "gear down", "wheels"),
            new EliteAction("ToggleCargoScoop", "cargoscoop", "Cargo Scoop", ShipControl, EliteScope.Ship, "cargoscoop.svg", "#92400E", "scoop", "cargo"),
            new EliteAction("ToggleFlightAssist", "flightassist", "Flight Assist", ShipControl, EliteScope.Ship, "flightassist.svg", "#14B8A6", "assist", "flight assist off", "flight assist on", "f a off"),
            new EliteAction("UseBoostJuice", "boost", "Boost", ShipControl, EliteScope.Ship, "", "#0EA5E9", "engine boost", "punch it"),
            new EliteAction("SetSpeedZero", "setzero", "Speed 0%", ShipControl, EliteScope.Ship, "speed0.svg", "#0F766E", "speed zero", "all stop", "full stop", "stop"),
            new EliteAction("SetSpeed25", "settwentyfive", "Speed 25%", ShipControl, EliteScope.Ship, "speed50.svg", "#0F766E", "speed twenty five", "quarter speed"),
            new EliteAction("SetSpeed50", "setfifty", "Speed 50%", ShipControl, EliteScope.Ship, "speed50.svg", "#0F766E", "speed fifty", "half speed", "fifty percent"),
            new EliteAction("SetSpeed75", "setseventyfive", "Speed 75%", ShipControl, EliteScope.Ship, "speed100.svg", "#0F766E", "speed seventy five", "three quarter speed"),
            new EliteAction("SetSpeed100", "sethundred", "Speed 100%", ShipControl, EliteScope.Ship, "speed100.svg", "#0F766E", "speed one hundred", "full speed", "full throttle", "hundred percent"),
            new EliteAction("ShipSpotLightToggle", "lights", "Ship Lights", ShipControl, EliteScope.Ship, "", "#FBBF24", "lights", "headlights", "spotlight"),
            new EliteAction("EjectAllCargo", "ejectcargo", "Eject All Cargo", ShipControl, EliteScope.Ship, "", "#B91C1C", "eject cargo", "dump cargo"),

            // Navigation
            new EliteAction("HyperSuperCombination", "fsd", "FSD Jump", Navigation, EliteScope.Ship, "fsd.svg", "#2563EB", "frame shift", "frame shift drive", "f s d", "jump", "engage"),
            new EliteAction("Supercruise", "supercruise", "Supercruise", Navigation, EliteScope.Ship, "fsd.svg", "#1D4ED8", "super cruise", "cruise"),
            new EliteAction("Hyperspace", "hyperspace", "Hyperspace", Navigation, EliteScope.Ship, "fsd.svg", "#1E40AF", "hyper space", "hyperspace jump"),
            new EliteAction("OrbitLinesToggle", "orbitlines", "Orbit Lines", Navigation, EliteScope.Ship, "orbitlines.svg", "#0EA5E9", "orbits", "orbit"),
            new EliteAction("GalaxyMapOpen", "galaxymap", "Galaxy Map", Navigation, EliteScope.Ship, "", "#7C3AED", "galaxy", "gal map"),
            new EliteAction("SystemMapOpen", "systemmap", "System Map", Navigation, EliteScope.Ship, "", "#6D28D9", "system", "sys map"),

            // Targeting
            new EliteAction("SelectTarget", "targetahead", "Target Ahead", Targeting, EliteScope.Ship, "targetahead.svg", "#10B981", "target", "select target", "lock target"),
            new EliteAction("CycleNextTarget", "nexttarget", "Next Target", Targeting, EliteScope.Ship, "nexttarget.svg", "#059669", "next"),
            new EliteAction("CyclePreviousTarget", "prevtarget", "Prev Target", Targeting, EliteScope.Ship, "nexttarget.svg", "#047857", "previous target", "previous"),
            new EliteAction("SelectHighestThreat", "highestthreat", "Highest Threat", Targeting, EliteScope.Ship, "highestthreat.svg", "#DC2626", "threat", "biggest threat", "target threat"),
            new EliteAction("CycleNextHostileTarget", "nexthostile", "Next Hostile", Targeting, EliteScope.Ship, "highestthreat.svg", "#B91C1C", "hostile", "next enemy"),
            new EliteAction("CyclePreviousHostileTarget", "prevhostile", "Prev Hostile", Targeting, EliteScope.Ship, "highestthreat.svg", "#991B1B", "previous hostile", "previous enemy"),

            // Panels and UI
            new EliteAction("FocusLeftPanel", "leftpanel", "Left Panel", Panels, EliteScope.Ship, "leftpanel.svg", "#1D4ED8", "left", "navigation panel", "nav panel", "target panel"),
            new EliteAction("FocusCommsPanel", "commspanel", "Comms Panel", Panels, EliteScope.Ship, "commspanel.svg", "#1E40AF", "comms", "communications"),
            new EliteAction("FocusRadarPanel", "radarpanel", "Radar Panel", Panels, EliteScope.Ship, "radarpanel.svg", "#1E3A8A", "radar", "role panel", "bottom panel"),
            new EliteAction("FocusRightPanel", "rightpanel", "Right Panel", Panels, EliteScope.Ship, "rightpanel.svg", "#1E3A8A", "right", "systems panel", "ship panel"),
            new EliteAction("QuickCommsPanel", "quickcomms", "Quick Comms", Panels, EliteScope.Ship, "quickcomms.svg", "#374151", "chat", "quick chat"),
            new EliteAction("UI_Back", "return", "Return", Panels, EliteScope.General, "return.svg", "#374151", "back", "go back"),
            new EliteAction("UI_Select", "select", "Select", Panels, EliteScope.General, "", "#374151", "confirm", "okay", "enter"),
            new EliteAction("UI_Up", "uiup", "UI Up", Panels, EliteScope.General, "", "#4B5563", "up"),
            new EliteAction("UI_Down", "uidown", "UI Down", Panels, EliteScope.General, "", "#4B5563", "down"),
            new EliteAction("UI_Left", "uileft", "UI Left", Panels, EliteScope.General, "", "#4B5563", "menu left"),
            new EliteAction("UI_Right", "uiright", "UI Right", Panels, EliteScope.General, "", "#4B5563", "menu right"),
            new EliteAction("CycleNextPanel", "nextpanel", "Next Tab", Panels, EliteScope.General, "", "#6B7280", "next tab", "tab right"),
            new EliteAction("CyclePreviousPanel", "prevpanel", "Prev Tab", Panels, EliteScope.General, "", "#6B7280", "previous tab", "tab left"),
            new EliteAction("Pause", "escape", "Menu", Panels, EliteScope.General, "escape.svg", "#374151", "escape", "pause", "main menu"),

            // Display
            new EliteAction("PlayerHUDModeToggle", "hudmode", "HUD Mode", Display, EliteScope.Ship, "hudmode.svg", "#6D28D9", "hud", "analysis mode", "combat mode", "switch mode"),
            new EliteAction("HeadLookToggle", "headlook", "Head Look", Display, EliteScope.Ship, "", "#8B5CF6", "headlook", "look"),
            new EliteAction("NightVisionToggle", "nightvision", "Night Vision", Display, EliteScope.Ship, "", "#16A34A", "night", "vision"),
            new EliteAction("RadarIncreaseRange", "radarplus", "Radar Range +", Display, EliteScope.Ship, "radarpanel.svg", "#0891B2", "radar range up", "zoom out radar"),
            new EliteAction("RadarDecreaseRange", "radarminus", "Radar Range -", Display, EliteScope.Ship, "radarpanel.svg", "#0E7490", "radar range down", "zoom in radar"),

            // Wing
            new EliteAction("TargetWingman0", "wingman1", "Wingman 1", Wing, EliteScope.Ship, "wingman.svg", "#2563EB", "wingman one", "wing one"),
            new EliteAction("TargetWingman1", "wingman2", "Wingman 2", Wing, EliteScope.Ship, "wingman.svg", "#1D4ED8", "wingman two", "wing two"),
            new EliteAction("TargetWingman2", "wingman3", "Wingman 3", Wing, EliteScope.Ship, "wingman.svg", "#1E40AF", "wingman three", "wing three"),
            new EliteAction("SelectTargetsTarget", "wingmantarget", "Wingman Target", Wing, EliteScope.Ship, "wingmantarget.svg", "#312E81", "wingman's target", "wing target"),
            new EliteAction("WingNavLock", "wingnavlock", "Wing Nav Lock", Wing, EliteScope.Ship, "wingnavlock.svg", "#4B5563", "nav lock", "wing lock"),

            // Fighter / crew orders
            new EliteAction("OrderRequestDock", "orderdock", "Order: Dock", Fighter, EliteScope.Ship, "", "#475569", "recall fighter", "fighter dock"),
            new EliteAction("OrderDefensiveBehaviour", "orderdefend", "Order: Defend", Fighter, EliteScope.Ship, "", "#475569", "defend", "defensive"),
            new EliteAction("OrderAggressiveBehaviour", "orderattack", "Order: Attack", Fighter, EliteScope.Ship, "", "#475569", "attack", "aggressive"),
            new EliteAction("OrderFocusTarget", "orderfocus", "Order: Focus", Fighter, EliteScope.Ship, "", "#475569", "focus target", "attack my target"),
            new EliteAction("OrderHoldFire", "orderholdfire", "Order: Hold Fire", Fighter, EliteScope.Ship, "", "#475569", "hold fire", "cease fire"),
            new EliteAction("OrderHoldPosition", "orderhold", "Order: Hold", Fighter, EliteScope.Ship, "", "#475569", "hold position", "hold"),
            new EliteAction("OrderFollow", "orderfollow", "Order: Follow", Fighter, EliteScope.Ship, "", "#475569", "follow me", "follow"),

            // Exploration
            new EliteAction("ExplorationFSSEnter", "fss", "FSS Scanner", Exploration, EliteScope.Ship, "", "#0D9488", "f s s", "scanner", "full spectrum"),
            new EliteAction("ExplorationFSSQuit", "fssquit", "Exit FSS", Exploration, EliteScope.Ship, "", "#0F766E", "exit scanner", "leave scanner"),

            // General
            new EliteAction("MicrophoneMute", "mute", "Mic Mute", General, EliteScope.General, "", "#6B7280", "mute", "microphone"),
            new EliteAction("PhotoCameraToggle", "camera", "Camera Suite", General, EliteScope.General, "", "#6B7280", "camera", "photo mode"),
        };
    }
}
