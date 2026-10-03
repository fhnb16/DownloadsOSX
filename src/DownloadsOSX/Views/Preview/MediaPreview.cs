using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using DownloadsOSX.Models;

namespace DownloadsOSX.Views.Preview
{
    /// <summary>Video/audio player (MediaElement → Windows Media Foundation/WMP codecs) with a compact control bar.</summary>
    internal sealed class MediaPreview : Grid, IPreviewView
    {
        private readonly MediaElement _media;
        private readonly Image _art;
        private readonly Border _bar;
        private readonly Slider _position;
        private readonly Slider _volume;
        private readonly TextBlock _time;
        private readonly Path _playIcon;
        private readonly Path _volumeIcon;
        private readonly ToggleButton _loop;
        private readonly DispatcherTimer _timer;
        private readonly DispatcherTimer _idle;
        private bool _playing;
        private bool _hasVideo;
        private bool _updatingSlider;
        private TaskCompletionSource<bool> _opened;

        public MediaPreview()
        {
            Background = Brushes.Transparent;
            RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _art = new Image { Stretch = Stretch.Uniform, MaxWidth = 160, MaxHeight = 160, Margin = new Thickness(0, 16, 0, 8) };
            Children.Add(_art);

            _media = new MediaElement
            {
                LoadedBehavior = MediaState.Manual,
                UnloadedBehavior = MediaState.Close,
                ScrubbingEnabled = true,
                Stretch = Stretch.Uniform,
            };
            _media.MediaOpened += (s, e) => _opened?.TrySetResult(true);
            _media.MediaFailed += (s, e) =>
            {
                Log.Info("Media failed: " + e.ErrorException?.Message);
                _opened?.TrySetResult(false);
            };
            _media.MediaEnded += (s, e) =>
            {
                if (App.Settings.LoopMedia) { _media.Position = TimeSpan.Zero; _media.Play(); }
                else SetPlaying(false);
            };
            _media.MouseLeftButtonUp += (s, e) => TogglePlay();
            Grid.SetRowSpan(_media, 2);
            Children.Add(_media);

            // Control bar
            _playIcon = Icon("Icon.Pause");
            var play = IconButton(_playIcon, (s, e) => TogglePlay());

            _position = new Slider { Minimum = 0, Maximum = 1, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
            _position.ValueChanged += (s, e) =>
            {
                if (!_updatingSlider) _media.Position = TimeSpan.FromSeconds(_position.Value);
            };

            _time = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), MinWidth = 86, TextAlignment = TextAlignment.Right };
            _time.SetResourceReference(TextBlock.FontSizeProperty, "App.FontSizeSmall");
            _time.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");

            _volumeIcon = Icon("Icon.Volume");
            var mute = IconButton(_volumeIcon, (s, e) => { _media.IsMuted = !_media.IsMuted; UpdateVolumeIcon(); });
            _volume = new Slider { Minimum = 0, Maximum = 1, Width = 80, VerticalAlignment = VerticalAlignment.Center, Value = App.Settings.Volume };
            _volume.ValueChanged += (s, e) => { _media.Volume = _volume.Value; App.Settings.Volume = _volume.Value; UpdateVolumeIcon(); };

            _loop = new ToggleButton { Content = Icon("Icon.Loop"), IsChecked = App.Settings.LoopMedia, ToolTip = App.Str("Loop") };
            _loop.SetResourceReference(StyleProperty, "Toggle.Icon");
            _loop.Click += (s, e) => App.Settings.LoopMedia = _loop.IsChecked == true;

            var bar = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(play, Dock.Left);
            DockPanel.SetDock(_loop, Dock.Right);
            DockPanel.SetDock(_volume, Dock.Right);
            DockPanel.SetDock(mute, Dock.Right);
            DockPanel.SetDock(_time, Dock.Right);
            bar.Children.Add(play);
            bar.Children.Add(_loop);
            bar.Children.Add(_volume);
            bar.Children.Add(mute);
            bar.Children.Add(_time);
            bar.Children.Add(_position);

            _bar = new Border
            {
                Child = bar,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(6, 4, 6, 4),
                Margin = new Thickness(10, 0, 10, 10),
                VerticalAlignment = VerticalAlignment.Bottom,
            };
            _bar.SetResourceReference(Border.BackgroundProperty, "Brush.MediaBar");
            Grid.SetRow(_bar, 1);
            Children.Add(_bar);

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _timer.Tick += (s, e) => UpdatePosition();

            // Auto-hide the bar over video while playing and the mouse is idle.
            _idle = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
            _idle.Tick += (s, e) =>
            {
                _idle.Stop();
                if (_hasVideo && _playing && !_bar.IsMouseOver) FadeBar(0);
            };
            MouseMove += (s, e) => { FadeBar(1); _idle.Stop(); _idle.Start(); };
        }

        public FrameworkElement Element => this;
        public Size NaturalSize { get; private set; }
        public string Info { get; private set; }

        public async Task<bool> LoadAsync(FileEntry entry, CancellationToken ct)
        {
            Unload();
            _opened = new TaskCompletionSource<bool>();
            _art.Source = entry.Thumbnail;
            LoadArt(entry.Path, ct);
            _media.Volume = App.Settings.Volume;
            _loop.IsChecked = App.Settings.LoopMedia;

            // MediaElement ignores open requests made before it is part of a loaded visual tree.
            if (!_media.IsLoaded)
            {
                var loaded = new TaskCompletionSource<bool>();
                RoutedEventHandler handler = null;
                handler = (s, e) => { _media.Loaded -= handler; loaded.TrySetResult(true); };
                _media.Loaded += handler;
                await Task.WhenAny(loaded.Task, Task.Delay(3000));
                if (ct.IsCancellationRequested) return false;
            }

            _media.Source = new Uri(entry.Path);
            _media.Pause(); // with Manual behavior, opening needs Play/Pause

            var done = await Task.WhenAny(_opened.Task, Task.Delay(10000, ct)).ConfigureAwait(true);
            if (done != _opened.Task || !_opened.Task.Result || ct.IsCancellationRequested)
            {
                Unload();
                return false;
            }

            _hasVideo = _media.HasVideo;
            double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
            NaturalSize = _hasVideo
                ? new Size(Math.Max(320, _media.NaturalVideoWidth / dpi), Math.Max(180, _media.NaturalVideoHeight / dpi))
                : new Size(460, 260);
            _art.Visibility = _hasVideo ? Visibility.Collapsed : Visibility.Visible;
            _media.Visibility = _hasVideo ? Visibility.Visible : Visibility.Hidden;
            Grid.SetRowSpan(_media, _hasVideo ? 2 : 1);

            var duration = _media.NaturalDuration.HasTimeSpan ? _media.NaturalDuration.TimeSpan : TimeSpan.Zero;
            _position.Maximum = Math.Max(1, duration.TotalSeconds);
            _position.IsEnabled = duration > TimeSpan.Zero;
            Info = (_hasVideo ? $"{_media.NaturalVideoWidth} × {_media.NaturalVideoHeight} · " : "") + Format(duration);

            UpdatePosition();
            FadeBar(1);
            if (App.Settings.AutoplayMedia) Play(); else SetPlaying(false);
            _timer.Start();
            return true;
        }

        private async void LoadArt(string path, CancellationToken ct)
        {
            int px = (int)Math.Ceiling(160 * VisualTreeHelper.GetDpi(this).DpiScaleX);
            try
            {
                var big = await App.Sta.Run(() => Interop.ShellThumbnail.Get(path, px), highPriority: true);
                if (big != null && !ct.IsCancellationRequested) _art.Source = big;
            }
            catch { }
        }

        public void Unload()
        {
            _timer.Stop();
            _idle.Stop();
            try
            {
                _media.Stop();
                _media.Close();
                _media.Source = null;
            }
            catch { }
            _playing = false;
        }

        public bool HandleKey(KeyEventArgs e)
        {
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            switch (e.Key)
            {
                case Key.K:
                case Key.P:
                case Key.MediaPlayPause:
                    TogglePlay(); return true;
                case Key.M:
                    _media.IsMuted = !_media.IsMuted; UpdateVolumeIcon(); return true;
                case Key.Left when shift:
                case Key.J:
                    Seek(-5); return true;
                case Key.Right when shift:
                case Key.L:
                    Seek(5); return true;
                case Key.Up when shift:
                    _volume.Value = Math.Min(1, _volume.Value + 0.1); return true;
                case Key.Down when shift:
                    _volume.Value = Math.Max(0, _volume.Value - 0.1); return true;
            }
            return false;
        }

        private void Seek(double seconds)
        {
            var t = _media.Position + TimeSpan.FromSeconds(seconds);
            if (t < TimeSpan.Zero) t = TimeSpan.Zero;
            _media.Position = t;
            UpdatePosition();
        }

        private void TogglePlay()
        {
            if (_playing) { _media.Pause(); SetPlaying(false); }
            else Play();
        }

        private void Play()
        {
            _media.Play();
            SetPlaying(true);
        }

        private void SetPlaying(bool playing)
        {
            _playing = playing;
            _playIcon.Data = (Geometry)Application.Current.FindResource(playing ? "Icon.Pause" : "Icon.Play");
            _playIcon.StrokeThickness = playing ? 2.2 : 1.35;
            if (playing) _playIcon.ClearValue(Shape.FillProperty);
            else _playIcon.SetBinding(Shape.FillProperty, new System.Windows.Data.Binding("Stroke") { Source = _playIcon });
            if (!playing) FadeBar(1);
        }

        private void UpdatePosition()
        {
            var pos = _media.Position;
            var dur = _media.NaturalDuration.HasTimeSpan ? _media.NaturalDuration.TimeSpan : TimeSpan.Zero;
            if (!_position.IsMouseCaptureWithin)
            {
                _updatingSlider = true;
                _position.Value = Math.Min(_position.Maximum, pos.TotalSeconds);
                _updatingSlider = false;
            }
            _time.Text = Format(pos) + " / " + Format(dur);
        }

        private void UpdateVolumeIcon()
        {
            _volumeIcon.Data = (Geometry)Application.Current.FindResource(_media.IsMuted || _volume.Value < 0.01 ? "Icon.Mute" : "Icon.Volume");
        }

        private void FadeBar(double to)
        {
            if (Math.Abs(_bar.Opacity - to) < 0.01) return;
            _bar.BeginAnimation(OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(to > 0 ? 120 : 400)));
            Cursor = to > 0 ? null : Cursors.None;
        }

        private static string Format(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

        private static Path Icon(string key)
        {
            var p = new Path { Data = (Geometry)Application.Current.FindResource(key) };
            p.SetResourceReference(StyleProperty, "IconPath");
            return p;
        }

        private static Button IconButton(object content, RoutedEventHandler click)
        {
            var b = new Button { Content = content };
            b.SetResourceReference(StyleProperty, "Button.Icon");
            b.Click += click;
            return b;
        }
    }
}
