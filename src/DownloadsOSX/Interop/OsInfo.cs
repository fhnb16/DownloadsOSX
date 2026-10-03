using System;
using System.Runtime.InteropServices;

namespace DownloadsOSX.Interop
{
    /// <summary>
    /// Real OS version via RtlGetVersion (Environment.OSVersion lies for unmanifested apps and can't tell Win11 apart).
    /// Set DOWNLOADSOSX_OS_BUILD (e.g. 19045, 7601) to simulate an older build when testing backdrop fallbacks.
    /// </summary>
    internal static class OsInfo
    {
        public static readonly int Major;
        public static readonly int Minor;
        public static readonly int Build;

        static OsInfo()
        {
            var info = new NativeMethods.OSVERSIONINFOEX { dwOSVersionInfoSize = Marshal.SizeOf(typeof(NativeMethods.OSVERSIONINFOEX)) };
            if (NativeMethods.RtlGetVersion(ref info) == 0)
            {
                Major = info.dwMajorVersion;
                Minor = info.dwMinorVersion;
                Build = info.dwBuildNumber;
            }
            else
            {
                Major = Environment.OSVersion.Version.Major;
                Minor = Environment.OSVersion.Version.Minor;
                Build = Environment.OSVersion.Version.Build;
            }

            if (int.TryParse(Environment.GetEnvironmentVariable("DOWNLOADSOSX_OS_BUILD"), out int simulated) && simulated > 0)
            {
                Build = simulated;
                if (simulated >= 10240) { Major = 10; Minor = 0; }
                else if (simulated >= 9600) { Major = 6; Minor = 3; }
                else if (simulated >= 9200) { Major = 6; Minor = 2; }
                else if (simulated >= 7600) { Major = 6; Minor = 1; }
                else { Major = 6; Minor = 0; }
            }
        }

        public static bool IsVistaOr7 => Major == 6 && Minor <= 1;
        public static bool IsWin8x => Major == 6 && Minor >= 2;
        public static bool IsWin10OrLater => Major >= 10;
        public static bool IsWin11 => Major >= 10 && Build >= 22000;

        /// <summary>DWMWA_SYSTEMBACKDROP_TYPE (Mica / Acrylic / Tabbed), Win11 22H2.</summary>
        public static bool SupportsSystemBackdrop => Major >= 10 && Build >= 22621;
        /// <summary>Undocumented DWMWA_MICA_EFFECT, Win11 21H2.</summary>
        public static bool SupportsLegacyMica => Major >= 10 && Build >= 22000;
        /// <summary>SetWindowCompositionAttribute acrylic, Win10 1803.</summary>
        public static bool SupportsAcrylic => Major >= 10 && Build >= 17134;
        /// <summary>SetWindowCompositionAttribute blur, Win10 RTM.</summary>
        public static bool SupportsAccentBlur => Major >= 10;
        public static bool SupportsDarkTitle => Major >= 10 && Build >= 17763;
        public static bool SupportsCornerPreference => IsWin11;

        public static string Describe()
        {
            string name = IsWin11 ? "Windows 11" : IsWin10OrLater ? "Windows 10"
                : Major == 6 && Minor == 3 ? "Windows 8.1" : Major == 6 && Minor == 2 ? "Windows 8"
                : Major == 6 && Minor == 1 ? "Windows 7" : Major == 6 ? "Windows Vista" : "Windows";
            return $"{name} (build {Build})";
        }

        public static bool IsCompositionEnabled
        {
            get
            {
                if (IsWin8x || IsWin10OrLater) return true; // always on since Windows 8
                try { return NativeMethods.DwmIsCompositionEnabled(out bool on) == 0 && on; }
                catch { return false; }
            }
        }
    }
}
