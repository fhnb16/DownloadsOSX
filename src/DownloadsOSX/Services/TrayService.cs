using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using DownloadsOSX.Interop;
using Forms = System.Windows.Forms;

namespace DownloadsOSX.Services
{
    /// <summary>Optional notification-area icon. Left click toggles the stack, right click shows the app menu.</summary>
    internal sealed class TrayService : IDisposable
    {
        private readonly Forms.NotifyIcon _icon;
        private readonly Func<ContextMenu> _menuFactory;

        public event Action Clicked;
        public event Action BalloonClicked;

        public TrayService(Func<ContextMenu> menuFactory)
        {
            _menuFactory = menuFactory;
            _icon = new Forms.NotifyIcon
            {
                Text = "Downloads",
                Icon = LoadIcon(),
                Visible = false,
            };
            _icon.MouseUp += (s, e) =>
            {
                if (e.Button == Forms.MouseButtons.Left) Clicked?.Invoke();
                else if (e.Button == Forms.MouseButtons.Right) ShowMenu();
            };
            _icon.BalloonTipClicked += (s, e) => BalloonClicked?.Invoke();
        }

        /// <summary>Tooltip (Windows limits it to 63 characters).</summary>
        public string Text
        {
            get => _icon.Text;
            set => _icon.Text = string.IsNullOrEmpty(value) ? "Downloads" : (value.Length > 63 ? value.Substring(0, 62) + "…" : value);
        }

        public bool Visible
        {
            get => _icon.Visible;
            set => _icon.Visible = value;
        }

        public void ShowBalloon(string title, string text)
        {
            if (!_icon.Visible) return;
            _icon.ShowBalloonTip(4000, title, text, Forms.ToolTipIcon.None);
        }

        private void ShowMenu()
        {
            var menu = _menuFactory();
            menu.Placement = PlacementMode.MousePoint;
            menu.Opened += (s, e) =>
            {
                // A popup without an active owner window won't close on outside clicks unless it is foreground.
                if (PresentationSource.FromVisual(menu) is HwndSource src) NativeMethods.SetForegroundWindow(src.Handle);
            };
            menu.IsOpen = true;
        }

        private static System.Drawing.Icon LoadIcon()
        {
            try
            {
                var info = Application.GetResourceStream(new Uri("pack://application:,,,/Downloads;component/Assets/Downloads.ico"));
                if (info != null)
                    using (info.Stream)
                        return new System.Drawing.Icon(info.Stream, Forms.SystemInformation.SmallIconSize);
            }
            catch { }
            return System.Drawing.SystemIcons.Application;
        }

        public void Dispose()
        {
            _icon.Visible = false;
            _icon.Dispose();
        }
    }
}
