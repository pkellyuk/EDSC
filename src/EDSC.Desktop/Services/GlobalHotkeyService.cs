using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace EDSC.Desktop.Services
{
    /// <summary>
    /// System-wide hotkey via a low-level keyboard hook, so it fires while the game has focus.
    /// Keystrokes injected by software (including EDSC's own simulated presses) are ignored,
    /// which stops a phone button bound to the same key from triggering the hotkey.
    ///
    /// Also supports capturing the next real keypress, for a "change key" button.
    ///
    /// Must be started on a thread that pumps Windows messages, such as the Avalonia UI thread.
    /// </summary>
    public sealed class GlobalHotkeyService : IDisposable
    {
        private const int WhKeyboardLl = 13;
        private const int WmKeyDown = 0x0100;
        private const int WmSysKeyDown = 0x0104;
        private const uint LlkhfInjected = 0x10;
        private const int VkEscape = 0x1B;

        /// <summary>Virtual key code for the "=" key on US and UK layouts.</summary>
        public const int VkOemPlus = 0xBB;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        private readonly bool _ignoreInjected;
        private LowLevelKeyboardProc? _hookProc;   // kept alive so the GC does not collect the callback
        private IntPtr _hookHandle = IntPtr.Zero;
        private volatile int _virtualKey;
        private Action? _onPressed;
        private Action<int?>? _captureCallback;

        public GlobalHotkeyService(bool ignoreInjected = true)
        {
            _ignoreInjected = ignoreInjected;
        }

        public bool IsActive
        {
            get { return _hookHandle != IntPtr.Zero; }
        }

        /// <summary>
        /// The key that triggers the hotkey. Can be changed while the hook is installed.
        /// </summary>
        public int VirtualKey
        {
            get { return _virtualKey; }
            set { _virtualKey = value; }
        }

        /// <summary>
        /// Install the hook. Returns false if Windows refused it.
        /// </summary>
        public bool Start(int virtualKey, Action onPressed)
        {
            if (onPressed == null)
            {
                throw new ArgumentNullException(nameof(onPressed));
            }

            if (_hookHandle != IntPtr.Zero)
            {
                return true;
            }

            _virtualKey = virtualKey;
            _onPressed = onPressed;
            _hookProc = HookCallback;

            using var module = Process.GetCurrentProcess().MainModule;
            var moduleHandle = GetModuleHandle(module?.ModuleName);

            _hookHandle = SetWindowsHookEx(WhKeyboardLl, _hookProc, moduleHandle, 0);
            if (_hookHandle == IntPtr.Zero)
            {
                Debug.WriteLine($"[GlobalHotkeyService] SetWindowsHookEx failed (win32={Marshal.GetLastWin32Error()})");
                _hookProc = null;
                return false;
            }

            Debug.WriteLine($"[GlobalHotkeyService] Hook installed for VK 0x{virtualKey:X}");
            return true;
        }

        /// <summary>
        /// Report the next real (non-injected, non-modifier) key press to <paramref name="onCaptured"/>
        /// instead of treating it as input. Escape cancels and reports null. The captured press is
        /// swallowed so it does not reach the focused application.
        /// </summary>
        public void CaptureNextKey(Action<int?> onCaptured)
        {
            _captureCallback = onCaptured ?? throw new ArgumentNullException(nameof(onCaptured));
        }

        public void CancelCapture()
        {
            _captureCallback = null;
        }

        public void Stop()
        {
            if (_hookHandle == IntPtr.Zero)
            {
                return;
            }

            if (!UnhookWindowsHookEx(_hookHandle))
            {
                Debug.WriteLine($"[GlobalHotkeyService] UnhookWindowsHookEx failed (win32={Marshal.GetLastWin32Error()})");
            }

            _hookHandle = IntPtr.Zero;
            _hookProc = null;
            _onPressed = null;
            _captureCallback = null;
        }

        public void Dispose()
        {
            Stop();
        }

        /// <summary>
        /// Friendly name for a virtual key on the current keyboard layout: the character it types
        /// where there is one, otherwise the key's name.
        /// </summary>
        public static string DescribeKey(int virtualKey)
        {
            if (virtualKey >= 0x60 && virtualKey <= 0x69)
            {
                return "Num " + (virtualKey - 0x60);
            }

            try
            {
                uint ch = MapVirtualKeyW((uint)virtualKey, 2 /* MAPVK_VK_TO_CHAR */) & 0xFFFF;
                if (ch > 32 && ch < 0xFFFF)
                {
                    return ((char)ch).ToString();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GlobalHotkeyService] MapVirtualKey failed: {ex.Message}");
            }

            return ((WindowsInput.Native.VirtualKeyCode)virtualKey).ToString().Replace("VK_", string.Empty);
        }

        private static bool IsModifierKey(uint vk)
        {
            return vk == 0x10 || vk == 0x11 || vk == 0x12          // SHIFT, CONTROL, MENU
                || (vk >= 0xA0 && vk <= 0xA5)                      // L/R SHIFT, CONTROL, MENU
                || vk == 0x5B || vk == 0x5C;                        // L/R WIN
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0)
                {
                    int message = wParam.ToInt32();
                    if (message == WmKeyDown || message == WmSysKeyDown)
                    {
                        var info = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
                        bool injected = (info.flags & LlkhfInjected) != 0;

                        if (!(_ignoreInjected && injected))
                        {
                            var capture = _captureCallback;
                            if (capture != null && !IsModifierKey(info.vkCode))
                            {
                                _captureCallback = null;
                                int? captured = info.vkCode == VkEscape ? null : (int)info.vkCode;
                                _ = Task.Run(() => capture(captured));
                                return (IntPtr)1;   // swallow the press that set the binding
                            }

                            if (info.vkCode == (uint)_virtualKey)
                            {
                                var handler = _onPressed;
                                if (handler != null)
                                {
                                    // Never do work inside the hook; Windows drops hooks that stall
                                    _ = Task.Run(handler);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GlobalHotkeyService] Hook callback error: {ex.Message}");
            }

            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KbdLlHookStruct
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern uint MapVirtualKeyW(uint uCode, uint uMapType);
    }
}
