using System;
using System.IO;
using System.Text;

namespace DownloadsOSX
{
    /// <summary>Tiny append-only log in the settings folder (log.txt, rotated at 512 KB).</summary>
    internal static class Log
    {
        private static readonly object Gate = new object();
        private static string _path;

        public static string FilePath => _path;

        public static void Init(string folder)
        {
            try
            {
                Directory.CreateDirectory(folder);
                _path = Path.Combine(folder, "log.txt");
                var fi = new FileInfo(_path);
                if (fi.Exists && fi.Length > 512 * 1024)
                {
                    File.Copy(_path, _path + ".1", true);
                    File.Delete(_path);
                }
            }
            catch { _path = null; }
        }

        public static void Info(string message) => Write("INFO", message, null);
        public static void Warn(string message, Exception ex = null) => Write("WARN", message, ex);
        public static void Error(string message, Exception ex = null) => Write("ERROR", message, ex);

        [System.Diagnostics.Conditional("DEBUG")]
        public static void Debug(string message) => Write("DEBUG", message, null);

        private static void Write(string level, string message, Exception ex)
        {
            var sb = new StringBuilder();
            sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")).Append(' ').Append(level).Append(' ').Append(message);
            if (ex != null) sb.Append(" | ").Append(ex.GetType().Name).Append(": ").Append(ex.Message);
            System.Diagnostics.Debug.WriteLine(sb.ToString());
            if (_path == null) return;
            lock (Gate)
            {
                try { File.AppendAllText(_path, sb.AppendLine().ToString(), Encoding.UTF8); } catch { }
            }
        }
    }
}
