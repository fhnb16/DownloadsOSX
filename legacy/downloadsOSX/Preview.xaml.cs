using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Interop;
using System.Runtime.InteropServices;

namespace downloadsOSX
{
    /// <summary>
    /// Логика взаимодействия для Preview.xaml
    /// </summary>
    public partial class Preview : Window
    {
        internal enum AccentState
        {
            ACCENT_DISABLED = 0,
            ACCENT_ENABLE_GRADIENT = 1,
            ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
            ACCENT_ENABLE_BLURBEHIND = 3,
            ACCENT_ENABLE_ACRYLICBLURBEHIND = 4,
            ACCENT_INVALID_STATE = 5
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct AccentPolicy
        {
            public AccentState AccentState;
            public uint AccentFlags;
            public uint GradientColor;
            public uint AnimationId;
        }



        [StructLayout(LayoutKind.Sequential)]
        internal struct WindowCompositionAttributeData
        {
            public WindowCompositionAttribute Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        internal enum WindowCompositionAttribute
        {
            // ...
            WCA_ACCENT_POLICY = 19
            // ...
        }

        [DllImport("user32.dll")]
        internal static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        public uint _blurOpacity;
        public uint _blurBackgroundColor;

        public Preview(string img)
        {
            InitializeComponent();

            if (Environment.OSVersion.Version.Major == 10 && Environment.OSVersion.Version.Minor == 0)
            {
                this.Background = Brushes.Transparent;
                this.ResizeMode = ResizeMode.CanMinimize;
                this.WindowStyle = WindowStyle.None;
                this.AllowsTransparency = true;
            }
            if (Environment.OSVersion.Version.Major == 6 && (Environment.OSVersion.Version.Minor == 2) || Environment.OSVersion.Version.Minor == 3)
            {
                this.Background = Brushes.Transparent;
                this.ResizeMode = ResizeMode.CanMinimize;
                this.WindowStyle = WindowStyle.None;
                this.AllowsTransparency = true;
            }
            if (Environment.OSVersion.Version.Major == 6 && (Environment.OSVersion.Version.Minor == 0) || Environment.OSVersion.Version.Minor == 1)
            {
                this.Background = Brushes.Transparent;
                this.WindowStyle = WindowStyle.ToolWindow;
                this.ResizeMode = ResizeMode.CanResizeWithGrip;
                this.AllowsTransparency = false;
                this.BorderBrush = Brushes.Transparent;
                this.BorderThickness = new Thickness(0);
                //this.Topmost = true;
            }
            //this.ShowInTaskbar = false;
            this.WindowState = WindowState.Normal;
            try
            {
                image1.Source = BitmapFromUri(new Uri(img));
            }
            catch
            {
                try
                {
                    image1.Source = BitmapFromUri(new Uri(img));
                }
                catch(Exception ex)
                {
                    MessageBox.Show("Error: " + ex);
                }
            }
        }

        public static ImageSource BitmapFromUri(Uri source)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = source;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            return bitmap;
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {

            try
            {
                if (e.Key == Key.Space)
                {
                    this.Close();
                    MainWindow mainWindow = new MainWindow();
                    mainWindow.WindowState = WindowState.Normal;
                }
            }
            catch { }
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            try
            {
                this.Close();
                MainWindow mainWindow = new MainWindow();
                mainWindow.WindowState = WindowState.Normal;
            }
            catch { }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                this.Close();
                MainWindow mainWindow = new MainWindow();
                mainWindow.WindowState = WindowState.Normal;
            }
            catch { }
        }



        public void blurVista7()
        {

            try
            {
                this.Background = Brushes.Transparent;

                IntPtr mainWindowPtr = new WindowInteropHelper(this).Handle;
                HwndSource mainWindowSrc = HwndSource.FromHwnd(mainWindowPtr);
                mainWindowSrc.CompositionTarget.BackgroundColor = Color.FromArgb(0, 0, 0, 0);
                System.Drawing.Graphics desktop = System.Drawing.Graphics.FromHwnd(mainWindowPtr);
                float DesktopDpiX = desktop.DpiX;
                float DesktopDpiY = desktop.DpiY;
                glassClass.MARGINS margins = new glassClass.MARGINS();
                margins.cxLeftWidth = -1;
                margins.cxRightWidth = -1;
                margins.cyTopHeight = -1;
                margins.cyBottomHeight = -1;
                int hr = glassClass.DwmExtendFrameIntoClientArea(mainWindowSrc.Handle, ref margins);
                //
                if (hr < 0)
                {
                    //DwmExtendFrameIntoClientArea Failed
                }
            }
            catch (DllNotFoundException)
            { }
            catch (Exception)
            { }

        }

        public void blur810()
        {
            try
            {
                _blurBackgroundColor = 0xFFFFFF; // 0x990000
                _blurOpacity = 1;

                var windowHelper = new WindowInteropHelper(this);
                var accent = new AccentPolicy();
                accent.AccentState = AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND;
                accent.GradientColor = (_blurOpacity << 24) | (_blurBackgroundColor & 0xFFFFFF);
                var accentStructSize = Marshal.SizeOf(accent);
                var accentPtr = Marshal.AllocHGlobal(accentStructSize);
                Marshal.StructureToPtr(accent, accentPtr, false);
                var data = new WindowCompositionAttributeData();
                data.Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY;
                data.SizeOfData = accentStructSize;
                data.Data = accentPtr;
                SetWindowCompositionAttribute(windowHelper.Handle, ref data);
                Marshal.FreeHGlobal(accentPtr);
            }
            catch
            {
            }

        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Environment.OSVersion.Version.Major == 10 && Environment.OSVersion.Version.Minor == 0)
                {
                    blur810();
                }
                if (Environment.OSVersion.Version.Major == 6 && (Environment.OSVersion.Version.Minor == 2) || Environment.OSVersion.Version.Minor == 3)
                {
                    blur810();
                }
                if (Environment.OSVersion.Version.Major == 6 && (Environment.OSVersion.Version.Minor == 0) || Environment.OSVersion.Version.Minor == 1)
                {
                    blurVista7();
                }

                //this.Topmost = false;

                this.WindowState = WindowState.Normal;
                this.Focus();
                this.Activate();
            }
            catch (Exception)
            {
                this.Background = Brushes.White;
            }
        }

        private void Window_LostFocus(object sender, RoutedEventArgs e)
        {

            try
            {
                this.Close();
                MainWindow mainWindow = new MainWindow();
                mainWindow.WindowState = WindowState.Normal;
            }
            catch { }
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {

            try
            {
                this.Close();
                MainWindow mainWindow = new MainWindow();
                mainWindow.WindowState = WindowState.Normal;
            }
            catch { }
        }

        private void image1_MouseDown(object sender, MouseButtonEventArgs e)
        {

            try
            {
                this.Close();
                MainWindow mainWindow = new MainWindow();
                mainWindow.WindowState = WindowState.Normal;
            }
            catch { }
        }

        private void Window_Activated(object sender, EventArgs e)
        {
            this.Topmost = true;
            this.WindowState = WindowState.Normal;
            this.Topmost = false;
        }

        private void image1_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Delta > 0)
            {
                this.Width = this.Width + 16;
                this.Height = this.Height + 16;
                this.Top = this.Top - 8;
                this.Left = this.Left - 8;
            }
            else
            {
                this.Width = this.Width - 16;
                this.Height = this.Height - 16;
                this.Top = this.Top + 8;
                this.Left = this.Left + 8;
            }
            
        }
    }
}
