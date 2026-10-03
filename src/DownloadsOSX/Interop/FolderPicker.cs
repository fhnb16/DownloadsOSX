using System;
using System.Runtime.InteropServices;

namespace DownloadsOSX.Interop
{
    /// <summary>Modern Vista+ folder picker (IFileOpenDialog with FOS_PICKFOLDERS); WinForms dialog as a fallback.</summary>
    internal static class FolderPicker
    {
        public static string Pick(IntPtr owner, string title, string initial)
        {
            try
            {
                var dialog = (IFileOpenDialog)new FileOpenDialogRCW();
                try
                {
                    dialog.GetOptions(out uint opts);
                    dialog.SetOptions(opts | FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST);
                    if (!string.IsNullOrEmpty(title)) dialog.SetTitle(title);
                    if (!string.IsNullOrEmpty(initial) && System.IO.Directory.Exists(initial))
                    {
                        NativeMethods.SHCreateItemFromParsingName(initial, IntPtr.Zero, typeof(IShellItem).GUID, out object item);
                        dialog.SetFolder((IShellItem)item);
                    }
                    if (dialog.Show(owner) != 0) return null; // cancelled
                    dialog.GetResult(out IShellItem result);
                    result.GetDisplayName(SIGDN_FILESYSPATH, out IntPtr p);
                    try { return Marshal.PtrToStringUni(p); }
                    finally { Marshal.FreeCoTaskMem(p); }
                }
                finally { Marshal.ReleaseComObject(dialog); }
            }
            catch (Exception ex)
            {
                Log.Warn("IFileOpenDialog failed, using WinForms folder dialog", ex);
                using (var d = new System.Windows.Forms.FolderBrowserDialog { Description = title, SelectedPath = initial ?? "" })
                    return d.ShowDialog() == System.Windows.Forms.DialogResult.OK ? d.SelectedPath : null;
            }
        }

        private const uint FOS_PICKFOLDERS = 0x20, FOS_FORCEFILESYSTEM = 0x40, FOS_PATHMUSTEXIST = 0x800;
        private const uint SIGDN_FILESYSPATH = 0x80058000;

        [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
        private class FileOpenDialogRCW { }

        [ComImport, Guid("d57c7288-d4ad-4768-be02-9d969532d960"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileOpenDialog
        {
            [PreserveSig] int Show(IntPtr parent);
            void SetFileTypes(uint cFileTypes, IntPtr rgFilterSpec);
            void SetFileTypeIndex(uint iFileType);
            void GetFileTypeIndex(out uint piFileType);
            void Advise(IntPtr pfde, out uint pdwCookie);
            void Unadvise(uint dwCookie);
            void SetOptions(uint fos);
            void GetOptions(out uint pfos);
            void SetDefaultFolder(IShellItem psi);
            void SetFolder(IShellItem psi);
            void GetFolder(out IShellItem ppsi);
            void GetCurrentSelection(out IShellItem ppsi);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
            void GetResult(out IShellItem ppsi);
            void AddPlace(IShellItem psi, int fdap);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
            void Close(int hr);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter(IntPtr pFilter);
            void GetResults(out IntPtr ppenum);
            void GetSelectedItems(out IntPtr ppsai);
        }

        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
            void GetParent(out IShellItem ppsi);
            void GetDisplayName(uint sigdnName, out IntPtr ppszName);
            void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
            void Compare(IShellItem psi, uint hint, out int piOrder);
        }
    }
}
