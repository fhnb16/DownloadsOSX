using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using DownloadsOSX.Interop;

namespace DownloadsOSX.Models
{
    public enum FileKind { Other, Folder, Image, Video, Audio, Text }

    internal static class FileKinds
    {
        public static readonly HashSet<string> Image = Set(".png", ".jpg", ".jpeg", ".jpe", ".jfif", ".gif", ".bmp", ".dib", ".tif", ".tiff", ".ico", ".wdp", ".jxr", ".webp", ".heic", ".heif", ".avif", ".dds");
        public static readonly HashSet<string> Video = Set(".mp4", ".m4v", ".mov", ".wmv", ".avi", ".mkv", ".webm", ".mpg", ".mpeg", ".3gp", ".ts", ".m2ts", ".asf");
        public static readonly HashSet<string> Audio = Set(".mp3", ".wav", ".wma", ".m4a", ".aac", ".flac", ".ogg", ".opus", ".mid", ".midi", ".aif", ".aiff");
        public static readonly HashSet<string> Text = Set(
            ".txt", ".log", ".md", ".markdown", ".json", ".xml", ".csv", ".tsv", ".ini", ".cfg", ".conf", ".config", ".yml", ".yaml", ".toml",
            ".cs", ".vb", ".fs", ".js", ".mjs", ".ts", ".tsx", ".jsx", ".py", ".rb", ".php", ".java", ".kt", ".go", ".rs", ".c", ".h", ".cpp", ".hpp",
            ".css", ".scss", ".less", ".html", ".htm", ".svg", ".sql", ".sh", ".bat", ".cmd", ".ps1", ".psm1", ".reg", ".inf", ".nfo", ".srt", ".vtt",
            ".gitignore", ".editorconfig", ".csproj", ".sln", ".props", ".targets", ".lua", ".pl", ".r", ".swift", ".dart", ".gradle", ".properties");
        /// <summary>Browser/download-manager temp files.</summary>
        public static readonly HashSet<string> Partial = Set(".crdownload", ".part", ".partial", ".opdownload", ".download", ".tmp", ".!ut", ".!qb", ".bc!", ".dtapart");

        private static HashSet<string> Set(params string[] items) => new HashSet<string>(items, StringComparer.OrdinalIgnoreCase);

        public static FileKind Detect(string path, bool isFolder)
        {
            if (isFolder) return FileKind.Folder;
            string ext = Path.GetExtension(path);
            if (Image.Contains(ext)) return FileKind.Image;
            if (Video.Contains(ext)) return FileKind.Video;
            if (Audio.Contains(ext)) return FileKind.Audio;
            if (Text.Contains(ext)) return FileKind.Text;
            return FileKind.Other;
        }
    }

    /// <summary>Immutable scan result for one item.</summary>
    internal sealed class FileSnapshot
    {
        public string Path;
        public string Name;
        public bool IsFolder;
        public long Size;
        public DateTime Modified;
        public DateTime Created;
        public string SourceFolder;
        public bool IsPartial;
    }

    public sealed class FileEntry : INotifyPropertyChanged
    {
        internal FileEntry(FileSnapshot s)
        {
            Path = s.Path;
            Kind = FileKinds.Detect(s.Path, s.IsFolder);
            Update(s);
        }

        public string Path { get; }
        public FileKind Kind { get; }
        public bool IsFolder => Kind == FileKind.Folder;
        public string Extension => System.IO.Path.GetExtension(Path);

        private string _name;
        public string Name { get => _name; private set => Set(ref _name, value); }

        private long _size;
        public long Size { get => _size; private set => Set(ref _size, value); }

        private DateTime _modified;
        public DateTime Modified { get => _modified; private set => Set(ref _modified, value); }

        private DateTime _created;
        public DateTime Created { get => _created; private set => Set(ref _created, value); }

        private string _sourceFolder;
        public string SourceFolder { get => _sourceFolder; private set => Set(ref _sourceFolder, value); }

        private bool _isPartial;
        public bool IsPartial { get => _isPartial; private set => Set(ref _isPartial, value); }

        private ImageSource _thumbnail;
        public ImageSource Thumbnail { get => _thumbnail; set => Set(ref _thumbnail, value); }

        private string _details;
        public string Details { get => _details; private set => Set(ref _details, value); }

        private string _groupKey;
        public string GroupKey { get => _groupKey; private set => Set(ref _groupKey, value); }

        private bool _isNew;
        /// <summary>Arrived since the stack was last opened.</summary>
        public bool IsNew { get => _isNew; set => Set(ref _isNew, value); }

        private bool _isRenaming;
        public bool IsRenaming { get => _isRenaming; set => Set(ref _isRenaming, value); }

        private string _sourceLabel;
        /// <summary>Folder name shown when several folders are watched.</summary>
        public string SourceLabel { get => _sourceLabel; set => Set(ref _sourceLabel, value); }

        internal int ThumbnailVersion;

        /// <summary>Returns true if the content likely changed (thumbnail should be reloaded).</summary>
        internal bool Update(FileSnapshot s)
        {
            bool changed = s.Modified != _modified || s.Size != _size || s.IsPartial != _isPartial;
            Name = s.Name;
            Size = s.Size;
            Modified = s.Modified;
            Created = s.Created;
            SourceFolder = s.SourceFolder;
            IsPartial = s.IsPartial;
            RefreshText();
            return changed;
        }

        public DateTime SortTime(SortBy sort) => sort == SortBy.Created ? Created : Modified;

        internal void RefreshText()
        {
            var when = App.Settings.SortBy == SortBy.Created ? Created : Modified;
            string time = RelativeTime(when);
            Details = IsPartial ? App.Str("InProgress")
                : IsFolder ? time
                : Shell.FormatSize(Size) + "  ·  " + time;
            GroupKey = GroupFor(when);
        }

        private static string RelativeTime(DateTime t)
        {
            var now = DateTime.Now;
            var diff = now - t;
            if (diff.TotalSeconds < 60 && diff.TotalSeconds > -60) return App.Str("JustNow");
            if (diff.TotalMinutes < 60 && diff.TotalMinutes > 0) return string.Format(App.Str("MinutesAgo"), (int)diff.TotalMinutes);
            if (t.Date == now.Date) return string.Format(App.Str("TodayAt"), t.ToString("t"));
            if (t.Date == now.Date.AddDays(-1)) return string.Format(App.Str("YesterdayAt"), t.ToString("t"));
            return t.Year == now.Year ? t.ToString("d MMM, HH:mm") : t.ToString("d");
        }

        private static string GroupFor(DateTime t)
        {
            var today = DateTime.Today;
            if (t.Date >= today) return App.Str("GroupToday");
            if (t.Date == today.AddDays(-1)) return App.Str("GroupYesterday");
            if (t.Date > today.AddDays(-7)) return App.Str("GroupThisWeek");
            if (t.Date > today.AddDays(-31)) return App.Str("GroupThisMonth");
            return App.Str("GroupEarlier");
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void Set<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public override string ToString() => Name;
    }
}
