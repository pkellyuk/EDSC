using EDSC.Services;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using WindowsInput;
using WindowsInput.Native;

namespace EDSC.Desktop.Services
{
    /// <summary>
    /// Windows implementation of keyboard service using InputSimulator
    /// </summary>
    public class WindowsKeyboardService : IKeyboardService
    {
        private readonly InputSimulator _simulator;

        public WindowsKeyboardService()
        {
            Debug.WriteLine("[WindowsKeyboardService] Entry: Constructor");

            _simulator = new InputSimulator();

            Debug.WriteLine("[WindowsKeyboardService] Exit: Constructor");
        }

        public Task SendKeyPressAsync(string key)
        {
            Debug.WriteLine($"[WindowsKeyboardService] Entry: SendKeyPressAsync(key={key})");

            if (string.IsNullOrEmpty(key))
            {
                Debug.WriteLine("[WindowsKeyboardService] Key is null or empty");
                return Task.CompletedTask;
            }

            try
            {
                var virtualKey = ParseKey(key);

                if (virtualKey == null)
                {
                    Debug.WriteLine($"[WindowsKeyboardService] Failed to parse key: {key}");
                    return Task.CompletedTask;
                }

                Debug.WriteLine($"[WindowsKeyboardService] Pressing key {key} (VK code: {virtualKey})");

                _simulator.Keyboard.KeyPress(virtualKey.Value);

                Debug.WriteLine($"[WindowsKeyboardService] Key {key} pressed successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WindowsKeyboardService] Error pressing key: {ex.Message}");
            }

            Debug.WriteLine("[WindowsKeyboardService] Exit: SendKeyPressAsync");
            return Task.CompletedTask;
        }

        public Task SendKeyDownAsync(string key)
        {
            Debug.WriteLine($"[WindowsKeyboardService] Entry: SendKeyDownAsync(key={key})");

            if (string.IsNullOrEmpty(key))
            {
                Debug.WriteLine("[WindowsKeyboardService] Key is null or empty");
                return Task.CompletedTask;
            }

            try
            {
                var virtualKey = ParseKey(key);

                if (virtualKey == null)
                {
                    Debug.WriteLine($"[WindowsKeyboardService] Failed to parse key: {key}");
                    return Task.CompletedTask;
                }

                Debug.WriteLine($"[WindowsKeyboardService] Key down: {key}");

                _simulator.Keyboard.KeyDown(virtualKey.Value);

                Debug.WriteLine($"[WindowsKeyboardService] Key {key} down");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WindowsKeyboardService] Error on key down: {ex.Message}");
            }

            Debug.WriteLine("[WindowsKeyboardService] Exit: SendKeyDownAsync");
            return Task.CompletedTask;
        }

        public Task SendKeyUpAsync(string key)
        {
            Debug.WriteLine($"[WindowsKeyboardService] Entry: SendKeyUpAsync(key={key})");

            if (string.IsNullOrEmpty(key))
            {
                Debug.WriteLine("[WindowsKeyboardService] Key is null or empty");
                return Task.CompletedTask;
            }

            try
            {
                var virtualKey = ParseKey(key);

                if (virtualKey == null)
                {
                    Debug.WriteLine($"[WindowsKeyboardService] Failed to parse key: {key}");
                    return Task.CompletedTask;
                }

                Debug.WriteLine($"[WindowsKeyboardService] Key up: {key}");

                _simulator.Keyboard.KeyUp(virtualKey.Value);

                Debug.WriteLine($"[WindowsKeyboardService] Key {key} up");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WindowsKeyboardService] Error on key up: {ex.Message}");
            }

            Debug.WriteLine("[WindowsKeyboardService] Exit: SendKeyUpAsync");
            return Task.CompletedTask;
        }

        private VirtualKeyCode? ParseKey(string key)
        {
            Debug.WriteLine($"[WindowsKeyboardService] Entry: ParseKey(key={key})");

            if (string.IsNullOrEmpty(key))
            {
                Debug.WriteLine("[WindowsKeyboardService] Key is null or empty");
                return null;
            }

            try
            {
                // Try to parse as enum directly (e.g., "F1", "Escape", "A")
                if (Enum.TryParse<VirtualKeyCode>(key, true, out var directMatch))
                {
                    Debug.WriteLine($"[WindowsKeyboardService] Direct match: {key} -> {directMatch}");
                    return directMatch;
                }

                // Try with "VK_" prefix (e.g., "F1" -> "VK_F1")
                var withPrefix = "VK_" + key.ToUpper();
                if (Enum.TryParse<VirtualKeyCode>(withPrefix, true, out var prefixMatch))
                {
                    Debug.WriteLine($"[WindowsKeyboardService] Prefix match: {key} -> {prefixMatch}");
                    return prefixMatch;
                }

                // Handle special cases
                var virtualKey = key.ToUpper() switch
                {
                    "ENTER" or "RETURN" => VirtualKeyCode.RETURN,
                    "ESC" or "ESCAPE" => VirtualKeyCode.ESCAPE,
                    "SPACE" or "SPACEBAR" => VirtualKeyCode.SPACE,
                    "CTRL" or "CONTROL" => VirtualKeyCode.CONTROL,
                    "ALT" => VirtualKeyCode.MENU,
                    "SHIFT" => VirtualKeyCode.SHIFT,
                    "TAB" => VirtualKeyCode.TAB,
                    _ => (VirtualKeyCode?)null
                };

                if (virtualKey.HasValue)
                {
                    Debug.WriteLine($"[WindowsKeyboardService] Special case match: {key} -> {virtualKey}");
                    return virtualKey;
                }

                Debug.WriteLine($"[WindowsKeyboardService] No match found for key: {key}");
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WindowsKeyboardService] Error parsing key: {ex.Message}");
                return null;
            }
            finally
            {
                Debug.WriteLine("[WindowsKeyboardService] Exit: ParseKey");
            }
        }
    }
}
