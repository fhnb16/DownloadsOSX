using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Threading;
using DownloadsOSX.Interop;
using DownloadsOSX.Models;

namespace DownloadsOSX.Services
{
    /// <summary>
    /// Watches the Downloads folder (+ extra folders) and keeps <see cref="Items"/> in display order.
    /// File system events are debounced (300 ms); the scan runs off the UI thread and the result is diffed into
    /// the collection so existing items (and their thumbnails) are kept.
    /// </summary>
    internal sealed class DownloadsFeed : INotifyPropertyChanged, IDisposable
    {
        private readonly AppSettings _settings;
        private readonly StaWorker _sta;
        private readonly DispatcherTimer _debounce;
        private readonly DispatcherTimer _minuteTimer;
        private readonly List<FileSystemWatcher> _watchers = new List<FileSystemWatcher>();
        private HashSet<string> _known;
        private bool _scanning, _rescanPending;
        private int _thumbPx = 64;

        public ObservableCollection<FileEntry> Items { get; } = new ObservableCollection<FileEntry>();

        /// <summary>Raised on the UI thread with items that just arrived (not on the first scan).</summary>
        public event Action<IReadOnlyList<FileEntry>> NewItems;
        public event Action Refreshed;

        public DownloadsFeed(AppSettings settings, StaWorker sta)
        {
            _settings = settings;
            _sta = sta;
            _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _debounce.Tick += (s, e) => { _debounce.Stop(); Rescan(); };
            _minuteTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
            _minuteTimer.Tick += (s, e) =>
            {
                foreach (var it in Items) it.RefreshText();
                if (FolderMissing) Restart(); // folder (USB/network) may be back
            };
            _settings.PropertyChanged += OnSettingChanged;
        }

        private bool _newestLast = true;
        /// <summary>True when the newest item should be at the bottom (nearest a bottom taskbar).</summary>
        public bool NewestLast
        {
            get => _newestLast;
            set { if (_newestLast != value) { _newestLast = value; Rescan(); } }
        }

        private int _totalCount;
        public int TotalCount { get => _totalCount; private set => Set(ref _totalCount, value); }

        private bool _folderMissing;
        public bool FolderMissing { get => _folderMissing; private set => Set(ref _folderMissing, value); }

        public string PrimaryFolder => string.IsNullOrWhiteSpace(_settings.CustomFolder) || !Directory.Exists(_settings.CustomFolder)
            ? Shell.GetDownloadsFolder()
            : _settings.CustomFolder;

        public IReadOnlyList<string> Folders
        {
            get
            {
                var list = new List<string> { PrimaryFolder };
                foreach (var f in _settings.ExtraFolders)
                    if (!string.IsNullOrWhiteSpace(f) && !list.Contains(f, StringComparer.OrdinalIgnoreCase)) list.Add(f);
                return list;
            }
        }

        /// <summary>Thumbnail size in physical pixels; changing it reloads thumbnails.</summary>
        public int ThumbnailPixels
        {
            get => _thumbPx;
            set
            {
                if (_thumbPx == value) return;
                _thumbPx = value;
                foreach (var it in Items) LoadThumbnail(it);
            }
        }

        public void Start()
        {
            Restart();
            _minuteTimer.Start();
        }

        private void OnSettingChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(AppSettings.CustomFolder):
                case nameof(AppSettings.ExtraFolders):
                    Restart();
                    break;
                case nameof(AppSettings.ItemCount):
                case nameof(AppSettings.SortBy):
                case nameof(AppSettings.ShowFolders):
                case nameof(AppSettings.ShowHidden):
                case nameof(AppSettings.ShowPartial):
                    Rescan();
                    break;
                case nameof(AppSettings.Language):
                    foreach (var it in Items) it.RefreshText();
                    break;
            }
        }

        private void Restart()
        {
            foreach (var w in _watchers) w.Dispose();
            _watchers.Clear();
            _known = null;

            var folders = Folders;
            FolderMissing = !Directory.Exists(folders[0]);
            foreach (var folder in folders)
            {
                if (!Directory.Exists(folder)) continue;
                try
                {
                    var w = new FileSystemWatcher(folder)
                    {
                        IncludeSubdirectories = false,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                        InternalBufferSize = 64 * 1024,
                    };
                    w.Created += OnFsEvent;
                    w.Deleted += OnFsEvent;
                    w.Changed += OnFsEvent;
                    w.Renamed += OnFsEvent;
                    w.Error += (s, e) =>
                    {
                        Log.Warn("FileSystemWatcher error, restarting", e.GetException());
                        App.Current.Dispatcher.BeginInvoke(new Action(Restart));
                    };
                    w.EnableRaisingEvents = true;
                    _watchers.Add(w);
                }
                catch (Exception ex) { Log.Warn("Cannot watch " + folder, ex); }
            }
            Rescan();
        }

        private void OnFsEvent(object sender, FileSystemEventArgs e)
        {
            App.Current.Dispatcher.BeginInvoke(new Action(() => { _debounce.Stop(); _debounce.Start(); }));
        }

        /// <summary>Rescans now (coalesces if a scan is already running).</summary>
        public async void Rescan()
        {
            if (_scanning) { _rescanPending = true; return; }
            _scanning = true;
            try
            {
                do
                {
                    _rescanPending = false;
                    var opts = new ScanOptions
                    {
                        Folders = Folders.ToArray(),
                        Count = _settings.ItemCount,
                        Sort = _settings.SortBy,
                        ShowFolders = _settings.ShowFolders,
                        ShowHidden = _settings.ShowHidden,
                        ShowPartial = _settings.ShowPartial,
                    };
                    var result = await Task.Run(() => Scan(opts));
                    Apply(result, opts);
                } while (_rescanPending);
            }
            catch (Exception ex) { Log.Warn("Scan failed", ex); }
            finally { _scanning = false; }
        }

        private sealed class ScanOptions
        {
            public string[] Folders;
            public int Count;
            public SortBy Sort;
            public bool ShowFolders, ShowHidden, ShowPartial;
        }

        private sealed class ScanResult
        {
            public List<FileSnapshot> Top;
            public HashSet<string> All;
            public int Total;
            public bool PrimaryMissing;
        }

        private static ScanResult Scan(ScanOptions o)
        {
            var all = new List<FileSnapshot>();
            var allPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool primaryMissing = false;

            for (int fi = 0; fi < o.Folders.Length; fi++)
            {
                string folder = o.Folders[fi];
                var dir = new DirectoryInfo(folder);
                if (!dir.Exists) { if (fi == 0) primaryMissing = true; continue; }

                List<FileSystemInfo> infos;
                try { infos = dir.EnumerateFileSystemInfos().ToList(); }
                catch (Exception ex) { Log.Warn("Enumerate failed: " + folder, ex); continue; }

                var names = new HashSet<string>(infos.Select(i => i.Name), StringComparer.OrdinalIgnoreCase);
                foreach (var info in infos)
                {
                    try
                    {
                        var attrs = info.Attributes;
                        bool isDir = (attrs & FileAttributes.Directory) != 0;
                        if (!o.ShowHidden && (attrs & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                        if (string.Equals(info.Name, "desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                        if (isDir && !o.ShowFolders) continue;

                        long size = isDir ? 0 : ((FileInfo)info).Length;
                        bool partial = !isDir && (FileKinds.Partial.Contains(info.Extension)
                            // Firefox writes "name.ext.part" next to an empty "name.ext" placeholder.
                            || (size == 0 && names.Contains(info.Name + ".part")));
                        if (partial && !o.ShowPartial) continue;

                        allPaths.Add(info.FullName);
                        all.Add(new FileSnapshot
                        {
                            Path = info.FullName,
                            Name = info.Name,
                            IsFolder = isDir,
                            Size = size,
                            Modified = info.LastWriteTime,
                            Created = info.CreationTime,
                            SourceFolder = folder,
                            IsPartial = partial,
                        });
                    }
                    catch { /* vanished meanwhile */ }
                }
            }

            Func<FileSnapshot, DateTime> key = o.Sort == SortBy.Created ? (Func<FileSnapshot, DateTime>)(s => s.Created) : s => s.Modified;
            var top = all.OrderByDescending(key).Take(o.Count).ToList();
            if (o.Sort == SortBy.Name)
                top = top.OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

            return new ScanResult { Top = top, All = allPaths, Total = all.Count, PrimaryMissing = primaryMissing };
        }

        private void Apply(ScanResult r, ScanOptions o)
        {
            FolderMissing = r.PrimaryMissing;
            TotalCount = r.Total;

            var existing = Items.ToDictionary(i => i.Path, StringComparer.OrdinalIgnoreCase);
            var desired = new List<FileEntry>(r.Top.Count);
            var arrived = new List<FileEntry>();
            bool multi = o.Folders.Length > 1;

            foreach (var snap in r.Top)
            {
                if (existing.TryGetValue(snap.Path, out var entry))
                {
                    if (entry.Update(snap)) LoadThumbnail(entry);
                }
                else
                {
                    entry = new FileEntry(snap);
                    LoadThumbnail(entry);
                }
                entry.SourceLabel = multi ? Path.GetFileName(snap.SourceFolder.TrimEnd('\\')) : null;

                if (_known != null && !_known.Contains(snap.Path) && !snap.IsPartial && (DateTime.Now - snap.Modified).TotalMinutes < 10)
                    arrived.Add(entry);
                desired.Add(entry);
            }

            // Scan order is newest first; flip when newest should sit nearest a bottom taskbar.
            if (_newestLast && o.Sort != SortBy.Name) desired.Reverse(); // name sort is always A→Z

            // Diff into the collection: remove, then move/insert into place.
            var keep = new HashSet<FileEntry>(desired);
            for (int i = Items.Count - 1; i >= 0; i--)
                if (!keep.Contains(Items[i])) Items.RemoveAt(i);
            for (int i = 0; i < desired.Count; i++)
            {
                int cur = Items.IndexOf(desired[i]);
                if (cur == i) continue;
                if (cur >= 0) Items.Move(cur, i);
                else Items.Insert(i, desired[i]);
            }

            bool first = _known == null;
            _known = r.All;
            if (!first && arrived.Count > 0)
            {
                foreach (var a in arrived) a.IsNew = true;
                NewItems?.Invoke(arrived);
            }
            Refreshed?.Invoke();
        }

        public async void LoadThumbnail(FileEntry entry)
        {
            int version = ++entry.ThumbnailVersion;
            int px = _thumbPx;
            string path = entry.Path;
            try
            {
                var bmp = await _sta.Run(() => ShellThumbnail.Get(path, px));
                if (entry.ThumbnailVersion == version && bmp != null) entry.Thumbnail = bmp;
            }
            catch (TaskCanceledException) { }
            catch (Exception ex) { Log.Debug("Thumbnail: " + ex.Message); }
        }

        public void Dispose()
        {
            _debounce.Stop();
            _minuteTimer.Stop();
            foreach (var w in _watchers) w.Dispose();
            _watchers.Clear();
            _settings.PropertyChanged -= OnSettingChanged;
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
