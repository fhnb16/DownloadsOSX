using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using DownloadsOSX.Interop;
using DownloadsOSX.Models;
using DownloadsOSX.Services;
using Path = System.IO.Path;

namespace DownloadsOSX.Views
{
    /// <summary>
    /// The stack: a borderless window that pops up next to its taskbar button (or the cursor / tray icon)
    /// with the newest downloads. It hides again when it loses focus.
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly AppSettings _settings = App.Settings;
        private readonly DownloadsFeed _feed = App.Feed;
        private readonly BackdropController _backdrop;
        private readonly ListCollectionView _view;

        private IntPtr _hwnd;
        private HotkeyService _hotkey;
        private TrayService _tray;
        private PreviewWindow _preview;

        private ScreenEdge _anchor = ScreenEdge.Bottom;
        private NativeMethods.RECT _anchorRect;
        private bool _hasAnchor;
        private bool _positionedForShow;
        private DateTime _hiddenByShellClickAt = DateTime.MinValue;
        private DateTime _hiddenAt = DateTime.Now;
        private int _suppressHide;
        private bool _dragging;
        private Point _dragStart;
        private FileEntry _dragItem;
        private bool _dragStarted;
        private string _filter = "";
        private string _selectAfterRefresh;
        private int _unseen;
        private string _titleFolder, _titleName;
        private DispatcherTimer _toastTimer;
        private DispatcherTimer _autoUpdateTimer;

        public MainWindow()
        {
            InitializeComponent();
            _backdrop = new BackdropController(this, BackdropKind.Acrylic);

            _view = (ListCollectionView)CollectionViewSource.GetDefaultView(_feed.Items);
            _view.Filter = o => _filter.Length == 0 || ((FileEntry)o).Name.IndexOf(_filter, StringComparison.CurrentCultureIgnoreCase) >= 0;
            List.ItemsSource = _view;

            _feed.Refreshed += OnFeedRefreshed;
            _feed.NewItems += OnNewItems;
            _feed.PropertyChanged += (s, e) => UpdateHeader();
            _settings.PropertyChanged += OnSettingChanged;
            App.Updater.PropertyChanged += (s, e) => UpdatePill_Refresh();
            App.Updater.UpdateFound += m => UpdatePill_Refresh();
            ThemeService.Changed += () => { if (_unseen > 0) UpdateBadge(); };

            SourceInitialized += OnSourceInitialized;
            Deactivated += OnDeactivated;
            StateChanged += OnStateChanged;
            SizeChanged += (s, e) => { if (IsVisible && WindowState == WindowState.Normal) Reanchor(); };
            PreviewKeyDown += OnPreviewKeyDown;
            PreviewTextInput += OnPreviewTextInput;
            Closing += (s, e) => App.Store.Flush();

            ApplyLayout();
            ApplyPresence();
            UpdateHeader();
        }

        #region Startup / lifetime

        /// <summary>Shows the window for the first time — at the cursor (the pinned icon was just clicked) or minimized.</summary>
        public void Start(bool minimized)
        {
            if (minimized)
            {
                if (_settings.Presence == PresenceMode.Tray)
                {
                    // Create the HWND (hotkey, messages) without showing anything.
                    new WindowInteropHelper(this).EnsureHandle();
                    return;
                }
                WindowState = WindowState.Minimized; // a taskbar button, nothing on screen
                ShowActivated = false;               // don't steal focus at sign-in
                Show();
                ShowActivated = true;
                return;
            }
            Show();
            ShowStack();
        }

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
            _backdrop.SetCaptionHeight(46);

            // No maximize, ever (also disables caption double-click maximize).
            long style = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_STYLE);
            NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_STYLE, style & ~NativeMethods.WS_MAXIMIZEBOX);

            _hotkey = new HotkeyService(_hwnd);
            _hotkey.Pressed += () => ToggleStack();
            ApplyHotkey(false);
        }

        public void DisposeServices()
        {
            _hotkey?.Dispose();
            _tray?.Dispose();
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == SingleInstance.Message && SingleInstance.Message != 0)
            {
                handled = true;
                HandleCommand((InstanceCommand)wParam.ToInt32());
                return IntPtr.Zero;
            }

            switch (msg)
            {
                case NativeMethods.WM_SYSCOMMAND:
                    int cmd = wParam.ToInt32() & 0xFFF0;
                    if (cmd == NativeMethods.SC_RESTORE && WindowState == WindowState.Minimized)
                    {
                        // The taskbar click that deactivated (and so hid) the stack also arrives as a restore: treat as toggle-off.
                        if ((DateTime.Now - _hiddenByShellClickAt).TotalMilliseconds < 600)
                        {
                            _hiddenByShellClickAt = DateTime.MinValue;
                            handled = true;
                            return IntPtr.Zero;
                        }
                        PositionForShow(fromMinimized: true);
                    }
                    break;
                case NativeMethods.WM_EXITSIZEMOVE:
                    if (_settings.Position == PositionMode.Remember && NativeMethods.GetWindowRect(_hwnd, out var r))
                    {
                        _settings.RememberedLeft = r.Left;
                        _settings.RememberedTop = r.Top;
                    }
                    CaptureAnchor();
                    break;
            }
            return IntPtr.Zero;
        }

        internal void HandleCommand(InstanceCommand command)
        {
            switch (command)
            {
                case InstanceCommand.Show: ShowStack(); break;
                case InstanceCommand.Settings: OpenSettings(); break;
                case InstanceCommand.CheckUpdates: CheckForUpdates(true); break;
                case InstanceCommand.OpenFolder: OpenFolder(); break;
                case InstanceCommand.Exit: Exit(); break;
            }
        }

        public void Exit()
        {
            App.Store.Flush();
            Application.Current.Shutdown();
        }

        #endregion

        #region Show / hide / position

        public bool IsStackVisible => IsVisible && WindowState != WindowState.Minimized;

        public void ToggleStack()
        {
            if (IsStackVisible && IsActive) HideStack();
            else ShowStack();
        }

        public void ShowStack()
        {
            if (_settings.Presence == PresenceMode.Tray && !IsVisible)
            {
                PositionForShow(fromMinimized: false);
                Show();
                PositionForShow(fromMinimized: false);
                OnShown();
            }
            else if (WindowState == WindowState.Minimized)
            {
                PositionForShow(fromMinimized: true);
                WindowState = WindowState.Normal; // OnStateChanged → OnShown
            }
            else
            {
                if (!IsVisible) Show();
                PositionForShow(fromMinimized: false);
                OnShown();
            }
            Activate();
            NativeMethods.SetForegroundWindow(_hwnd);
        }

        public void HideStack()
        {
            if (_preview != null && _preview.IsVisible) _preview.HideNow();
            CommitRename();
            ClearFilter();
            foreach (var it in _feed.Items) it.IsNew = false;
            _hiddenAt = DateTime.Now;

            if (_settings.Presence == PresenceMode.Tray) Hide();
            else WindowState = WindowState.Minimized;
        }

        private void OnStateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Normal)
            {
                if (!_positionedForShow) PositionForShow(fromMinimized: false);
                OnShown();
            }
            else if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
            }
            else if (WindowState == WindowState.Minimized)
            {
                _positionedForShow = false;
                if (_preview != null && _preview.IsVisible) _preview.HideNow();
            }
        }

        private void OnShown()
        {
            _positionedForShow = false;
            _unseen = 0;
            UpdateBadge();
            AnimateIn();
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                Reanchor();
                FocusList();
            }));
        }

        private void FocusList()
        {
            if (List.Items.Count == 0) { List.Focus(); return; }
            if (List.SelectedItem == null)
            {
                // Select the newest item (bottom when newest is last).
                List.SelectedIndex = _feed.NewestLast && _settings.SortBy != SortBy.Name ? List.Items.Count - 1 : 0;
            }
            List.ScrollIntoView(List.SelectedItem);
            var container = List.ItemContainerGenerator.ContainerFromItem(List.SelectedItem) as ListViewItem;
            if (container != null) container.Focus(); else List.Focus();
        }

        /// <summary>Computes the target rect on the monitor under the cursor and moves the window there.</summary>
        private void PositionForShow(bool fromMinimized)
        {
            if (_hwnd == IntPtr.Zero) return;
            var cur = Placement.Cursor();
            double nowScale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            var ctx = Placement.ForPoint(cur, nowScale);

            double maxDip = Math.Max(240, ctx.Work.Height / ctx.Scale * 0.85);
            if (Math.Abs(MaxHeight - maxDip) > 1) MaxHeight = maxDip;
            UpdateLayout();

            double wDip = ActualWidth > 0 ? ActualWidth : Width;
            double hDip = ActualHeight > 0 ? ActualHeight : 400;
            int w = (int)Math.Round(wDip * ctx.Scale), h = (int)Math.Round(hDip * ctx.Scale);

            NativeMethods.RECT rect;
            switch (_settings.Position)
            {
                case PositionMode.Center:
                    rect = Placement.Center(w, h, ctx);
                    _anchor = ScreenEdge.Top;
                    break;
                case PositionMode.Remember when _settings.RememberedLeft.HasValue && _settings.RememberedTop.HasValue:
                    var remembered = new NativeMethods.POINT((int)_settings.RememberedLeft.Value, (int)_settings.RememberedTop.Value);
                    var rctx = Placement.ForPoint(remembered, nowScale);
                    rect = Placement.Clamp(new NativeMethods.RECT(remembered.X, remembered.Y, remembered.X + w, remembered.Y + h), rctx);
                    _anchor = ScreenEdge.Top;
                    ctx = rctx;
                    break;
                default:
                    rect = Placement.NearCursor(cur, w, h, ctx, out _anchor);
                    break;
            }

            _anchorRect = rect;
            _hasAnchor = true;
            _positionedForShow = true;
            _feed.NewestLast = !_settings.NewestNearTaskbar || _anchor != ScreenEdge.Top;

            if (fromMinimized)
            {
                // A minimized window restores to its "normal" rect; set it so the restore animation lands in place.
                var wp = new NativeMethods.WINDOWPLACEMENT { length = Marshal.SizeOf(typeof(NativeMethods.WINDOWPLACEMENT)) };
                NativeMethods.GetWindowPlacement(_hwnd, ref wp);
                wp.rcNormalPosition = Placement.ToWorkspace(rect, ctx);
                wp.showCmd = 2; // SW_SHOWMINIMIZED (keep state; the restore follows)
                wp.flags = 0;
                NativeMethods.SetWindowPlacement(_hwnd, ref wp);
            }
            else
            {
                NativeMethods.SetWindowPos(_hwnd, IntPtr.Zero, rect.Left, rect.Top, 0, 0,
                    NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
            }
        }

        /// <summary>Keeps the edge facing the taskbar fixed when the window's size changes (items added, DPI change).</summary>
        private void Reanchor()
        {
            if (!_hasAnchor || _hwnd == IntPtr.Zero || WindowState != WindowState.Normal) return;
            if (!NativeMethods.GetWindowRect(_hwnd, out var cur)) return;
            int w = cur.Width, h = cur.Height;
            int x = cur.Left, y = cur.Top;
            switch (_anchor)
            {
                case ScreenEdge.Bottom: y = _anchorRect.Bottom - h; x = _anchorRect.Left + (_anchorRect.Width - w) / 2; break;
                case ScreenEdge.Top: y = _anchorRect.Top; x = _anchorRect.Left + (_anchorRect.Width - w) / 2; break;
                case ScreenEdge.Left: x = _anchorRect.Left; y = _anchorRect.Top + (_anchorRect.Height - h) / 2; break;
                case ScreenEdge.Right: x = _anchorRect.Right - w; y = _anchorRect.Top + (_anchorRect.Height - h) / 2; break;
            }
            var ctx = Placement.ForPoint(new NativeMethods.POINT(x + w / 2, y + h / 2), VisualTreeHelper.GetDpi(this).DpiScaleX);
            var r = Placement.Clamp(new NativeMethods.RECT(x, y, x + w, y + h), ctx);
            if (r.Left != cur.Left || r.Top != cur.Top)
                NativeMethods.SetWindowPos(_hwnd, IntPtr.Zero, r.Left, r.Top, 0, 0, NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
        }

        /// <summary>After the user drags the window, keep its new position as the anchor.</summary>
        private void CaptureAnchor()
        {
            if (NativeMethods.GetWindowRect(_hwnd, out var r))
            {
                _anchorRect = r;
                _anchor = ScreenEdge.Top;
                _hasAnchor = true;
            }
        }

        private void AnimateIn()
        {
            if (!_settings.Animations || !SystemParameters.ClientAreaAnimation) return;
            double from = _anchor == ScreenEdge.Top ? -10 : _anchor == ScreenEdge.Bottom ? 10 : 0;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            ShowShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(from, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
            Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
        }

        private void OnDeactivated(object sender, EventArgs e)
        {
            if (!_settings.HideOnDeactivate || _suppressHide > 0 || ShellContextMenu.IsOpen || _dragging) return;
            string fgClass = NativeMethods.GetClassName(NativeMethods.GetForegroundWindow());
            bool shellClick = fgClass == "Shell_TrayWnd" || fgClass == "Shell_SecondaryTrayWnd" || fgClass == "NotifyIconOverflowWindow"
                              || fgClass == "TopLevelWindowForOverflowXamlIsland";

            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                if (IsActive || _suppressHide > 0 || (_preview != null && _preview.IsActive)) return;
                if (!IsStackVisible) return;
                if (shellClick) _hiddenByShellClickAt = DateTime.Now;
                HideStack();
            }));
        }

        /// <summary>Called by the preview when it loses focus to something outside the app.</summary>
        internal void OnPreviewDeactivated()
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                if (IsActive || (_preview != null && _preview.IsActive)) return;
                if (_preview != null && _preview.IsVisible) _preview.HideNow();
                if (_settings.HideOnDeactivate && IsStackVisible && _suppressHide == 0) HideStack();
            }));
        }

        private IDisposable SuppressHide() => new HideSuppressor(this);

        private sealed class HideSuppressor : IDisposable
        {
            private MainWindow _w;
            public HideSuppressor(MainWindow w) { _w = w; w._suppressHide++; }
            public void Dispose()
            {
                if (_w == null) return;
                var w = _w;
                _w = null;
                // Let the activation messages caused by the dialog settle before re-enabling auto-hide.
                w.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => w._suppressHide--));
            }
        }

        #endregion

        #region Layout & settings

        private void OnSettingChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(AppSettings.ViewMode):
                case nameof(AppSettings.ItemSize):
                case nameof(AppSettings.ShowDetails):
                case nameof(AppSettings.GroupByDate):
                case nameof(AppSettings.FontSize):
                    ApplyLayout();
                    break;
                case nameof(AppSettings.Presence):
                    ApplyPresence();
                    break;
                case nameof(AppSettings.Hotkey):
                case nameof(AppSettings.HotkeyEnabled):
                    ApplyHotkey(true);
                    break;
                case nameof(AppSettings.JumpList):
                case nameof(AppSettings.JumpListCount):
                case nameof(AppSettings.Language):
                    UpdateJumpList();
                    UpdateHeader();
                    break;
                case nameof(AppSettings.NewestNearTaskbar):
                    _feed.NewestLast = !_settings.NewestNearTaskbar || _anchor != ScreenEdge.Top;
                    break;
                case nameof(AppSettings.UpdateMode):
                    if (_settings.UpdateMode != UpdateMode.Off) App.Updater.StartSchedule();
                    break;
            }
        }

        private void ApplyLayout()
        {
            var s = _settings;
            double font = s.FontSize;
            double line = Math.Ceiling(font * 1.36);
            bool grid = s.ViewMode == ViewMode.Grid;

            double thumb = s.ItemSize == ItemSize.Compact ? 28 : s.ItemSize == ItemSize.Large ? 60 : 44;
            double tileThumb = s.ItemSize == ItemSize.Compact ? 48 : s.ItemSize == ItemSize.Large ? 96 : 64;
            double tile = tileThumb + 36;
            int columns = s.ItemSize == ItemSize.Large ? 3 : 4;

            Resources["Stack.ThumbSize"] = thumb;
            Resources["Stack.TileThumbSize"] = tileThumb;
            Resources["Stack.TileWidth"] = tile;
            Resources["Stack.NameMaxHeight"] = grid ? Math.Ceiling((font - 2) * 1.36) * 2 : (s.ItemSize == ItemSize.Compact ? line : line * 2);
            Resources["Stack.DetailsVisibility"] = s.ShowDetails && s.ItemSize != ItemSize.Compact ? Visibility.Visible : Visibility.Collapsed;

            List.ItemTemplate = (DataTemplate)FindResource(grid ? "GridItemTemplate" : "ListItemTemplate");
            List.ItemsPanel = (ItemsPanelTemplate)FindResource(grid ? "GridPanel" : "ListPanel");
            List.ItemContainerStyle = (Style)FindResource("StackItem");

            Width = grid
                ? columns * (tile + 2 + 2 * 6) + 12 + 12 + 10 // items + container padding + list margin + scrollbar
                : (s.ItemSize == ItemSize.Compact ? 300 : s.ItemSize == ItemSize.Large ? 390 : 345) + (font - 13) * 10;

            List.GroupStyle.Clear();
            _view.GroupDescriptions.Clear();
            if (s.GroupByDate)
            {
                List.GroupStyle.Add((GroupStyle)FindResource("DateGroupStyle"));
                _view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(FileEntry.GroupKey)));
            }

            double scale = _hwnd != IntPtr.Zero ? VisualTreeHelper.GetDpi(this).DpiScaleX : 1.0;
            _feed.ThumbnailPixels = (int)Math.Ceiling((grid ? tileThumb : thumb) * scale);
        }

        private void ApplyPresence()
        {
            var p = _settings.Presence;
            bool wasVisible = IsStackVisible;
            ShowInTaskbar = p != PresenceMode.Tray;

            if (p != PresenceMode.Taskbar)
            {
                if (_tray == null)
                {
                    _tray = new TrayService(() => BuildAppMenu(true));
                    _tray.Clicked += () =>
                    {
                        if ((DateTime.Now - _hiddenByShellClickAt).TotalMilliseconds < 600) { _hiddenByShellClickAt = DateTime.MinValue; return; }
                        ToggleStack();
                    };
                    _tray.BalloonClicked += ShowStack;
                }
                _tray.Visible = true;
            }
            else if (_tray != null)
            {
                _tray.Visible = false;
            }

            // Switching to tray-only while minimized would leave a minimized caption stub: hide properly instead.
            if (p == PresenceMode.Tray && IsVisible && WindowState == WindowState.Minimized)
            {
                Hide();
                WindowState = WindowState.Normal; // applied silently while hidden
            }
            else if (p != PresenceMode.Tray && !IsVisible && _hwnd != IntPtr.Zero && !wasVisible)
            {
                WindowState = WindowState.Minimized;
                Show();
            }
        }

        private void ApplyHotkey(bool announce)
        {
            if (_hotkey == null) return;
            if (!_settings.HotkeyEnabled) { _hotkey.Unregister(); return; }
            if (!_hotkey.Register(_settings.Hotkey) && announce)
                ShowToast(string.Format(App.Str("HotkeyFailed"), _settings.Hotkey), 5);
        }

        private void OnFeedRefreshed()
        {
            if (_settings.GroupByDate) _view.Refresh();
            UpdateHeader();

            if (_selectAfterRefresh != null)
            {
                var item = _feed.Items.FirstOrDefault(i => string.Equals(i.Path, _selectAfterRefresh, StringComparison.OrdinalIgnoreCase));
                if (item != null)
                {
                    List.SelectedItem = item;
                    List.ScrollIntoView(item);
                    _selectAfterRefresh = null;
                }
            }
            UpdateJumpList();
            _preview?.OnItemsChanged(_feed.Items);
        }

        private void UpdateJumpList()
        {
            var newestFirst = _feed.Items.OrderByDescending(i => i.SortTime(_settings.SortBy));
            JumpListService.Update(newestFirst);
        }

        private void UpdateHeader()
        {
            string folder = _feed.PrimaryFolder;
            if (folder != _titleFolder)
            {
                _titleFolder = folder;
                _titleName = Shell.GetDisplayName(folder);
            }
            TitleText.Text = string.IsNullOrEmpty(_titleName) ? App.Str("AppTitle") : _titleName;
            int shown = _view.Count;
            CountText.Text = _feed.TotalCount > shown ? string.Format(App.Str("CountOfTotal"), shown, _feed.TotalCount) : "";

            bool empty = _feed.Items.Count == 0 || shown == 0;
            EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            EmptyText.Text = _feed.FolderMissing ? string.Format(App.Str("FolderMissing"), folder)
                : _feed.Items.Count > 0 && shown == 0 ? App.Str("NoMatches")
                : App.Str("EmptyFolder");
            if (TaskbarItemInfo != null) TaskbarItemInfo.Description = folder;
        }

        #endregion

        #region New downloads: badge / flash / sound / balloon

        private void OnNewItems(IReadOnlyList<FileEntry> items)
        {
            if (IsStackVisible && IsActive) return;

            _unseen += items.Count;
            if (_settings.BadgeOnNew) UpdateBadge();

            if (_settings.FlashOnNew && ShowInTaskbar && _hwnd != IntPtr.Zero)
            {
                var fw = new NativeMethods.FLASHWINFO
                {
                    cbSize = (uint)Marshal.SizeOf(typeof(NativeMethods.FLASHWINFO)),
                    hwnd = _hwnd,
                    dwFlags = NativeMethods.FLASHW_TRAY,
                    uCount = 3,
                };
                NativeMethods.FlashWindowEx(ref fw);
            }

            if (_settings.SoundOnNew)
            {
                bool played = false;
                try { played = NativeMethods.PlaySound("Notification.Default", IntPtr.Zero, NativeMethods.SND_ALIAS | NativeMethods.SND_ASYNC | NativeMethods.SND_NODEFAULT); } catch { }
                if (!played) SystemSounds.Asterisk.Play();
            }

            if (_settings.BalloonOnNew && _tray != null && _tray.Visible)
            {
                string text = items.Count == 1 ? items[0].Name : string.Format(App.Str("NewDownloadsCount"), items.Count);
                _tray.ShowBalloon(App.Str("NewDownload"), text);
            }
        }

        private void UpdateBadge()
        {
            if (TaskbarItemInfo == null) return;
            if (_unseen <= 0 || !_settings.BadgeOnNew)
            {
                TaskbarItemInfo.Overlay = null;
                return;
            }

            double scale = _hwnd != IntPtr.Zero ? VisualTreeHelper.GetDpi(this).DpiScaleX : 1.0;
            int px = (int)Math.Round(16 * scale);
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                var fill = (Brush)FindResource("Brush.Badge");
                dc.DrawEllipse(fill, new Pen(Brushes.White, 1), new Point(8, 8), 7.5, 7.5);
                string text = _unseen > 9 ? "9+" : _unseen.ToString(CultureInfo.InvariantCulture);
                var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                    text.Length > 1 ? 8 : 10, Brushes.White, 1.0);
                dc.DrawText(ft, new Point(8 - ft.Width / 2, 8 - ft.Height / 2));
            }
            var bmp = new RenderTargetBitmap(px, px, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bmp.Render(visual);
            bmp.Freeze();
            TaskbarItemInfo.Overlay = bmp;
        }

        #endregion

        #region Item actions

        private IReadOnlyList<FileEntry> Selected =>
            List.SelectedItems.Cast<FileEntry>().OrderBy(i => List.Items.IndexOf(i)).ToList();

        private FileEntry Current => List.SelectedItem as FileEntry;

        private FileEntry ItemFromEvent(RoutedEventArgs e)
        {
            var container = ItemsControl.ContainerFromElement(List, e.OriginalSource as DependencyObject) as ListViewItem;
            return container?.DataContext as FileEntry;
        }

        private void OpenItems(IEnumerable<FileEntry> items)
        {
            var list = items.Take(15).ToList();
            if (list.Count == 0) return;
            foreach (var it in list)
            {
                try
                {
                    if (it.IsFolder) Shell.OpenFolder(it.Path); else Shell.Open(it.Path);
                }
                catch (Exception ex) { ShowToast(ex.Message, 5); }
            }
            if (_settings.HideOnDeactivate) HideStack();
        }

        private void ShowInFolder(IEnumerable<FileEntry> items)
        {
            var paths = items.Select(i => i.Path).ToList();
            if (paths.Count == 0) return;
            Shell.ShowInFolder(paths);
            if (_settings.HideOnDeactivate) HideStack();
        }

        private void DeleteItems(IReadOnlyList<FileEntry> items, bool permanently)
        {
            if (items.Count == 0) return;
            using (SuppressHide())
            {
                if (permanently || _settings.ConfirmDelete)
                {
                    string what = items.Count == 1 ? items[0].Name : string.Format(App.Str("ItemsCount"), items.Count);
                    string q = string.Format(App.Str(permanently ? "ConfirmDeletePermanent" : "ConfirmDelete"), what);
                    if (MessageBox.Show(this, q, App.Str("AppTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
                }
                int index = List.SelectedIndex;
                try
                {
                    if (Shell.Delete(items.Select(i => i.Path), permanently))
                        ShowToast(items.Count == 1
                            ? string.Format(App.Str(permanently ? "DeletedOne" : "RecycledOne"), items[0].Name)
                            : string.Format(App.Str(permanently ? "DeletedMany" : "RecycledMany"), items.Count), 3);
                }
                catch (Exception ex) { ShowToast(ex.Message, 5); }
                _feed.Rescan();
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    if (List.Items.Count > 0) List.SelectedIndex = Math.Max(0, Math.Min(index, List.Items.Count - 1));
                    FocusList();
                }));
            }
        }

        private void CopyItems(IEnumerable<FileEntry> items, bool cut)
        {
            var paths = items.Select(i => i.Path).ToList();
            if (paths.Count == 0) return;
            try
            {
                Shell.CopyFilesToClipboard(paths, cut);
                ShowToast(App.Str(cut ? "CutToClipboard" : "CopiedToClipboard"), 2);
            }
            catch (Exception ex) { ShowToast(ex.Message, 4); }
        }

        private void CopyPaths(IEnumerable<FileEntry> items)
        {
            var text = string.Join(Environment.NewLine, items.Select(i => i.Path));
            if (text.Length == 0) return;
            try
            {
                Clipboard.SetText(text);
                ShowToast(App.Str("PathCopied"), 2);
            }
            catch (Exception ex) { ShowToast(ex.Message, 4); }
        }

        private void CopyImage(FileEntry item)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(item.Path);
                bmp.EndInit();
                Clipboard.SetImage(bmp);
                ShowToast(App.Str("ImageCopied"), 2);
            }
            catch (Exception ex) { ShowToast(ex.Message, 4); }
        }

        private void Unblock(FileEntry item)
        {
            using (SuppressHide())
            {
                if (MessageBox.Show(this, string.Format(App.Str("UnblockConfirm"), item.Name), App.Str("AppTitle"),
                        MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            }
            ShowToast(Shell.RemoveZoneIdentifier(item.Path) ? App.Str("Unblocked") : App.Str("UnblockFailed"), 3);
        }

        private void OpenWith(FileEntry item)
        {
            using (SuppressHide()) Shell.OpenWith(_hwnd, item.Path);
        }

        public void TogglePreview()
        {
            if (_preview != null && _preview.IsVisible) { _preview.HideNow(); return; }
            var item = Current;
            if (item == null) return;
            if (_preview == null)
            {
                _preview = new PreviewWindow(this);
                _preview.Navigated += e =>
                {
                    List.SelectedItem = e;
                    List.ScrollIntoView(e);
                };
            }
            _preview.ShowFor(item, _view.Cast<FileEntry>().ToList());
        }

        // ---- Rename ----

        private FileEntry _renaming;

        private void BeginRename(FileEntry item)
        {
            if (item == null) return;
            CommitRename();
            _renaming = item;
            item.IsRenaming = true; // template swaps in the TextBox → RenameBox_IsVisibleChanged focuses it
        }

        private void RenameBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!(sender is TextBox box) || !(bool)e.NewValue || !(box.DataContext is FileEntry item)) return;
            box.Text = item.Name;
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                box.Focus();
                Keyboard.Focus(box);
                int dot = item.IsFolder ? -1 : item.Name.LastIndexOf('.');
                box.Select(0, dot > 0 ? dot : item.Name.Length);
            }));
        }

        private void RenameBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { CommitRename(((TextBox)sender).Text); e.Handled = true; }
            else if (e.Key == Key.Escape) { CancelRename(); e.Handled = true; }
        }

        private void RenameBox_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (_renaming != null && sender is TextBox box && box.DataContext == _renaming) CommitRename(box.Text);
        }

        private void CommitRename(string newName = null)
        {
            var item = _renaming;
            if (item == null) return;
            _renaming = null;
            item.IsRenaming = false;
            if (newName == null) return;

            newName = newName.Trim();
            if (newName.Length == 0 || newName == item.Name) { FocusList(); return; }
            if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                ShowToast(App.Str("InvalidName"), 4);
                return;
            }
            string target = Path.Combine(Path.GetDirectoryName(item.Path), newName);
            try
            {
                if (item.IsFolder) Directory.Move(item.Path, target);
                else File.Move(item.Path, target);
                _selectAfterRefresh = target;
            }
            catch (Exception ex) { ShowToast(ex.Message, 5); }
            FocusList();
        }

        private void CancelRename()
        {
            if (_renaming == null) return;
            _renaming.IsRenaming = false;
            _renaming = null;
            FocusList();
        }

        #endregion

        #region Input

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_renaming != null) return; // the rename box handles its own keys
            var mods = Keyboard.Modifiers;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            bool ctrl = (mods & ModifierKeys.Control) != 0, shift = (mods & ModifierKeys.Shift) != 0;

            switch (key)
            {
                case Key.Enter:
                    if (mods == ModifierKeys.Alt) ShowProperties();
                    else OpenItems(Selected);
                    break;
                case Key.Space:
                    TogglePreview();
                    break;
                case Key.Delete:
                    DeleteItems(Selected, shift);
                    break;
                case Key.F2:
                    BeginRename(Current);
                    break;
                case Key.F5:
                    _feed.Rescan();
                    break;
                case Key.Escape:
                    if (_filter.Length > 0) ClearFilter();
                    else HideStack();
                    break;
                case Key.Back:
                    if (_filter.Length > 0) SetFilter(_filter.Substring(0, _filter.Length - 1));
                    else return;
                    break;
                case Key.C when ctrl && shift:
                    CopyPaths(Selected);
                    break;
                case Key.C when ctrl:
                    CopyItems(Selected, false);
                    break;
                case Key.X when ctrl:
                    CopyItems(Selected, true);
                    break;
                case Key.E when ctrl:
                    ShowInFolder(Selected);
                    break;
                case Key.O when ctrl:
                    OpenFolder();
                    break;
                case Key.OemComma when ctrl:
                    OpenSettings();
                    break;
                case Key.F when ctrl:
                    FilterBar.Visibility = Visibility.Visible;
                    break;
                case Key.Apps:
                case Key.F10 when shift:
                    ShowShellMenuForSelection();
                    break;
                default:
                    return;
            }
            e.Handled = true;
        }

        private void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (_renaming != null || string.IsNullOrEmpty(e.Text)) return;
            char c = e.Text[0];
            if (char.IsControl(c) || c == ' ' && _filter.Length == 0) return;
            if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) != 0) return;
            SetFilter(_filter + e.Text);
            e.Handled = true;
        }

        private void SetFilter(string text)
        {
            _filter = text ?? "";
            FilterText.Text = _filter;
            FilterBar.Visibility = _filter.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            _view.Refresh();
            UpdateHeader();
            if (List.Items.Count > 0 && List.SelectedItem == null) List.SelectedIndex = _feed.NewestLast ? List.Items.Count - 1 : 0;
        }

        private void ClearFilter()
        {
            if (_filter.Length == 0 && FilterBar.Visibility != Visibility.Visible) return;
            SetFilter("");
        }

        private void ClearFilter_Click(object sender, RoutedEventArgs e) => ClearFilter();

        private void List_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStart = e.GetPosition(List);
            _dragItem = ItemFromEvent(e);
            _dragStarted = false;
        }

        private void List_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_dragItem == null || e.LeftButton != MouseButtonState.Pressed || _dragging) return;
            var pos = e.GetPosition(List);
            if (Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

            if (!List.SelectedItems.Contains(_dragItem)) List.SelectedItem = _dragItem;
            var paths = Selected.Where(i => !i.IsPartial).Select(i => i.Path).ToArray();
            _dragItem = null;
            if (paths.Length == 0) return;

            _dragStarted = true;
            _dragging = true;
            DragDropEffects result;
            try
            {
                var data = new DataObject(DataFormats.FileDrop, paths);
                result = DragDrop.DoDragDrop(List, data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
            }
            finally { _dragging = false; }

            if (result != DragDropEffects.None && _settings.HideOnDeactivate && !IsActive) HideStack();
        }

        private void List_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var item = _dragItem;
            _dragItem = null;
            if (_dragStarted || item == null) return;
            if (_settings.SingleClickOpen && Keyboard.Modifiers == ModifierKeys.None && e.ClickCount == 1)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => OpenItems(new[] { item })));
            }
        }

        private void List_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (_settings.SingleClickOpen || e.ChangedButton != MouseButton.Left) return;
            var item = ItemFromEvent(e);
            if (item != null) OpenItems(new[] { item });
        }

        private void List_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_preview != null && _preview.IsVisible && Current != null && List.SelectedItems.Count == 1)
                _preview.ShowFor(Current, _view.Cast<FileEntry>().ToList());
        }

        private void List_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            var item = ItemFromEvent(e);
            var screen = PointToScreen(e.GetPosition(this)); // physical pixels
            if (item == null)
            {
                ShowAppMenu(null);
                return;
            }
            if (!List.SelectedItems.Contains(item)) List.SelectedItem = item;
            ShowShellMenu(Selected, screen);
        }

        /// <summary>Keyboard context menu (Apps key / Shift+F10) under the selected item.</summary>
        private void ShowShellMenuForSelection()
        {
            var item = Current;
            if (item == null) { ShowAppMenu(MenuButton); return; }
            var container = List.ItemContainerGenerator.ContainerFromItem(item) as ListViewItem;
            Point pt = container != null
                ? container.PointToScreen(new Point(24, container.ActualHeight - 4))
                : PointToScreen(new Point(24, 60));
            ShowShellMenu(Selected, pt);
        }

        private void ShowShellMenu(IReadOnlyList<FileEntry> items, Point screenPx)
        {
            if (items.Count == 0) return;
            var first = items[0];
            var custom = new List<ShellContextMenu.Item>
            {
                new ShellContextMenu.Item { Text = App.Str("Preview") + "\tSpace", Action = TogglePreview },
                new ShellContextMenu.Item { Text = App.Str("ShowInFolder") + "\tCtrl+E", Action = () => ShowInFolder(items) },
                new ShellContextMenu.Item { Text = App.Str("CopyPath") + "\tCtrl+Shift+C", Action = () => CopyPaths(items) },
            };
            if (items.Count == 1 && first.Kind == FileKind.Image)
                custom.Add(new ShellContextMenu.Item { Text = App.Str("CopyImage"), Action = () => CopyImage(first) });
            if (items.Count == 1 && !first.IsFolder && Shell.HasZoneIdentifier(first.Path))
                custom.Add(new ShellContextMenu.Item { Text = App.Str("Unblock"), Action = () => Unblock(first) });

            using (SuppressHide())
            {
                ShellContextMenu.Show(this, items.Select(i => i.Path).ToList(), screenPx, custom,
                    onRename: items.Count == 1 ? (Action)(() => BeginRename(first)) : null);
            }
        }

        private void ShowProperties()
        {
            var item = Current;
            if (item == null) return;
            try
            {
                var info = new ProcessStartInfo(item.Path) { Verb = "properties", UseShellExecute = true };
                Process.Start(info);
            }
            catch { }
        }

        #endregion

        #region Menus & commands

        private void MenuButton_Click(object sender, RoutedEventArgs e) => ShowAppMenu(MenuButton);

        private void ShowAppMenu(UIElement target)
        {
            var menu = BuildAppMenu(false);
            if (target != null)
            {
                menu.PlacementTarget = target;
                menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            }
            menu.IsOpen = true;
        }

        private ContextMenu BuildAppMenu(bool forTray)
        {
            var menu = new ContextMenu();
            if (forTray) menu.Items.Add(CreateMenuItem(App.Str("ShowStack"), "Icon.Download", ShowStack));
            menu.Items.Add(CreateMenuItem(App.Str("OpenFolder"), "Icon.Folder", OpenFolder, "Ctrl+O"));
            if (!forTray)
            {
                menu.Items.Add(CreateMenuItem(App.Str("Refresh"), "Icon.Refresh", () => _feed.Rescan(), "F5"));
                bool grid = _settings.ViewMode == ViewMode.Grid;
                menu.Items.Add(CreateMenuItem(App.Str(grid ? "ViewAsList" : "ViewAsGrid"), grid ? "Icon.List" : "Icon.Grid",
                    () => _settings.ViewMode = grid ? ViewMode.List : ViewMode.Grid));
            }
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMenuItem(App.Str("Settings"), "Icon.Settings", OpenSettings, forTray ? null : "Ctrl+,"));
            menu.Items.Add(CreateMenuItem(App.Str("CheckForUpdates"), "Icon.Update", () => CheckForUpdates(true)));
            menu.Items.Add(CreateMenuItem(App.Str("Website"), "Icon.Globe", () => OpenUrl(UpdateService.ProjectPage)));
            menu.Items.Add(new Separator());
            if (!forTray) menu.Items.Add(CreateMenuItem(App.Str("HideStack"), "Icon.Minimize", HideStack, "Esc"));
            menu.Items.Add(CreateMenuItem(App.Str("Exit"), "Icon.Close", Exit));
            if (!forTray)
            {
                menu.Opened += (s, e) => _suppressHide++;
                menu.Closed += (s, e) => Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => _suppressHide--));
            }
            return menu;
        }

        internal static MenuItem CreateMenuItem(string header, string icon, Action action, string gesture = null)
        {
            var item = new MenuItem { Header = header, InputGestureText = gesture ?? "" };
            if (icon != null)
                item.Icon = new System.Windows.Shapes.Path { Data = (Geometry)Application.Current.FindResource(icon), Style = (Style)Application.Current.FindResource("IconPath") };
            item.Click += (s, e) => action();
            return item;
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e) => OpenFolder();

        public void OpenFolder()
        {
            try
            {
                Shell.OpenFolder(_feed.PrimaryFolder);
                if (_settings.HideOnDeactivate && IsStackVisible) HideStack();
            }
            catch (Exception ex) { ShowToast(ex.Message, 4); }
        }

        public void OpenSettings()
        {
            if (IsStackVisible) HideStack();
            SettingsWindow.ShowSingle();
        }

        private static void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        }

        public async void CheckForUpdates(bool manual)
        {
            if (manual) ShowToast(App.Str("CheckingUpdates"), 2);
            var result = await App.Updater.CheckAsync(manual);
            switch (result.Status)
            {
                case UpdateStatus.Available:
                    if (manual) UpdateWindow.ShowFor(result.Manifest);
                    break;
                case UpdateStatus.UpToDate:
                    if (manual)
                    {
                        if (IsStackVisible) ShowToast(string.Format(App.Str("UpToDate"), UpdateService.CurrentVersionText), 3);
                        else _tray?.ShowBalloon(App.Str("AppTitle"), string.Format(App.Str("UpToDate"), UpdateService.CurrentVersionText));
                        SettingsWindow.NotifyUpdateResult(result);
                    }
                    break;
                case UpdateStatus.Failed:
                    if (manual)
                    {
                        ShowToast(App.Str("UpdateCheckFailed"), 4);
                        SettingsWindow.NotifyUpdateResult(result);
                    }
                    break;
            }
        }

        private void UpdatePill_Click(object sender, RoutedEventArgs e)
        {
            var m = App.Updater.Available;
            if (m == null) return;
            HideStack();
            UpdateWindow.ShowFor(m);
        }

        private void UpdatePill_Refresh()
        {
            var m = App.Updater.Available;
            UpdatePill.Visibility = m != null ? Visibility.Visible : Visibility.Collapsed;
            if (m != null)
            {
                UpdatePillText.Text = string.Format(App.Str("UpdatePill"), m.Version);
                UpdatePill.ToolTip = string.Format(App.Str("UpdateAvailableTip"), m.Version, UpdateService.CurrentVersionText);
            }

            // Auto mode: install the verified download once the stack has been hidden and idle for a while.
            if (App.Updater.PendingPath != null && _autoUpdateTimer == null)
            {
                _autoUpdateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
                _autoUpdateTimer.Tick += (s, e) =>
                {
                    string pending = App.Updater.PendingPath;
                    if (pending == null || _settings.UpdateMode != UpdateMode.Auto) { _autoUpdateTimer.Stop(); _autoUpdateTimer = null; return; }
                    if (IsStackVisible || (DateTime.Now - _hiddenAt).TotalMinutes < 2 || Application.Current.Windows.OfType<Window>().Any(w => w != this && w.IsVisible)) return;
                    _autoUpdateTimer.Stop();
                    try
                    {
                        Log.Info("Auto-installing update " + App.Updater.Available?.Version);
                        UpdateService.ApplyAndRestart(pending, startMinimized: true);
                        Exit();
                    }
                    catch (Exception ex) { Log.Warn("Auto update failed", ex); }
                };
                _autoUpdateTimer.Start();
            }
        }

        #endregion

        #region Toast

        public void ShowToast(string text, double seconds)
        {
            ToastText.Text = text;
            Toast.Visibility = Visibility.Visible;
            Toast.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(150)));
            if (_toastTimer == null)
            {
                _toastTimer = new DispatcherTimer();
                _toastTimer.Tick += (s, e) =>
                {
                    _toastTimer.Stop();
                    var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(250));
                    fade.Completed += (s2, e2) => { if (Toast.Opacity < 0.01) Toast.Visibility = Visibility.Collapsed; };
                    Toast.BeginAnimation(OpacityProperty, fade);
                };
            }
            _toastTimer.Stop();
            _toastTimer.Interval = TimeSpan.FromSeconds(seconds);
            _toastTimer.Start();
        }

        #endregion
    }
}
