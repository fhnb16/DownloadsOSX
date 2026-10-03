using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using DownloadsOSX.Models;

namespace DownloadsOSX.Views.Preview
{
    /// <summary>
    /// Smooth image viewer: decodes off the UI thread at screen resolution, loads full resolution lazily when zooming
    /// in, zooms around the cursor with eased transforms (never by resizing the window), pans by dragging,
    /// honors EXIF orientation and plays animated GIFs.
    /// </summary>
    internal sealed class ImagePreview : Grid, IPreviewView
    {
        private enum ZoomMode { Fit, Actual, Custom }

        private readonly Image _image;
        private readonly ScaleTransform _scale = new ScaleTransform(1, 1);
        private readonly TranslateTransform _translate = new TranslateTransform();
        private readonly TextBlock _zoomBadge;

        private string _path;
        private DateTime _stamp;
        private int _pixelW, _pixelH;      // oriented pixel size
        private int _decodedW;             // oriented decoded width
        private int _rotation;             // 0/90/180/270
        private bool _flipX;
        private bool _isAnimated;
        private Size _logical;             // DIP size at 100%
        private double _s = 1, _x, _y;     // target transform
        private ZoomMode _mode = ZoomMode.Fit;
        private bool _panning;
        private Point _panStart;
        private double _panX, _panY;
        private CancellationTokenSource _fullResCts;

        private static readonly LinkedList<KeyValuePair<string, BitmapSource>> Cache = new LinkedList<KeyValuePair<string, BitmapSource>>();

        public ImagePreview()
        {
            ClipToBounds = true;
            Background = Brushes.Transparent;
            Focusable = false;

            _image = new Image
            {
                Stretch = Stretch.Fill,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                RenderTransformOrigin = new Point(0, 0),
                RenderTransform = new TransformGroup { Children = { _scale, _translate } },
            };
            RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
            // A Canvas never layout-clips its children. In a Grid the oversized Image would get a layout clip in its
            // local (pre-transform) space, which the zoom transform then shrinks — showing only a cropped corner.
            var canvas = new Canvas { ClipToBounds = false };
            canvas.Children.Add(_image);
            Children.Add(canvas);

            _zoomBadge = new TextBlock
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 12),
                Padding = new Thickness(10, 4, 10, 5),
                Opacity = 0,
                IsHitTestVisible = false,
            };
            _zoomBadge.SetResourceReference(TextBlock.BackgroundProperty, "Brush.Toast");
            _zoomBadge.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
            Children.Add(_zoomBadge);

            SizeChanged += (s, e) => { if (_mode == ZoomMode.Fit) Fit(false); else Clamp(true); };
            MouseWheel += OnWheel;
            MouseLeftButtonDown += OnMouseDown;
            MouseMove += OnMouseMove;
            MouseLeftButtonUp += (s, e) => EndPan();
            LostMouseCapture += (s, e) => EndPan();
        }

        public FrameworkElement Element => this;
        public Size NaturalSize => _logical;
        public string Info { get; private set; }
        public BitmapSource CurrentBitmap => _image.Source as BitmapSource;

        public async Task<bool> LoadAsync(FileEntry entry, CancellationToken ct)
        {
            _fullResCts?.Cancel();
            string path = entry.Path;
            DateTime stamp = entry.Modified;
            double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;

            ImageInfo info;
            try { info = await Task.Run(() => ReadInfo(path), ct); }
            catch (OperationCanceledException) { return false; }
            catch (Exception ex)
            {
                Log.Info("Image decode not possible (" + ex.Message + "), falling back: " + path);
                return false;
            }
            if (ct.IsCancellationRequested) return false;

            // Decode at roughly screen size; full resolution later only if the user zooms in.
            var work = SystemParameters.WorkArea;
            int screenPx = (int)Math.Ceiling(Math.Max(work.Width, work.Height) * dpi);
            int storedW = info.Swap ? info.Height : info.Width;
            int decodeStoredW = Math.Min(storedW, screenPx);

            BitmapSource bmp;
            List<Tuple<BitmapSource, TimeSpan>> frames = null;
            try
            {
                if (info.IsGif && info.FrameCount > 1 && (long)info.Width * info.Height * info.FrameCount < 120_000_000L)
                {
                    frames = await App.Sta.Run(() => ComposeGif(path), highPriority: true);
                    bmp = frames.Count > 0 ? frames[0].Item1 : null;
                }
                else
                {
                    bmp = await Task.Run(() => DecodeCached(path, stamp, decodeStoredW < storedW ? decodeStoredW : 0, info), ct);
                }
            }
            catch (OperationCanceledException) { return false; }
            catch (Exception ex)
            {
                Log.Info("Image decode failed (" + ex.Message + "): " + path);
                return false;
            }
            if (bmp == null || ct.IsCancellationRequested) return false;

            bool samePath = _path == path;
            _path = path;
            _stamp = stamp;
            _pixelW = info.Width;
            _pixelH = info.Height;
            _rotation = info.Rotation;
            _flipX = info.FlipX;
            _decodedW = bmp.PixelWidth;
            _isAnimated = frames != null && frames.Count > 1;
            _logical = new Size(_pixelW / dpi, _pixelH / dpi);

            _image.BeginAnimation(Image.SourceProperty, null);
            _image.Source = bmp;
            _image.Width = _logical.Width;
            _image.Height = _logical.Height;
            if (_isAnimated) StartGif(frames);

            Info = $"{_pixelW} × {_pixelH}" + (_isAnimated ? " · GIF" : "");

            if (!samePath)
            {
                if (!App.Settings.KeepZoom || _mode == ZoomMode.Fit) _mode = ZoomMode.Fit;
                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_mode == ZoomMode.Fit) Fit(false);
                    else ZoomTo(_mode == ZoomMode.Actual ? 1.0 : _s, new Point(ActualWidth / 2, ActualHeight / 2), false, keepMode: true);
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }
            return true;
        }

        public void Unload()
        {
            _fullResCts?.Cancel();
            _image.BeginAnimation(Image.SourceProperty, null);
        }

        #region Zoom & pan

        private double FitScale()
        {
            if (_logical.Width <= 0 || ActualWidth <= 0) return 1;
            return Math.Min(1.0, Math.Min(ActualWidth / _logical.Width, ActualHeight / _logical.Height));
        }

        public void Fit(bool animate)
        {
            _mode = ZoomMode.Fit;
            double s = FitScale();
            AnimateTo(s, (ActualWidth - _logical.Width * s) / 2, (ActualHeight - _logical.Height * s) / 2, animate);
        }

        public void ZoomTo(double scale, Point at, bool animate, bool keepMode = false)
        {
            double min = Math.Min(FitScale(), 1.0) * 0.5;
            double ns = Math.Max(min, Math.Min(32, scale));
            double x = at.X - (at.X - _x) * ns / _s;
            double y = at.Y - (at.Y - _y) * ns / _s;
            if (!keepMode) _mode = Math.Abs(ns - 1.0) < 0.001 ? ZoomMode.Actual : ZoomMode.Custom;
            ClampPan(ns, ref x, ref y);
            AnimateTo(ns, x, y, animate);
            ShowZoomBadge(ns);
            EnsureResolution(ns);
        }

        private void ToggleActual(Point at)
        {
            if (_mode == ZoomMode.Fit && FitScale() < 0.999) ZoomTo(1.0, at, true);
            else Fit(true);
        }

        private void ClampPan(double s, ref double x, ref double y)
        {
            double w = _logical.Width * s, h = _logical.Height * s;
            x = w <= ActualWidth ? (ActualWidth - w) / 2 : Math.Max(ActualWidth - w, Math.Min(0, x));
            y = h <= ActualHeight ? (ActualHeight - h) / 2 : Math.Max(ActualHeight - h, Math.Min(0, y));
        }

        private void Clamp(bool animate)
        {
            double x = _x, y = _y;
            ClampPan(_s, ref x, ref y);
            AnimateTo(_s, x, y, animate);
        }

        private void AnimateTo(double s, double x, double y, bool animate)
        {
            _s = s; _x = x; _y = y;
            RenderOptions.SetBitmapScalingMode(_image, s >= 3 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);

            if (animate && App.Settings.Animations)
            {
                var d = TimeSpan.FromMilliseconds(170);
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                _scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(s, d) { EasingFunction = ease }, HandoffBehavior.SnapshotAndReplace);
                _scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(s, d) { EasingFunction = ease }, HandoffBehavior.SnapshotAndReplace);
                _translate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x, d) { EasingFunction = ease }, HandoffBehavior.SnapshotAndReplace);
                _translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(y, d) { EasingFunction = ease }, HandoffBehavior.SnapshotAndReplace);
            }
            else
            {
                _scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                _scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                _translate.BeginAnimation(TranslateTransform.XProperty, null);
                _translate.BeginAnimation(TranslateTransform.YProperty, null);
                _scale.ScaleX = s; _scale.ScaleY = s;
                _translate.X = x; _translate.Y = y;
            }
        }

        private void ShowZoomBadge(double s)
        {
            _zoomBadge.Text = $"{Math.Round(s * 100)} %";
            var a = new DoubleAnimationUsingKeyFrames();
            a.KeyFrames.Add(new LinearDoubleKeyFrame(1, TimeSpan.FromMilliseconds(80)));
            a.KeyFrames.Add(new LinearDoubleKeyFrame(1, TimeSpan.FromMilliseconds(900)));
            a.KeyFrames.Add(new LinearDoubleKeyFrame(0, TimeSpan.FromMilliseconds(1200)));
            _zoomBadge.BeginAnimation(OpacityProperty, a);
        }

        private void OnWheel(object sender, MouseWheelEventArgs e)
        {
            double factor = e.Delta > 0 ? 1.25 : 1 / 1.25;
            ZoomTo(_s * factor, e.GetPosition(this), true);
            e.Handled = true;
        }

        private void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                ToggleActual(e.GetPosition(this));
                e.Handled = true;
                return;
            }
            if (_logical.Width * _s > ActualWidth + 1 || _logical.Height * _s > ActualHeight + 1)
            {
                _panning = true;
                _panStart = e.GetPosition(this);
                _panX = _x; _panY = _y;
                CaptureMouse();
                Cursor = Cursors.SizeAll;
                e.Handled = true;
            }
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (!_panning) return;
            var p = e.GetPosition(this);
            double x = _panX + p.X - _panStart.X, y = _panY + p.Y - _panStart.Y;
            ClampPan(_s, ref x, ref y);
            AnimateTo(_s, x, y, false);
        }

        private void EndPan()
        {
            if (!_panning) return;
            _panning = false;
            Cursor = null;
            if (IsMouseCaptured) ReleaseMouseCapture();
        }

        public bool HandleKey(KeyEventArgs e)
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            var center = new Point(ActualWidth / 2, ActualHeight / 2);
            switch (e.Key)
            {
                case Key.D0 when ctrl:
                case Key.NumPad0 when ctrl:
                    Fit(true); return true;
                case Key.D1 when ctrl:
                case Key.NumPad1 when ctrl:
                    ZoomTo(1.0, center, true); return true;
                case Key.OemPlus:
                case Key.Add:
                    ZoomTo(_s * 1.25, center, true); return true;
                case Key.OemMinus:
                case Key.Subtract:
                    ZoomTo(_s / 1.25, center, true); return true;
                case Key.C when ctrl:
                    try
                    {
                        Clipboard.SetImage((BitmapSource)_image.Source);
                        return true;
                    }
                    catch { return false; }
            }
            return false;
        }

        public void ZoomIn() => ZoomTo(_s * 1.25, new Point(ActualWidth / 2, ActualHeight / 2), true);
        public void ZoomOut() => ZoomTo(_s / 1.25, new Point(ActualWidth / 2, ActualHeight / 2), true);
        public void ToggleFit() => ToggleActual(new Point(ActualWidth / 2, ActualHeight / 2));

        /// <summary>Swap in a full-resolution decode once the zoom exceeds what was decoded.</summary>
        private async void EnsureResolution(double s)
        {
            if (_isAnimated || _path == null || _decodedW >= _pixelW) return;
            double neededPx = _logical.Width * s * VisualTreeHelper.GetDpi(this).DpiScaleX;
            if (neededPx <= _decodedW * 1.05) return;

            _fullResCts?.Cancel();
            _fullResCts = new CancellationTokenSource();
            var ct = _fullResCts.Token;
            string path = _path;
            var stamp = _stamp;
            int rotation = _rotation;
            bool flip = _flipX;
            try
            {
                var info = new ImageInfo { Width = _pixelW, Height = _pixelH, Rotation = rotation, FlipX = flip, Swap = rotation == 90 || rotation == 270 };
                var full = await Task.Run(() => DecodeCached(path, stamp, 0, info), ct);
                if (ct.IsCancellationRequested || path != _path || full == null) return;
                _image.Source = full;
                _decodedW = full.PixelWidth;
            }
            catch { }
        }

        #endregion

        #region Decoding

        private sealed class ImageInfo
        {
            public int Width, Height;   // oriented
            public int Rotation;
            public bool FlipX, Swap;
            public bool IsGif;
            public int FrameCount;
        }

        private static ImageInfo ReadInfo(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
                var frame = decoder.Frames[0];
                int w = frame.PixelWidth, h = frame.PixelHeight;
                int rotation = 0;
                bool flip = false;
                try
                {
                    if (frame.Metadata is BitmapMetadata meta && meta.ContainsQuery("System.Photo.Orientation") &&
                        meta.GetQuery("System.Photo.Orientation") is ushort o)
                    {
                        switch (o)
                        {
                            case 2: flip = true; break;
                            case 3: rotation = 180; break;
                            case 4: rotation = 180; flip = true; break;
                            case 5: rotation = 90; flip = true; break;
                            case 6: rotation = 90; break;
                            case 7: rotation = 270; flip = true; break;
                            case 8: rotation = 270; break;
                        }
                    }
                }
                catch { /* not all codecs expose metadata */ }

                bool swap = rotation == 90 || rotation == 270;
                return new ImageInfo
                {
                    Width = swap ? h : w,
                    Height = swap ? w : h,
                    Rotation = rotation,
                    FlipX = flip,
                    Swap = swap,
                    IsGif = decoder is GifBitmapDecoder,
                    FrameCount = decoder.Frames.Count,
                };
            }
        }

        private static BitmapSource DecodeCached(string path, DateTime stamp, int decodeStoredWidth, ImageInfo info)
        {
            string key = path + "|" + stamp.Ticks + "|" + decodeStoredWidth;
            lock (Cache)
            {
                var hit = Cache.FirstOrDefault(kv => kv.Key == key);
                if (hit.Value != null) return hit.Value;
            }

            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
            bi.UriSource = new Uri(path);
            if (decodeStoredWidth > 0) bi.DecodePixelWidth = decodeStoredWidth;
            bi.EndInit();
            bi.Freeze();

            BitmapSource result = bi;
            if (info.Rotation != 0 || info.FlipX)
            {
                var group = new TransformGroup();
                if (info.FlipX) group.Children.Add(new ScaleTransform(-1, 1));
                if (info.Rotation != 0) group.Children.Add(new RotateTransform(info.Rotation));
                var tb = new TransformedBitmap(bi, group);
                tb.Freeze();
                result = tb;
            }

            lock (Cache)
            {
                Cache.AddFirst(new KeyValuePair<string, BitmapSource>(key, result));
                while (Cache.Count > 4) Cache.RemoveLast();
            }
            return result;
        }

        /// <summary>Composites GIF frames (offsets + disposal methods) into full frames. Runs on an STA thread.</summary>
        private static List<Tuple<BitmapSource, TimeSpan>> ComposeGif(string path)
        {
            var result = new List<Tuple<BitmapSource, TimeSpan>>();
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                var dec = new GifBitmapDecoder(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                int width = dec.Frames[0].PixelWidth, height = dec.Frames[0].PixelHeight;
                try
                {
                    if (dec.Metadata?.GetQuery("/logscrdesc/Width") is ushort lw) width = lw;
                    if (dec.Metadata?.GetQuery("/logscrdesc/Height") is ushort lh) height = lh;
                }
                catch { }

                var full = new Rect(0, 0, width, height);
                BitmapSource baseImage = null;
                foreach (var frame in dec.Frames.Take(400))
                {
                    var meta = frame.Metadata as BitmapMetadata;
                    double left = Query(meta, "/imgdesc/Left"), top = Query(meta, "/imgdesc/Top");
                    double fw = Query(meta, "/imgdesc/Width", frame.PixelWidth), fh = Query(meta, "/imgdesc/Height", frame.PixelHeight);
                    double delay = Query(meta, "/grctlext/Delay", 10);
                    int disposal = (int)Query(meta, "/grctlext/Disposal");
                    if (delay < 2) delay = 10; // browsers treat 0/1 as 100 ms

                    var frameRect = new Rect(left, top, fw, fh);
                    var dv = new DrawingVisual();
                    using (var dc = dv.RenderOpen())
                    {
                        if (baseImage != null) dc.DrawImage(baseImage, full);
                        dc.DrawImage(frame, frameRect);
                    }
                    var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                    rtb.Render(dv);
                    rtb.Freeze();
                    result.Add(Tuple.Create<BitmapSource, TimeSpan>(rtb, TimeSpan.FromMilliseconds(delay * 10)));

                    if (disposal == 2)
                    {
                        // Restore to background: clear this frame's area for the next frame.
                        var clear = new DrawingVisual();
                        using (var dc = clear.RenderOpen())
                        {
                            dc.PushClip(new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(full), new RectangleGeometry(frameRect)));
                            dc.DrawImage(rtb, full);
                            dc.Pop();
                        }
                        var cleared = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                        cleared.Render(clear);
                        cleared.Freeze();
                        baseImage = cleared;
                    }
                    else if (disposal != 3)
                    {
                        baseImage = rtb; // 3 = restore previous: keep the old base
                    }
                }
            }
            return result;
        }

        private static double Query(BitmapMetadata meta, string query, double fallback = 0)
        {
            try
            {
                object v = meta?.GetQuery(query);
                return v == null ? fallback : Convert.ToDouble(v);
            }
            catch { return fallback; }
        }

        private void StartGif(List<Tuple<BitmapSource, TimeSpan>> frames)
        {
            var anim = new ObjectAnimationUsingKeyFrames();
            var t = TimeSpan.Zero;
            foreach (var f in frames)
            {
                anim.KeyFrames.Add(new DiscreteObjectKeyFrame(f.Item1, KeyTime.FromTimeSpan(t)));
                t += f.Item2;
            }
            anim.Duration = t;
            anim.RepeatBehavior = RepeatBehavior.Forever;
            _image.BeginAnimation(Image.SourceProperty, anim);
        }

        #endregion
    }
}
