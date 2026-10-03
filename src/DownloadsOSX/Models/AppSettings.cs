using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

namespace DownloadsOSX.Models
{
    public enum BackdropKind { Auto = 0, Mica = 1, MicaAlt = 2, Acrylic = 3, Blur = 4, Solid = 5 }
    public enum ThemeMode { System = 0, Light = 1, Dark = 2 }
    public enum TintMode { Auto = 0, Custom = 1 }
    public enum CornerStyle { Default = 0, Round = 1, Small = 2, Square = 3 }
    public enum ViewMode { List = 0, Grid = 1 }
    public enum ItemSize { Compact = 0, Normal = 1, Large = 2 }
    public enum FontChoice { System = 0, Ubuntu = 1 }
    public enum SortBy { Modified = 0, Created = 1, Name = 2 }
    public enum PositionMode { NearCursor = 0, Center = 1, Remember = 2 }
    public enum PresenceMode { Taskbar = 0, Tray = 1, Both = 2 }
    public enum UpdateMode { Notify = 0, Auto = 1, Off = 2 }
    public enum LanguageChoice { Auto = 0, English = 1, Russian = 2, Ukrainian = 3 }

    /// <summary>
    /// All user settings. Bound two-way from the settings window; every change raises PropertyChanged,
    /// which the store persists (debounced) and services react to live.
    /// </summary>
    [DataContract]
    public sealed class AppSettings : INotifyPropertyChanged
    {
        public AppSettings() { SetDefaults(); }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext _) { SetDefaults(); }

        private void SetDefaults()
        {
            _backdrop = BackdropKind.Auto;
            _theme = ThemeMode.System;
            _tintMode = TintMode.Auto;
            _tintColor = "#202020";
            _tintOpacity = 40;
            _corners = CornerStyle.Round;
            _roundOldWindows = false;
            _viewMode = ViewMode.List;
            _groupByDate = false;
            _itemSize = ItemSize.Normal;
            _font = FontChoice.System;
            _fontSize = 13;
            _showDetails = true;
            _animations = true;
            _newestNearTaskbar = true;

            _itemCount = 10;
            _sortBy = SortBy.Modified;
            _showFolders = true;
            _showHidden = false;
            _showPartial = false;
            _customFolder = "";
            _extraFolders = new List<string>();
            _position = PositionMode.NearCursor;
            _hideOnDeactivate = true;
            _singleClickOpen = false;
            _confirmDelete = false;
            _flashOnNew = true;
            _badgeOnNew = true;
            _soundOnNew = false;
            _balloonOnNew = false;
            _jumpList = true;
            _jumpListCount = 8;
            _startMinimized = true;
            _language = LanguageChoice.Auto;
            _hotkeyEnabled = true;
            _hotkey = "Ctrl+Alt+D";
            _presence = PresenceMode.Taskbar;

            _previewMedia = true;
            _previewText = true;
            _previewHandlers = true;
            _autoplayMedia = true;
            _loopMedia = false;
            _keepZoom = false;
            _volume = 0.7;

            _updateMode = UpdateMode.Notify;
            _skippedVersion = "";
            _lastUpdateCheckUtc = DateTime.MinValue;
            _updateManifestUrls = new List<string>();

            _rememberedLeft = null;
            _rememberedTop = null;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void Set<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        /// <summary>Raise a change for list properties that were mutated in place.</summary>
        public void Touch(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        // ---- Appearance ----
        private BackdropKind _backdrop;
        [DataMember] public BackdropKind Backdrop { get => _backdrop; set => Set(ref _backdrop, value); }

        private ThemeMode _theme;
        [DataMember] public ThemeMode Theme { get => _theme; set => Set(ref _theme, value); }

        private TintMode _tintMode;
        [DataMember] public TintMode TintMode { get => _tintMode; set => Set(ref _tintMode, value); }

        private string _tintColor;
        [DataMember] public string TintColor { get => _tintColor; set => Set(ref _tintColor, value); }

        private int _tintOpacity;
        /// <summary>0..100, used when TintMode is Custom.</summary>
        [DataMember] public int TintOpacity { get => _tintOpacity; set => Set(ref _tintOpacity, Math.Max(0, Math.Min(100, value))); }

        private CornerStyle _corners;
        [DataMember] public CornerStyle Corners { get => _corners; set => Set(ref _corners, value); }

        private bool _roundOldWindows;
        [DataMember] public bool RoundOldWindows { get => _roundOldWindows; set => Set(ref _roundOldWindows, value); }

        private ViewMode _viewMode;
        [DataMember] public ViewMode ViewMode { get => _viewMode; set => Set(ref _viewMode, value); }

        private bool _groupByDate;
        [DataMember] public bool GroupByDate { get => _groupByDate; set => Set(ref _groupByDate, value); }

        private ItemSize _itemSize;
        [DataMember] public ItemSize ItemSize { get => _itemSize; set => Set(ref _itemSize, value); }

        private FontChoice _font;
        [DataMember] public FontChoice Font { get => _font; set => Set(ref _font, value); }

        private double _fontSize;
        [DataMember] public double FontSize { get => _fontSize; set => Set(ref _fontSize, Math.Max(10, Math.Min(20, value))); }

        private bool _showDetails;
        [DataMember] public bool ShowDetails { get => _showDetails; set => Set(ref _showDetails, value); }

        private bool _animations;
        [DataMember] public bool Animations { get => _animations; set => Set(ref _animations, value); }

        private bool _newestNearTaskbar;
        [DataMember] public bool NewestNearTaskbar { get => _newestNearTaskbar; set => Set(ref _newestNearTaskbar, value); }

        // ---- Behavior ----
        private int _itemCount;
        [DataMember] public int ItemCount { get => _itemCount; set => Set(ref _itemCount, Math.Max(3, Math.Min(100, value))); }

        private SortBy _sortBy;
        [DataMember] public SortBy SortBy { get => _sortBy; set => Set(ref _sortBy, value); }

        private bool _showFolders;
        [DataMember] public bool ShowFolders { get => _showFolders; set => Set(ref _showFolders, value); }

        private bool _showHidden;
        [DataMember] public bool ShowHidden { get => _showHidden; set => Set(ref _showHidden, value); }

        private bool _showPartial;
        [DataMember] public bool ShowPartial { get => _showPartial; set => Set(ref _showPartial, value); }

        private string _customFolder;
        [DataMember] public string CustomFolder { get => _customFolder; set => Set(ref _customFolder, value ?? ""); }

        private List<string> _extraFolders;
        [DataMember] public List<string> ExtraFolders { get => _extraFolders; set => Set(ref _extraFolders, value ?? new List<string>()); }

        private PositionMode _position;
        [DataMember] public PositionMode Position { get => _position; set => Set(ref _position, value); }

        private double? _rememberedLeft;
        [DataMember] public double? RememberedLeft { get => _rememberedLeft; set => Set(ref _rememberedLeft, value); }

        private double? _rememberedTop;
        [DataMember] public double? RememberedTop { get => _rememberedTop; set => Set(ref _rememberedTop, value); }

        private bool _hideOnDeactivate;
        [DataMember] public bool HideOnDeactivate { get => _hideOnDeactivate; set => Set(ref _hideOnDeactivate, value); }

        private bool _singleClickOpen;
        [DataMember] public bool SingleClickOpen { get => _singleClickOpen; set => Set(ref _singleClickOpen, value); }

        private bool _confirmDelete;
        [DataMember] public bool ConfirmDelete { get => _confirmDelete; set => Set(ref _confirmDelete, value); }

        private bool _flashOnNew;
        [DataMember] public bool FlashOnNew { get => _flashOnNew; set => Set(ref _flashOnNew, value); }

        private bool _badgeOnNew;
        [DataMember] public bool BadgeOnNew { get => _badgeOnNew; set => Set(ref _badgeOnNew, value); }

        private bool _soundOnNew;
        [DataMember] public bool SoundOnNew { get => _soundOnNew; set => Set(ref _soundOnNew, value); }

        private bool _balloonOnNew;
        [DataMember] public bool BalloonOnNew { get => _balloonOnNew; set => Set(ref _balloonOnNew, value); }

        private bool _jumpList;
        [DataMember] public bool JumpList { get => _jumpList; set => Set(ref _jumpList, value); }

        private int _jumpListCount;
        [DataMember] public int JumpListCount { get => _jumpListCount; set => Set(ref _jumpListCount, Math.Max(1, Math.Min(20, value))); }

        private bool _startMinimized;
        [DataMember] public bool StartMinimized { get => _startMinimized; set => Set(ref _startMinimized, value); }

        private LanguageChoice _language;
        [DataMember] public LanguageChoice Language { get => _language; set => Set(ref _language, value); }

        private bool _hotkeyEnabled;
        [DataMember] public bool HotkeyEnabled { get => _hotkeyEnabled; set => Set(ref _hotkeyEnabled, value); }

        private string _hotkey;
        [DataMember] public string Hotkey { get => _hotkey; set => Set(ref _hotkey, value ?? ""); }

        private PresenceMode _presence;
        [DataMember] public PresenceMode Presence { get => _presence; set => Set(ref _presence, value); }

        // ---- Preview ----
        private bool _previewMedia;
        [DataMember] public bool PreviewMedia { get => _previewMedia; set => Set(ref _previewMedia, value); }

        private bool _previewText;
        [DataMember] public bool PreviewText { get => _previewText; set => Set(ref _previewText, value); }

        private bool _previewHandlers;
        [DataMember] public bool PreviewHandlers { get => _previewHandlers; set => Set(ref _previewHandlers, value); }

        private bool _autoplayMedia;
        [DataMember] public bool AutoplayMedia { get => _autoplayMedia; set => Set(ref _autoplayMedia, value); }

        private bool _loopMedia;
        [DataMember] public bool LoopMedia { get => _loopMedia; set => Set(ref _loopMedia, value); }

        private bool _keepZoom;
        [DataMember] public bool KeepZoom { get => _keepZoom; set => Set(ref _keepZoom, value); }

        private double _volume;
        [DataMember] public double Volume { get => _volume; set => Set(ref _volume, Math.Max(0, Math.Min(1, value))); }

        // ---- Updates ----
        private UpdateMode _updateMode;
        [DataMember] public UpdateMode UpdateMode { get => _updateMode; set => Set(ref _updateMode, value); }

        private string _skippedVersion;
        [DataMember] public string SkippedVersion { get => _skippedVersion; set => Set(ref _skippedVersion, value ?? ""); }

        private DateTime _lastUpdateCheckUtc;
        [DataMember] public DateTime LastUpdateCheckUtc { get => _lastUpdateCheckUtc; set => Set(ref _lastUpdateCheckUtc, value); }

        private List<string> _updateManifestUrls;
        /// <summary>Hidden override for testing the updater against another manifest.</summary>
        [DataMember] public List<string> UpdateManifestUrls { get => _updateManifestUrls; set => Set(ref _updateManifestUrls, value ?? new List<string>()); }
    }
}
