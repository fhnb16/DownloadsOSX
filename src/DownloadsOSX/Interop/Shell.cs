using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using Microsoft.VisualBasic.FileIO;

namespace DownloadsOSX.Interop
{
    /// <summary>Explorer integration helpers.</summary>
    internal static class Shell
    {
        private static readonly Guid FOLDERID_Downloads = new Guid("374DE290-123F-4565-9164-39C4925E467B");

        /// <summary>Standard Windows folders with an app-localized name (resource key), resolved through redirection/OneDrive.</summary>
        private static readonly (Guid Id, string Key)[] NamedFolders =
        {
            (FOLDERID_Downloads, "FolderDownloads"),
            (new Guid("FDD39AD0-238F-46AF-ADB4-6C85480369C7"), "FolderDocuments"),
            (new Guid("B4BFCC3A-DB2C-424C-B029-7FE99A87C641"), "FolderDesktop"),
            (new Guid("33E28130-4E1E-4676-835A-98395C3BC3BB"), "FolderPictures"),
            (new Guid("4BD8D571-6D19-48D3-BE97-422220080E43"), "FolderMusic"),
            (new Guid("18989B1D-99B5-455B-841C-AB7C74E4DDFC"), "FolderVideos"),
            (new Guid("B7BEDE81-DF94-4682-A7D8-57A52620B86F"), "FolderScreenshots"),
        };

        private static string GetKnownFolder(Guid id)
        {
            try
            {
                if (NativeMethods.SHGetKnownFolderPath(id, 0, IntPtr.Zero, out IntPtr p) == 0)
                {
                    try { return Marshal.PtrToStringUni(p); }
                    finally { Marshal.FreeCoTaskMem(p); }
                }
            }
            catch (Exception ex) { Log.Debug("SHGetKnownFolderPath failed: " + ex.Message); }
            return null; // e.g. Screenshots doesn't exist before Windows 8
        }

        public static string GetDownloadsFolder()
        {
            return GetKnownFolder(FOLDERID_Downloads)
                   ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }

        /// <summary>
        /// Title for a watched folder: the app-localized name of a standard Windows folder (Downloads, Documents, …),
        /// otherwise the folder's own name (or the drive/path for a root).
        /// </summary>
        public static string GetFolderTitle(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return App.Str("AppTitle");
            string norm = Normalize(path);
            foreach (var (id, key) in NamedFolders)
            {
                string known = GetKnownFolder(id);
                if (known != null && string.Equals(Normalize(known), norm, StringComparison.OrdinalIgnoreCase))
                    return App.Str(key);
            }
            string name = Path.GetFileName(path.TrimEnd('\\', '/'));
            return string.IsNullOrEmpty(name) ? path : name;
        }

        private static string Normalize(string path)
        {
            try { path = Path.GetFullPath(path); } catch { }
            return path.TrimEnd('\\', '/');
        }

        public static void Open(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(path) ?? "" });
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1155) // no association
            {
                OpenWith(IntPtr.Zero, path);
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) { } // cancelled (UAC etc.)
        }

        public static void OpenFolder(string folder)
        {
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }

        public static void OpenWith(IntPtr owner, string path)
        {
            try
            {
                var info = new NativeMethods.OPENASINFO
                {
                    pcszFile = path,
                    oaifInFlags = NativeMethods.OAIF_ALLOW_REGISTRATION | NativeMethods.OAIF_EXEC,
                };
                NativeMethods.SHOpenWithDialog(owner, ref info);
            }
            catch (EntryPointNotFoundException)
            {
                Process.Start("rundll32.exe", "shell32.dll,OpenAs_RunDLL " + path);
            }
        }

        /// <summary>Opens an Explorer window on the parent folder with the given items selected.</summary>
        public static void ShowInFolder(IReadOnlyList<string> paths)
        {
            if (paths == null || paths.Count == 0) return;
            string folder = Path.GetDirectoryName(paths[0]);
            IntPtr folderPidl = IntPtr.Zero;
            var items = new List<IntPtr>();
            try
            {
                folderPidl = NativeMethods.ILCreateFromPath(folder);
                foreach (var p in paths.Where(p => string.Equals(Path.GetDirectoryName(p), folder, StringComparison.OrdinalIgnoreCase)))
                {
                    IntPtr pidl = NativeMethods.ILCreateFromPath(p);
                    if (pidl != IntPtr.Zero) items.Add(pidl);
                }
                if (folderPidl == IntPtr.Zero || NativeMethods.SHOpenFolderAndSelectItems(folderPidl, (uint)items.Count, items.ToArray(), 0) != 0)
                    throw new COMException("SHOpenFolderAndSelectItems failed");
            }
            catch (Exception ex)
            {
                Log.Warn("ShowInFolder fallback", ex);
                Process.Start("explorer.exe", "/select,\"" + paths[0] + "\"");
            }
            finally
            {
                if (folderPidl != IntPtr.Zero) NativeMethods.ILFree(folderPidl);
                foreach (var p in items) NativeMethods.ILFree(p);
            }
        }

        /// <summary>Sends to the Recycle Bin (or deletes permanently). Returns false if the user cancelled.</summary>
        public static bool Delete(IEnumerable<string> paths, bool permanently)
        {
            var recycle = permanently ? RecycleOption.DeletePermanently : RecycleOption.SendToRecycleBin;
            foreach (var path in paths)
            {
                try
                {
                    if (Directory.Exists(path))
                        FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs, recycle, UICancelOption.ThrowException);
                    else if (File.Exists(path))
                        FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, recycle, UICancelOption.ThrowException);
                }
                catch (OperationCanceledException) { return false; }
            }
            return true;
        }

        public static void CopyFilesToClipboard(IEnumerable<string> paths, bool cut = false)
        {
            var list = new StringCollection();
            list.AddRange(paths.ToArray());
            var data = new DataObject();
            data.SetFileDropList(list);
            // Preferred DropEffect: 1 = copy, 2 = move.
            data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(cut ? 2 : 5)));
            Clipboard.SetDataObject(data, true);
        }

        /// <summary>True if the file carries a Mark-of-the-Web (Zone.Identifier stream).</summary>
        public static bool HasZoneIdentifier(string path)
        {
            try { return NativeMethods.GetFileAttributes(path + ":Zone.Identifier") != NativeMethods.INVALID_FILE_ATTRIBUTES; }
            catch { return false; }
        }

        public static bool RemoveZoneIdentifier(string path)
        {
            return NativeMethods.DeleteFile(path + ":Zone.Identifier");
        }

        public static string GetTypeName(string path, bool isFolder)
        {
            try
            {
                var info = new NativeMethods.SHFILEINFO();
                uint attrs = isFolder ? 0x10u : 0x80u; // FILE_ATTRIBUTE_DIRECTORY / NORMAL
                NativeMethods.SHGetFileInfo(path, attrs, ref info, (uint)Marshal.SizeOf(info),
                    NativeMethods.SHGFI_TYPENAME | NativeMethods.SHGFI_USEFILEATTRIBUTES);
                if (!string.IsNullOrEmpty(info.szTypeName)) return info.szTypeName;
            }
            catch { }
            return isFolder ? "Folder" : (Path.GetExtension(path).TrimStart('.').ToUpperInvariant() + " file");
        }

        /// <summary>Associated application exe for an extension (used for Jump List icons).</summary>
        public static string GetAssociatedExecutable(string extension)
        {
            return AssocQuery(NativeMethods.ASSOCSTR_EXECUTABLE, extension, null);
        }

        /// <summary>CLSID of a shell extension handler (e.g. preview handler) registered for an extension.</summary>
        public static Guid? GetShellExtension(string extension, Guid handlerIid)
        {
            string s = AssocQuery(NativeMethods.ASSOCSTR_SHELLEXTENSION, extension, handlerIid.ToString("B"));
            return Guid.TryParse(s, out Guid g) ? g : (Guid?)null;
        }

        private static string AssocQuery(uint what, string assoc, string extra)
        {
            if (string.IsNullOrEmpty(assoc)) return null;
            try
            {
                uint len = 1024;
                var sb = new StringBuilder((int)len);
                int hr = NativeMethods.AssocQueryString(NativeMethods.ASSOCF_INIT_DEFAULTTOSTAR | NativeMethods.ASSOCF_NOTRUNCATE, what, assoc, extra, sb, ref len);
                return hr == 0 ? sb.ToString() : null;
            }
            catch { return null; }
        }

        public static string FormatSize(long bytes)
        {
            string[] units = { App.Str("UnitB"), App.Str("UnitKB"), App.Str("UnitMB"), App.Str("UnitGB"), App.Str("UnitTB") };
            double v = bytes;
            int u = 0;
            while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
            return u == 0 ? $"{bytes} {units[0]}" : $"{v:0.#} {units[u]}";
        }
    }
}
