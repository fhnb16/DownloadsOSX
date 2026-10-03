using System;
using System.Runtime.InteropServices;
using DownloadsOSX.Interop;

namespace DownloadsOSX.Services
{
    internal enum ScreenEdge { Bottom, Top, Left, Right }

    /// <summary>Window placement math in physical pixels (per-monitor DPI aware).</summary>
    internal static class Placement
    {
        public sealed class MonitorContext
        {
            public NativeMethods.RECT Monitor;
            public NativeMethods.RECT Work;
            public ScreenEdge TaskbarEdge;
            public double Scale; // DPI / 96
        }

        public static NativeMethods.POINT Cursor()
        {
            NativeMethods.GetCursorPos(out var p);
            return p;
        }

        public static MonitorContext ForPoint(NativeMethods.POINT pt, double fallbackScale)
        {
            IntPtr mon = NativeMethods.MonitorFromPoint(pt, NativeMethods.MONITOR_DEFAULTTONEAREST);
            var mi = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf(typeof(NativeMethods.MONITORINFO)) };
            NativeMethods.GetMonitorInfo(mon, ref mi);

            var ctx = new MonitorContext { Monitor = mi.rcMonitor, Work = mi.rcWork, Scale = fallbackScale };
            try
            {
                if (OsInfo.Major > 6 || (OsInfo.Major == 6 && OsInfo.Minor >= 3))
                    if (NativeMethods.GetDpiForMonitor(mon, 0, out uint dx, out _) == 0) ctx.Scale = dx / 96.0;
            }
            catch { }

            if (mi.rcWork.Bottom < mi.rcMonitor.Bottom) ctx.TaskbarEdge = ScreenEdge.Bottom;
            else if (mi.rcWork.Top > mi.rcMonitor.Top) ctx.TaskbarEdge = ScreenEdge.Top;
            else if (mi.rcWork.Left > mi.rcMonitor.Left) ctx.TaskbarEdge = ScreenEdge.Left;
            else if (mi.rcWork.Right < mi.rcMonitor.Right) ctx.TaskbarEdge = ScreenEdge.Right;
            else ctx.TaskbarEdge = PrimaryTaskbarEdge(); // auto-hide or no taskbar on this monitor
            return ctx;
        }

        private static ScreenEdge PrimaryTaskbarEdge()
        {
            try
            {
                var abd = new NativeMethods.APPBARDATA { cbSize = Marshal.SizeOf(typeof(NativeMethods.APPBARDATA)) };
                if (NativeMethods.SHAppBarMessage(NativeMethods.ABM_GETTASKBARPOS, ref abd) != IntPtr.Zero)
                {
                    switch (abd.uEdge)
                    {
                        case NativeMethods.ABE_TOP: return ScreenEdge.Top;
                        case NativeMethods.ABE_LEFT: return ScreenEdge.Left;
                        case NativeMethods.ABE_RIGHT: return ScreenEdge.Right;
                    }
                }
            }
            catch { }
            return ScreenEdge.Bottom;
        }

        /// <summary>
        /// Stack position: hugging the taskbar under the cursor when the cursor is on the taskbar,
        /// otherwise just above (or below) the cursor. Always clamped to the work area.
        /// </summary>
        public static NativeMethods.RECT NearCursor(NativeMethods.POINT cur, int w, int h, MonitorContext ctx, out ScreenEdge anchor)
        {
            var work = ctx.Work;
            int m = (int)Math.Round((OsInfo.IsWin11 ? 12 : 8) * ctx.Scale);
            bool onTaskbar = cur.X < work.Left || cur.X >= work.Right || cur.Y < work.Top || cur.Y >= work.Bottom;
            int x, y;

            if (onTaskbar)
            {
                anchor = ctx.TaskbarEdge;
                switch (anchor)
                {
                    case ScreenEdge.Top: x = cur.X - w / 2; y = work.Top + m; break;
                    case ScreenEdge.Left: x = work.Left + m; y = cur.Y - h / 2; break;
                    case ScreenEdge.Right: x = work.Right - m - w; y = cur.Y - h / 2; break;
                    default: x = cur.X - w / 2; y = work.Bottom - m - h; break;
                }
            }
            else
            {
                x = cur.X - w / 2;
                y = cur.Y - h - m;
                anchor = ScreenEdge.Bottom;
                if (y < work.Top + m) { y = cur.Y + m; anchor = ScreenEdge.Top; }
            }

            x = Math.Max(work.Left + m, Math.Min(x, work.Right - m - w));
            y = Math.Max(work.Top + m, Math.Min(y, work.Bottom - m - h));
            return new NativeMethods.RECT(x, y, x + w, y + h);
        }

        public static NativeMethods.RECT Center(int w, int h, MonitorContext ctx)
        {
            var work = ctx.Work;
            int x = work.Left + (work.Width - w) / 2;
            int y = work.Top + (work.Height - h) / 2;
            return new NativeMethods.RECT(x, y, x + w, y + h);
        }

        public static NativeMethods.RECT Clamp(NativeMethods.RECT r, MonitorContext ctx)
        {
            var work = ctx.Work;
            int w = r.Width, h = r.Height;
            int x = Math.Max(work.Left, Math.Min(r.Left, work.Right - w));
            int y = Math.Max(work.Top, Math.Min(r.Top, work.Bottom - h));
            return new NativeMethods.RECT(x, y, x + w, y + h);
        }

        /// <summary>
        /// Converts a screen rect to the "workspace" coordinates WINDOWPLACEMENT uses for normal windows
        /// (relative to the monitor work area, which differs from screen coords when the taskbar is on the top/left).
        /// </summary>
        public static NativeMethods.RECT ToWorkspace(NativeMethods.RECT r, MonitorContext ctx)
        {
            int dx = ctx.Work.Left - ctx.Monitor.Left, dy = ctx.Work.Top - ctx.Monitor.Top;
            return new NativeMethods.RECT(r.Left - dx, r.Top - dy, r.Right - dx, r.Bottom - dy);
        }
    }
}
