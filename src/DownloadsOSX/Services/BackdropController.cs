using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using DownloadsOSX.Interop;
using DownloadsOSX.Models;

namespace DownloadsOSX.Services
{
    /// <summary>
    /// Gives a WPF window a system backdrop (Mica / Mica Alt / Acrylic / Blur / Solid) with automatic fallback
    /// for the running OS, plus Win11 rounded corners, dark-mode frame and optional region rounding on older Windows.
    ///
    /// Every window uses WindowChrome with the glass frame extended over the whole client area and a transparent
    /// composition background, and is NOT a layered window (AllowsTransparency=false). Layered windows are why the
    /// legacy app had square corners on Win11: DWM never rounds them.
    /// </summary>
    internal sealed class BackdropController
    {
        private static readonly DependencyProperty ControllerProperty =
            DependencyProperty.RegisterAttached("BackdropController", typeof(BackdropController), typeof(BackdropController));

        public static BackdropController Get(Window w) => w?.GetValue(ControllerProperty) as BackdropController;

        private readonly Window _window;
        private readonly BackdropKind _autoKind;
        private readonly double _resizeBorder;
        private IntPtr _hwnd;
        private bool _inSizeMove;
        private bool _accentActive;
        private bool _systemBackdropActive;

        /// <summary>The backdrop actually in use after fallback.</summary>
        public BackdropKind Effective { get; private set; } = BackdropKind.Solid;

        public static event Action<BackdropController> EffectiveChanged;

        /// <param name="autoKind">What "Auto" means for this window (Acrylic for transient surfaces, Mica for long-lived ones).</param>
        /// <param name="resizeBorder">Resize border thickness in DIPs (0 = not resizable by dragging edges).</param>
        public BackdropController(Window window, BackdropKind autoKind, double resizeBorder = 0)
        {
            _window = window;
            _autoKind = autoKind;
            _resizeBorder = resizeBorder;
            window.SetValue(ControllerProperty, this);

            window.AllowsTransparency = false;
            window.Background = Brushes.Transparent;
            ApplyChrome(true);

            window.SourceInitialized += OnSourceInitialized;
            ThemeService.Changed += OnThemeChanged;
            App.Settings.PropertyChanged += OnSettingChanged;
            window.Closed += (s, e) =>
            {
                ThemeService.Changed -= OnThemeChanged;
                App.Settings.PropertyChanged -= OnSettingChanged;
            };
        }

        private void OnThemeChanged() => Apply();

        private void OnSettingChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(AppSettings.Backdrop):
                case nameof(AppSettings.TintMode):
                case nameof(AppSettings.TintColor):
                case nameof(AppSettings.TintOpacity):
                case nameof(AppSettings.AutoOpacity):
                case nameof(AppSettings.Corners):
                case nameof(AppSettings.RoundOldWindows):
                case nameof(AppSettings.Animations):
                    Apply();
                    break;
            }
        }

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            _hwnd = new WindowInteropHelper(_window).Handle;
            var source = HwndSource.FromHwnd(_hwnd);
            if (source?.CompositionTarget != null) source.CompositionTarget.BackgroundColor = Colors.Transparent;
            source?.AddHook(WndProc);

            // Hide the system caption/icon/sysmenu drawing DWM would otherwise paint into the extended frame.
            try
            {
                var opts = new NativeMethods.WTA_OPTIONS
                {
                    dwFlags = NativeMethods.WTNCA_NODRAWCAPTION | NativeMethods.WTNCA_NODRAWICON | NativeMethods.WTNCA_NOSYSMENU,
                    dwMask = NativeMethods.WTNCA_NODRAWCAPTION | NativeMethods.WTNCA_NODRAWICON | NativeMethods.WTNCA_NOSYSMENU,
                };
                NativeMethods.SetWindowThemeAttribute(_hwnd, NativeMethods.WTA_NONCLIENT, ref opts, (uint)Marshal.SizeOf(opts));
            }
            catch { }

            // DWM still paints min/max/close into the glass frame while WS_SYSMENU is set; our windows draw their own.
            // WS_CAPTION/WS_MINIMIZEBOX stay, so taskbar minimize/restore and its animations keep working.
            long style = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_STYLE);
            NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_STYLE, style & ~(long)NativeMethods.WS_SYSMENU);

            Apply();
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            switch (msg)
            {
                case NativeMethods.WM_ENTERSIZEMOVE:
                    // Win10 SWCA acrylic lags badly while dragging; switch to plain blur until the move ends.
                    _inSizeMove = true;
                    if (_accentActive && Effective == BackdropKind.Acrylic) SetAccent(NativeMethods.AccentState.EnableBlurBehind, TintColor(), AccentAlpha());
                    break;
                case NativeMethods.WM_EXITSIZEMOVE:
                    _inSizeMove = false;
                    if (_accentActive && Effective == BackdropKind.Acrylic) Apply();
                    break;
                case NativeMethods.WM_DWMCOMPOSITIONCHANGED:
                case NativeMethods.WM_DPICHANGED:
                    _window.Dispatcher.BeginInvoke(new Action(Apply));
                    break;
                case NativeMethods.WM_SIZE:
                    if (NeedsRegion || _hasRegion) ApplyRegion();
                    break;
            }
            return IntPtr.Zero;
        }

        public void Apply()
        {
            var s = App.Settings;
            var requested = s.Backdrop == BackdropKind.Auto ? _autoKind : s.Backdrop;
            var effective = Resolve(requested);
            bool dark = ThemeService.IsDark;

            if (_hwnd == IntPtr.Zero)
            {
                Effective = effective;
                return;
            }

            // Frame first: re-applying WindowChrome re-extends the DWM frame and must not happen after the backdrop is set.
            ApplyChrome(effective != BackdropKind.Solid);
            ExtendFrame(effective != BackdropKind.Solid);

            ResetNative();
            SetDwm(OsInfo.SupportsDarkTitle ? (OsInfo.Build >= 18985 ? NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE : NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE_OLD) : 0, dark ? 1 : 0);
            SetDwm(NativeMethods.DWMWA_TRANSITIONS_FORCEDISABLED, s.Animations ? 0 : 1);
            if (OsInfo.SupportsCornerPreference)
                SetDwm(NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, CornerValue(s.Corners));

            bool ok = true;
            switch (effective)
            {
                case BackdropKind.Mica:
                    ok = OsInfo.SupportsSystemBackdrop ? SetSystemBackdrop(2) : SetDwm(NativeMethods.DWMWA_MICA_EFFECT, 1);
                    break;
                case BackdropKind.MicaAlt:
                    ok = SetSystemBackdrop(4);
                    break;
                case BackdropKind.Acrylic:
                    ok = OsInfo.SupportsSystemBackdrop
                        ? SetSystemBackdrop(3)
                        : SetAccent(_inSizeMove ? NativeMethods.AccentState.EnableBlurBehind : NativeMethods.AccentState.EnableAcrylicBlurBehind, TintColor(), AccentAlpha());
                    break;
                case BackdropKind.Blur:
                    ok = OsInfo.SupportsAccentBlur ? SetAccent(NativeMethods.AccentState.EnableBlurBehind, Colors.Black, 0) : true; // untinted; tint is the overlay. Win7: Aero glass from the extended frame
                    break;
            }

            if (!ok && effective != BackdropKind.Solid)
            {
                Log.Warn($"Backdrop {effective} failed on {OsInfo.Describe()}, falling back to Solid");
                effective = BackdropKind.Solid;
                ResetNative();
                ApplyChrome(false);
                ExtendFrame(false);
            }

            _window.Background = BackgroundFor(effective);
            ApplyRegion(); // after the accent: whether a region is needed depends on it

            if (Effective != effective)
            {
                Effective = effective;
                EffectiveChanged?.Invoke(this);
            }
        }

        /// <summary>
        /// Name of a running tool that forces its own backdrop on every window (our settings then have no visible effect),
        /// or null. Mica For Everyone is the common one.
        /// </summary>
        public static string DetectOverrideTool()
        {
            try
            {
                foreach (var p in System.Diagnostics.Process.GetProcesses())
                {
                    using (p)
                    {
                        if (p.ProcessName.StartsWith("MicaForEveryone", StringComparison.OrdinalIgnoreCase)) return "Mica For Everyone";
                    }
                }
            }
            catch { }
            return null;
        }

        /// <summary>Maps a requested backdrop to what the OS can actually do.</summary>
        public static BackdropKind Resolve(BackdropKind requested)
        {
            if (requested == BackdropKind.Auto) requested = BackdropKind.Acrylic;
            if (requested == BackdropKind.Solid || !OsInfo.IsCompositionEnabled) return BackdropKind.Solid;

            if (OsInfo.SupportsSystemBackdrop) return requested;
            if (OsInfo.SupportsLegacyMica)
                return requested == BackdropKind.MicaAlt ? BackdropKind.Mica : requested;
            if (OsInfo.SupportsAcrylic)
                return requested == BackdropKind.Blur ? BackdropKind.Blur : BackdropKind.Acrylic;
            if (OsInfo.SupportsAccentBlur) return BackdropKind.Blur;
            if (OsInfo.IsWin8x) return BackdropKind.Solid; // DWM on Windows 8 has no blur
            if (OsInfo.IsVistaOr7) return BackdropKind.Blur; // Aero glass
            return BackdropKind.Solid;
        }

        // ---- Tint layer -------------------------------------------------------------------------------------
        // Every material gets one tint layer: a color (theme or custom) at an opacity (per-material default or the
        // user's value). Where it goes depends on the mechanism:
        //   * DWM system backdrops (Mica, Mica Alt, Win11 Acrylic): Window.Background overlay. Windows' own tint stays,
        //     so 0 % opacity = the pure system material, higher = more solid.
        //   * SetWindowCompositionAttribute acrylic (Win10, Win11 21H2): the accent policy's GradientColor (true acrylic
        //     tint — 0 % is nearly clear glass).
        //   * SWCA blur (Win10/11) and Aero glass (Win7): Window.Background overlay over an untinted blur.
        //   * Solid: the color, opaque.

        /// <summary>Default tint opacity (0..100) per material, used when "Auto" transparency is on.</summary>
        public static int AutoOpacityFor(BackdropKind kind, bool dark)
        {
            switch (kind)
            {
                case BackdropKind.Solid: return 100;
                case BackdropKind.Acrylic: return OsInfo.SupportsSystemBackdrop ? 0 : (dark ? 60 : 55);
                case BackdropKind.Blur: return OsInfo.SupportsAccentBlur ? (dark ? 55 : 50) : (dark ? 50 : 38);
                default: return 0; // Mica / Mica Alt: Windows already tints them
            }
        }

        /// <summary>Tint opacity in effect for a material (0..100).</summary>
        public static int OpacityFor(BackdropKind kind)
        {
            if (kind == BackdropKind.Solid) return 100;
            return App.Settings.AutoOpacity ? AutoOpacityFor(kind, ThemeService.IsDark) : App.Settings.TintOpacity;
        }

        private static Color TintColor()
        {
            var s = App.Settings;
            Color auto = ThemeService.IsDark ? Color.FromRgb(0x20, 0x20, 0x20) : Color.FromRgb(0xF3, 0xF3, 0xF3);
            return s.TintMode == TintMode.Custom ? ThemeService.ParseColor(s.TintColor, auto) : auto;
        }

        private static byte Alpha(int percent) => (byte)Math.Round(255 * Math.Max(0, Math.Min(100, percent)) / 100.0);

        private Brush BackgroundFor(BackdropKind kind)
        {
            Color tint = TintColor();
            Color c;
            if (kind == BackdropKind.Solid) c = Color.FromRgb(tint.R, tint.G, tint.B);
            else if (kind == BackdropKind.Acrylic && _accentActive) c = Colors.Transparent; // tint lives in the accent policy
            else c = Color.FromArgb(Alpha(OpacityFor(kind)), tint.R, tint.G, tint.B);

            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        /// <summary>GradientColor alpha for SWCA acrylic. 0 makes some Windows 10 builds render it black, so keep ≥ 1.</summary>
        private static byte AccentAlpha() => Math.Max((byte)1, Alpha(OpacityFor(BackdropKind.Acrylic)));

        // DWM_WINDOW_CORNER_PREFERENCE: 0 default, 1 do not round, 2 round, 3 round small.
        private static int CornerValue(CornerStyle c) =>
            c == CornerStyle.Round ? 2 : c == CornerStyle.Small ? 3 : c == CornerStyle.Square ? 1 : 0;

        private void ExtendFrame(bool glass)
        {
            if (_hwnd == IntPtr.Zero) return;
            var m = glass ? new NativeMethods.MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 } : new NativeMethods.MARGINS();
            try { NativeMethods.DwmExtendFrameIntoClientArea(_hwnd, ref m); } catch { }
        }

        private bool? _chromeGlass;

        private void ApplyChrome(bool glass)
        {
            if (_chromeGlass == glass) return; // re-setting WindowChrome is not free: it re-extends the frame
            _chromeGlass = glass;
            var chrome = new WindowChrome
            {
                CaptionHeight = 0,
                ResizeBorderThickness = new Thickness(_resizeBorder),
                CornerRadius = new CornerRadius(0),
                GlassFrameThickness = glass ? new Thickness(-1) : new Thickness(0),
                UseAeroCaptionButtons = false,
            };
            var existing = WindowChrome.GetWindowChrome(_window);
            if (existing != null) chrome.CaptionHeight = existing.CaptionHeight;
            WindowChrome.SetWindowChrome(_window, chrome);
        }

        /// <summary>Sets the draggable caption height (DIPs) of the window.</summary>
        public void SetCaptionHeight(double height)
        {
            var chrome = WindowChrome.GetWindowChrome(_window);
            if (chrome == null) return;
            var copy = (WindowChrome)chrome.Clone();
            copy.CaptionHeight = height;
            WindowChrome.SetWindowChrome(_window, copy);
        }

        /// <summary>
        /// Window region on Windows 10/8/7. Two reasons to set one:
        ///  * the "rounded corners on older Windows" option (round-rect region);
        ///  * Win10 SetWindowCompositionAttribute blur/acrylic: without a region DWM paints its own (opaque black)
        ///    frame under our extended glass, so transparent pixels show black instead of the accent. A plain
        ///    rectangular region switches DWM's frame drawing off and the accent shows through.
        /// Never on Windows 11 — a region disables DWM's rounded corners and shadow there.
        /// </summary>
        private bool NeedsRegion => !OsInfo.IsWin11 && (App.Settings.RoundOldWindows || _accentActive);

        private void ApplyRegion()
        {
            if (_hwnd == IntPtr.Zero) return;
            var state = _window.WindowState;
            if (state == WindowState.Minimized) return; // the 160×28 iconic stub; WM_SIZE re-applies on restore

            if (NeedsRegion && state != WindowState.Maximized)
            {
                NativeMethods.GetWindowRect(_hwnd, out var r);
                IntPtr rgn;
                if (App.Settings.RoundOldWindows)
                {
                    int d = (int)Math.Round(16 * VisualTreeHelper.GetDpi(_window).DpiScaleX);
                    rgn = NativeMethods.CreateRoundRectRgn(0, 0, r.Width + 1, r.Height + 1, d, d);
                }
                else
                {
                    rgn = NativeMethods.CreateRectRgn(0, 0, r.Width, r.Height);
                }
                if (NativeMethods.SetWindowRgn(_hwnd, rgn, true) == 0) NativeMethods.DeleteObject(rgn); // system owns it on success
                _hasRegion = true;
            }
            else if (_hasRegion)
            {
                NativeMethods.SetWindowRgn(_hwnd, IntPtr.Zero, true);
                _hasRegion = false;
            }
        }

        private bool _hasRegion;

        private void ResetNative()
        {
            if (_systemBackdropActive)
            {
                SetDwm(NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE, 1); // DWMSBT_NONE
                _systemBackdropActive = false;
            }
            if (OsInfo.SupportsLegacyMica && !OsInfo.SupportsSystemBackdrop) SetDwm(NativeMethods.DWMWA_MICA_EFFECT, 0);
            if (_accentActive)
            {
                SetAccent(NativeMethods.AccentState.Disabled, Colors.Transparent, 0);
                _accentActive = false;
            }
        }

        private bool SetSystemBackdrop(int type)
        {
            bool ok = SetDwm(NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE, type);
            _systemBackdropActive = ok;
            return ok;
        }

        private bool SetDwm(int attr, int value)
        {
            if (attr == 0 || _hwnd == IntPtr.Zero) return false;
            try
            {
                int hr = NativeMethods.DwmSetWindowAttribute(_hwnd, attr, ref value, sizeof(int));
                return hr == 0;
            }
            catch { return false; }
        }

        private bool SetAccent(NativeMethods.AccentState state, Color tint, byte alpha)
        {
            if (_hwnd == IntPtr.Zero) return false;
            var accent = new NativeMethods.AccentPolicy
            {
                AccentState = state,
                AccentFlags = state == NativeMethods.AccentState.Disabled ? 0u : 2u,
                // ABGR
                GradientColor = ((uint)alpha << 24) | ((uint)tint.B << 16) | ((uint)tint.G << 8) | tint.R,
            };
            int size = Marshal.SizeOf(accent);
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(accent, ptr, false);
                var data = new NativeMethods.WindowCompositionAttributeData { Attribute = NativeMethods.WCA_ACCENT_POLICY, Data = ptr, SizeOfData = size };
                bool ok = NativeMethods.SetWindowCompositionAttribute(_hwnd, ref data) != 0;
                if (state != NativeMethods.AccentState.Disabled) _accentActive = ok;
                return ok;
            }
            catch { return false; }
            finally { Marshal.FreeHGlobal(ptr); }
        }
    }
}
