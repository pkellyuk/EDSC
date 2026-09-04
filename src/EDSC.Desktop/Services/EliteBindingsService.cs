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

            var existing = (config.Buttons ?? new List<ButtonConfig>())
                .Where(b => b != null && !string.IsNullOrEmpty(b.Id))
                .GroupBy(b => b.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var produced = new List<ButtonConfig>();
            var producedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int bound = 0;
            int unbound = 0;

            foreach (var item in bindings.Actions)
            {
                existing.TryGetValue(item.Action.Id, out var previous);

                if (!item.IsBound && previous == null)
                {
                    continue;
                }

                var button = new ButtonConfig
                {
                    Id = item.Action.Id,
                    Key = item.Key ?? string.Empty,
                    Label = previous?.Label is { Length: > 0 } ? previous.Label : item.Action.Label,
                    Category = item.Action.Category,
                    IconSvg = previous?.IconSvg is { Length: > 0 } ? previous.IconSvg : item.Action.IconSvg,
                    Icon = previous?.Icon ?? string.Empty,
                    Color = previous?.Color is { Length: > 0 } ? previous.Color : item.Action.Color,
                    Size = previous?.Size > 0 ? previous.Size : 80
                };

                produced.Add(button);
                producedIds.Add(button.Id);

                if (item.IsBound)
                {
                    bound++;
                }
                else
                {
                    unbound++;
                }
            }

            // Keep anything the user added that the catalogue does not cover
            int kept = 0;
            foreach (var button in config.Buttons ?? new List<ButtonConfig>())
            {
                if (button == null || string.IsNullOrEmpty(button.Id) || producedIds.Contains(button.Id))
                {
                    continue;
                }

                produced.Add(button);
                kept++;
            }

            config.Buttons = produced;

            var presetSummary = bindings.PresetFiles.Count > 0
                ? string.Join(", ", bindings.PresetFiles.Select(Path.GetFileName))
                : "none";

            var message = $"Imported {bound} bound buttons from {presetSummary}.";
            if (unbound > 0)
            {
                message += $" {unbound} existing buttons have no keyboard key in your Elite controls and are greyed out.";
            }
            if (kept > 0)
            {
                message += $" Kept {kept} custom buttons.";
            }

            return message;
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

        private static string? KeyNameForCharacter(char ch)
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
