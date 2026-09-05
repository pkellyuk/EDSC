using EDSC.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace EDSC.Desktop.Services
{
    public sealed class StarCitizenBindingsResult
    {
        /// <summary>True when an actionmaps.xml was read. Defaults still apply when it was not.</summary>
        public bool Found { get; set; }
        public string? File { get; set; }
        public List<ImportedAction> Actions { get; } = new List<ImportedAction>();
        public List<string> Notes { get; } = new List<string>();
    }

    /// <summary>
    /// Reads Star Citizen's keyboard bindings and turns them into EDSC buttons.
    ///
    /// The game writes only the actions the player has changed to
    /// &lt;channel&gt;\user\client\0\Profiles\default\actionmaps.xml, as
    /// &lt;action name="v_toggle_landing_system"&gt;&lt;rebind input="kb1_n"/&gt;&lt;/action&gt;.
    /// Everything else is at its stock default, which the catalogue carries.
    /// </summary>
    public sealed class StarCitizenBindingsService
    {
        private static readonly string[] Channels = { "LIVE", "PTU", "EPTU", "HOTFIX", "TECH-PREVIEW" };
        private static readonly string[] ProfileRelativePaths =
        {
            Path.Combine("user", "client", "0", "Profiles", "default", "actionmaps.xml"),
            Path.Combine("USER", "Client", "0", "Profiles", "default", "actionmaps.xml")
        };

        /// <summary>
        /// Read the player's rebinds (if the game is installed) and resolve a key for every catalogue action.
        /// </summary>
        /// <param name="installOverride">A channel folder (the one containing Bin64 and user) or the actionmaps.xml itself.</param>
        public StarCitizenBindingsResult Load(string? installOverride = null)
        {
            var result = new StarCitizenBindingsResult();
            var file = FindActionMaps(installOverride, result.Notes);

            var rebinds = new Dictionary<string, List<XElement>>(StringComparer.OrdinalIgnoreCase);

            if (file != null)
            {
                try
                {
                    var document = XDocument.Load(file);
                    foreach (var action in document.Descendants("action"))
                    {
                        var name = (string?)action.Attribute("name");
                        if (string.IsNullOrEmpty(name))
                        {
                            continue;
                        }

                        if (!rebinds.TryGetValue(name, out var list))
                        {
                            list = new List<XElement>();
                            rebinds[name] = list;
                        }

                        list.AddRange(action.Elements("rebind"));
                    }

                    result.Found = true;
                    result.File = file;
                    result.Notes.Add($"Read {rebinds.Count} rebound actions from {file}");
                }
                catch (Exception ex)
                {
                    result.Notes.Add($"Could not read {file}: {ex.Message}. Using the game's default keys.");
                }
            }
            else
            {
                result.Notes.Add("actionmaps.xml not found, so the game's stock keyboard defaults are used. Set starCitizenPath in config.json if the game is installed somewhere unusual.");
            }

            foreach (var action in StarCitizenActionCatalog.Actions)
            {
                string? key;
                string? input = null;
                bool rebound = false;

                foreach (var name in action.Names)
                {
                    if (!rebinds.TryGetValue(name, out var list))
                    {
                        continue;
                    }

                    // Only a keyboard rebind replaces the keyboard default; joystick or mouse
                    // rebinds leave the keyboard binding alone
                    var keyboard = list.FirstOrDefault(r => ((string?)r.Attribute("input") ?? string.Empty).StartsWith("kb1_", StringComparison.OrdinalIgnoreCase));
                    if (keyboard == null)
                    {
                        continue;
                    }

                    rebound = true;
                    input = ((string?)keyboard.Attribute("input") ?? string.Empty).Substring(4).Trim();

                    var multiTap = (string?)keyboard.Attribute("multiTap");
                    if (int.TryParse(multiTap, NumberStyles.Integer, CultureInfo.InvariantCulture, out var taps) && taps > 1)
                    {
                        result.Notes.Add($"{action.Label}: bound to a {taps}-tap of '{input}', which EDSC cannot send; shown as unbound");
                        input = string.Empty;
                    }

                    break;
                }

                if (!rebound)
                {
                    input = action.DefaultInput;
                }

                if (string.IsNullOrEmpty(input))
                {
                    key = null;
                }
                else
                {
                    key = TranslateInput(input);
                    if (key == null)
                    {
                        result.Notes.Add($"{action.Label}: unrecognised key '{input}'");
                    }
                }

                result.Actions.Add(new ImportedAction(action.Id, action.Label, action.Category, action.IconSvg, action.Color, action.VoiceAliases, key, action.HoldMs));
            }

            return result;
        }

        /// <summary>
        /// Rebuild the Star Citizen button list from the bindings, keeping the user's customisations.
        /// </summary>
        public static string ApplyToConfig(AppConfig config, StarCitizenBindingsResult bindings)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if (bindings == null)
            {
                return "No Star Citizen bindings to apply.";
            }

            var source = bindings.Found && bindings.File != null
                ? Path.GetFileName(bindings.File) + " plus the game's defaults"
                : "the game's default keys (actionmaps.xml not found)";

            config.StarCitizenButtons = BindingImport.Apply(config.StarCitizenButtons, bindings.Actions, source, out var message);
            return message;
        }

        /// <summary>
        /// Locate actionmaps.xml: an explicit path, the RSI Launcher's library folder, the default
        /// install folder, or the root of any fixed drive. LIVE wins; otherwise the newest file.
        /// </summary>
        public static string? FindActionMaps(string? installOverride, List<string>? notes = null)
        {
            var candidates = new List<string>();

            if (!string.IsNullOrWhiteSpace(installOverride))
            {
                if (File.Exists(installOverride))
                {
                    return installOverride;
                }

                if (Directory.Exists(installOverride))
                {
                    foreach (var relative in ProfileRelativePaths)
                    {
                        candidates.Add(Path.Combine(installOverride, relative));
                    }

                    // The override may be the StarCitizen folder above the channels
                    foreach (var channel in Channels)
                    {
                        candidates.Add(Path.Combine(installOverride, channel, ProfileRelativePaths[0]));
                    }
                }
                else
                {
                    notes?.Add($"starCitizenPath '{installOverride}' does not exist");
                }
            }

            foreach (var root in InstallRoots())
            {
                foreach (var channel in Channels)
                {
                    candidates.Add(Path.Combine(root, channel, ProfileRelativePaths[0]));
                }
            }

            string? best = null;
            DateTime bestTime = DateTime.MinValue;

            foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    if (!File.Exists(candidate))
                    {
                        continue;
                    }

                    if (candidate.IndexOf($"{Path.DirectorySeparatorChar}LIVE{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return candidate;
                    }

                    var time = File.GetLastWriteTimeUtc(candidate);
                    if (best == null || time > bestTime)
                    {
                        best = candidate;
                        bestTime = time;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[StarCitizenBindings] Probe failed for {candidate}: {ex.Message}");
                }
            }

            return best;
        }

        /// <summary>
        /// Folders that may contain LIVE, PTU and so on.
        /// </summary>
        private static IEnumerable<string> InstallRoots()
        {
            var roots = new List<string>();

            // RSI Launcher settings mention the library folder; the exact key has changed between
            // launcher versions, so any quoted path under that folder is taken
            try
            {
                var launcherDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "rsilauncher");
                if (Directory.Exists(launcherDir))
                {
                    foreach (var file in Directory.EnumerateFiles(launcherDir, "*.json", SearchOption.TopDirectoryOnly))
                    {
                        var info = new FileInfo(file);
                        if (info.Length > 2 * 1024 * 1024)
                        {
                            continue;
                        }

                        var text = File.ReadAllText(file);
                        foreach (Match m in Regex.Matches(text, "\"(?<p>[A-Za-z]:(?:\\\\\\\\|/)[^\"]*?)\""))
                        {
                            var path = m.Groups["p"].Value.Replace("\\\\", "\\").Replace('/', '\\').TrimEnd('\\');
                            if (path.Length < 4)
                            {
                                continue;
                            }

                            roots.Add(Path.Combine(path, "StarCitizen"));
                            roots.Add(path);
                            roots.Add(Path.Combine(path, "Roberts Space Industries", "StarCitizen"));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[StarCitizenBindings] Launcher settings lookup failed: {ex.Message}");
            }

            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            roots.Add(Path.Combine(programFiles, "Roberts Space Industries", "StarCitizen"));

            try
            {
                foreach (var drive in DriveInfo.GetDrives())
                {
                    if (drive.DriveType != DriveType.Fixed || !drive.IsReady)
                    {
                        continue;
                    }

                    roots.Add(Path.Combine(drive.RootDirectory.FullName, "Roberts Space Industries", "StarCitizen"));
                    roots.Add(Path.Combine(drive.RootDirectory.FullName, "Games", "Roberts Space Industries", "StarCitizen"));
                    roots.Add(Path.Combine(drive.RootDirectory.FullName, "Program Files", "Roberts Space Industries", "StarCitizen"));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[StarCitizenBindings] Drive enumeration failed: {ex.Message}");
            }

            return roots.Distinct(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Convert a Star Citizen keyboard input ("n", "lalt+n", "np_8", "ralt+y") to an EDSC key
        /// string ("N", "LMENU+N", "NUMPAD8", "RMENU+Y"). Returns null if any part is unknown.
        /// </summary>
        public static string? TranslateInput(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return null;
            }

            var tokens = input.Trim().ToLowerInvariant().Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (tokens.Length == 0)
            {
                return null;
            }

            var parts = new List<string>();
            foreach (var token in tokens)
            {
                var translated = TranslateToken(token);
                if (translated == null)
                {
                    return null;
                }

                parts.Add(translated);
            }

            return string.Join("+", parts);
        }

        private static string? TranslateToken(string token)
        {
            if (token.Length == 1 && char.IsLetterOrDigit(token[0]))
            {
                return token.ToUpperInvariant();
            }

            if (token.StartsWith("np_", StringComparison.Ordinal))
            {
                var rest = token.Substring(3);
                if (rest.Length == 1 && char.IsDigit(rest[0]))
                {
                    return "NUMPAD" + rest;
                }

                return rest switch
                {
                    "add" => "ADD",
                    "subtract" => "SUBTRACT",
                    "multiply" => "MULTIPLY",
                    "divide" => "DIVIDE",
                    "period" => "DECIMAL",
                    "enter" => "RETURN",
                    _ => null
                };
            }

            if (token.Length >= 2 && token[0] == 'f' && int.TryParse(token.Substring(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var fn) && fn >= 1 && fn <= 24)
            {
                return "F" + fn;
            }

            var named = token switch
            {
                "space" => "SPACE",
                "enter" => "RETURN",
                "escape" => "ESCAPE",
                "backspace" => "BACK",
                "tab" => "TAB",
                "capslock" => "CAPITAL",
                "insert" => "INSERT",
                "delete" => "DELETE",
                "home" => "HOME",
                "end" => "END",
                "pgup" => "PRIOR",
                "pgdn" => "NEXT",
                "up" => "UP",
                "down" => "DOWN",
                "left" => "LEFT",
                "right" => "RIGHT",
                "lshift" => "LSHIFT",
                "rshift" => "RSHIFT",
                "lctrl" => "LCONTROL",
                "rctrl" => "RCONTROL",
                "lalt" => "LMENU",
                "ralt" => "RMENU",
                "print" => "SNAPSHOT",
                "scrolllock" => "SCROLL",
                "pause" => "PAUSE",
                "numlock" => "NUMLOCK",
                _ => null
            };

            if (named != null)
            {
                return named;
            }

            var ch = token switch
            {
                "minus" => '-',
                "equals" => '=',
                "lbracket" => '[',
                "rbracket" => ']',
                "semicolon" => ';',
                "apostrophe" => '\'',
                "grave" => '`',
                "backslash" => '\\',
                "comma" => ',',
                "period" => '.',
                "slash" => '/',
                _ => '\0'
            };

            return ch != '\0' ? EliteBindingsService.KeyNameForCharacter(ch) : null;
        }
    }
}
