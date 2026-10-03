using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using DownloadsOSX.Interop;
using DownloadsOSX.Models;
using Microsoft.Win32;

namespace DownloadsOSX.Services
{
    /// <summary>
    /// Light/dark theme + accent color. Follows the Windows app theme live (WM_SETTINGCHANGE "ImmersiveColorSet"
    /// arrives as SystemEvents.UserPreferenceChanged) and swaps the theme resource dictionary in place.
    /// </summary>
    internal static class ThemeService
    {
        public static event Action Changed;

        public static bool IsDark { get; private set; }
        public static Color Accent { get; private set; } = Color.FromRgb(0x00, 0x67, 0xC0);

        private static ResourceDictionary _current;
        private static DispatcherTimer _debounce;

        public static void Init()
        {
            _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _debounce.Tick += (s, e) => { _debounce.Stop(); Refresh(); };
            SystemEvents.UserPreferenceChanged += (s, e) =>
                Application.Current?.Dispatcher.BeginInvoke(new Action(() => { _debounce.Stop(); _debounce.Start(); }));
            App.Settings.PropertyChanged += OnSettingChanged;
            Refresh();
        }

        private static void OnSettingChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(AppSettings.Theme):
                case nameof(AppSettings.Font):
                case nameof(AppSettings.FontSize):
                    Refresh();
                    break;
            }
        }

        public static void Refresh()
        {
            var s = App.Settings;
            bool dark = s.Theme == ThemeMode.Dark || (s.Theme == ThemeMode.System && SystemPrefersDark());
            Color accent = ReadAccent();

            bool changed = dark != IsDark || accent != Accent || _current == null;
            IsDark = dark;
            Accent = accent;

            var res = Application.Current.Resources;
            var dict = new ResourceDictionary
            {
                Source = new Uri($"pack://application:,,,/Downloads;component/Resources/Theme.{(dark ? "Dark" : "Light")}.xaml")
            };
            var merged = res.MergedDictionaries;
            int index = _current != null ? merged.IndexOf(_current) : -1;
            if (index >= 0) merged[index] = dict; else merged.Insert(Math.Min(2, merged.Count), dict);
            _current = dict;

            // Accent-derived brushes.
            res["Brush.Accent"] = Frozen(accent);
            res["Brush.AccentHover"] = Frozen(Shade(accent, dark ? 1.12 : 0.9));
            res["Brush.AccentText"] = Frozen(Luminance(accent) > 0.55 ? Colors.Black : Colors.White);
            res["Brush.Selected"] = Frozen(Color.FromArgb((byte)(dark ? 0x55 : 0x40), accent.R, accent.G, accent.B));
            res["Brush.SelectedHover"] = Frozen(Color.FromArgb((byte)(dark ? 0x70 : 0x58), accent.R, accent.G, accent.B));
            res["Color.Accent"] = accent;

            // Typography.
            res["App.FontFamily"] = s.Font == FontChoice.Ubuntu
                ? new FontFamily(new Uri("pack://application:,,,/Downloads;component/"), "./Assets/Fonts/#Ubuntu")
                : new FontFamily(OsInfo.IsWin11 ? "Segoe UI Variable Text, Segoe UI" : "Segoe UI");
            res["App.FontSize"] = s.FontSize;
            res["App.FontSizeSmall"] = Math.Max(10, s.FontSize - 2);
            res["App.FontSizeLarge"] = s.FontSize + 3;

            if (changed) Log.Info($"Theme: {(dark ? "dark" : "light")}, accent {accent}");
            Changed?.Invoke();
        }

        public static bool SystemPrefersDark()
        {
            if (!OsInfo.IsWin10OrLater) return false;
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
            }
            catch { return false; }
        }

        private static Color ReadAccent()
        {
            try
            {
                if (OsInfo.IsWin10OrLater)
                {
                    using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM"))
                    {
                        if (key?.GetValue("AccentColor") is int abgr)
                            return Color.FromRgb((byte)(abgr & 0xFF), (byte)((abgr >> 8) & 0xFF), (byte)((abgr >> 16) & 0xFF));
                    }
                }
                if (NativeMethods.DwmGetColorizationColor(out uint argb, out _) == 0)
                    return Color.FromRgb((byte)((argb >> 16) & 0xFF), (byte)((argb >> 8) & 0xFF), (byte)(argb & 0xFF));
            }
            catch { }
            return Color.FromRgb(0x00, 0x67, 0xC0);
        }

        public static Color ParseColor(string hex, Color fallback)
        {
            try { return (Color)ColorConverter.ConvertFromString(hex); }
            catch { return fallback; }
        }

        private static SolidColorBrush Frozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        private static double Luminance(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;

        private static Color Shade(Color c, double f) => Color.FromRgb(
            (byte)Math.Max(0, Math.Min(255, c.R * f)), (byte)Math.Max(0, Math.Min(255, c.G * f)), (byte)Math.Max(0, Math.Min(255, c.B * f)));

        public static void ApplyToAllWindows()
        {
            foreach (var w in Application.Current.Windows.OfType<Window>())
                BackdropController.Get(w)?.Apply();
        }
    }
}
