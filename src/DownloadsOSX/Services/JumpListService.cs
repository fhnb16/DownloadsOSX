using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Shell;
using DownloadsOSX.Interop;
using DownloadsOSX.Models;

namespace DownloadsOSX.Services
{
    /// <summary>Taskbar Jump List: recent downloads + tasks. Items relaunch the exe with --open, which opens the file and exits.</summary>
    internal static class JumpListService
    {
        private static string _signature;

        public static void Update(IEnumerable<FileEntry> newestFirst)
        {
            var s = App.Settings;
            var items = s.JumpList ? newestFirst.Where(e => !e.IsPartial).Take(s.JumpListCount).ToList() : new List<FileEntry>();
            string sig = s.JumpList + "|" + App.Str("JumpRecent") + "|" + string.Join("|", items.Select(i => i.Path));
            if (sig == _signature) return;
            _signature = sig;

            try
            {
                string exe = Autostart.ExePath;
                var list = new JumpList { ShowFrequentCategory = false, ShowRecentCategory = false };

                if (s.JumpList)
                {
                    string recent = App.Str("JumpRecent");
                    foreach (var e in items)
                    {
                        list.JumpItems.Add(new JumpTask
                        {
                            Title = e.Name,
                            Description = e.Path,
                            ApplicationPath = exe,
                            Arguments = "--open \"" + e.Path + "\"",
                            IconResourcePath = IconFor(e, exe),
                            IconResourceIndex = 0,
                            CustomCategory = recent,
                        });
                    }

                    list.JumpItems.Add(new JumpTask { Title = App.Str("OpenFolder"), ApplicationPath = exe, Arguments = "--open-folder", IconResourcePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe") });
                    list.JumpItems.Add(new JumpTask { Title = App.Str("Settings"), ApplicationPath = exe, Arguments = "--settings", IconResourcePath = exe });
                    list.JumpItems.Add(new JumpTask { Title = App.Str("CheckForUpdates"), ApplicationPath = exe, Arguments = "--check-updates", IconResourcePath = exe });
                }

                JumpList.SetJumpList(Application.Current, list);
                list.Apply();
            }
            catch (Exception ex) { Log.Warn("Jump list update failed", ex); }
        }

        private static string IconFor(FileEntry e, string fallback)
        {
            if (e.IsFolder) return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            string ext = e.Extension;
            if (string.Equals(ext, ".exe", StringComparison.OrdinalIgnoreCase)) return e.Path;
            string assoc = Shell.GetAssociatedExecutable(ext);
            // Store apps resolve to WindowsApps paths the shell can't load icons from.
            if (!string.IsNullOrEmpty(assoc) && File.Exists(assoc) && assoc.IndexOf("WindowsApps", StringComparison.OrdinalIgnoreCase) < 0) return assoc;
            return fallback;
        }
    }
}
