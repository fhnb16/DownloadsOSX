using System;
using System.Threading;
using DownloadsOSX.Interop;

namespace DownloadsOSX.Services
{
    internal enum InstanceCommand { Show = 1, Settings = 2, CheckUpdates = 3, OpenFolder = 4, Exit = 5 }

    /// <summary>
    /// One instance per user session. A second launch broadcasts a registered window message with a command
    /// (from the Jump List or the pinned icon) to the first instance and exits.
    /// </summary>
    internal static class SingleInstance
    {
        private const string MutexName = @"Local\DownloadsOSX.SingleInstance.7F3B2C1E";
        private static Mutex _mutex;

        public static readonly uint Message = NativeMethods.RegisterWindowMessage("DownloadsOSX.Command.7F3B2C1E");

        /// <param name="waitMs">How long to wait for a previous instance to exit (used right after an update).</param>
        public static bool TryAcquire(int waitMs = 0)
        {
            _mutex = new Mutex(false, MutexName);
            try
            {
                return _mutex.WaitOne(waitMs);
            }
            catch (AbandonedMutexException)
            {
                return true; // previous instance crashed
            }
        }

        public static void Release()
        {
            try { _mutex?.ReleaseMutex(); } catch { }
            _mutex?.Dispose();
            _mutex = null;
        }

        public static void Signal(InstanceCommand command)
        {
            NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);
            NativeMethods.PostMessage(NativeMethods.HWND_BROADCAST, Message, new IntPtr((int)command), IntPtr.Zero);
        }
    }
}
