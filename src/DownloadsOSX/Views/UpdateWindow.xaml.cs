using System;
using System.Threading;
using System.Windows;
using DownloadsOSX.Models;
using DownloadsOSX.Services;

namespace DownloadsOSX.Views
{
    /// <summary>"Update available" dialog: notes, then download + verify + install with progress.</summary>
    public partial class UpdateWindow : Window
    {
        private static UpdateWindow _instance;
        private readonly UpdateManifest _manifest;
        private CancellationTokenSource _cts;

        public static void ShowFor(UpdateManifest manifest)
        {
            if (_instance != null) { _instance.Activate(); return; }
            _instance = new UpdateWindow(manifest);
            _instance.Closed += (s, e) => _instance = null;
            _instance.Show();
            _instance.Activate();
        }

        private UpdateWindow(UpdateManifest manifest)
        {
            InitializeComponent();
            _manifest = manifest;
            new BackdropController(this, BackdropKind.Mica);
            SourceInitialized += (s, e) => BackdropController.Get(this).SetCaptionHeight(40);

            Headline.Text = string.Format(App.Str("UpdateAvailableHeadline"), manifest.Version);
            Versions.Text = string.Format(App.Str("UpdateVersions"), UpdateService.CurrentVersionText, manifest.Version)
                            + (string.IsNullOrEmpty(manifest.Published) ? "" : "  ·  " + manifest.Published);
            Notes.Text = string.IsNullOrWhiteSpace(manifest.Notes) ? App.Str("NoReleaseNotes") : manifest.Notes.Trim();
            if (!UpdateService.CanInstallInPlace) InstallButton.Content = App.Str("InstallUpdateAdmin");
            Closing += (s, e) => _cts?.Cancel();
        }

        private async void Install_Click(object sender, RoutedEventArgs e)
        {
            InstallButton.IsEnabled = false;
            SkipButton.IsEnabled = false;
            ProgressPanel.Visibility = Visibility.Visible;
            ProgressText.Text = App.Str("Downloading");
            _cts = new CancellationTokenSource();
            var progress = new Progress<double>(p =>
            {
                Progress.Value = p;
                ProgressText.Text = $"{App.Str("Downloading")} {p:P0}";
            });

            try
            {
                string path = App.Updater.PendingPath ?? await App.Updater.DownloadAsync(_manifest, progress, _cts.Token);
                Progress.Value = 1;
                ProgressText.Text = App.Str("Installing");
                UpdateService.ApplyAndRestart(path, startMinimized: false);
                App.Stack.Exit();
            }
            catch (OperationCanceledException) { }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                ProgressText.Text = App.Str("UpdateCancelledUac");
                InstallButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                Log.Warn("Update install failed", ex);
                ProgressText.Text = App.Str("UpdateFailed") + " " + ex.Message;
                InstallButton.IsEnabled = true;
                SkipButton.IsEnabled = true;
            }
        }

        private void Skip_Click(object sender, RoutedEventArgs e)
        {
            App.Updater.Skip(_manifest);
            Close();
        }

        private void Later_Click(object sender, RoutedEventArgs e) => Close();
    }
}
