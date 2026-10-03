using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace DownloadsOSX.Interop
{
    /// <summary>
    /// Native Explorer context menu for files in one folder, hosted on a WPF window.
    /// Based on the legacy CodeProject-derived class, rewritten around SHBindToParent and an HwndSource hook
    /// (the owner window receives the owner-draw messages for "Open with"/"Send to" submenus).
    /// </summary>
    internal static class ShellContextMenu
    {
        public sealed class Item
        {
            public string Text;
            public Action Action;
            public bool IsSeparator;
            public static Item Separator => new Item { IsSeparator = true };
        }

        private const uint CmdFirst = 1;
        private const uint CmdLast = 0x6FFF;
        private const uint CustomFirst = 0x7000;

        private const uint CMF_NORMAL = 0x0, CMF_EXPLORE = 0x4, CMF_CANRENAME = 0x10, CMF_EXTENDEDVERBS = 0x100;
        private const uint GCS_VERBW = 0x4;
        private const int CMIC_MASK_UNICODE = 0x4000, CMIC_MASK_PTINVOKE = 0x20000000,
            CMIC_MASK_SHIFT_DOWN = 0x10000000, CMIC_MASK_CONTROL_DOWN = 0x40000000, CMIC_MASK_ASYNCOK = 0x00100000;

        private static readonly Guid IID_IShellFolder = new Guid("000214E6-0000-0000-C000-000000000046");
        private static readonly Guid IID_IContextMenu = new Guid("000214e4-0000-0000-c000-000000000046");

        public static bool IsOpen { get; private set; }

        /// <summary>Shows the menu at a screen point (physical pixels). Blocks until the menu closes.</summary>
        public static void Show(Window owner, IReadOnlyList<string> paths, Point screenPx, IList<Item> customTop, Action onRename)
        {
            if (paths == null || paths.Count == 0) return;
            IntPtr hwnd = new WindowInteropHelper(owner).Handle;
            var source = HwndSource.FromHwnd(hwnd);

            string folder = Path.GetDirectoryName(paths[0]);
            var sameFolder = paths.Where(p => string.Equals(Path.GetDirectoryName(p), folder, StringComparison.OrdinalIgnoreCase)).ToList();

            var absPidls = new List<IntPtr>();
            IntPtr parentPtr = IntPtr.Zero, cmPtr = IntPtr.Zero, hMenu = IntPtr.Zero;
            IShellFolder parent = null;
            IContextMenu cm = null;
            IContextMenu2 cm2 = null;
            IContextMenu3 cm3 = null;
            HwndSourceHook hook = null;

            try
            {
                foreach (var p in sameFolder)
                {
                    if (NativeMethods.SHParseDisplayName(p, IntPtr.Zero, out IntPtr pidl, 0, out _) == 0 && pidl != IntPtr.Zero)
                        absPidls.Add(pidl);
                }
                if (absPidls.Count == 0) return;

                if (NativeMethods.SHBindToParent(absPidls[0], IID_IShellFolder, out parentPtr, out _) != 0) return;
                parent = (IShellFolder)Marshal.GetObjectForIUnknown(parentPtr);

                var children = absPidls.Select(ILFindLastID).ToArray();
                Guid iid = IID_IContextMenu;
                if (parent.GetUIObjectOf(hwnd, (uint)children.Length, children, ref iid, IntPtr.Zero, out cmPtr) != 0 || cmPtr == IntPtr.Zero) return;

                cm = (IContextMenu)Marshal.GetObjectForIUnknown(cmPtr);
                cm2 = cm as IContextMenu2;
                cm3 = cm as IContextMenu3;

                hMenu = NativeMethods.CreatePopupMenu();
                bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
                cm.QueryContextMenu(hMenu, 0, CmdFirst, CmdLast, CMF_NORMAL | CMF_EXPLORE | CMF_CANRENAME | (shift ? CMF_EXTENDEDVERBS : 0));

                // Find the shell "rename" verb; it can't work outside an Explorer view, so we handle it ourselves.
                uint renameId = FindVerb(cm, hMenu, "rename");

                // Our own commands at the top.
                if (customTop != null && customTop.Count > 0)
                {
                    uint pos = 0;
                    for (int i = 0; i < customTop.Count; i++)
                    {
                        var it = customTop[i];
                        if (it.IsSeparator)
                            NativeMethods.InsertMenu(hMenu, pos++, NativeMethods.MF_BYPOSITION | NativeMethods.MF_SEPARATOR, UIntPtr.Zero, null);
                        else
                            NativeMethods.InsertMenu(hMenu, pos++, NativeMethods.MF_BYPOSITION | NativeMethods.MF_STRING, new UIntPtr(CustomFirst + (uint)i), it.Text);
                    }
                    NativeMethods.InsertMenu(hMenu, pos, NativeMethods.MF_BYPOSITION | NativeMethods.MF_SEPARATOR, UIntPtr.Zero, null);
                }

                hook = (IntPtr h, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
                {
                    try
                    {
                        if (cm3 != null && (msg == NativeMethods.WM_MENUCHAR || msg == NativeMethods.WM_INITMENUPOPUP || msg == NativeMethods.WM_DRAWITEM || msg == NativeMethods.WM_MEASUREITEM))
                        {
                            if (cm3.HandleMenuMsg2((uint)msg, wParam, lParam, out IntPtr result) == 0) { handled = true; return result; }
                        }
                        else if (cm2 != null && (msg == NativeMethods.WM_INITMENUPOPUP || msg == NativeMethods.WM_DRAWITEM || msg == NativeMethods.WM_MEASUREITEM))
                        {
                            if (cm2.HandleMenuMsg((uint)msg, wParam, lParam) == 0) { handled = true; return IntPtr.Zero; }
                        }
                    }
                    catch { }
                    return IntPtr.Zero;
                };
                source?.AddHook(hook);

                IsOpen = true;
                NativeMethods.SetForegroundWindow(hwnd);
                uint cmd = NativeMethods.TrackPopupMenuEx(hMenu, NativeMethods.TPM_RETURNCMD | NativeMethods.TPM_RIGHTBUTTON,
                    (int)screenPx.X, (int)screenPx.Y, hwnd, IntPtr.Zero);
                IsOpen = false;

                if (cmd == 0) return;
                if (cmd >= CustomFirst && customTop != null && cmd - CustomFirst < customTop.Count)
                {
                    customTop[(int)(cmd - CustomFirst)].Action?.Invoke();
                }
                else if (cmd == renameId && onRename != null)
                {
                    onRename();
                }
                else if (cmd >= CmdFirst && cmd <= CmdLast)
                {
                    Invoke(cm, hwnd, cmd - CmdFirst, folder, screenPx);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Shell context menu failed", ex);
            }
            finally
            {
                IsOpen = false;
                if (hook != null) source?.RemoveHook(hook);
                if (hMenu != IntPtr.Zero) NativeMethods.DestroyMenu(hMenu);
                if (cm != null) Marshal.ReleaseComObject(cm);
                if (cmPtr != IntPtr.Zero) Marshal.Release(cmPtr);
                if (parent != null) Marshal.ReleaseComObject(parent);
                if (parentPtr != IntPtr.Zero) Marshal.Release(parentPtr);
                foreach (var p in absPidls) Marshal.FreeCoTaskMem(p);
            }
        }

        private static uint FindVerb(IContextMenu cm, IntPtr hMenu, string verb)
        {
            int count = NativeMethods.GetMenuItemCount(hMenu);
            IntPtr buf = Marshal.AllocCoTaskMem(520);
            try
            {
                for (int i = 0; i < count; i++)
                {
                    uint id = NativeMethods.GetMenuItemID(hMenu, i);
                    if (id < CmdFirst || id > CmdLast) continue;
                    Marshal.WriteInt16(buf, 0);
                    if (cm.GetCommandString(new UIntPtr(id - CmdFirst), GCS_VERBW, IntPtr.Zero, buf, 260) == 0 &&
                        string.Equals(Marshal.PtrToStringUni(buf), verb, StringComparison.OrdinalIgnoreCase))
                        return id;
                }
            }
            catch { }
            finally { Marshal.FreeCoTaskMem(buf); }
            return uint.MaxValue;
        }

        private static void Invoke(IContextMenu cm, IntPtr hwnd, uint offset, string folder, Point pt)
        {
            IntPtr dirA = Marshal.StringToHGlobalAnsi(folder);
            IntPtr dirW = Marshal.StringToHGlobalUni(folder);
            try
            {
                var mods = Keyboard.Modifiers;
                var info = new CMINVOKECOMMANDINFOEX
                {
                    cbSize = Marshal.SizeOf(typeof(CMINVOKECOMMANDINFOEX)),
                    fMask = CMIC_MASK_UNICODE | CMIC_MASK_PTINVOKE | CMIC_MASK_ASYNCOK
                            | ((mods & ModifierKeys.Control) != 0 ? CMIC_MASK_CONTROL_DOWN : 0)
                            | ((mods & ModifierKeys.Shift) != 0 ? CMIC_MASK_SHIFT_DOWN : 0),
                    hwnd = hwnd,
                    lpVerb = new IntPtr(offset),
                    lpVerbW = new IntPtr(offset),
                    lpDirectory = dirA,
                    lpDirectoryW = dirW,
                    nShow = 1, // SW_SHOWNORMAL
                    ptInvoke = new NativeMethods.POINT((int)pt.X, (int)pt.Y),
                };
                cm.InvokeCommand(ref info);
            }
            finally
            {
                Marshal.FreeHGlobal(dirA);
                Marshal.FreeHGlobal(dirW);
            }
        }

        /// <summary>Pointer to the last SHITEMID in an absolute PIDL (no allocation).</summary>
        private static IntPtr ILFindLastID(IntPtr pidl)
        {
            IntPtr last = pidl, cur = pidl;
            while (true)
            {
                ushort cb = (ushort)Marshal.ReadInt16(cur);
                if (cb == 0) return last;
                last = cur;
                cur = IntPtr.Add(cur, cb);
            }
        }

        #region COM

        [StructLayout(LayoutKind.Sequential)]
        private struct CMINVOKECOMMANDINFOEX
        {
            public int cbSize;
            public int fMask;
            public IntPtr hwnd;
            public IntPtr lpVerb;
            public IntPtr lpParameters;
            public IntPtr lpDirectory;
            public int nShow;
            public int dwHotKey;
            public IntPtr hIcon;
            public IntPtr lpTitle;
            public IntPtr lpVerbW;
            public IntPtr lpParametersW;
            public IntPtr lpDirectoryW;
            public IntPtr lpTitleW;
            public NativeMethods.POINT ptInvoke;
        }

        [ComImport, Guid("000214E6-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellFolder
        {
            [PreserveSig] int ParseDisplayName(IntPtr hwnd, IntPtr pbc, [MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr pchEaten, out IntPtr ppidl, IntPtr attrs);
            [PreserveSig] int EnumObjects(IntPtr hwnd, int flags, out IntPtr ppenum);
            [PreserveSig] int BindToObject(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
            [PreserveSig] int BindToStorage(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
            [PreserveSig] int CompareIDs(IntPtr lParam, IntPtr pidl1, IntPtr pidl2);
            [PreserveSig] int CreateViewObject(IntPtr hwndOwner, ref Guid riid, out IntPtr ppv);
            [PreserveSig] int GetAttributesOf(uint cidl, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl, ref uint rgfInOut);
            [PreserveSig] int GetUIObjectOf(IntPtr hwndOwner, uint cidl, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl, ref Guid riid, IntPtr rgfReserved, out IntPtr ppv);
            [PreserveSig] int GetDisplayNameOf(IntPtr pidl, uint flags, IntPtr pName);
            [PreserveSig] int SetNameOf(IntPtr hwnd, IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string name, uint flags, out IntPtr ppidlOut);
        }

        [ComImport, Guid("000214e4-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IContextMenu
        {
            [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
            [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFOEX pici);
            [PreserveSig] int GetCommandString(UIntPtr idCmd, uint uType, IntPtr pReserved, IntPtr pszName, uint cchMax);
        }

        [ComImport, Guid("000214f4-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IContextMenu2
        {
            [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
            [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFOEX pici);
            [PreserveSig] int GetCommandString(UIntPtr idCmd, uint uType, IntPtr pReserved, IntPtr pszName, uint cchMax);
            [PreserveSig] int HandleMenuMsg(uint uMsg, IntPtr wParam, IntPtr lParam);
        }

        [ComImport, Guid("bcfce0a0-ec17-11d0-8d10-00a0c90f2719"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IContextMenu3
        {
            [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
            [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFOEX pici);
            [PreserveSig] int GetCommandString(UIntPtr idCmd, uint uType, IntPtr pReserved, IntPtr pszName, uint cchMax);
            [PreserveSig] int HandleMenuMsg(uint uMsg, IntPtr wParam, IntPtr lParam);
            [PreserveSig] int HandleMenuMsg2(uint uMsg, IntPtr wParam, IntPtr lParam, out IntPtr plResult);
        }

        #endregion
    }
}
