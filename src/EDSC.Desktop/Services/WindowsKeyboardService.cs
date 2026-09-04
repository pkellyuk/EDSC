using EDSC.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
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
        private const uint InputKeyboard = 1;
        private const uint KeyEventFlagExtendedKey = 0x0001;
        private const uint KeyEventFlagKeyUp = 0x0002;
        private const uint KeyEventFlagScanCode = 0x0008;
        private const uint MapVkToVsc = 0x0000;
        private const string LogFileName = "edsc.keyboard.log";
        private const int LongPressDelayMs = 80;
        private const int ModifierSettleDelayMs = 30;

        private readonly InputSimulator _simulator;
        private static readonly object LogLock = new object();

        public WindowsKeyboardService()
        {
            Debug.WriteLine("[WindowsKeyboardService] Entry: Constructor");

            _simulator = new InputSimulator();

            Debug.WriteLine("[WindowsKeyboardService] Exit: Constructor");
        }

        public async Task SendKeyPressAsync(string key)
        {
            Debug.WriteLine($"[WindowsKeyboardService] Entry: SendKeyPressAsync(key={key})");
            LogToFile($"Entry: SendKeyPressAsync key={key}");

            if (string.IsNullOrEmpty(key))
            {
                Debug.WriteLine("[WindowsKeyboardService] Key is null or empty");
                return;
            }

            try
            {
                // "LSHIFT+U" style combos: everything before the last '+' is held as a modifier
                var parts = key.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length == 0)
                {
                    return;
                }

                var virtualKey = ParseKey(parts[parts.Length - 1]);

                if (virtualKey == null)
                {
                    Debug.WriteLine($"[WindowsKeyboardService] Failed to parse key: {key}");
                    return;
                }

                var modifiers = new List<VirtualKeyCode>();
                for (int i = 0; i < parts.Length - 1; i++)
                {
                    var modifier = ParseKey(parts[i]);
                    if (modifier == null)
                    {
                        Debug.WriteLine($"[WindowsKeyboardService] Failed to parse modifier: {parts[i]}");
                        LogToFile($"Failed to parse modifier '{parts[i]}' in '{key}'");
                        return;
                    }

                    modifiers.Add(modifier.Value);
                }

                Debug.WriteLine($"[WindowsKeyboardService] Pressing key {key} (VK code: {virtualKey}, modifiers: {modifiers.Count})");
                LogToFile($"Pressing key {key} (VK code: {virtualKey}, modifiers: {modifiers.Count})");

                foreach (var modifier in modifiers)
                {
                    SendKeyEvent(modifier, keyUp: false, key);
                }

                if (modifiers.Count > 0)
                {
                    await Task.Delay(ModifierSettleDelayMs);
                }

                SendKeyEvent(virtualKey.Value, keyUp: false, key);
                await Task.Delay(LongPressDelayMs);
                SendKeyEvent(virtualKey.Value, keyUp: true, key);

                if (modifiers.Count > 0)
                {
                    await Task.Delay(ModifierSettleDelayMs);
                    for (int i = modifiers.Count - 1; i >= 0; i--)
                    {
                        SendKeyEvent(modifiers[i], keyUp: true, key);
                    }
                }

                Debug.WriteLine($"[WindowsKeyboardService] Key {key} pressed successfully");
                LogToFile($"Key {key} pressed successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WindowsKeyboardService] Error pressing key: {ex.Message}");
                LogToFile($"Error pressing key: {ex}");
            }

            Debug.WriteLine("[WindowsKeyboardService] Exit: SendKeyPressAsync");
            LogToFile("Exit: SendKeyPressAsync");
            return;
        }

        public Task SendKeyDownAsync(string key)
        {
            Debug.WriteLine($"[WindowsKeyboardService] Entry: SendKeyDownAsync(key={key})");
            LogToFile($"Entry: SendKeyDownAsync key={key}");

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
                LogToFile($"Key down: {key}");
                if (!TrySendInput(virtualKey.Value, keyUp: false))
                {
                    Debug.WriteLine($"[WindowsKeyboardService] Falling back to InputSimulator for key down: {key}");
                    LogToFile($"Fallback InputSimulator KeyDown key={key}");
                    _simulator.Keyboard.KeyDown(virtualKey.Value);
                }

                Debug.WriteLine($"[WindowsKeyboardService] Key {key} down");
                LogToFile($"Key {key} down");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WindowsKeyboardService] Error on key down: {ex.Message}");
                LogToFile($"Error on key down: {ex}");
            }

            Debug.WriteLine("[WindowsKeyboardService] Exit: SendKeyDownAsync");
            LogToFile("Exit: SendKeyDownAsync");
            return Task.CompletedTask;
        }

        public Task SendKeyUpAsync(string key)
        {
            Debug.WriteLine($"[WindowsKeyboardService] Entry: SendKeyUpAsync(key={key})");
            LogToFile($"Entry: SendKeyUpAsync key={key}");

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
                LogToFile($"Key up: {key}");
                if (!TrySendInput(virtualKey.Value, keyUp: true))
                {
                    Debug.WriteLine($"[WindowsKeyboardService] Falling back to InputSimulator for key up: {key}");
                    LogToFile($"Fallback InputSimulator KeyUp key={key}");
                    _simulator.Keyboard.KeyUp(virtualKey.Value);
                }

                Debug.WriteLine($"[WindowsKeyboardService] Key {key} up");
                LogToFile($"Key {key} up");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WindowsKeyboardService] Error on key up: {ex.Message}");
                LogToFile($"Error on key up: {ex}");
            }

            Debug.WriteLine("[WindowsKeyboardService] Exit: SendKeyUpAsync");
            LogToFile("Exit: SendKeyUpAsync");
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
                if (key.Length == 1 && char.IsDigit(key[0]))
                {
                    var digitValue = key[0] - '0';
                    var digitKey = VirtualKeyCode.VK_0 + digitValue;
                    Debug.WriteLine($"[WindowsKeyboardService] Digit match: {key} -> {digitKey}");
                    return digitKey;
                }

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

        private static bool TrySendInput(VirtualKeyCode virtualKey, bool keyUp)
        {
            var vk = (ushort)virtualKey;
            var scanCode = (ushort)MapVirtualKey(vk, MapVkToVsc);
            var flags = keyUp ? KeyEventFlagKeyUp : 0u;

            if (IsExtendedKey(virtualKey))
            {
                flags |= KeyEventFlagExtendedKey;
            }

            var useScanCode = scanCode != 0;
            if (useScanCode)
            {
                flags |= KeyEventFlagScanCode;
            }

            Debug.WriteLine($"[WindowsKeyboardService] SendInput key={(VirtualKeyCode)vk}, scanCode={scanCode}, flags=0x{flags:X}");
            LogToFile($"SendInput key={(VirtualKeyCode)vk}, scanCode={scanCode}, flags=0x{flags:X}, keyUp={keyUp}");

            var input = new INPUT
            {
                type = InputKeyboard,
                U = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = useScanCode ? (ushort)0 : vk,
                        wScan = useScanCode ? scanCode : (ushort)0,
                        dwFlags = flags,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };

            var sent = SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
            if (sent == 0)
            {
                Debug.WriteLine($"[WindowsKeyboardService] SendInput failed (win32={Marshal.GetLastWin32Error()})");
                LogToFile($"SendInput failed (win32={Marshal.GetLastWin32Error()})");
                return false;
            }

            return true;
        }

        private void SendKeyEvent(VirtualKeyCode virtualKey, bool keyUp, string originalKey)
        {
            if (TrySendInput(virtualKey, keyUp))
            {
                return;
            }

            Debug.WriteLine($"[WindowsKeyboardService] Falling back to InputSimulator for {(keyUp ? "key up" : "key down")}: {virtualKey} ({originalKey})");
            LogToFile($"Fallback InputSimulator {(keyUp ? "KeyUp" : "KeyDown")} vk={virtualKey} key={originalKey}");

            if (keyUp)
            {
                _simulator.Keyboard.KeyUp(virtualKey);
            }
            else
            {
                _simulator.Keyboard.KeyDown(virtualKey);
            }
        }

        private static bool RequiresLongPress(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            return true;
        }

        private static void LogToFile(string message)
        {
            try
            {
                var logPath = Path.Combine(AppContext.BaseDirectory, LogFileName);
                var line = $"{DateTime.UtcNow:O} {message}{Environment.NewLine}";
                lock (LogLock)
                {
                    File.AppendAllText(logPath, line);
                }
            }
            catch
            {
                // Swallow logging failures to avoid breaking key send path.
            }
        }

        private static bool IsExtendedKey(VirtualKeyCode virtualKey)
        {
            return virtualKey switch
            {
                VirtualKeyCode.RMENU => true,
                VirtualKeyCode.RCONTROL => true,
                VirtualKeyCode.INSERT => true,
                VirtualKeyCode.DELETE => true,
                VirtualKeyCode.HOME => true,
                VirtualKeyCode.END => true,
                VirtualKeyCode.PRIOR => true,
                VirtualKeyCode.NEXT => true,
                VirtualKeyCode.LEFT => true,
                VirtualKeyCode.RIGHT => true,
                VirtualKeyCode.UP => true,
                VirtualKeyCode.DOWN => true,
                VirtualKeyCode.NUMLOCK => true,
                VirtualKeyCode.DIVIDE => true,
                VirtualKeyCode.SNAPSHOT => true,
                _ => false
            };
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint uCode, uint uMapType);

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public InputUnion U;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)]
            public KEYBDINPUT ki;

            [FieldOffset(0)]
            public MOUSEINPUT mi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }
    }
}
