using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using System.Windows.Interop;
using DownloadsOSX.Interop;

namespace DownloadsOSX.Services
{
    /// <summary>System-wide hotkey (RegisterHotKey) bound to a window.</summary>
    internal sealed class HotkeyService : IDisposable
    {
        private const int HotkeyId = 0xB00D;
        private readonly IntPtr _hwnd;
        private bool _registered;

        public event Action Pressed;

        public HotkeyService(IntPtr hwnd)
        {
            _hwnd = hwnd;
            HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
        }

        /// <summary>Registers the gesture ("Win+Alt+D"). Returns false if it is invalid or taken by another app.</summary>
        public bool Register(string gesture)
        {
            Unregister();
            if (!TryParse(gesture, out var mods, out var key)) return false;
            uint fs = NativeMethods.MOD_NOREPEAT;
            if (mods.HasFlag(ModifierKeys.Alt)) fs |= NativeMethods.MOD_ALT;
            if (mods.HasFlag(ModifierKeys.Control)) fs |= NativeMethods.MOD_CONTROL;
            if (mods.HasFlag(ModifierKeys.Shift)) fs |= NativeMethods.MOD_SHIFT;
            if (mods.HasFlag(ModifierKeys.Windows)) fs |= NativeMethods.MOD_WIN;
            _registered = NativeMethods.RegisterHotKey(_hwnd, HotkeyId, fs, (uint)KeyInterop.VirtualKeyFromKey(key));
            if (!_registered) Log.Warn("RegisterHotKey failed for " + gesture);
            return _registered;
        }

        public void Unregister()
        {
            if (_registered) NativeMethods.UnregisterHotKey(_hwnd, HotkeyId);
            _registered = false;
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
            {
                handled = true;
                Pressed?.Invoke();
            }
            return IntPtr.Zero;
        }

        public static string Format(ModifierKeys mods, Key key)
        {
            var parts = new List<string>();
            if (mods.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
            if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
            if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
            if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
            parts.Add(KeyName(key));
            return string.Join("+", parts);
        }

        private static string KeyName(Key key)
        {
            if (key >= Key.D0 && key <= Key.D9) return ((int)(key - Key.D0)).ToString();
            switch (key)
            {
                case Key.OemTilde: return "`";
                case Key.OemMinus: return "-";
                case Key.OemPlus: return "=";
                case Key.OemComma: return ",";
                case Key.OemPeriod: return ".";
                default: return key.ToString();
            }
        }

        public static bool TryParse(string gesture, out ModifierKeys mods, out Key key)
        {
            mods = ModifierKeys.None;
            key = Key.None;
            if (string.IsNullOrWhiteSpace(gesture)) return false;
            foreach (var raw in gesture.Split('+').Select(p => p.Trim()))
            {
                switch (raw.ToLowerInvariant())
                {
                    case "win": mods |= ModifierKeys.Windows; continue;
                    case "ctrl": case "control": mods |= ModifierKeys.Control; continue;
                    case "alt": mods |= ModifierKeys.Alt; continue;
                    case "shift": mods |= ModifierKeys.Shift; continue;
                    case "`": key = Key.OemTilde; continue;
                    case "-": key = Key.OemMinus; continue;
                    case "=": key = Key.OemPlus; continue;
                    case ",": key = Key.OemComma; continue;
                    case ".": key = Key.OemPeriod; continue;
                }
                if (raw.Length == 1 && char.IsDigit(raw[0])) key = Key.D0 + (raw[0] - '0');
                else if (!Enum.TryParse(raw, true, out key)) return false;
            }
            return key != Key.None && mods != ModifierKeys.None;
        }

        public void Dispose()
        {
            Unregister();
            HwndSource.FromHwnd(_hwnd)?.RemoveHook(WndProc);
        }
    }
}
