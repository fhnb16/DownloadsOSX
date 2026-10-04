using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DownloadsOSX.Interop;
using DownloadsOSX.Models;
using DownloadsOSX.Services;

namespace DownloadsOSX.Views
{
    public partial class SettingsWindow : Window
    {
        private static SettingsWindow _instance;
        private readonly AppSettings _s = App.Settings;
        private readonly BackdropController _backdrop;
        private bool _recordingHotkey;

        public static void ShowSingle(string page = null)
        {
            if (_instance == null)
            {
                _instance = new SettingsWindow();
                _instance.Closed += (s, e) => _instance = null;
                _instance.Show();
            }
            if (_instance.WindowState == WindowState.Minimized) _instance.WindowState = WindowState.Normal;
            _instance.Activate();
            if (page != null) _instance.SelectPage(page);
        }

        internal static void NotifyUpdateResult(UpdateCheckResult result) => _instance?.ShowUpdateResult(result);

        private SettingsWindow()
        {
            InitializeComponent();
            DataContext = _s;
            _backdrop = new BackdropController(this, BackdropKind.Mica, resizeBorder: 6);
            SourceInitialized += (s, e) => _backdrop.SetCaptionHeight(40);

            Nav.SelectedIndex = 0;
            VersionFooter.Text = "v" + UpdateService.CurrentVersionText;

            _s.PropertyChanged += OnSettingChanged;
            BackdropController.EffectiveChanged += OnEffectiveChanged;
            App.Updater.PropertyChanged += OnUpdaterChanged;
            Closed += (s, e) =>
            {
                _s.PropertyChanged -= OnSettingChanged;
                BackdropController.EffectiveChanged -= OnEffectiveChanged;
                App.Updater.PropertyChanged -= OnUpdaterChanged;
            };

            BuildSwatches();
            BuildShortcuts();
            _overrideTool = BackdropController.DetectOverrideTool();
            Activated += (s, e) =>
            {
                _overrideTool = BackdropController.DetectOverrideTool(); // the tool may have been closed meanwhile
                RefreshBackdropStatus();
            };
            ThemeService.Changed += RefreshBackdropStatus;
            Closed += (s, e) => ThemeService.Changed -= RefreshBackdropStatus;
            Refresh();
        }

        private void OnEffectiveChanged(BackdropController c) => Dispatcher.BeginInvoke(new Action(RefreshBackdropStatus));
        private void OnUpdaterChanged(object sender, PropertyChangedEventArgs e) => Dispatcher.BeginInvoke(new Action(RefreshUpdates));

        private void OnSettingChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(AppSettings.AutoOpacity):
                case nameof(AppSettings.TintOpacity):
                    Dispatcher.BeginInvoke(new Action(RefreshBackdropStatus));
                    break;
                case nameof(AppSettings.ViewMode):
                case nameof(AppSettings.TintMode):
                case nameof(AppSettings.TintColor):
                case nameof(AppSettings.Backdrop):
                case nameof(AppSettings.CustomFolder):
                case nameof(AppSettings.ExtraFolders):
                case nameof(AppSettings.SkippedVersion):
                case nameof(AppSettings.Language):
                case nameof(AppSettings.UpdateManifestUrls):
                    Dispatcher.BeginInvoke(new Action(Refresh));
                    break;
            }
        }

        private void Refresh()
        {
            // Appearance
            TintCustomPanel.Visibility = _s.TintMode == TintMode.Custom ? Visibility.Visible : Visibility.Collapsed;
            TintPreview.Background = new SolidColorBrush(ThemeService.ParseColor(_s.TintColor, Colors.Gray));
            CornersCard.IsEnabled = OsInfo.SupportsCornerPreference;
            CornersCard.ToolTip = OsInfo.SupportsCornerPreference ? null : App.Str("Win11Only");
            RoundOldCard.Visibility = OsInfo.IsWin11 ? Visibility.Collapsed : Visibility.Visible;
            GridColumnsCard.Visibility = _s.ViewMode == ViewMode.Grid ? Visibility.Visible : Visibility.Collapsed;
            RefreshBackdropStatus();

            // Behavior
            FolderBox.Text = string.IsNullOrWhiteSpace(_s.CustomFolder) ? Shell.GetDownloadsFolder() + "  (" + App.Str("Default") + ")" : _s.CustomFolder;
            ExtraFoldersList.ItemsSource = _s.ExtraFolders.ToList();
            AutostartBox.IsChecked = Autostart.IsEnabled;

            // Updates & about
            RefreshUpdates();
            AboutVersion.Text = $"{App.Str("Version")} {UpdateService.CurrentVersionText}";
            AboutSystem.Text = string.Join("\n", new[]
            {
                $"{OsInfo.Describe()} · {(Environment.Is64BitProcess ? "64-bit" : "32-bit")} · .NET {Environment.Version}",
                $"{App.Str("Backdrop")}: {BackdropName(BackdropController.Get(App.Stack)?.Effective ?? BackdropKind.Solid)}",
                $"{App.Str("SettingsFile")}: {App.Store.FilePath}",
            });
            PinImage.Source = new BitmapImage(new Uri(OsInfo.IsWin11 ? "pack://application:,,,/Downloads;component/Assets/Images/pin.png"
                : OsInfo.IsVistaOr7 ? "pack://application:,,,/Downloads;component/Assets/Images/pinSeven.png"
                : "pack://application:,,,/Downloads;component/Assets/Images/pinTen.png"));
        }

        private string _overrideTool;

        private void RefreshBackdropStatus()
        {
            var requested = _s.Backdrop;
            var effective = BackdropController.Get(App.Stack)?.Effective ?? BackdropController.Resolve(requested);
            bool fallback = requested != BackdropKind.Auto && requested != effective;
            BackdropStatus.Text = string.Format(App.Str(fallback ? "BackdropFallback" : "BackdropActive"), BackdropName(effective), OsInfo.Describe());

            OverrideWarning.Visibility = _overrideTool != null ? Visibility.Visible : Visibility.Collapsed;
            if (_overrideTool != null) OverrideWarning.Text = string.Format(App.Str("BackdropOverridden"), _overrideTool);

            // Transparency: meaningless for Solid; slider only when not automatic.
            bool solid = effective == BackdropKind.Solid;
            TransparencyCard.IsEnabled = !solid;
            TransparencyCard.ToolTip = solid ? App.Str("TransparencySolid") : null;
            TransparencyRow.IsEnabled = !_s.AutoOpacity;
            TransparencyValue.Text = _s.AutoOpacity
                ? string.Format(App.Str("TransparencyAutoValue"), 100 - BackdropController.AutoOpacityFor(effective, ThemeService.IsDark))
                : _s.Transparency + " %";
        }

        private static string BackdropName(BackdropKind k) => App.Str("Backdrop" + k);

        private void RefreshUpdates()
        {
            var u = App.Updater;
            if (u.Available != null)
            {
                UpdateHeadline.Text = string.Format(App.Str("UpdateAvailableHeadline"), u.Available.Version);
                CheckNowButton.Content = App.Str("InstallUpdate");
            }
            else
            {
                UpdateHeadline.Text = string.Format(App.Str("CurrentVersionHeadline"), UpdateService.CurrentVersionText);
                CheckNowButton.Content = App.Str("CheckNow");
            }
            CheckNowButton.IsEnabled = !u.IsBusy;
            UpdateStatusText.Text = u.IsBusy ? App.Str("CheckingUpdates")
                : _s.LastUpdateCheckUtc > DateTime.MinValue.AddDays(1)
                    ? string.Format(App.Str("LastChecked"), _s.LastUpdateCheckUtc.ToLocalTime().ToString("g"))
                    : App.Str("NeverChecked");
            SourcesText.Text = string.Join("\n", u.Sources.Select((src, i) => $"{i + 1}. {src}"));
            SkippedCard.Visibility = string.IsNullOrEmpty(_s.SkippedVersion) ? Visibility.Collapsed : Visibility.Visible;
            SkippedText.Text = string.Format(App.Str("SkippedVersion"), _s.SkippedVersion);
        }

        private void ShowUpdateResult(UpdateCheckResult r)
        {
            RefreshUpdates();
            if (r.Status == UpdateStatus.UpToDate) UpdateStatusText.Text = string.Format(App.Str("UpToDate"), UpdateService.CurrentVersionText);
            else if (r.Status == UpdateStatus.Failed) UpdateStatusText.Text = App.Str("UpdateCheckFailed") + " " + r.Error;
        }

        #region Navigation

        private void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!(Nav.SelectedItem is ListBoxItem item)) return;
            string tag = item.Tag as string;
            foreach (var page in new FrameworkElement[] { PageAppearance, PageBehavior, PageNotifications, PagePreview, PageUpdates, PageAbout })
                page.Visibility = page.Name == "Page" + tag ? Visibility.Visible : Visibility.Collapsed;
            PageScroller?.ScrollToTop();
        }

        public void SelectPage(string tag)
        {
            foreach (ListBoxItem item in Nav.Items)
                if ((string)item.Tag == tag) Nav.SelectedItem = item;
        }

        #endregion

        #region Appearance

        private void BuildSwatches()
        {
            var colors = new[] { "#202020", "#2B2B2B", "#F3F3F3", "#FFFFFF", "#1B2A41", "#2D1B3D", "#1E3326", "#3D1F1F", "#3A3320" };
            foreach (var c in colors)
            {
                var b = new Button { Background = new SolidColorBrush(ThemeService.ParseColor(c, Colors.Gray)), ToolTip = c, Tag = c };
                b.Style = (Style)FindResource("Swatch");
                b.Click += (s, e) => { _s.TintColor = (string)((Button)s).Tag; _s.TintMode = TintMode.Custom; };
                Swatches.Children.Add(b);
            }
            var accent = new Button { ToolTip = App.Str("AccentColor") };
            accent.SetResourceReference(BackgroundProperty, "Brush.Accent");
            accent.Style = (Style)FindResource("Swatch");
            accent.Click += (s, e) =>
            {
                var a = ThemeService.Accent;
                _s.TintColor = $"#{a.R:X2}{a.G:X2}{a.B:X2}";
                _s.TintMode = TintMode.Custom;
            };
            Swatches.Children.Add(accent);
        }

        private void TintColorBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) TintColorBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        }

        #endregion

        #region Behavior

        private IntPtr Hwnd => new WindowInteropHelper(this).Handle;

        private void BrowseFolder_Click(object sender, RoutedEventArgs e)
        {
            string picked = FolderPicker.Pick(Hwnd, App.Str("DownloadsFolder"), App.Feed.PrimaryFolder);
            if (picked != null) _s.CustomFolder = picked;
        }

        private void ResetFolder_Click(object sender, RoutedEventArgs e) => _s.CustomFolder = "";

        private void AddExtraFolder_Click(object sender, RoutedEventArgs e)
        {
            string picked = FolderPicker.Pick(Hwnd, App.Str("ExtraFolders"), null);
            if (picked == null || _s.ExtraFolders.Contains(picked, StringComparer.OrdinalIgnoreCase)) return;
            _s.ExtraFolders = new List<string>(_s.ExtraFolders) { picked };
        }

        private void RemoveExtraFolder_Click(object sender, RoutedEventArgs e)
        {
            string path = (string)((Button)sender).Tag;
            _s.ExtraFolders = _s.ExtraFolders.Where(f => !string.Equals(f, path, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        private void AutostartBox_Click(object sender, RoutedEventArgs e)
        {
            try { Autostart.Set(AutostartBox.IsChecked == true, _s.StartMinimized); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, App.Str("AppTitle"), MessageBoxButton.OK, MessageBoxImage.Warning); }
            AutostartBox.IsChecked = Autostart.IsEnabled;
        }

        private void HotkeyBox_GotFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            _recordingHotkey = true;
            HotkeyStatus.Text = App.Str("HotkeyRecording");
        }

        private void HotkeyBox_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            _recordingHotkey = false;
            HotkeyStatus.Text = "";
        }

        private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!_recordingHotkey) return;
            e.Handled = true;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Tab) { e.Handled = false; return; }
            if (key == Key.Escape) { Keyboard.ClearFocus(); return; }
            if (key == Key.LeftCtrl || key == Key.RightCtrl || key == Key.LeftAlt || key == Key.RightAlt ||
                key == Key.LeftShift || key == Key.RightShift || key == Key.LWin || key == Key.RWin) return;

            var mods = Keyboard.Modifiers;
            if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin)) mods |= ModifierKeys.Windows;
            if (mods == ModifierKeys.None)
            {
                HotkeyStatus.Text = App.Str("HotkeyNeedsModifier");
                return;
            }
            _s.Hotkey = HotkeyService.Format(mods, key);
            HotkeyStatus.Text = App.Str("HotkeySaved");
        }

        #endregion

        #region Updates & about

        private void CheckNow_Click(object sender, RoutedEventArgs e)
        {
            if (App.Updater.Available != null) { UpdateWindow.ShowFor(App.Updater.Available); return; }
            App.Stack.CheckForUpdates(true);
            RefreshUpdates();
        }

        private void ResetSkipped_Click(object sender, RoutedEventArgs e) => _s.SkippedVersion = "";

        private void GitHub_Click(object sender, RoutedEventArgs e) => Start(Links.GitHub);
        private void DeveloperSite_Click(object sender, RoutedEventArgs e) => Start(Links.Developer);
        private void OpenSettingsFolder_Click(object sender, RoutedEventArgs e) => Start(App.Store.Folder);
        private void OpenLog_Click(object sender, RoutedEventArgs e) { if (Log.FilePath != null) Start(Log.FilePath); }

        private static void Start(string target)
        {
            try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch { }
        }

        private void BuildShortcuts()
        {
            var rows = new[]
            {
                ("Enter", "ShortcutOpen"), ("Space", "ShortcutPreview"), ("Del / Shift+Del", "ShortcutDelete"),
                ("F2", "ShortcutRename"), ("Ctrl+C / Ctrl+X", "ShortcutCopy"), ("Ctrl+Shift+C", "CopyPath"),
                ("Ctrl+E", "ShowInFolder"), ("Ctrl+O", "OpenFolder"), ("Alt+Enter", "ShortcutProperties"),
                (App.Str("ShortcutTypeKey"), "ShortcutFilter"), ("Ctrl+G", "ShortcutToggleView"), ("F5", "Refresh"), ("Esc", "ShortcutHide"),
                ("← / →", "ShortcutPreviewNav"), ("Ctrl+0 / Ctrl+1", "ShortcutZoom"), ("K / J / L", "ShortcutMedia"),
            };
            int r = 0;
            foreach (var (keys, label) in rows)
            {
                ShortcutsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var k = new TextBlock { Text = keys, Margin = new Thickness(0, 3, 12, 3), FontWeight = FontWeights.SemiBold };
                var v = new TextBlock { Margin = new Thickness(0, 3, 0, 3), TextWrapping = TextWrapping.Wrap };
                v.SetResourceReference(TextBlock.TextProperty, label);
                v.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
                Grid.SetRow(k, r); Grid.SetRow(v, r); Grid.SetColumn(v, 1);
                ShortcutsGrid.Children.Add(k);
                ShortcutsGrid.Children.Add(v);
                r++;
            }
        }

        #endregion

        private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
