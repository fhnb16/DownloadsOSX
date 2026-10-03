using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DownloadsOSX.Interop;
using DownloadsOSX.Models;
using DownloadsOSX.Services;

namespace DownloadsOSX.Views.Preview
{
    /// <summary>Explorer preview handler (PDF, Office, fonts, …) hosted in the preview window.</summary>
    internal sealed class HandlerPreview : Border, IPreviewView
    {
        private PreviewHandlerHost _host;

        public HandlerPreview()
        {
            CornerRadius = new CornerRadius(6);
            ClipToBounds = true;
        }

        public FrameworkElement Element => this;
        public Size NaturalSize => new Size(900, 680);
        public string Info => null;

        public static bool CanPreview(FileEntry entry)
        {
            return !entry.IsFolder && PreviewHandlerHost.FindHandler(entry.Extension) != null;
        }

        public async Task<bool> LoadAsync(FileEntry entry, CancellationToken ct)
        {
            var clsid = PreviewHandlerHost.FindHandler(entry.Extension);
            if (clsid == null) return false;

            // A fresh host per file: some handlers don't support re-initialization.
            Unload();
            _host = new PreviewHandlerHost();
            Child = _host;

            // Wait for the child HWND to exist (the host must be in a loaded visual tree).
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
            if (_host.Handle == IntPtr.Zero)
            {
                var tcs = new TaskCompletionSource<bool>();
                RoutedEventHandler loaded = null;
                loaded = (s, e) => { _host.Loaded -= loaded; tcs.TrySetResult(true); };
                _host.Loaded += loaded;
                await Task.WhenAny(tcs.Task, Task.Delay(2000));
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
            }
            if (ct.IsCancellationRequested || _host.Handle == IntPtr.Zero) return false;

            bool dark = ThemeService.IsDark;
            var bg = dark ? Color.FromRgb(0x20, 0x20, 0x20) : Colors.White;
            var fg = dark ? Colors.White : Colors.Black;
            Background = new SolidColorBrush(bg);
            return _host.Load(entry.Path, clsid.Value, bg, fg);
        }

        public bool HandleKey(KeyEventArgs e) => false;

        public void Unload()
        {
            if (_host != null)
            {
                _host.Unload();
                Child = null;
                _host.Dispose();
                _host = null;
            }
        }
    }
}
