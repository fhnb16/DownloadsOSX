using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Cache;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using DownloadsOSX.Interop;
using DownloadsOSX.Models;

namespace DownloadsOSX.Services
{
    /// <summary>update.json — identical on GitHub Releases and on the own server.</summary>
    [DataContract]
    public sealed class UpdateManifest
    {
        [DataMember(Name = "version")] public string Version { get; set; }
        [DataMember(Name = "url")] public string Url { get; set; }
        [DataMember(Name = "sha256")] public string Sha256 { get; set; }
        [DataMember(Name = "notes", IsRequired = false)] public string Notes { get; set; }
        [DataMember(Name = "minOsBuild", IsRequired = false)] public int MinOsBuild { get; set; }
        [DataMember(Name = "published", IsRequired = false)] public string Published { get; set; }

        public System.Version ParsedVersion => UpdateService.ParseVersion(Version);
    }

    public enum UpdateStatus { UpToDate, Available, Failed }

    public sealed class UpdateCheckResult
    {
        public UpdateStatus Status;
        public UpdateManifest Manifest;
        public string Source;
        public string Error;
    }

    /// <summary>
    /// Self-updater. Checks a list of manifest URLs in order (GitHub Releases first, own server as fallback),
    /// downloads the new exe, verifies SHA-256 and swaps it in place (renaming a running exe is allowed).
    /// If the exe folder isn't writable, an elevated copy of the new exe installs itself.
    /// </summary>
    internal sealed class UpdateService : INotifyPropertyChanged
    {
        /// <summary>GitHub owner/repo that publishes releases with an update.json asset.</summary>
        public const string GitHubRepo = "fhnb16/DownloadsOSX";
        public const string ServerManifest = "https://root.fhnb.ru/DownloadsOSX/update.json";
        public static string GitHubManifest => $"https://github.com/{GitHubRepo}/releases/latest/download/update.json";
        public static string ProjectPage => $"https://github.com/{GitHubRepo}";

        private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);
        private readonly AppSettings _settings;
        private DispatcherTimer _timer;

        public UpdateService(AppSettings settings) { _settings = settings; }

        public static Version CurrentVersion
        {
            get
            {
                var v = Assembly.GetEntryAssembly().GetName().Version;
                return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
            }
        }

        public static string CurrentVersionText
        {
            get
            {
                var info = Assembly.GetEntryAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                if (!string.IsNullOrEmpty(info)) return info.Split('+')[0];
                return CurrentVersion.ToString(3);
            }
        }

        private UpdateManifest _available;
        public UpdateManifest Available { get => _available; private set => Set(ref _available, value); }

        private string _pendingPath;
        /// <summary>Verified download ready to install (auto mode).</summary>
        public string PendingPath { get => _pendingPath; private set => Set(ref _pendingPath, value); }

        private bool _busy;
        public bool IsBusy { get => _busy; private set => Set(ref _busy, value); }

        public event Action<UpdateManifest> UpdateFound;

        public IReadOnlyList<string> Sources =>
            _settings.UpdateManifestUrls != null && _settings.UpdateManifestUrls.Count > 0
                ? _settings.UpdateManifestUrls
                : new[] { GitHubManifest, ServerManifest };

        public void StartSchedule()
        {
            var first = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            first.Tick += (s, e) => { first.Stop(); CheckIfDue(); };
            first.Start();
            _timer = new DispatcherTimer { Interval = TimeSpan.FromHours(1) };
            _timer.Tick += (s, e) => CheckIfDue();
            _timer.Start();
        }

        private async void CheckIfDue()
        {
            if (_settings.UpdateMode == UpdateMode.Off || IsBusy) return;
            if (DateTime.UtcNow - _settings.LastUpdateCheckUtc < CheckInterval) return;
            var result = await CheckAsync(false);
            if (result.Status == UpdateStatus.Available && _settings.UpdateMode == UpdateMode.Auto && CanInstallInPlace)
            {
                try { PendingPath = await DownloadAsync(result.Manifest, null, CancellationToken.None); }
                catch (Exception ex) { Log.Warn("Background update download failed", ex); }
            }
        }

        public async Task<UpdateCheckResult> CheckAsync(bool manual)
        {
            IsBusy = true;
            try
            {
                var errors = new List<string>();
                foreach (var url in Sources)
                {
                    try
                    {
                        var manifest = await Task.Run(() => FetchManifest(url));
                        _settings.LastUpdateCheckUtc = DateTime.UtcNow;
                        Log.Info($"Update check via {url}: latest {manifest.Version}, current {CurrentVersion}");

                        var latest = manifest.ParsedVersion;
                        bool newer = latest != null && latest > CurrentVersion;
                        bool osOk = manifest.MinOsBuild <= 0 || OsInfo.Build >= manifest.MinOsBuild;
                        bool skipped = !manual && string.Equals(_settings.SkippedVersion, manifest.Version, StringComparison.OrdinalIgnoreCase);

                        if (newer && osOk && !skipped)
                        {
                            Available = manifest;
                            UpdateFound?.Invoke(manifest);
                            return new UpdateCheckResult { Status = UpdateStatus.Available, Manifest = manifest, Source = url };
                        }
                        Available = null;
                        return new UpdateCheckResult { Status = UpdateStatus.UpToDate, Manifest = manifest, Source = url };
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("Update source failed: " + url, ex);
                        errors.Add(ex.Message);
                    }
                }
                return new UpdateCheckResult { Status = UpdateStatus.Failed, Error = string.Join("; ", errors) };
            }
            finally { IsBusy = false; }
        }

        public void Skip(UpdateManifest m)
        {
            _settings.SkippedVersion = m.Version;
            Available = null;
            PendingPath = null;
        }

        private static UpdateManifest FetchManifest(string url)
        {
            using (var response = Get(url, 15000))
            using (var stream = response.GetResponseStream())
            {
                var m = (UpdateManifest)new DataContractJsonSerializer(typeof(UpdateManifest)).ReadObject(stream);
                if (m == null || ParseVersion(m.Version) == null || string.IsNullOrWhiteSpace(m.Url) || string.IsNullOrWhiteSpace(m.Sha256))
                    throw new InvalidDataException("Manifest is incomplete");
                return m;
            }
        }

        public static string ExePath => Assembly.GetEntryAssembly().Location;

        /// <summary>True if the exe folder is writable (no elevation needed).</summary>
        public static bool CanInstallInPlace
        {
            get
            {
                string probe = ExePath + ".write-test";
                try
                {
                    using (File.Create(probe, 1, FileOptions.DeleteOnClose)) { }
                    return true;
                }
                catch { return false; }
            }
        }

        private static string TempDir => Path.Combine(Path.GetTempPath(), "DownloadsOSX-update");

        /// <summary>Downloads and verifies the update. Returns the path of the verified exe.</summary>
        public async Task<string> DownloadAsync(UpdateManifest m, IProgress<double> progress, CancellationToken ct)
        {
            IsBusy = true;
            try
            {
                string target = CanInstallInPlace ? ExePath + ".new" : Path.Combine(TempDir, "Downloads.exe");
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                await Task.Run(() => Download(m, target, progress, ct), ct);
                Log.Info($"Update {m.Version} downloaded and verified: {target}");
                return target;
            }
            finally { IsBusy = false; }
        }

        private static void Download(UpdateManifest m, string target, IProgress<double> progress, CancellationToken ct)
        {
            using (var sha = SHA256.Create())
            {
                try
                {
                    using (var response = Get(m.Url, 30000))
                    using (var input = response.GetResponseStream())
                    using (var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        long total = response.ContentLength;
                        long done = 0;
                        var buffer = new byte[81920];
                        int read;
                        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            ct.ThrowIfCancellationRequested();
                            output.Write(buffer, 0, read);
                            sha.TransformBlock(buffer, 0, read, null, 0);
                            done += read;
                            if (total > 0) progress?.Report((double)done / total);
                        }
                        sha.TransformFinalBlock(buffer, 0, 0);
                    }

                    string actual = BitConverter.ToString(sha.Hash).Replace("-", "");
                    if (!string.Equals(actual, m.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException(App.Str("UpdateHashMismatch"));
                }
                catch
                {
                    try { File.Delete(target); } catch { }
                    throw;
                }
            }
        }

        /// <summary>Installs a verified download and starts the new version. The caller must shut the app down after.</summary>
        public static void ApplyAndRestart(string downloaded, bool startMinimized)
        {
            string exe = ExePath;
            string args = "--updated " + CurrentVersion.ToString(3) + (startMinimized ? " --minimized" : "");

            if (downloaded.StartsWith(exe, StringComparison.OrdinalIgnoreCase))
            {
                string old = exe + ".old";
                try { if (File.Exists(old)) File.Delete(old); }
                catch { old = exe + ".old" + DateTime.Now.Ticks; }
                File.Move(exe, old); // allowed while running
                try { File.Move(downloaded, exe); }
                catch { File.Move(old, exe); throw; }
                Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe) });
            }
            else
            {
                // Exe folder not writable (e.g. Program Files): let the new exe install itself elevated.
                Process.Start(new ProcessStartInfo(downloaded,
                    $"--install-update \"{exe}\" {Process.GetCurrentProcess().Id}" + (startMinimized ? " --minimized" : ""))
                {
                    UseShellExecute = true,
                    Verb = "runas",
                });
            }
        }

        /// <summary>
        /// Handles "--install-update target pid" (running elevated from %TEMP%): waits for the old process,
        /// copies itself over the target and relaunches it un-elevated via Explorer. Returns true if handled.
        /// </summary>
        public static bool HandleInstallCommand(string[] args)
        {
            int i = Array.IndexOf(args, "--install-update");
            if (i < 0 || i + 2 >= args.Length) return false;
            string target = args[i + 1];
            int.TryParse(args[i + 2], out int pid);
            try
            {
                try { Process.GetProcessById(pid).WaitForExit(20000); } catch { }
                Exception last = null;
                for (int attempt = 0; attempt < 20; attempt++)
                {
                    try
                    {
                        File.Copy(ExePath, target, true);
                        last = null;
                        break;
                    }
                    catch (Exception ex) { last = ex; Thread.Sleep(500); }
                }
                if (last != null) throw last;
                // Explorer starts the app with the user's normal (non-elevated) token.
                Process.Start("explorer.exe", "\"" + target + "\"");
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Update failed: " + ex.Message, "Downloads", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
            return true;
        }

        /// <summary>Removes leftovers of a previous update.</summary>
        public static void Cleanup()
        {
            try
            {
                string exe = ExePath;
                string dir = Path.GetDirectoryName(exe);
                foreach (var f in Directory.GetFiles(dir, Path.GetFileName(exe) + ".old*").Concat(Directory.GetFiles(dir, Path.GetFileName(exe) + ".new")))
                {
                    try { File.Delete(f); } catch { }
                }
                if (Directory.Exists(TempDir) && !ExePath.StartsWith(TempDir, StringComparison.OrdinalIgnoreCase))
                    Directory.Delete(TempDir, true);
            }
            catch { }
        }

        private static HttpWebResponse Get(string url, int timeoutMs)
        {
            var uri = new Uri(url);
            EnsureAllowed(uri);
            var req = (HttpWebRequest)WebRequest.Create(uri);
            req.UserAgent = "DownloadsOSX/" + CurrentVersion;
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;
            req.AllowAutoRedirect = true;
            req.CachePolicy = new RequestCachePolicy(RequestCacheLevel.NoCacheNoStore);
            req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            var response = (HttpWebResponse)req.GetResponse();
            try { EnsureAllowed(response.ResponseUri); }
            catch { response.Dispose(); throw; }
            return response;
        }

        /// <summary>HTTPS only; plain HTTP is accepted for loopback test servers.</summary>
        private static void EnsureAllowed(Uri uri)
        {
            if (uri.Scheme == Uri.UriSchemeHttps) return;
            if (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback) return;
            throw new InvalidOperationException("Insecure update URL refused: " + uri);
        }

        public static Version ParseVersion(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            s = s.Trim().TrimStart('v', 'V');
            int cut = s.IndexOfAny(new[] { '-', '+', ' ' });
            if (cut >= 0) s = s.Substring(0, cut);
            if (!System.Version.TryParse(s.Contains('.') ? s : s + ".0", out var v)) return null;
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void Set<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
