using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DownloadsOSX.Interop;
using DownloadsOSX.Models;
using DownloadsOSX.Services;
using DownloadsOSX.Views.Preview;

namespace DownloadsOSX.Views
{
    /// <summary>
    /// Quick Look window (Space). One instance, owned by the stack, hidden instead of closed.
    /// Picks a view by file type with fallbacks: image → media → text → shell preview handler → info card.
    /// </summary>
    public partial class PreviewWindow : Window
    {
        private readonly MainWindow _stack;
        private readonly BackdropController _backdrop;
        private IntPtr _hwnd;

        private ImagePreview _imageView;
        private MediaPreview _mediaView;
        private TextPreview _textView;
        private HandlerPreview _handlerView;
        private InfoPreview _infoView;
        private IPreviewView _current;

        private List<FileEntry> _items = new List<FileEntry>();
        private FileEntry _entry;
        private CancellationTokenSource _cts;
        private bool _sizedOnce;

        /// <summary>Raised when the user moves to another item with ←/→ (the stack follows the selection).</summary>
        public event Action<FileEntry> Navigated;

        internal PreviewWindow(MainWindow stack)
        {
            InitializeComponent();
            _stack = stack;
            Owner = stack;
            _backdrop = new BackdropController(this, BackdropKind.Acrylic, resizeBorder: 6);
            SourceInitialized += (s, e) =>
            {
                _hwnd = new WindowInteropHelper(this).Handle;
                _backdrop.SetCaptionHeight(48);
                long style = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_STYLE);
                NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_STYLE, style & ~NativeMethods.WS_MAXIMIZEBOX);
            };
            Deactivated += (s, e) => _stack.OnPreviewDeactivated();
            PreviewKeyDown += OnKey;
            Closing += (s, e) => { e.Cancel = true; HideNow(); };
        }

        internal void ShowFor(FileEntry entry, IList<FileEntry> siblings)
        {
            _items = siblings.ToList();
            bool opening = !IsVisible;
            if (opening)
            {
                if (_hwnd == IntPtr.Zero) new WindowInteropHelper(this).EnsureHandle();
                FitWindowTo(new Size(640, 440), true); // placeholder size until the content reports its own
            }
            if (entry != _entry || opening) Load(entry, opening);

            if (opening)
            {
                Show();
                Activate();
                AnimateOpen();
            }
            EnsureKeyboardFocus();
            UpdateNav();
        }

        /// <summary>
        /// Keys must keep arriving while views are swapped: if focus was inside the old view (now detached),
        /// WPF would have no focused element and drop key presses. Park focus on the window root instead.
        /// </summary>
        private void EnsureKeyboardFocus()
        {
            if (!IsActive) return;
            var focused = Keyboard.FocusedElement as DependencyObject;
            bool inside = focused != null && (focused == Root || Root.IsAncestorOf(focused)) && !(focused is UIElement u && !u.IsVisible);
            if (!inside) Root.Focus();
        }

        internal void HideNow()
        {
            _cts?.Cancel();
            _current?.Unload();
            _entry = null;
            if (IsVisible) Hide();
            if (_stack.IsStackVisible) _stack.Activate();
        }

        /// <summary>The stack's items changed (file added/removed/renamed).</summary>
        internal void OnItemsChanged(IEnumerable<FileEntry> items)
        {
            if (!IsVisible) return;
            int oldIndex = _items.IndexOf(_entry);
            _items = items.ToList();
            if (_entry != null && !_items.Contains(_entry))
            {
                if (_items.Count == 0) { HideNow(); return; }
                var next = _items[Math.Max(0, Math.Min(oldIndex, _items.Count - 1))];
                Load(next, false);
                Navigated?.Invoke(next);
            }
            UpdateNav();
        }

        private async void Load(FileEntry entry, bool opening)
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            _entry = entry;

            NameText.Text = entry.Name;
            HeaderIcon.Source = entry.Thumbnail;
            InfoText.Text = BaseInfo(entry, null);
            Title = entry.Name;

            var s = App.Settings;
            var chain = new List<Func<IPreviewView>>();
            switch (entry.Kind)
            {
                case FileKind.Image: chain.Add(() => _imageView ?? (_imageView = new ImagePreview())); break;
                case FileKind.Video:
                case FileKind.Audio:
                    if (s.PreviewMedia) chain.Add(() => _mediaView ?? (_mediaView = new MediaPreview()));
                    break;
                case FileKind.Text:
                    if (s.PreviewText) chain.Add(() => _textView ?? (_textView = new TextPreview()));
                    break;
            }
            if (s.PreviewHandlers && !entry.IsFolder && !entry.IsPartial && HandlerPreview.CanPreview(entry))
                chain.Add(() => _handlerView ?? (_handlerView = new HandlerPreview()));
            if (s.PreviewText && entry.Kind == FileKind.Other && !entry.IsFolder && entry.Size < 20 * 1024 * 1024)
                chain.Add(() => TextPreview.LooksLikeText(entry.Path) ? (_textView ?? (_textView = new TextPreview())) : null);
            chain.Add(() => _infoView ?? (_infoView = new InfoPreview()));

            LoadingText.Visibility = Visibility.Visible;
            foreach (var make in chain)
            {
                IPreviewView view;
                try { view = make(); }
                catch (Exception ex) { Log.Warn("Preview view creation failed", ex); continue; }
                if (view == null) continue;

                if (_current != null && _current != view) _current.Unload();
                _current = view;
                Presenter.Content = view.Element; // attached before loading (preview handlers need an HWND)
                EnsureKeyboardFocus();
                ZoomButtons.Visibility = view is ImagePreview ? Visibility.Visible : Visibility.Collapsed;

                bool ok;
                try { ok = await view.LoadAsync(entry, ct); }
                catch (Exception ex)
                {
                    Log.Warn("Preview failed for " + entry.Path, ex);
                    ok = false;
                }
                if (ct.IsCancellationRequested) return;
                if (!ok) continue;

                LoadingText.Visibility = Visibility.Collapsed;
                InfoText.Text = BaseInfo(entry, view.Info);
                FadeIn(view.Element);
                FitWindowTo(view.NaturalSize, opening || !_sizedOnce);
                _sizedOnce = true;
                return;
            }
        }

        private static string BaseInfo(FileEntry e, string extra)
        {
            var parts = new List<string> { Shell.GetTypeName(e.Path, e.IsFolder) };
            if (!string.IsNullOrEmpty(extra)) parts.Add(extra);
            if (!e.IsFolder) parts.Add(Shell.FormatSize(e.Size));
            return string.Join("  ·  ", parts);
        }

        /// <summary>Sizes the window to the content (≤ 85% of the work area), keeping its center when already open.</summary>
        private void FitWindowTo(Size content, bool center)
        {
            if (_hwnd == IntPtr.Zero) return;
            double chromeW = 16, chromeH = 48 + 8;
            double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;

            NativeMethods.RECT anchorRect;
            if (center || !NativeMethods.GetWindowRect(_hwnd, out anchorRect))
            {
                IntPtr stackHwnd = new WindowInteropHelper(_stack).Handle;
                NativeMethods.GetWindowRect(stackHwnd, out anchorRect);
            }
            var ctx = Placement.ForPoint(new NativeMethods.POINT(anchorRect.Left + anchorRect.Width / 2, anchorRect.Top + anchorRect.Height / 2), scale);
            scale = ctx.Scale;

            double maxW = ctx.Work.Width / scale * 0.85, maxH = ctx.Work.Height / scale * 0.85;
            double cw = Math.Max(content.Width, 1), ch = Math.Max(content.Height, 1);
            double k = Math.Min(1.0, Math.Min((maxW - chromeW) / cw, (maxH - chromeH) / ch));
            double w = Math.Max(MinWidth, cw * k + chromeW);
            double h = Math.Max(MinHeight, ch * k + chromeH);

            int pw = (int)Math.Round(w * scale), ph = (int)Math.Round(h * scale);
            int x, y;
            if (center)
            {
                var r = Placement.Center(pw, ph, ctx);
                x = r.Left; y = r.Top;
            }
            else
            {
                x = anchorRect.Left + (anchorRect.Width - pw) / 2;
                y = anchorRect.Top + (anchorRect.Height - ph) / 2;
                var r = Placement.Clamp(new NativeMethods.RECT(x, y, x + pw, y + ph), ctx);
                x = r.Left; y = r.Top;
            }
            NativeMethods.SetWindowPos(_hwnd, IntPtr.Zero, x, y, pw, ph, NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
        }

        private void AnimateOpen()
        {
            if (!App.Settings.Animations || !SystemParameters.ClientAreaAnimation) return;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var d = TimeSpan.FromMilliseconds(200);
            OpenScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.94, 1, d) { EasingFunction = ease });
            OpenScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.94, 1, d) { EasingFunction = ease });
            Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease });
        }

        private static void FadeIn(UIElement e)
        {
            if (!App.Settings.Animations) return;
            e.BeginAnimation(OpacityProperty, new DoubleAnimation(0.35, 1, TimeSpan.FromMilliseconds(140)));
        }

        private void UpdateNav()
        {
            int i = _items.IndexOf(_entry);
            PrevButton.IsEnabled = i > 0;
            NextButton.IsEnabled = i >= 0 && i < _items.Count - 1;
            PositionText.Text = i >= 0 && _items.Count > 1 ? $"{i + 1}/{_items.Count}" : "";
        }

        private void Navigate(int delta)
        {
            int i = _items.IndexOf(_entry);
            int n = i + delta;
            if (i < 0 || n < 0 || n >= _items.Count) return;
            var next = _items[n];
            Load(next, false);
            UpdateNav();
            Navigated?.Invoke(next);
        }

        private void OnKey(object sender, KeyEventArgs e)
        {
            if (_current != null && _current.HandleKey(e)) { e.Handled = true; return; }
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            switch (e.Key)
            {
                case Key.Space:
                case Key.Escape:
                    HideNow();
                    break;
                case Key.Left:
                case Key.Up when !(_current is TextPreview):
                    Navigate(-1);
                    break;
                case Key.Right:
                case Key.Down when !(_current is TextPreview):
                    Navigate(1);
                    break;
                case Key.Enter:
                    Open_Click(null, null);
                    break;
                case Key.C when ctrl:
                    if (_entry != null) Shell.CopyFilesToClipboard(new[] { _entry.Path });
                    break;
                default:
                    return;
            }
            e.Handled = true;
        }

        private void Prev_Click(object sender, RoutedEventArgs e) => Navigate(-1);
        private void Next_Click(object sender, RoutedEventArgs e) => Navigate(1);
        private void ZoomIn_Click(object sender, RoutedEventArgs e) => _imageView?.ZoomIn();
        private void ZoomOut_Click(object sender, RoutedEventArgs e) => _imageView?.ZoomOut();
        private void ZoomFit_Click(object sender, RoutedEventArgs e) => _imageView?.ToggleFit();
        private void Close_Click(object sender, RoutedEventArgs e) => HideNow();

        private void Open_Click(object sender, RoutedEventArgs e)
        {
            var entry = _entry;
            if (entry == null) return;
            HideNow();
            try
            {
                if (entry.IsFolder) Shell.OpenFolder(entry.Path); else Shell.Open(entry.Path);
                if (App.Settings.HideOnDeactivate) _stack.HideStack();
            }
            catch (Exception ex) { _stack.ShowToast(ex.Message, 4); }
        }

        private void OpenWith_Click(object sender, RoutedEventArgs e)
        {
            var entry = _entry;
            if (entry == null || entry.IsFolder) return;
            Shell.OpenWith(_hwnd, entry.Path);
        }

        private void ShowInFolder_Click(object sender, RoutedEventArgs e)
        {
            var entry = _entry;
            if (entry == null) return;
            HideNow();
            Shell.ShowInFolder(new[] { entry.Path });
            if (App.Settings.HideOnDeactivate) _stack.HideStack();
        }
    }
}
