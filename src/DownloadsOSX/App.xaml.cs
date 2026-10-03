using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using DownloadsOSX.Interop;
using DownloadsOSX.Models;
using DownloadsOSX.Services;
using DownloadsOSX.Views;
using Microsoft.Win32;

namespace DownloadsOSX
{
    public partial class App : Application
    {
        internal static AppSettings Settings { get; private set; }
        internal static SettingsStore Store { get; private set; }
        internal static StaWorker Sta { get; private set; }
        internal static DownloadsFeed Feed { get; private set; }
        internal static UpdateService Updater { get; private set; }
        internal static MainWindow Stack { get; private set; }

        /// <summary>Localized string by resource key (falls back to the key).</summary>
        public static string Str(string key) => Current?.TryFindResource(key) as string ?? key;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            var args = e.Args ?? new string[0];

            // Elevated self-install of an update (runs from %TEMP%, no UI).
            if (UpdateService.HandleInstallCommand(args)) { Shutdown(); return; }

            // Jump List "open this file" items: open and leave.
            int open = Array.IndexOf(args, "--open");
            if (open >= 0 && open + 1 < args.Length)
            {
                try { Shell.Open(args[open + 1]); } catch (Exception ex) { MessageBox.Show(ex.Message, "Downloads"); }
                Shutdown();
                return;
            }

            InstanceCommand command = InstanceCommand.Show;
            if (args.Contains("--settings")) command = InstanceCommand.Settings;
            else if (args.Contains("--check-updates")) command = InstanceCommand.CheckUpdates;
            else if (args.Contains("--open-folder")) command = InstanceCommand.OpenFolder;
            else if (args.Contains("--exit")) command = InstanceCommand.Exit;

            bool justUpdated = args.Contains("--updated");
            if (!SingleInstance.TryAcquire(justUpdated ? 15000 : 0))
            {
                SingleInstance.Signal(command);
                Shutdown();
                return;
            }
            if (command == InstanceCommand.Exit) { Shutdown(); return; }

            string folder = SettingsStore.ResolveFolder();
            Log.Init(folder);
            Log.Info($"Start {UpdateService.CurrentVersionText} on {OsInfo.Describe()}, {(Environment.Is64BitProcess ? "x64" : "x86")}, args: {string.Join(" ", args)}");

            DispatcherUnhandledException += OnDispatcherException;
            AppDomain.CurrentDomain.UnhandledException += (s, ev) => Log.Error("Unhandled", ev.ExceptionObject as Exception);
            TaskScheduler.UnobservedTaskException += (s, ev) => { Log.Warn("Unobserved task", ev.Exception); ev.SetObserved(); };

            // Windows 7 doesn't offer TLS 1.2 to .NET by default; GitHub requires it.
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11;

            if (!CheckFramework()) { Shutdown(); return; }

            Store = new SettingsStore(folder);
            Settings = Store.Settings;
            ApplyLanguage();
            Settings.PropertyChanged += (s, ev) =>
            {
                if (ev.PropertyName == nameof(AppSettings.Language)) ApplyLanguage();
                if (ev.PropertyName == nameof(AppSettings.StartMinimized)) Autostart.Refresh(Settings.StartMinimized);
            };

            ThemeService.Init();
            UpdateService.Cleanup();
            Autostart.Refresh(Settings.StartMinimized);

            Sta = new StaWorker("Shell worker");
            Feed = new DownloadsFeed(Settings, Sta);
            Updater = new UpdateService(Settings);

            Stack = new MainWindow();
            Stack.Closed += (s, ev) => Shutdown();

            bool minimized = args.Contains("--minimized") || command == InstanceCommand.CheckUpdates;
            Stack.Start(minimized);
            Feed.Start();
            if (Settings.UpdateMode != UpdateMode.Off) Updater.StartSchedule();

            if (justUpdated) Stack.ShowToast(string.Format(Str("UpdatedTo"), UpdateService.CurrentVersionText), 6);
            if (command != InstanceCommand.Show) Stack.HandleCommand(command);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                Store?.Flush();
                Feed?.Dispose();
                Sta?.Dispose();
                Stack?.DisposeServices();
            }
            catch { }
            SingleInstance.Release();
            base.OnExit(e);
        }

        public void ApplyLanguage()
        {
            string code;
            switch (Settings.Language)
            {
                case LanguageChoice.English: code = "en"; break;
                case LanguageChoice.Russian: code = "ru"; break;
                case LanguageChoice.Ukrainian: code = "uk"; break;
                default:
                    string ui = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
                    code = ui == "ru" || ui == "be" || ui == "kk" ? "ru" : ui == "uk" ? "uk" : "en";
                    break;
            }

            var overlay = code == "en"
                ? new ResourceDictionary()
                : new ResourceDictionary { Source = new Uri($"pack://application:,,,/Downloads;component/Resources/Strings.{code}.xaml") };
            Resources.MergedDictionaries[1] = overlay;
            Log.Info("Language: " + code);
        }

        private static void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Log.Error("UI exception", e.Exception);
            e.Handled = true;
            try { Stack?.ShowToast(Str("ErrorGeneric") + " " + e.Exception.Message, 6); } catch { }
        }

        /// <summary>.NET Framework 4.8 is required (Release ≥ 528040). Offer the download otherwise.</summary>
        private static bool CheckFramework()
        {
            try
            {
                using (var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32).OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"))
                {
                    if (key?.GetValue("Release") is int release && release >= 528040) return true;
                }
            }
            catch { return true; }

            var answer = MessageBox.Show(
                "Downloads needs Microsoft .NET Framework 4.8.\n\nOpen the download page now?\n(Choose \"No\" to try running anyway.)",
                "Downloads", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
            if (answer == MessageBoxResult.Yes)
            {
                try { System.Diagnostics.Process.Start("https://dotnet.microsoft.com/download/dotnet-framework/net48"); } catch { }
                return false;
            }
            return answer == MessageBoxResult.No;
        }
    }
}
