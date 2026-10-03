using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using DownloadsOSX.Models;

namespace DownloadsOSX.Views.Preview
{
    /// <summary>Read-only text/code view: first 512 KB, BOM → UTF-8 → ANSI detection, binary files rejected.</summary>
    internal sealed class TextPreview : Grid, IPreviewView
    {
        private const int MaxBytes = 512 * 1024;
        private readonly TextBox _text;
        private readonly TextBlock _note;
        private readonly ToggleButton _wrap;

        public TextPreview()
        {
            RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _text = new TextBox
            {
                IsReadOnly = true,
                AcceptsReturn = true,
                AcceptsTab = true,
                TextWrapping = TextWrapping.NoWrap,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalContentAlignment = VerticalAlignment.Top,
                Padding = new Thickness(10, 8, 4, 4),
                IsUndoEnabled = false,
            };
            _text.SetResourceReference(Control.FontFamilyProperty, "App.MonoFont");
            _text.SetResourceReference(Control.BackgroundProperty, "Brush.Card");
            Children.Add(_text);

            var footer = new DockPanel { Margin = new Thickness(2, 6, 0, 0) };
            _wrap = new ToggleButton { ToolTip = App.Str("WordWrap"), Width = 28, Height = 26 };
            var icon = new System.Windows.Shapes.Path { Data = (Geometry)Application.Current.FindResource("Icon.Wrap") };
            icon.SetResourceReference(StyleProperty, "IconPath");
            _wrap.Content = icon;
            _wrap.SetResourceReference(StyleProperty, "Toggle.Icon");
            _wrap.Click += (s, e) => _text.TextWrapping = _wrap.IsChecked == true ? TextWrapping.Wrap : TextWrapping.NoWrap;
            DockPanel.SetDock(_wrap, Dock.Right);
            footer.Children.Add(_wrap);
            _note = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            _note.SetResourceReference(StyleProperty, "Text.Secondary");
            footer.Children.Add(_note);
            Grid.SetRow(footer, 1);
            Children.Add(footer);
        }

        public FrameworkElement Element => this;
        public Size NaturalSize => new Size(780, 540);
        public string Info { get; private set; }

        public async Task<bool> LoadAsync(FileEntry entry, CancellationToken ct)
        {
            Result r;
            try { r = await Task.Run(() => Read(entry.Path), ct); }
            catch (Exception ex)
            {
                Log.Info("Text preview failed: " + ex.Message);
                return false;
            }
            if (r == null || ct.IsCancellationRequested) return false;

            _text.Text = r.Text;
            _text.ScrollToHome();
            int lines = r.Text.Count(c => c == '\n') + 1;
            Info = $"{r.Encoding} · {string.Format(App.Str("LinesCount"), lines)}";
            _note.Text = r.Truncated ? App.Str("TextTruncated") : "";
            return true;
        }

        private sealed class Result
        {
            public string Text;
            public string Encoding;
            public bool Truncated;
        }

        /// <summary>Returns null for binary content.</summary>
        public static bool LooksLikeText(string path)
        {
            try { return Read(path, 8192) != null; }
            catch { return false; }
        }

        private static Result Read(string path, int max = MaxBytes)
        {
            byte[] bytes;
            bool truncated;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                int len = (int)Math.Min(fs.Length, max);
                truncated = fs.Length > max;
                bytes = new byte[len];
                int read = 0;
                while (read < len)
                {
                    int n = fs.Read(bytes, read, len - read);
                    if (n <= 0) break;
                    read += n;
                }
                if (read < len) Array.Resize(ref bytes, read);
            }

            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return new Result { Text = new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3), Encoding = "UTF-8 BOM", Truncated = truncated };
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return new Result { Text = Encoding.Unicode.GetString(bytes, 2, (bytes.Length - 2) & ~1), Encoding = "UTF-16 LE", Truncated = truncated };
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                return new Result { Text = Encoding.BigEndianUnicode.GetString(bytes, 2, (bytes.Length - 2) & ~1), Encoding = "UTF-16 BE", Truncated = truncated };

            // Binary check: NUL bytes or lots of control characters in the first 8 KB.
            int sample = Math.Min(bytes.Length, 8192), control = 0;
            for (int i = 0; i < sample; i++)
            {
                byte b = bytes[i];
                if (b == 0) return null;
                if (b < 0x09 || (b > 0x0D && b < 0x20 && b != 0x1B)) control++;
            }
            if (sample > 0 && control * 20 > sample) return null;

            // UTF-8 (strict) else the system ANSI code page. A truncated tail may cut a multi-byte sequence.
            int usable = bytes.Length;
            if (truncated) while (usable > 0 && usable > bytes.Length - 4 && (bytes[usable - 1] & 0xC0) == 0x80) usable--;
            if (truncated && usable > 0 && bytes[usable - 1] >= 0xC0) usable--;
            try
            {
                string s = new UTF8Encoding(false, true).GetString(bytes, 0, usable);
                return new Result { Text = s, Encoding = "UTF-8", Truncated = truncated };
            }
            catch (DecoderFallbackException)
            {
                return new Result { Text = Encoding.Default.GetString(bytes), Encoding = Encoding.Default.WebName.ToUpperInvariant(), Truncated = truncated };
            }
        }

        public bool HandleKey(KeyEventArgs e)
        {
            if (e.Key == Key.W && Keyboard.Modifiers == ModifierKeys.None)
            {
                _wrap.IsChecked = _wrap.IsChecked != true;
                _text.TextWrapping = _wrap.IsChecked == true ? TextWrapping.Wrap : TextWrapping.NoWrap;
                return true;
            }
            return false;
        }

        public void Unload() => _text.Clear();
    }
}
