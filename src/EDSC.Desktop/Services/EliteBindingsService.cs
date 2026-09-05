using EDSC.Models;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using WindowsInput.Native;

namespace EDSC.Desktop.Services
{
    /// <summary>
    /// The keyboard binding found for one catalogue action.
    /// </summary>
    public sealed class EliteBoundAction
    {
        public EliteAction Action { get; }
        public string? Key { get; }
        public string PresetName { get; }

        public EliteBoundAction(EliteAction action, string? key, string presetName)
        {
            Action = action;
            Key = key;
            PresetName = presetName;
        }

        public bool IsBound
        {
            get { return !string.IsNullOrEmpty(Key); }
        }
    }

    public sealed class EliteBindingsResult
    {
        public bool Found { get; set; }
        public List<EliteBoundAction> Actions { get; } = new List<EliteBoundAction>();
        public List<string> Notes { get; } = new List<string>();
        public List<string> PresetFiles { get; } = new List<string>();
    }

    /// <summary>
    /// Reads Elite Dangerous control bindings from the game's files and turns them into EDSC buttons.
    ///
    /// The game records which preset is active per scope in Options\Bindings\StartPreset.*.start.
    /// Custom presets live in that same folder as &lt;name&gt;.&lt;version&gt;.binds; stock presets
    /// live in the game install under ControlSchemes\&lt;name&gt;.binds.
    /// </summary>
    public sealed class EliteBindingsService
    {
        private static readonly Regex VersionedBinds = new Regex(@"^(?<name>.+?)\.(?<major>\d+)\.(?<minor>\d+)\.binds$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static string DefaultBindingsDirectory
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Frontier Developments", "Elite Dangerous", "Options", "Bindings");
            }
        }

        /// <summary>
        /// Read the active presets and resolve a keyboard key for every catalogue action.
        /// </summary>
        /// <param name="controlSchemesOverride">Optional path to the game's ControlSchemes folder if auto-detection fails.</param>
        public EliteBindingsResult Load(string? controlSchemesOverride = null)
        {
            var result = new EliteBindingsResult();
            var bindingsDir = DefaultBindingsDirectory;

            if (!Directory.Exists(bindingsDir))
            {
                result.Notes.Add($"Elite Dangerous bindings folder not found: {bindingsDir}");
                return result;
            }

            var presets = ReadStartPresets(bindingsDir, result);
            var schemesDir = !string.IsNullOrWhiteSpace(controlSchemesOverride) && Directory.Exists(controlSchemesOverride)
                ? controlSchemesOverride
                : FindControlSchemesDirectory();

            if (schemesDir == null)
            {
                result.Notes.Add("Game install not found, so stock presets cannot be read. Custom presets in the bindings folder still work.");
            }

            // Load each distinct preset once
            var documents = new Dictionary<string, XDocument>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in presets.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var file = ResolvePresetFile(name, bindingsDir, schemesDir);
                if (file == null)
                {
                    result.Notes.Add($"Preset '{name}' is active but its .binds file was not found.");
                    continue;
                }

                try
                {
                    documents[name] = XDocument.Load(file);
                    result.PresetFiles.Add(file);
                }
                catch (Exception ex)
                {
                    result.Notes.Add($"Could not read {file}: {ex.Message}");
                }
            }

            if (documents.Count == 0)
            {
                return result;
            }

            result.Found = true;

            foreach (var action in EliteActionCatalog.Actions)
            {
                string? key = null;
                string usedPreset = string.Empty;

                // The scope's own preset first, since that is what the game consults
                var scopePreset = presets[(int)action.Scope];
                if (documents.TryGetValue(scopePreset, out var scopeDoc))
                {
                    key = ExtractKeyboardKey(scopeDoc, action.Name, result);
                    usedPreset = scopePreset;
                }

                // Then any other active preset, in case the scope guess is off
                if (key == null)
                {
                    foreach (var name in presets.Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        if (string.Equals(name, scopePreset, StringComparison.OrdinalIgnoreCase) || !documents.TryGetValue(name, out var doc))
                        {
                            continue;
                        }

                        key = ExtractKeyboardKey(doc, action.Name, result);
                        if (key != null)
                        {
                            usedPreset = name;
                            break;
                        }
                    }
                }

                result.Actions.Add(new EliteBoundAction(action, key, usedPreset));
            }

            return result;
        }

        /// <summary>
        /// Rebuild the button list from the bindings. Existing buttons with a matching id keep their
        /// colour, icon, and size; buttons EDSC does not know about are kept at the end.
        /// Actions without a keyboard binding are only included if a button for them already existed,
        /// and are left with an empty key so the UI can show them as unbound.
        /// </summary>
        public static string ApplyToConfig(AppConfig config, EliteBindingsResult bindings)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if (bindings == null || !bindings.Found)
            {
                return "No Elite Dangerous bindings were found.";
            }

            var actions = bindings.Actions.Select(item => new ImportedAction(
                item.Action.Id,
                item.Action.Label,
                item.Action.Category,
                item.Action.IconSvg,
                item.Action.Color,
                item.Action.VoiceAliases,
                item.Key,
                0));

            var presetSummary = bindings.PresetFiles.Count > 0
                ? string.Join(", ", bindings.PresetFiles.Select(Path.GetFileName))
                : "none";

            config.Buttons = BindingImport.Apply(config.Buttons, actions, presetSummary, out var message);
            return message;
        }

        /// <summary>
        /// Outcome of writing keyboard bindings into the game's Custom preset.
        /// </summary>
        public sealed class BindResult
        {
            public bool Success { get; set; }
            public string Message { get; set; } = string.Empty;
            public List<string> Details { get; } = new List<string>();
            public string? BackupPath { get; set; }
        }

        // Keys to hand out for unbound actions, least likely to collide with typing or existing bindings first
        private static readonly string[] SpareKeyPool =
        {
            "Key_Numpad_Add", "Key_Numpad_Subtract", "Key_Numpad_Multiply", "Key_Numpad_Divide", "Key_Numpad_Decimal",
            "Key_Numpad_0", "Key_Numpad_1", "Key_Numpad_2", "Key_Numpad_3", "Key_Numpad_4",
            "Key_Numpad_5", "Key_Numpad_6", "Key_Numpad_7", "Key_Numpad_8", "Key_Numpad_9",
            "Key_F5", "Key_F6", "Key_F7", "Key_F8", "Key_F9", "Key_F10", "Key_F11", "Key_F12",
            "Key_Insert", "Key_Home", "Key_End", "Key_PageUp", "Key_PageDown",
            "Key_B", "Key_I", "Key_K", "Key_V", "Key_G", "Key_H", "Key_J", "Key_L", "Key_M", "Key_N", "Key_O", "Key_P",
            "Key_5", "Key_6", "Key_7", "Key_8", "Key_9", "Key_0",
            "Key_LeftBracket", "Key_RightBracket", "Key_SemiColon", "Key_Comma", "Key_Period", "Key_Slash"
        };

        /// <summary>
        /// Give keyboard keys to catalogue actions that have none, by editing the game's Custom preset.
        /// The Custom file is created from the active ship preset if it does not exist, backed up before
        /// being changed, and the StartPreset file is pointed at Custom for each scope that gained a key.
        /// The game reads bindings at startup, so it must be restarted afterwards.
        /// </summary>
        /// <param name="actionsToBind">Catalogue actions that currently have no keyboard key.</param>
        /// <param name="controlSchemesOverride">Optional path to the game's ControlSchemes folder.</param>
        /// <param name="dryRun">True to work out what would change without touching any file.</param>
        public BindResult BindMissingKeys(IEnumerable<EliteAction> actionsToBind, string? controlSchemesOverride = null, bool dryRun = false)
        {
            var result = new BindResult();
            var wanted = (actionsToBind ?? Enumerable.Empty<EliteAction>()).ToList();

            if (wanted.Count == 0)
            {
                result.Message = "Nothing to bind: every button already has a key in the game.";
                return result;
            }

            var bindingsDir = DefaultBindingsDirectory;
            if (!Directory.Exists(bindingsDir))
            {
                result.Message = $"Elite Dangerous bindings folder not found: {bindingsDir}";
                return result;
            }

            try
            {
                var notes = new EliteBindingsResult();
                var presets = ReadStartPresets(bindingsDir, notes);
                var schemesDir = !string.IsNullOrWhiteSpace(controlSchemesOverride) && Directory.Exists(controlSchemesOverride)
                    ? controlSchemesOverride
                    : FindControlSchemesDirectory();

                // Locate or create the Custom preset file
                var customFile = ResolvePresetFile("Custom", bindingsDir, null);
                XDocument doc;

                if (customFile == null)
                {
                    var shipPreset = presets[(int)EliteScope.Ship];
                    var source = ResolvePresetFile(shipPreset, bindingsDir, schemesDir);
                    if (source == null)
                    {
                        result.Message = $"No Custom preset exists and the active ship preset '{shipPreset}' could not be found to copy from.";
                        return result;
                    }

                    doc = XDocument.Load(source);
                    var root = doc.Root ?? throw new InvalidOperationException("Preset file has no root element");
                    root.SetAttributeValue("PresetName", "Custom");
                    root.SetAttributeValue("MajorVersion", root.Attribute("MajorVersion")?.Value ?? "4");
                    root.SetAttributeValue("MinorVersion", root.Attribute("MinorVersion")?.Value ?? "2");
                    customFile = Path.Combine(bindingsDir, $"Custom.{root.Attribute("MajorVersion")!.Value}.{root.Attribute("MinorVersion")!.Value}.binds");
                    result.Details.Add($"{(dryRun ? "Will create" : "Created")} {Path.GetFileName(customFile)} as a copy of {Path.GetFileName(source)}");
                }
                else
                {
                    doc = XDocument.Load(customFile);
                    result.BackupPath = customFile + ".edsc-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                    result.Details.Add($"{(dryRun ? "Will edit" : "Edited")} {Path.GetFileName(customFile)} (backup: {Path.GetFileName(result.BackupPath)})");
                    if (!dryRun)
                    {
                        File.Copy(customFile, result.BackupPath, overwrite: false);
                    }
                }

                var rootElement = doc.Root ?? throw new InvalidOperationException("Preset file has no root element");

                // Every keyboard key already in use in this file, so nothing gets double-assigned
                var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var element in rootElement.Descendants())
                {
                    if (string.Equals((string?)element.Attribute("Device"), "Keyboard", StringComparison.OrdinalIgnoreCase))
                    {
                        var key = (string?)element.Attribute("Key");
                        if (!string.IsNullOrEmpty(key))
                        {
                            used.Add(key);
                        }
                    }
                }

                var boundScopes = new HashSet<EliteScope>();
                int bound = 0;

                foreach (var action in wanted)
                {
                    var element = rootElement.Element(action.Name);
                    if (element == null)
                    {
                        result.Details.Add($"{action.Label}: not present in the preset file, skipped");
                        continue;
                    }

                    // Already has a keyboard key here? Then the game just needs this file to be active
                    if (ExtractKeyboardKey(doc, action.Name, notes) != null)
                    {
                        boundScopes.Add(action.Scope);
                        result.Details.Add($"{action.Label}: already has a key in Custom");
                        continue;
                    }

                    var slot = element.Element("Primary");
                    if (slot == null || !IsNoDevice(slot))
                    {
                        slot = element.Element("Secondary");
                    }

                    if (slot == null || !IsNoDevice(slot))
                    {
                        result.Details.Add($"{action.Label}: both slots are taken by other devices, skipped");
                        continue;
                    }

                    var spare = SpareKeyPool.FirstOrDefault(k => !used.Contains(k));
                    if (spare == null)
                    {
                        result.Details.Add($"{action.Label}: no spare keys left, skipped");
                        continue;
                    }

                    slot.SetAttributeValue("Device", "Keyboard");
                    slot.SetAttributeValue("Key", spare);
                    slot.Elements("Modifier").Remove();
                    used.Add(spare);
                    boundScopes.Add(action.Scope);
                    bound++;
                    result.Details.Add($"{action.Label} = {spare.Substring(4).Replace("Numpad_", "Numpad ")}");
                }

                if (bound == 0 && boundScopes.Count == 0)
                {
                    result.Message = "No keys could be assigned. " + string.Join(" ", result.Details);
                    return result;
                }

                // Which scopes the game must be told to read from Custom
                var startFile = Directory.GetFiles(bindingsDir, "StartPreset*.start")
                    .OrderByDescending(f => f, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault() ?? Path.Combine(bindingsDir, "StartPreset.4.start");

                bool startChanged = false;
                foreach (var scope in boundScopes)
                {
                    if (!string.Equals(presets[(int)scope], "Custom", StringComparison.OrdinalIgnoreCase))
                    {
                        result.Details.Add($"{scope} controls switch from the '{presets[(int)scope]}' preset to 'Custom' in {Path.GetFileName(startFile)}");
                        presets[(int)scope] = "Custom";
                        startChanged = true;
                    }
                }

                if (dryRun)
                {
                    result.Success = true;
                    result.Message = $"{bound} key(s) would be assigned.";
                    return result;
                }

                var settings = new System.Xml.XmlWriterSettings
                {
                    Indent = true,
                    IndentChars = "\t",
                    Encoding = new System.Text.UTF8Encoding(false),
                    NewLineChars = "\r\n"
                };
                using (var writer = System.Xml.XmlWriter.Create(customFile, settings))
                {
                    doc.Save(writer);
                }

                if (startChanged)
                {
                    if (File.Exists(startFile))
                    {
                        File.Copy(startFile, startFile + ".edsc-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture), overwrite: false);
                    }
                    File.WriteAllLines(startFile, presets);
                }

                result.Success = true;
                result.Message = bound > 0
                    ? $"Assigned {bound} key(s) in {Path.GetFileName(customFile)}. Restart Elite Dangerous for the game to load them."
                    : $"{Path.GetFileName(customFile)} already had the keys; the game now uses it. Restart Elite Dangerous.";
                return result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[EliteBindingsService] BindMissingKeys failed: {ex}");
                result.Message = $"Binding failed: {ex.Message}";
                return result;
            }
        }

        private static bool IsNoDevice(XElement slot)
        {
            var device = (string?)slot.Attribute("Device");
            return string.IsNullOrEmpty(device) || device == "{NoDevice}";
        }

        private static string[] ReadStartPresets(string bindingsDir, EliteBindingsResult result)
        {
            // Newest format first
            var candidates = Directory.GetFiles(bindingsDir, "StartPreset*.start")
                .OrderByDescending(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var presets = new[] { "Custom", "Custom", "Custom", "Custom" };

            foreach (var file in candidates)
            {
                try
                {
                    var lines = File.ReadAllLines(file)
                        .Select(l => l.Trim())
                        .Where(l => l.Length > 0)
                        .ToArray();

                    if (lines.Length == 0)
                    {
                        continue;
                    }

                    for (int i = 0; i < presets.Length; i++)
                    {
                        presets[i] = i < lines.Length ? lines[i] : lines[lines.Length - 1];
                    }

                    result.Notes.Add($"Active presets from {Path.GetFileName(file)}: general={presets[0]}, ship={presets[1]}, SRV={presets[2]}, on foot={presets[3]}");
                    return presets;
                }
                catch (Exception ex)
                {
                    result.Notes.Add($"Could not read {file}: {ex.Message}");
                }
            }

            result.Notes.Add("No StartPreset file found; assuming the 'Custom' preset.");
            return presets;
        }

        private static string? ResolvePresetFile(string presetName, string bindingsDir, string? schemesDir)
        {
            // Custom presets: pick the highest version present
            string? best = null;
            var bestVersion = new Version(0, 0);

            foreach (var file in Directory.GetFiles(bindingsDir, presetName + "*.binds"))
            {
                var fileName = Path.GetFileName(file);
                var match = VersionedBinds.Match(fileName);

                if (match.Success)
                {
                    if (!string.Equals(match.Groups["name"].Value, presetName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var version = new Version(int.Parse(match.Groups["major"].Value, CultureInfo.InvariantCulture), int.Parse(match.Groups["minor"].Value, CultureInfo.InvariantCulture));
                    if (best == null || version > bestVersion)
                    {
                        best = file;
                        bestVersion = version;
                    }
                }
                else if (string.Equals(fileName, presetName + ".binds", StringComparison.OrdinalIgnoreCase) && best == null)
                {
                    best = file;
                }
            }

            if (best != null)
            {
                return best;
            }

            if (schemesDir != null)
            {
                var stock = Path.Combine(schemesDir, presetName + ".binds");
                if (File.Exists(stock))
                {
                    return stock;
                }
            }

            return null;
        }

        /// <summary>
        /// Find the game's ControlSchemes folder via Steam library folders and the usual launcher paths.
        /// </summary>
        public static string? FindControlSchemesDirectory()
        {
            var productRoots = new List<string>();

            try
            {
                using var steamKey = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                var steamPath = steamKey?.GetValue("SteamPath") as string;
                if (!string.IsNullOrEmpty(steamPath))
                {
                    steamPath = steamPath.Replace('/', '\\');
                    productRoots.Add(Path.Combine(steamPath, "steamapps", "common", "Elite Dangerous", "Products"));

                    var libraryFile = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
                    if (File.Exists(libraryFile))
                    {
                        foreach (Match m in Regex.Matches(File.ReadAllText(libraryFile), "\"path\"\\s*\"(?<p>[^\"]+)\""))
                        {
                            var library = m.Groups["p"].Value.Replace("\\\\", "\\");
                            productRoots.Add(Path.Combine(library, "steamapps", "common", "Elite Dangerous", "Products"));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[EliteBindingsService] Steam lookup failed: {ex.Message}");
            }

            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            productRoots.Add(Path.Combine(programFilesX86, "Frontier", "EDLaunch", "Products"));
            productRoots.Add(Path.Combine(programFiles, "Frontier", "EDLaunch", "Products"));
            productRoots.Add(Path.Combine(programFiles, "Epic Games", "EliteDangerous", "Products"));
            productRoots.Add(Path.Combine(localAppData, "Frontier_Developments", "Products"));

            string? fallback = null;

            foreach (var root in productRoots.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                foreach (var product in Directory.GetDirectories(root))
                {
                    var schemes = Path.Combine(product, "ControlSchemes");
                    if (!Directory.Exists(schemes))
                    {
                        continue;
                    }

                    // Prefer the current (Odyssey) client
                    if (Path.GetFileName(product).Contains("odyssey", StringComparison.OrdinalIgnoreCase))
                    {
                        return schemes;
                    }

                    fallback ??= schemes;
                }
            }

            return fallback;
        }

        /// <summary>
        /// Return the EDSC key string for an action, such as "U" or "LSHIFT+U", or null if the
        /// action has no keyboard binding.
        /// </summary>
        private static string? ExtractKeyboardKey(XDocument document, string actionName, EliteBindingsResult result)
        {
            var element = document.Root?.Element(actionName);
            if (element == null)
            {
                return null;
            }

            foreach (var slotName in new[] { "Primary", "Secondary" })
            {
                var slot = element.Element(slotName);
                if (slot == null)
                {
                    continue;
                }

                if (!string.Equals((string?)slot.Attribute("Device"), "Keyboard", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var mainKey = TranslateKey((string?)slot.Attribute("Key"));
                if (mainKey == null)
                {
                    result.Notes.Add($"{actionName}: unrecognised key '{(string?)slot.Attribute("Key")}'");
                    continue;
                }

                var parts = new List<string>();
                bool modifiersOk = true;

                foreach (var modifier in slot.Elements("Modifier"))
                {
                    if (!string.Equals((string?)modifier.Attribute("Device"), "Keyboard", StringComparison.OrdinalIgnoreCase))
                    {
                        modifiersOk = false;
                        break;
                    }

                    var modKey = TranslateKey((string?)modifier.Attribute("Key"));
                    if (modKey == null)
                    {
                        modifiersOk = false;
                        break;
                    }

                    parts.Add(modKey);
                }

                if (!modifiersOk)
                {
                    continue;
                }

                parts.Add(mainKey);
                return string.Join("+", parts);
            }

            return null;
        }

        /// <summary>
        /// Map an Elite key name (Key_U, Key_Numpad_5, Key_LeftShift...) to a name EDSC's keyboard
        /// service understands. Punctuation keys are resolved through the current keyboard layout
        /// so UK and US layouts both land on the right physical key.
        /// </summary>
        public static string? TranslateKey(string? eliteKey)
        {
            if (string.IsNullOrWhiteSpace(eliteKey) || !eliteKey.StartsWith("Key_", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var name = eliteKey.Substring(4);

            if (name.Length == 1 && (char.IsLetterOrDigit(name[0])))
            {
                return name.ToUpperInvariant();
            }

            if (name.StartsWith("Numpad_", StringComparison.OrdinalIgnoreCase))
            {
                var rest = name.Substring(7);
                if (rest.Length == 1 && char.IsDigit(rest[0]))
                {
                    return "NUMPAD" + rest;
                }

                return rest.ToUpperInvariant() switch
                {
                    "ADD" => "ADD",
                    "SUBTRACT" => "SUBTRACT",
                    "MULTIPLY" => "MULTIPLY",
                    "DIVIDE" => "DIVIDE",
                    "DECIMAL" => "DECIMAL",
                    "ENTER" => "RETURN",
                    _ => null
                };
            }

            if (name.Length >= 2 && name[0] == 'F' && int.TryParse(name.Substring(1), out var fn) && fn >= 1 && fn <= 24)
            {
                return "F" + fn;
            }

            var named = name.ToUpperInvariant() switch
            {
                "SPACE" => "SPACE",
                "ENTER" => "RETURN",
                "ESCAPE" => "ESCAPE",
                "BACKSPACE" => "BACK",
                "TAB" => "TAB",
                "DELETE" => "DELETE",
                "INSERT" => "INSERT",
                "HOME" => "HOME",
                "END" => "END",
                "PAGEUP" => "PRIOR",
                "PAGEDOWN" => "NEXT",
                "UPARROW" => "UP",
                "DOWNARROW" => "DOWN",
                "LEFTARROW" => "LEFT",
                "RIGHTARROW" => "RIGHT",
                "LEFTSHIFT" => "LSHIFT",
                "RIGHTSHIFT" => "RSHIFT",
                "LEFTCONTROL" => "LCONTROL",
                "RIGHTCONTROL" => "RCONTROL",
                "LEFTALT" => "LMENU",
                "RIGHTALT" => "RMENU",
                "CAPSLOCK" => "CAPITAL",
                "NUMLOCK" => "NUMLOCK",
                "SCROLLLOCK" => "SCROLL",
                "PRINTSCREEN" => "SNAPSHOT",
                _ => null
            };

            if (named != null)
            {
                return named;
            }

            var ch = name.ToUpperInvariant() switch
            {
                "MINUS" => '-',
                "EQUALS" => '=',
                "PLUS" => '+',
                "LEFTBRACKET" => '[',
                "RIGHTBRACKET" => ']',
                "SEMICOLON" => ';',
                "APOSTROPHE" => '\'',
                "GRAVE" => '`',
                "COMMA" => ',',
                "PERIOD" => '.',
                "SLASH" => '/',
                "BACKSLASH" => '\\',
                "HASH" => '#',
                "TILDE" => '~',
                _ => '\0'
            };

            if (ch != '\0')
            {
                return KeyNameForCharacter(ch);
            }

            return null;
        }

        /// <summary>
        /// EDSC key name for a punctuation character on the current keyboard layout, or null if
        /// the layout has no key for it. Shared with the Star Citizen importer.
        /// </summary>
        internal static string? KeyNameForCharacter(char ch)
        {
            try
            {
                short scan = VkKeyScanW(ch);
                if (scan == -1)
                {
                    return null;
                }

                var vk = (VirtualKeyCode)(scan & 0xFF);
                return vk.ToString();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[EliteBindingsService] VkKeyScan failed for '{ch}': {ex.Message}");
                return null;
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern short VkKeyScanW(char ch);
    }
}
