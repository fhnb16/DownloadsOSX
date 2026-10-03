using System;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DownloadsOSX.Interop
{
    [Flags]
    internal enum ThumbnailOptions
    {
        None = 0x00,
        BiggerSizeOk = 0x01,
        InMemoryOnly = 0x02,
        IconOnly = 0x04,
        ThumbnailOnly = 0x08,
        InCacheOnly = 0x10,
        ScaleUp = 0x100,
    }

    /// <summary>
    /// Shell thumbnails/icons through IShellItemImageFactory (Vista+), converted straight to a frozen
    /// BitmapSource with correct alpha. Must be called on an STA thread.
    /// </summary>
    internal static class ShellThumbnail
    {
        private static readonly Guid IID_IShellItem = new Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe");

        [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemImageFactory
        {
            [PreserveSig]
            int GetImage([In] SIZE size, [In] ThumbnailOptions flags, out IntPtr phbm);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE { public int cx, cy; }

        public static BitmapSource Get(string path, int sizePx, ThumbnailOptions options = ThumbnailOptions.BiggerSizeOk)
        {
            object item = null;
            IntPtr hbmp = IntPtr.Zero;
            try
            {
                NativeMethods.SHCreateItemFromParsingName(path, IntPtr.Zero, IID_IShellItem, out item);
                var factory = (IShellItemImageFactory)item;
                int hr = factory.GetImage(new SIZE { cx = sizePx, cy = sizePx }, options, out hbmp);
                if (hr != 0 || hbmp == IntPtr.Zero)
                {
                    if ((options & ThumbnailOptions.IconOnly) == 0)
                        hr = factory.GetImage(new SIZE { cx = sizePx, cy = sizePx }, ThumbnailOptions.IconOnly, out hbmp);
                    if (hr != 0 || hbmp == IntPtr.Zero) return null;
                }
                return FromHBitmap(hbmp);
            }
            catch (Exception ex)
            {
                Log.Debug("Thumbnail failed for " + path + ": " + ex.Message);
                return null;
            }
            finally
            {
                if (hbmp != IntPtr.Zero) NativeMethods.DeleteObject(hbmp);
                if (item != null) Marshal.FinalReleaseComObject(item);
            }
        }

        /// <summary>
        /// Reads the 32bpp DIB top-down. Shell thumbnails are premultiplied BGRA; images without any alpha
        /// are treated as opaque BGR32 (Imaging.CreateBitmapSourceFromHBitmap drops alpha, hence this).
        /// </summary>
        private static BitmapSource FromHBitmap(IntPtr hbmp)
        {
            if (NativeMethods.GetObject(hbmp, Marshal.SizeOf(typeof(NativeMethods.BITMAP)), out var bm) == 0) return null;
            int w = bm.bmWidth, h = Math.Abs(bm.bmHeight);
            if (w <= 0 || h <= 0) return null;

            var bih = new NativeMethods.BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf(typeof(NativeMethods.BITMAPINFOHEADER)),
                biWidth = w,
                biHeight = -h, // top-down
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0, // BI_RGB
            };
            int stride = w * 4;
            var pixels = new byte[stride * h];
            IntPtr hdc = NativeMethods.GetDC(IntPtr.Zero);
            try
            {
                if (NativeMethods.GetDIBits(hdc, hbmp, 0, (uint)h, pixels, ref bih, 0) == 0) return null;
            }
            finally { NativeMethods.ReleaseDC(IntPtr.Zero, hdc); }

            bool hasAlpha = false;
            for (int i = 3; i < pixels.Length; i += 4)
            {
                if (pixels[i] != 0) { hasAlpha = true; break; }
            }

            var bmp = BitmapSource.Create(w, h, 96, 96, hasAlpha ? PixelFormats.Pbgra32 : PixelFormats.Bgr32, null, pixels, stride);
            bmp.Freeze();
            return bmp;
        }
    }
}
