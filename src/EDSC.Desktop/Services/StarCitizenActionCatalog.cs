using System;
using System.Collections.Generic;

namespace EDSC.Desktop.Services
{
    /// <summary>
    /// One Star Citizen action and how EDSC should present it.
    /// </summary>
    public sealed class StarCitizenAction
    {
        /// <summary>
        /// Action names as they appear in actionmaps.xml. The first is the current name; later ones are
        /// older spellings the game may still write, so a rebind under any of them is honoured.
        /// </summary>
        public IReadOnlyList<string> Names { get; }
        public string Id { get; }
        public string Label { get; }
        public string Category { get; }

        /// <summary>
        /// The game's stock keyboard binding in its own notation ("n", "lalt+n", "np_8"), or empty if
        /// the action has no keyboard default. Used when the player has not rebound the action.
        /// </summary>
        public string DefaultInput { get; }

        /// <summary>
        /// Milliseconds to hold the key; the game treats a long press of these keys as a separate action.
        /// </summary>
        public int HoldMs { get; }

        public string IconSvg { get; }
        public string Color { get; }
        public IReadOnlyList<string> VoiceAliases { get; }

        public StarCitizenAction(string names, string id, string label, string category, string defaultInput, int holdMs, string iconSvg, string color, params string[] voiceAliases)
        {
            Names = names.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            Id = id;
            Label = label;
            Category = category;
            DefaultInput = defaultInput ?? string.Empty;
            HoldMs = holdMs;
            IconSvg = iconSvg ?? string.Empty;
            Color = color;
            VoiceAliases = voiceAliases ?? Array.Empty<string>();
        }
    }

    /// <summary>
    /// Curated map from Star Citizen actions to EDSC buttons, in display order, with the game's
    /// stock keyboard defaults (Alpha 4.x). The game only writes rebound actions to actionmaps.xml,
    /// so these defaults stand in for everything the player has left alone.
    /// </summary>
    public static class StarCitizenActionCatalog
    {
        private const string Flight = "Flight";
        private const string Modes = "Modes";
        private const string Landing = "Landing";
        private const string Power = "Power";
        private const string Targeting = "Targeting";
        private const string Weapons = "Weapons";
        private const string Shields = "Shields";
        private const string Ship = "Ship";
        private const string View = "View";
        private const string Ui = "Comms & UI";
        private const string HeadTracking = "Head Tracking";

        // A long press in Star Citizen is roughly half a second; a bit more leaves margin for the game
        private const int LongPress = 700;

        public static readonly IReadOnlyList<StarCitizenAction> Actions = new List<StarCitizenAction>
        {
            // Flight
            new StarCitizenAction("v_afterburner|v_boost", "scboost", "Boost", Flight, "lshift", 0, "speed100.svg", "#0EA5E9", "afterburner", "punch it"),
            new StarCitizenAction("v_space_brake|v_brake", "scspacebrake", "Space Brake", Flight, "x", 0, "speed0.svg", "#DC2626", "brake", "stop", "all stop"),
            new StarCitizenAction("v_ifcs_toggle_vector_decoupling", "scdecoupled", "Decoupled", Flight, "c", 0, "flightassist.svg", "#14B8A6", "decouple", "coupled", "decoupled mode"),
            new StarCitizenAction("v_ifcs_toggle_cruise_control", "sccruise", "Cruise Control", Flight, "lalt+c", 0, "speed50.svg", "#0F766E", "cruise"),
            new StarCitizenAction("v_ifcs_toggle_speed_limiter", "scspeedlimiter", "Speed Limiter", Flight, "", 0, "speed50.svg", "#0E7490", "limiter"),
            new StarCitizenAction("v_toggle_vtol", "scvtol", "VTOL", Flight, "k", 0, "", "#0891B2", "v tol", "vertical thrust"),
            new StarCitizenAction("v_flightready", "scflightready", "Flight Ready", Flight, "ralt+r", 0, "", "#10B981", "systems ready", "ready ship", "power up ship"),

            // Modes
            new StarCitizenAction("v_toggle_quantum_mode", "scmastermode", "NAV / SCM Mode", Modes, "b", LongPress, "fsd.svg", "#2563EB", "nav mode", "s c m mode", "master mode", "quantum mode", "quantum"),
            new StarCitizenAction("v_toggle_qdrive_engagement|v_toggle_qdrive_system", "scquantumjump", "Quantum Jump", Modes, "", LongPress, "fsd.svg", "#1D4ED8", "jump", "engage quantum", "quantum travel"),
            new StarCitizenAction("v_toggle_scan_mode", "scscanmode", "Scan Mode", Modes, "v", 0, "radarpanel.svg", "#0D9488", "scanning", "scanner", "scan"),
            new StarCitizenAction("v_toggle_mining_mode", "scminingmode", "Mining Mode", Modes, "m", 0, "cargoscoop.svg", "#B45309", "mining"),
            new StarCitizenAction("v_toggle_salvage_mode", "scsalvagemode", "Salvage Mode", Modes, "m", 0, "cargoscoop.svg", "#92400E", "salvage"),
            new StarCitizenAction("v_toggle_missile_mode", "scmissilemode", "Missile Mode", Modes, "", 0, "hardpoints.svg", "#EA580C", "missiles"),
            new StarCitizenAction("v_invoke_ping", "scping", "Radar Ping", Modes, "tab", 0, "radarpanel.svg", "#0891B2", "ping"),

            // Landing
            new StarCitizenAction("v_toggle_landing_system", "sclandinggear", "Landing Gear", Landing, "n", 0, "landinggear.svg", "#4B5563", "gear", "gear up", "gear down", "landing mode"),
            new StarCitizenAction("v_autoland", "scautoland", "Autoland", Landing, "n", LongPress, "landinggear.svg", "#374151", "auto land", "automatic landing"),
            new StarCitizenAction("v_atc_request", "scatc", "Request Landing", Landing, "lalt+n", 0, "commspanel.svg", "#1E40AF", "a t c", "request takeoff", "landing request", "request landing"),

            // Power
            new StarCitizenAction("v_power_toggle", "scpower", "Power", Power, "u", 0, "", "#F59E0B", "power on", "power off", "all power", "ship power"),
            new StarCitizenAction("v_power_toggle_thrusters", "scthrusters", "Thrusters", Power, "i", 0, "", "#F97316", "engines", "thruster power", "engine power"),
            new StarCitizenAction("v_power_toggle_shields", "scshieldspower", "Shields", Power, "o", 0, "shieldbooster.svg", "#2563EB", "shield power", "shields on", "shields off"),
            new StarCitizenAction("v_power_toggle_weapons", "scweaponspower", "Weapons Power", Power, "p", 0, "hardpoints.svg", "#DC2626", "weapon power", "weapons on", "weapons off"),
            new StarCitizenAction("v_capacitor_assignment_weapon_increase|v_power_focus_group_1", "scpowerweapons", "Power to Weapons", Power, "f5", 0, "", "#B91C1C", "weapons power up", "more weapons"),
            new StarCitizenAction("v_capacitor_assignment_engine_increase|v_power_focus_group_2", "scpowerengines", "Power to Engines", Power, "f6", 0, "", "#C2410C", "engines power up", "more engines"),
            new StarCitizenAction("v_capacitor_assignment_shield_increase|v_power_focus_group_3", "scpowershields", "Power to Shields", Power, "f7", 0, "", "#1D4ED8", "shields power up", "more shields"),
            new StarCitizenAction("v_capacitor_assignment_reset|v_power_reset_focus", "scpowerreset", "Reset Power", Power, "f8", 0, "", "#6B7280", "balance power", "power reset"),

            // Targeting
            new StarCitizenAction("v_target_cycle_in_view_fwd|v_target_lock_selected", "sctargetahead", "Target Ahead", Targeting, "t", 0, "targetahead.svg", "#10B981", "target", "lock target", "lock", "select target"),
            new StarCitizenAction("v_target_unlock_selected", "scunlock", "Unlock Target", Targeting, "lalt+t", 0, "targetahead.svg", "#047857", "unlock", "clear target"),
            new StarCitizenAction("v_target_cycle_attacker_fwd", "scnextattacker", "Next Attacker", Targeting, "4", 0, "highestthreat.svg", "#DC2626", "attacker", "threat", "who is shooting me"),
            new StarCitizenAction("v_target_cycle_hostile_fwd", "scnexthostile", "Next Hostile", Targeting, "5", 0, "highestthreat.svg", "#B91C1C", "hostile", "next enemy", "enemy"),
            new StarCitizenAction("v_target_cycle_friendly_fwd", "scnextfriendly", "Next Friendly", Targeting, "6", 0, "wingman.svg", "#2563EB", "friendly", "next friend"),
            new StarCitizenAction("v_target_cycle_all_fwd", "scnexttarget", "Next Target", Targeting, "7", 0, "nexttarget.svg", "#059669", "next", "cycle targets"),
            new StarCitizenAction("v_target_cycle_subitem_fwd", "scnextsubtarget", "Next Subtarget", Targeting, "r", 0, "nextsubsystem.svg", "#F59E0B", "subsystem", "sub target", "component", "next component"),
            new StarCitizenAction("v_target_cycle_subitem_reset", "scsubtargetreset", "Reset Subtarget", Targeting, "lalt+r", 0, "nextsubsystem.svg", "#D97706", "main target", "clear subtarget"),
            new StarCitizenAction("v_target_toggle_pin_index_1", "scpin1", "Pin 1", Targeting, "lalt+1", 0, "", "#4338CA", "pin one", "pin target one"),
            new StarCitizenAction("v_target_toggle_pin_index_2", "scpin2", "Pin 2", Targeting, "lalt+2", 0, "", "#4338CA", "pin two", "pin target two"),
            new StarCitizenAction("v_target_toggle_pin_index_3", "scpin3", "Pin 3", Targeting, "lalt+3", 0, "", "#4338CA", "pin three", "pin target three"),
            new StarCitizenAction("v_target_toggle_lock_index_1", "sclockpin1", "Lock Pin 1", Targeting, "1", 0, "", "#3730A3", "lock one", "target pin one"),
            new StarCitizenAction("v_target_toggle_lock_index_2", "sclockpin2", "Lock Pin 2", Targeting, "2", 0, "", "#3730A3", "lock two", "target pin two"),
            new StarCitizenAction("v_target_toggle_lock_index_3", "sclockpin3", "Lock Pin 3", Targeting, "3", 0, "", "#3730A3", "lock three", "target pin three"),
            new StarCitizenAction("v_target_hail", "schail", "Hail Target", Targeting, "9", 0, "quickcomms.svg", "#374151", "hail", "hail them"),

            // Weapons
            new StarCitizenAction("v_weapon_cycle_aimmode|v_toggle_weapon_gimbal_lock", "scgimbal", "Gimbal Mode", Weapons, "g", 0, "hardpoints.svg", "#6B7280", "gimbal", "gimbal lock", "gimbals"),
            new StarCitizenAction("v_weapon_increase_max_missiles", "scarmmissile", "Arm +1 Missile", Weapons, "g", 0, "hardpoints.svg", "#EA580C", "arm missile", "more missiles"),
            new StarCitizenAction("v_weapon_reset_max_missiles", "scresetmissiles", "Reset Missiles", Weapons, "lalt+g", 0, "", "#9A3412", "disarm missiles", "missiles reset"),

            // Shields and countermeasures
            new StarCitizenAction("v_weapon_countermeasure_decoy_launch|v_weapon_launch_countermeasure", "scdecoy", "Decoy", Shields, "h", 0, "chaff.svg", "#DC2626", "flares", "chaff", "launch decoy", "decoys"),
            new StarCitizenAction("v_weapon_countermeasure_noise_launch", "scnoise", "Noise", Shields, "j", 0, "chaff.svg", "#7C3AED", "launch noise", "jammer"),
            new StarCitizenAction("v_shield_raise_level_forward", "scshieldfront", "Shield Front", Shields, "np_8", 0, "shieldbooster.svg", "#1D4ED8", "shields forward", "front shields", "shields front"),
            new StarCitizenAction("v_shield_raise_level_back", "scshieldback", "Shield Back", Shields, "np_2", 0, "shieldbooster.svg", "#1E40AF", "shields back", "rear shields", "shields rear"),
            new StarCitizenAction("v_shield_raise_level_left", "scshieldleft", "Shield Left", Shields, "np_4", 0, "shieldbooster.svg", "#1E3A8A", "shields left", "left shields"),
            new StarCitizenAction("v_shield_raise_level_right", "scshieldright", "Shield Right", Shields, "np_6", 0, "shieldbooster.svg", "#1E3A8A", "shields right", "right shields"),
            new StarCitizenAction("v_shield_raise_level_up", "scshieldtop", "Shield Top", Shields, "np_7", 0, "shieldbooster.svg", "#312E81", "shields up", "top shields"),
            new StarCitizenAction("v_shield_raise_level_down", "scshieldbottom", "Shield Bottom", Shields, "np_1", 0, "shieldbooster.svg", "#312E81", "shields down", "bottom shields"),
            new StarCitizenAction("v_shield_reset_level", "scshieldreset", "Shields Reset", Shields, "np_5", 0, "shieldbooster.svg", "#4B5563", "balance shields", "reset shields", "shields balance"),

            // Ship
            new StarCitizenAction("v_lights", "sclights", "Lights", Ship, "l", 0, "", "#FBBF24", "headlights", "ship lights"),
            new StarCitizenAction("v_toggle_all_doorlocks", "scdoorlocks", "Lock Doors", Ship, "ralt+k", 0, "", "#4B5563", "lock doors", "unlock doors", "door locks"),
            new StarCitizenAction("v_toggle_all_doors", "scdoors", "All Doors", Ship, "", 0, "", "#6B7280", "open doors", "close doors", "doors"),
            new StarCitizenAction("v_transform_cycle", "scconfig", "Cycle Config", Ship, "lalt+k", 0, "", "#0E7490", "configuration", "transform", "wings"),
            new StarCitizenAction("v_jettison_volatile_cargo", "scjettison", "Jettison Cargo", Ship, "lalt+j", 0, "cargoscoop.svg", "#B91C1C", "jettison", "dump cargo"),
            new StarCitizenAction("v_exit|pl_exit", "scexitseat", "Exit Seat", Ship, "y", LongPress, "return.svg", "#374151", "get up", "leave seat", "exit", "stand up"),
            new StarCitizenAction("v_emergency_exit", "scemergencyexit", "Emergency Exit", Ship, "lshift+u", 0, "", "#B91C1C", "emergency", "bail out"),
            new StarCitizenAction("v_eject", "sceject", "Eject", Ship, "ralt+y", 1200, "escape.svg", "#991B1B", "eject eject", "punch out"),
            new StarCitizenAction("v_self_destruct", "scselfdestruct", "Self Destruct", Ship, "backspace", 1200, "escape.svg", "#7F1D1D", "destruct", "scuttle"),

            // View
            new StarCitizenAction("v_view_look_behind", "sclookbehind", "Look Behind", View, "comma", 1500, "hudmode.svg", "#6D28D9", "behind", "check six"),
            new StarCitizenAction("v_view_cycle_fwd", "sccamera", "Camera View", View, "f4", 0, "hudmode.svg", "#7C3AED", "third person", "camera", "cycle camera"),

            // Comms and UI
            new StarCitizenAction("mobiglas", "scmobiglas", "mobiGlas", Ui, "f1", 0, "leftpanel.svg", "#1D4ED8", "mobi glass", "mobiglass", "mobi", "moby glass"),
            new StarCitizenAction("v_starmap", "scstarmap", "Star Map", Ui, "f2", 0, "orbitlines.svg", "#0EA5E9", "map", "starmap", "galaxy map"),
            new StarCitizenAction("toggle_chat", "scchat", "Chat", Ui, "f12", 0, "quickcomms.svg", "#374151", "chat window"),
            new StarCitizenAction("toggle_contact", "sccontacts", "Contacts", Ui, "f11", 0, "commspanel.svg", "#1E40AF", "comm link", "commlink", "friends"),
            new StarCitizenAction("visor_wipe", "scvisorwipe", "Wipe Visor", Ui, "lalt+x", 0, "", "#6B7280", "wipe helmet", "clean visor"),

            // Head tracking
            new StarCitizenAction("headtrack_enabled", "scheadtrack", "Head Tracking", HeadTracking, "np_divide", 0, "hudmode.svg", "#8B5CF6", "head track", "toggle head tracking", "tracking"),
            new StarCitizenAction("headtrack_recenter_device", "scheadtrackcenter", "Recentre Tracking", HeadTracking, "", 0, "", "#7C3AED", "recenter", "recentre", "center tracking"),
        };
    }
}
