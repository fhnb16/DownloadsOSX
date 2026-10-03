using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Windows.Threading;
using DownloadsOSX.Models;

namespace DownloadsOSX.Services
{
    /// <summary>
    /// Loads/saves settings.json. Location: %AppData%\DownloadsOSX, or next to the exe when a
    /// "portable.flag" file exists there.
    /// </summary>
    internal sealed class SettingsStore
    {
        private readonly DispatcherTimer _saveTimer;
        public AppSettings Settings { get; }
        public string Folder { get; }
        public string FilePath => Path.Combine(Folder, "settings.json");

        public static string ResolveFolder()
        {
            string exeDir = AppContext.BaseDirectory;
            if (File.Exists(Path.Combine(exeDir, "portable.flag"))) return exeDir;
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DownloadsOSX");
        }

        public SettingsStore(string folder)
        {
            Folder = folder;
            Settings = Load(FilePath) ?? new AppSettings();
            Settings.PropertyChanged += (s, e) => { _saveTimer.Stop(); _saveTimer.Start(); };
            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            _saveTimer.Tick += (s, e) => { _saveTimer.Stop(); Save(); };
        }

        private static AppSettings Load(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                using (var fs = File.OpenRead(path))
                    return (AppSettings)new DataContractJsonSerializer(typeof(AppSettings), Serializer()).ReadObject(fs);
            }
            catch (Exception ex)
            {
                Log.Warn("Settings could not be read, using defaults", ex);
                try { File.Copy(path, path + ".bad", true); } catch { }
                return null;
            }
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                string tmp = FilePath + ".tmp";
                using (var ms = new MemoryStream())
                {
                    using (var writer = JsonReaderWriterFactory.CreateJsonWriter(ms, Encoding.UTF8, false, true, "  "))
                    {
                        new DataContractJsonSerializer(typeof(AppSettings), Serializer()).WriteObject(writer, Settings);
                    }
                    File.WriteAllBytes(tmp, ms.ToArray());
                }
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
                else File.Move(tmp, FilePath);
            }
            catch (Exception ex) { Log.Warn("Settings save failed", ex); }
        }

        public void Flush()
        {
            if (_saveTimer.IsEnabled) { _saveTimer.Stop(); Save(); }
        }

        private static DataContractJsonSerializerSettings Serializer() => new DataContractJsonSerializerSettings
        {
            DateTimeFormat = new System.Runtime.Serialization.DateTimeFormat("o"),
            UseSimpleDictionaryFormat = true,
        };
    }
}
