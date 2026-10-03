using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DownloadsOSX.Interop;
using DownloadsOSX.Models;

namespace DownloadsOSX.Views.Preview
{
    /// <summary>Fallback card: big shell thumbnail + file facts (and a listing for folders).</summary>
    internal sealed class InfoPreview : ScrollViewer, IPreviewView
    {
        private readonly Image _thumb;
        private readonly TextBlock _name;
        private readonly TextBlock _facts;
        private readonly TextBox _path;
        private readonly TextBlock _listing;

        public InfoPreview()
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            Focusable = false;

            var panel = new StackPanel { Margin = new Thickness(24, 16, 24, 20), HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 520 };
            _thumb = new Image { Width = 160, Height = 160, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 0, 14) };
            RenderOptions.SetBitmapScalingMode(_thumb, BitmapScalingMode.HighQuality);
            panel.Children.Add(_thumb);

            _name = new TextBlock { TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) };
            _name.SetResourceReference(TextBlock.FontSizeProperty, "App.FontSizeLarge");
            panel.Children.Add(_name);

            _facts = new TextBlock { TextAlignment = TextAlignment.Center, LineHeight = 20 };
            _facts.SetResourceReference(StyleProperty, "Text.Secondary");
            panel.Children.Add(_facts);

            _path = new TextBox
            {
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 12, 0, 0),
                TextAlignment = TextAlignment.Center,
            };
            _path.SetResourceReference(StyleProperty, "TextBox.Plain");
            _path.SetResourceReference(TextBox.FontSizeProperty, "App.FontSizeSmall");
            panel.Children.Add(_path);

            _listing = new TextBlock { Margin = new Thickness(0, 12, 0, 0), TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis, LineHeight = 20 };
            _listing.SetResourceReference(StyleProperty, "Text.Secondary");
            panel.Children.Add(_listing);

            Content = panel;
        }

        public FrameworkElement Element => this;
        public Size NaturalSize { get; private set; } = new Size(520, 420);
        public string Info => null;

        public async Task<bool> LoadAsync(FileEntry entry, CancellationToken ct)
        {
            _thumb.Source = entry.Thumbnail;
            _name.Text = entry.Name;
            _path.Text = entry.Path;
            _listing.Text = "";

            string type = Shell.GetTypeName(entry.Path, entry.IsFolder);
            var lines = new[]
            {
                type,
                entry.IsFolder ? null : $"{Shell.FormatSize(entry.Size)} ({entry.Size:N0} {App.Str("UnitBytes")})",
                $"{App.Str("Modified")}: {entry.Modified:f}",
                $"{App.Str("Created")}: {entry.Created:f}",
            };
            _facts.Text = string.Join("\n", lines.Where(l => l != null));
            NaturalSize = new Size(520, entry.IsFolder ? 520 : 420);

            int px = (int)Math.Ceiling(160 * VisualTreeHelper.GetDpi(this).DpiScaleX);
            string path = entry.Path;
            try
            {
                var big = await App.Sta.Run(() => ShellThumbnail.Get(path, px), highPriority: true);
                if (big != null && !ct.IsCancellationRequested) _thumb.Source = big;
            }
            catch { }

            if (entry.IsFolder)
            {
                try
                {
                    var listing = await Task.Run(() =>
                    {
                        var all = new DirectoryInfo(path).EnumerateFileSystemInfos().Select(i => i.Name).Take(501).ToList();
                        return all;
                    }, ct);
                    string count = listing.Count > 500 ? "500+" : listing.Count.ToString();
                    _facts.Text += "\n" + string.Format(App.Str("FolderItems"), count);
                    _listing.Text = string.Join("\n", listing.Take(14)) + (listing.Count > 14 ? "\n…" : "");
                }
                catch (Exception ex) { _listing.Text = ex.Message; }
            }
            return true;
        }

        public bool HandleKey(KeyEventArgs e) => false;
        public void Unload() { }
    }
}
