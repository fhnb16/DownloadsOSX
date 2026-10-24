# DownloadsOSX

A macOS-style **Downloads stack** for the Windows taskbar. Pin it, click it, and your newest downloads pop up right above the icon — with real Explorer thumbnails, the native right-click menu, drag & drop, and instant Quick Look-style previews.

Runs on **Windows 7 SP1 through Windows 11** (.NET Framework 4.8), as a single portable `Downloads.exe` with no dependencies.

<!-- Screenshot: docs/screenshot.png -->

---

## Features

**The stack**
- Newest files from your Downloads folder (the real one — moved/redirected folders are detected), newest item closest to the taskbar
- List or grid view, three item sizes, optional grouping by date (Today / Yesterday / This week …)
- Explorer thumbnails and icons, unfinished downloads (`.crdownload`, `.part`, …) hidden or shown dimmed
- Watch extra folders (Screenshots, Desktop, …) merged into the same stack
- Live updates when files appear, change or disappear (debounced, no flicker)

**Explorer integration**
- Native Explorer context menu (Open with, Send to, 7-Zip, …) plus *Preview*, *Show in folder*, *Copy path*, *Copy image*, *Unblock*
- Drag files out to Explorer, browsers, chats, editors
- Delete to Recycle Bin, inline rename, copy/cut to paste in Explorer
- Taskbar **Jump List** with recent downloads, new-download **badge** and flashing button
- Optional **notification-area icon** and **global hotkey** (default `Ctrl+Alt+D`)

**Quick Look preview** (press <kbd>Space</kbd>)
- Images: decoded at screen resolution off the UI thread, full resolution loaded on demand, smooth cursor-anchored zoom, pan, EXIF rotation, animated GIFs
- Video and audio with a compact player (system codecs)
- Text and code (encoding detection, word wrap)
- PDF, Office documents, fonts … through the same preview handlers as Explorer's preview pane
- Everything else: a large thumbnail with file details
- <kbd>←</kbd>/<kbd>→</kbd> to step through the stack without closing the preview

**Looks native everywhere**
- Mica, Mica Alt, Acrylic, Blur/Aero glass or Solid — with automatic fallback (see below)
- Light/dark theme that follows Windows live, Windows accent color, custom tint
- Real rounded corners on Windows 11; optional rounded corners on older Windows
- Per-monitor DPI aware, any taskbar position, multi-monitor
- English, Русский, Українська

**Updates**
- Checks GitHub Releases, then the project server as a fallback; SHA-256-verified, swaps itself in place and restarts — no installer

## Requirements

| | |
|---|---|
| OS | Windows 7 SP1, 8, 8.1, 10, 11 (x86 or x64) |
| Runtime | .NET Framework 4.8 — preinstalled on Windows 10 1903+ and Windows 11; [download](https://dotnet.microsoft.com/download/dotnet-framework/net48) for older systems (the app offers the link if it's missing) |

## Install

1. Download `Downloads.exe` from the [latest release](../../releases/latest) and put it anywhere you like (for example `%AppData%\fhnb16\DownloadsOSX`).
2. Run it, then right-click its taskbar button → **Pin to taskbar**.
3. Optional: *Settings → Behavior → Start with Windows*.

Clicking the pinned icon now opens the stack right above it; clicking anywhere else hides it.

**Portable mode:** create an empty `portable.flag` file next to the exe and settings are stored beside it instead of `%AppData%\fhnb16\DownloadsOSX`.

## Keyboard

| Key | Action |
|---|---|
| <kbd>Enter</kbd> | Open selected items |
| <kbd>Space</kbd> | Preview (again or <kbd>Esc</kbd> to close) |
| <kbd>←</kbd> <kbd>→</kbd> | Previous / next item in the preview |
| <kbd>Del</kbd> / <kbd>Shift+Del</kbd> | Recycle / delete permanently |
| <kbd>F2</kbd> | Rename |
| <kbd>Ctrl+C</kbd> / <kbd>Ctrl+X</kbd> | Copy / cut files (paste in Explorer) |
| <kbd>Ctrl+Shift+C</kbd> | Copy full path |
| <kbd>Ctrl+E</kbd> | Show in folder |
| <kbd>Ctrl+O</kbd> | Open the Downloads folder |
| <kbd>Alt+Enter</kbd> | Properties |
| <kbd>Apps</kbd> / <kbd>Shift+F10</kbd> | Explorer context menu |
| Type letters | Filter by name |
| <kbd>Ctrl+G</kbd> | Switch list / grid |
| <kbd>F5</kbd> | Refresh |
| <kbd>Esc</kbd> | Clear filter, then hide |
| <kbd>Ctrl+0</kbd> / <kbd>Ctrl+1</kbd>, <kbd>+</kbd> / <kbd>−</kbd>, double-click | Image preview: fit / 100 %, zoom |
| <kbd>K</kbd>, <kbd>J</kbd> / <kbd>L</kbd>, <kbd>M</kbd> | Media preview: play-pause, −5 s / +5 s, mute |

## Backdrops and fallbacks

Pick a material in *Settings → Appearance*. If the running Windows can't do it, the next best one is used automatically (the settings page shows what is active):

| Windows | Mica | Mica Alt | Acrylic | Blur |
|---|---|---|---|---|
| 11 22H2+ | Mica | Mica Alt | Acrylic | Blur |
| 11 21H2 | Mica | → Mica | Acrylic | Blur |
| 10 1803+ | → Acrylic | → Acrylic | Acrylic | Blur |
| 10 (older) | → Blur | → Blur | → Blur | Blur |
| 8 / 8.1 | Solid (Windows 8 has no blur) ||||
| 7 / Vista | → Aero glass | → Aero glass | → Aero glass | Aero glass |
| Composition off (Basic/Classic theme) | Solid ||||

*Automatic* uses Acrylic for the stack and preview (short-lived surfaces) and Mica for the settings window.

**Transparency** (*Settings → Appearance*) controls one tint layer over the material, in the tint color (theme color or custom). *Automatic* picks a sensible value per material; turn it off and use the slider: 100 % = the pure material, 0 % = a solid color. On Windows 11 Mica / Mica Alt / Acrylic, Windows' own tint always stays, so 100 % means "exactly the system look"; *Blur* (and Acrylic on Windows 10) has no built-in tint, so it can go all the way to clear glass. Note that Windows draws Mica and Acrylic flat while a window is inactive.
Tip: tools such as Mica For Everyone override window backdrops globally (the settings page warns when it is running). Add a rule for the `Downloads` process with backdrop *Default* and title bar color *Default* to see the app's own materials.

## Settings overview

- **Appearance** — backdrop, theme, tint color, transparency (auto or 0–100 %), corner style, view (list/grid) and grid columns, item size, details line, date groups, newest-near-taskbar, font, font size, animations
- **Behavior** — Downloads folder override, extra folders, item count, sort, folders/hidden/unfinished files, taskbar/tray/both, open position (cursor / center / remembered), hide on focus loss, single-click open, delete confirmation, global hotkey, start with Windows, language
- **Notifications** — taskbar badge, flash, sound, balloon, Jump List and its size
- **Preview** — enable media / text / document previews, autoplay, loop, keep zoom between images
- **Updates** — notify / install automatically / off, check now, skipped version

Settings live in `%AppData%\fhnb16\DownloadsOSX\settings.json` (with a `log.txt` next to it).

## Building

```powershell
# Visual Studio 2019/2022+: open DownloadsOSX.sln and build.
# Or from the command line:
dotnet build src/DownloadsOSX -c Release
# or
msbuild src/DownloadsOSX/DownloadsOSX.csproj -restore -p:Configuration=Release
```

The output is a single `src/DownloadsOSX/bin/Release/net48/Downloads.exe`. The .NET 4.8 targeting pack is not required (the `Microsoft.NETFramework.ReferenceAssemblies` build package supplies it). If you switch between `dotnet build` and Visual Studio's MSBuild and see `*.g.cs not found` errors, delete `src/DownloadsOSX/obj`.

Pushes to other branches and pull requests are built by the [Build workflow](.github/workflows/build.yml); the resulting `Downloads.exe` is attached to the run as an artifact (kept 30 days). `master` is built by the Release workflow (see below).

To test backdrop fallbacks on a newer Windows, set `DOWNLOADSOSX_OS_BUILD` (e.g. `19045` for Windows 10, `7601` for Windows 7) before launching.

## Releasing

1. Bump `<Version>` in `src/DownloadsOSX/DownloadsOSX.csproj` (and `assemblyIdentity version` in `app.manifest`) and push to `master`. The commit message becomes the release notes shown in the in-app update dialog.
   Alternatively, tag and push by hand (the tag message becomes the notes):
   ```powershell
   git tag -a v1.2.0 -m "What changed"
   git push origin v1.2.0
   ```
2. The [Release workflow](.github/workflows/release.yml) builds with that version, generates `update.json` (version, download URL, SHA-256, notes) and publishes a GitHub release `v<version>` with `Downloads.exe` + `update.json`. A `master` push whose version already has a tag only builds an artifact, without publishing.
   Versions with a suffix (`1.2.0-rc1`) become pre-releases, which installed apps ignore.
3. **Own server mirror (fallback source):** upload the same `Downloads.exe` and a manifest pointing at it:
   ```powershell
   ./tools/make-manifest.ps1 -Exe dist/Downloads.exe -Url https://root.fhnb.ru/DownloadsOSX/Downloads.exe -Out update.json -Notes "What changed"
   ```
   and place both at `https://root.fhnb.ru/DownloadsOSX/`.

## How updates work

- Sources, in order: `https://github.com/<owner>/<repo>/releases/latest/download/update.json` (a redirect — no API call, no rate limit), then `https://root.fhnb.ru/DownloadsOSX/update.json`. The repository is the `GitHubRepo` constant in [`UpdateService.cs`](src/DownloadsOSX/Services/UpdateService.cs).
- Checked 30 s after start and then at most once a day (or on demand). HTTPS only, TLS 1.2 forced (Windows 7 needs it).
- The new exe is downloaded next to the current one, **SHA-256 verified**, the running exe is renamed to `.old` (Windows allows this), the new one takes its place and is started with `--updated`; leftovers are cleaned on the next start.
- If the folder isn't writable (e.g. Program Files), the downloaded exe installs itself with a single UAC prompt and relaunches the app un-elevated.
- *Install automatically* mode downloads in the background and swaps only while the stack is hidden and idle.
- **Testing:** add `"UpdateManifestUrls": ["http://localhost:8765/update.json"]` to `settings.json` (plain HTTP is accepted for localhost only) and serve a manifest + exe from a local folder.

### Code signing and Smart App Control

Windows 11 **Smart App Control** (and SmartScreen) can block unsigned executables with no reputation — including freshly built or freshly downloaded versions of `Downloads.exe`. Signing releases with a code-signing certificate avoids this. The updater is defensive about it: if the new version is blocked from starting, the previous exe is restored and the error is shown in the update dialog, so the app is never left broken.

### Migrating users of the legacy version

Old builds (0.x) check `root.fhnb.ru/DownloadsOSX/config.json` → `LatestVersion` and update through the Omicron installer (app ID `0002`). Point that installer at the new `Downloads.exe` once; from then on the built-in updater takes over. The autostart entry keeps its legacy name (`DownloaderOSX`), so enabling autostart in the new version replaces the old entry instead of starting both.

## Project layout

```
DownloadsOSX.sln
src/DownloadsOSX/
  App.xaml(.cs)          startup, single instance, command line, language
  Interop/               Win32/COM: shell context menu, thumbnails, preview handlers, known folders, folder picker
  Services/              backdrop/theme, downloads watcher, updater, jump list, hotkey, tray, settings, autostart
  Models/                settings and file entries
  Views/                 stack, preview (+ Preview/ views), settings, update dialog
  Resources/             strings (en/ru/uk), light/dark theme, vector icons, control styles
  Assets/                icon, images, Ubuntu font
tools/make-manifest.ps1  update.json generator
legacy/                  the original 0.x project, kept for reference
```

## License

[Apache License 2.0](LICENSE)
