using System;
using System.Reflection;
using Microsoft.Win32;

namespace DownloadsOSX.Services
{
    /// <summary>
    /// HKCU Run entry. Uses the legacy value name so enabling it replaces the old app's entry instead of starting both.
    /// An entry that points to another copy of the app (e.g. the legacy install) is never changed automatically.
    /// </summary>
    internal static class Autostart
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "DownloaderOSX";

        public static string ExePath => Assembly.GetEntryAssembly().Location;

        private static string ReadValue()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKey))
                    return key?.GetValue(ValueName) as string;
            }
            catch { return null; }
        }

        /// <summary>True if Windows starts this exe at sign-in.</summary>
        public static bool IsEnabled
        {
            get
            {
                string v = ReadValue();
                return !string.IsNullOrEmpty(v) && v.IndexOf(ExePath, StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        public static void Set(bool enabled, bool minimized)
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (enabled) key.SetValue(ValueName, "\"" + ExePath + "\"" + (minimized ? " --minimized" : ""));
                    else if (IsEnabled) key.DeleteValue(ValueName); // only remove our own entry
                }
            }
            catch (Exception ex) { Log.Warn("Autostart update failed", ex); throw; }
        }

        /// <summary>Updates the flags of this exe's entry (e.g. --minimized). Leaves other copies' entries alone.</summary>
        public static void Refresh(bool minimized)
        {
            if (IsEnabled)
            {
                try { Set(true, minimized); } catch { }
            }
        }
    }
}
