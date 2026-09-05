using EDSC.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EDSC.Desktop.Services
{
    /// <summary>
    /// One catalogue action after a game's bindings have been read: what to show and which
    /// EDSC key (if any) presses it. Shared by the Elite Dangerous and Star Citizen importers.
    /// </summary>
    public sealed class ImportedAction
    {
        public string Id { get; }
        public string Label { get; }
        public string Category { get; }
        public string IconSvg { get; }
        public string Color { get; }
        public IReadOnlyList<string> VoiceAliases { get; }
        public string? Key { get; }
        public int HoldMs { get; }

        public ImportedAction(string id, string label, string category, string iconSvg, string color, IReadOnlyList<string> voiceAliases, string? key, int holdMs)
        {
            Id = id;
            Label = label;
            Category = category;
            IconSvg = iconSvg ?? string.Empty;
            Color = color;
            VoiceAliases = voiceAliases ?? Array.Empty<string>();
            Key = key;
            HoldMs = Math.Max(0, holdMs);
        }

        public bool IsBound
        {
            get { return !string.IsNullOrEmpty(Key); }
        }
    }

    /// <summary>
    /// Turns imported actions into a button list while keeping the user's customisations.
    /// </summary>
    public static class BindingImport
    {
        /// <summary>
        /// Rebuild a button list from imported actions. Existing buttons with a matching id keep their
        /// label, colour, icon, size and voice aliases; buttons the catalogue does not know about are
        /// kept at the end. Actions without a keyboard binding are only included if a button for them
        /// already existed, and are left with an empty key so the UI can show them as unbound.
        /// </summary>
        /// <returns>The new list and a summary line for the status bar.</returns>
        public static List<ButtonConfig> Apply(IEnumerable<ButtonConfig>? existingButtons, IEnumerable<ImportedAction> actions, string sourceSummary, out string message)
        {
            var existingList = (existingButtons ?? Enumerable.Empty<ButtonConfig>())
                .Where(b => b != null && !string.IsNullOrEmpty(b.Id))
                .ToList();

            var existing = existingList
                .GroupBy(b => b.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var produced = new List<ButtonConfig>();
            var producedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int bound = 0;
            int unbound = 0;

            foreach (var item in actions)
            {
                existing.TryGetValue(item.Id, out var previous);

                if (!item.IsBound && previous == null)
                {
                    continue;
                }

                var button = new ButtonConfig
                {
                    Id = item.Id,
                    Key = item.Key ?? string.Empty,
                    Label = previous?.Label is { Length: > 0 } ? previous.Label : item.Label,
                    Category = item.Category,
                    IconSvg = previous?.IconSvg is { Length: > 0 } ? previous.IconSvg : item.IconSvg,
                    Icon = previous?.Icon ?? string.Empty,
                    Color = previous?.Color is { Length: > 0 } ? previous.Color : item.Color,
                    Size = previous?.Size > 0 ? previous.Size : 80,
                    HoldMs = previous != null && previous.HoldMs > 0 ? previous.HoldMs : item.HoldMs,
                    VoiceAliases = previous?.VoiceAliases is { Count: > 0 }
                        ? new List<string>(previous.VoiceAliases)
                        : new List<string>(item.VoiceAliases)
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
            foreach (var button in existingList)
            {
                if (producedIds.Contains(button.Id))
                {
                    continue;
                }

                produced.Add(button);
                kept++;
            }

            message = $"Imported {bound} bound buttons from {sourceSummary}.";
            if (unbound > 0)
            {
                message += $" {unbound} existing buttons have no keyboard key and are greyed out.";
            }
            if (kept > 0)
            {
                message += $" Kept {kept} custom buttons.";
            }

            return produced;
        }
    }
}
