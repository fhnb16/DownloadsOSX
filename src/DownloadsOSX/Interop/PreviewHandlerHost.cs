using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace DownloadsOSX.Interop
{
    /// <summary>
    /// Hosts a registered shell preview handler (IPreviewHandler) — the same component Explorer's preview pane uses
    /// for PDF, Office documents, fonts, etc. Most handlers run out-of-process (prevhost.exe), so a slow handler
    /// doesn't freeze our UI for long.
    /// </summary>
    internal sealed class PreviewHandlerHost : HwndHost
    {
        public static readonly Guid IID_IPreviewHandler = new Guid("8895b1c6-b41f-4c1c-a562-0d564250836f");
        private static readonly Guid IID_IShellItem = new Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe");

        private IntPtr _child;
        private object _handler;
        private IStream _stream;

        public static Guid? FindHandler(string extension)
        {
            return Shell.GetShellExtension(extension, IID_IPreviewHandler);
        }

        protected override HandleRef BuildWindowCore(HandleRef hwndParent)
        {
            _child = NativeMethods.CreateWindowEx(0, "Static", "", NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE | NativeMethods.WS_CLIPCHILDREN,
                0, 0, 100, 100, hwndParent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            return new HandleRef(this, _child);
        }

        protected override void DestroyWindowCore(HandleRef hwnd)
        {
            Unload();
            NativeMethods.DestroyWindow(hwnd.Handle);
            _child = IntPtr.Zero;
        }

        /// <summary>Loads the file with the given handler. Returns false if the handler refused it.</summary>
        public bool Load(string path, Guid clsid, Color background, Color foreground)
        {
            Unload();
            if (_child == IntPtr.Zero) return false;
            try
            {
                var type = Type.GetTypeFromCLSID(clsid, true);
                _handler = Activator.CreateInstance(type);

                if (_handler is IInitializeWithStream withStream)
                {
                    NativeMethods.SHCreateStreamOnFileEx(path, NativeMethods.STGM_READ | NativeMethods.STGM_SHARE_DENY_NONE, 0, false, IntPtr.Zero, out _stream);
                    withStream.Initialize(_stream, 0);
                }
                else if (_handler is IInitializeWithFile withFile)
                {
                    withFile.Initialize(path, 0);
                }
                else if (_handler is IInitializeWithItem withItem)
                {
                    NativeMethods.SHCreateItemFromParsingName(path, IntPtr.Zero, IID_IShellItem, out object item);
                    withItem.Initialize(item, 0);
                }
                else
                {
                    Unload();
                    return false;
                }

                if (_handler is IPreviewHandlerVisuals visuals)
                {
                    try
                    {
                        visuals.SetBackgroundColor(ToColorRef(background));
                        visuals.SetTextColor(ToColorRef(foreground));
                    }
                    catch { }
                }

                var r = ClientRect();
                var ph = (IPreviewHandler)_handler;
                ph.SetWindow(_child, ref r);
                ph.SetRect(ref r);
                ph.DoPreview();
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("Preview handler " + clsid + " failed for " + path, ex);
                Unload();
                return false;
            }
        }

        public void Unload()
        {
            if (_handler != null)
            {
                try { ((IPreviewHandler)_handler).Unload(); } catch { }
                try { Marshal.FinalReleaseComObject(_handler); } catch { }
                _handler = null;
            }
            if (_stream != null)
            {
                try { Marshal.FinalReleaseComObject(_stream); } catch { }
                _stream = null;
            }
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            if (_handler is IPreviewHandler ph)
            {
                try { var r = ClientRect(); ph.SetRect(ref r); } catch { }
            }
        }

        private NativeMethods.RECT ClientRect()
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            return new NativeMethods.RECT(0, 0, (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY));
        }

        private static uint ToColorRef(Color c) => (uint)(c.R | (c.G << 8) | (c.B << 16));

        #region COM

        [ComImport, Guid("8895b1c6-b41f-4c1c-a562-0d564250836f"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPreviewHandler
        {
            void SetWindow(IntPtr hwnd, ref NativeMethods.RECT rect);
            void SetRect(ref NativeMethods.RECT rect);
            void DoPreview();
            void Unload();
            void SetFocus();
            void QueryFocus(out IntPtr phwnd);
            [PreserveSig] uint TranslateAccelerator(IntPtr pmsg);
        }

        [ComImport, Guid("196bf9a5-b346-4ef0-aa1e-5dcdb76768b1"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPreviewHandlerVisuals
        {
            void SetBackgroundColor(uint color);
            void SetFont(IntPtr plf);
            void SetTextColor(uint color);
        }

        [ComImport, Guid("b824b49d-22ac-4161-ac8a-9916e8fa3f7f"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IInitializeWithStream
        {
            void Initialize(IStream pstream, uint grfMode);
        }

        [ComImport, Guid("b7d14566-0509-4cce-a71f-0a554233bd9b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IInitializeWithFile
        {
            void Initialize([MarshalAs(UnmanagedType.LPWStr)] string pszFilePath, uint grfMode);
        }

        [ComImport, Guid("7f73be3f-fb79-493c-a6c7-7ee14e245841"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IInitializeWithItem
        {
            void Initialize([MarshalAs(UnmanagedType.IUnknown)] object psi, uint grfMode);
        }

        #endregion
    }
}
