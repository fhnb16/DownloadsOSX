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
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.IO;
using System.Windows.Media.Animation;
using System.Windows.Shell;
using Microsoft.Win32;
using System.Net;
using System.Diagnostics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace downloadsOSX
{
    /// <summary>
    /// Логика взаимодействия для MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        string downloadsPath = null;
        public static string AppID = "0002";

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

        [DllImport("user32.dll")]
        static extern Int32 FlashWindowEx(ref FLASHWINFO pwfi);

        [StructLayout(LayoutKind.Sequential)]
        public struct FLASHWINFO
        {
            public UInt32 cbSize;
            public IntPtr hwnd;
            public Int32 dwFlags;
            public UInt32 uCount;
            public Int32 dwTimeout;
        }

        [Flags]
        public enum FlashMode
        {
            /// 
            /// Stop flashing. The system restores the window to its original state.
            /// 
            FLASHW_STOP = 0,
            /// 
            /// Flash the window caption.
            /// 
            FLASHW_CAPTION = 1,
            /// 
            /// Flash the taskbar button.
            /// 
            FLASHW_TRAY = 2,
            /// 
            /// Flash both the window caption and taskbar button.
            /// This is equivalent to setting the FLASHW_CAPTION | FLASHW_TRAY flags.
            /// 
            FLASHW_ALL = 3,
            /// 
            /// Flash continuously, until the FLASHW_STOP flag is set.
            /// 
            FLASHW_TIMER = 4,
            /// 
            /// Flash continuously until the window comes to the foreground.
            /// 
            FLASHW_TIMERNOFG = 12
        }

        public string VersionString = "root.fhnb.ru/DownloadsOSX/config.json";
        string newVer = null;
        string CurrentVersion = null;

        public MainWindow()
        {



            InitializeComponent();

            var reg = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");

            string name = (string)reg.GetValue("ProductName");

            if (((Environment.OSVersion.Version.Major == 10 && Environment.OSVersion.Version.Minor == 0) || (Environment.OSVersion.Version.Major > 6)) || name.Contains("Windows 10"))
            {
                window.Background = Brushes.Transparent;
                window.ResizeMode = ResizeMode.CanMinimize;
                window.WindowStyle = WindowStyle.None;
                window.AllowsTransparency = true;
                accent.Color = Colors.Transparent;
                furry.Color = Colors.Transparent;
                //MessageBox.Show(name);
            }
            else if (Environment.OSVersion.Version.Major == 6 && (Environment.OSVersion.Version.Minor == 0) || Environment.OSVersion.Version.Minor == 1)
            {
                window.Background = Brushes.Transparent;
                this.Background = Brushes.Transparent;
                window.WindowStyle = WindowStyle.ToolWindow;
                window.ResizeMode = ResizeMode.CanMinimize;
                window.AllowsTransparency = false;
                window.BorderBrush = Brushes.Transparent;
                accent.Color = Colors.Transparent;
                furry.Color = Colors.Transparent;
                window.BorderThickness = new Thickness(0);
            }
            else// if (Environment.OSVersion.Version.Major == 6 && (Environment.OSVersion.Version.Minor == 2) || Environment.OSVersion.Version.Minor == 3)
            {
                //MessageBox.Show(Environment.OSVersion.Version.Major + " " + Environment.OSVersion.Version.Minor + " " + name);
                try
                {
                    window.Background = Brushes.Transparent;
                    window.ResizeMode = ResizeMode.CanMinimize;
                    window.WindowStyle = WindowStyle.None;
                    window.AllowsTransparency = true;
                    try
                    {
                        accent.Color = SystemParameters.WindowGlassColor;
                    }
                    catch
                    {
                        accent.Color = Color.FromRgb(0, 34, 108);
                    }
                    furry.Color = Colors.Black;
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Err: " + ex);
                }
            }


            //window.ShowInTaskbar = true;
            window.WindowState = WindowState.Normal;

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

            }
            catch (Exception)
            {
                this.Background = Brushes.White;
                windowPlane.MouseDown -= window_MouseDown;
            }

            /*string downloadsPath = System.IO.Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.Personal));
            path = System.IO.Path.Combine(path, "Downloads");

            

            var di = new DirectoryInfo(path);

            var result = di.GetFiles().OrderByDescending(x => x.LastWriteTime).Take(10).ToList();

            foreach (FileInfo file in result)
            {
                listBox1.Items.Add(file.Name);
                listView1.Items.Add(file.Name);
            }*/

            /*DirectoryInfo dinfo = new DirectoryInfo(path);
            FileInfo[] Files = dinfo.GetFiles("*");*/

            downloadsPath = System.IO.Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.Personal));
            downloadsPath = System.IO.Path.Combine(downloadsPath, "Downloads");
            CurrentVersion = Convert.ToString(System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(2));

            //this.Title = "Downloads";
            //button1.Content = Convert.ToString(Properties.Settings.Default["locale"]);

            try
            {
                using (var client = new System.Net.WebClient())
                {
                    //ServicePointManager.SecurityProtocol |= SecurityProtocolType.Ssl3 | SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
                    try
                    {
                        newVer = (string)JsonConvert.DeserializeObject<JToken>(new TimedWebClient { Timeout = 10000 }.DownloadString("https://" + VersionString))["LatestVersion"];
                    }
                    catch
                    {
                        newVer = (string)JsonConvert.DeserializeObject<JToken>(new TimedWebClient { Timeout = 5000 }.DownloadString("http://" + VersionString))["LatestVersion"];
                    }
                }
                //this.Title = this.Title + " v." + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(2);
                if (Convert.ToString(System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(2)) == newVer)
                {
                    upgradeBtn.Visibility = Visibility.Hidden;
                }
                else
                {
                    upgradeBtn.Visibility = Visibility.Visible;
                }
            }
            catch (Exception)
            {
                //MessageBox.Show("Can't check updates!");
                newVer = Convert.ToString(System.Reflection.Assembly.GetEntryAssembly().GetName().Version).Substring(0, Convert.ToString(System.Reflection.Assembly.GetEntryAssembly().GetName().Version).IndexOf(".") + 2);
                upgradeBtn.Visibility = Visibility.Hidden;
            }

            Run();


            window.WindowState = WindowState.Minimized;

        }

        public void blurVista7()
        {

            try
            {
                windowPlane.MouseDown -= window_MouseDown;
                window.Background = Brushes.Transparent;
                accent.Color = Colors.Transparent;
                furry.Color = Colors.Transparent;
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
                filesView.Width = filesView.Width - 2;
                //kebab.Width = kebab.Width - 10;
                int hr = glassClass.DwmExtendFrameIntoClientArea(mainWindowSrc.Handle, ref margins);
                //
                if (hr < 0)
                {
                    //DwmExtendFrameIntoClientArea Failed
                }
            }
            catch (DllNotFoundException)
            {}
            catch (Exception)
            {}

        }

        public void blur810()
        {
            try
            {
                windowPlane.MouseDown += window_MouseDown;
                _blurBackgroundColor = 0xFFFFFF; // 0x990000
                _blurOpacity = 1;

                var windowHelper = new WindowInteropHelper(window);
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

        private void window_MouseDown(object sender, MouseButtonEventArgs e)
        {

            try
            {
                if (e.ChangedButton == MouseButton.Left)
                    DragMove();
            }
            catch { }
        }

        FileSystemWatcher watcher;

        public void Run()
        {

            reloadList();

            watcher = new FileSystemWatcher();
            watcher.Path = downloadsPath;
            /* Watch for changes in LastAccess and LastWrite times, and
               the renaming of files or directories. */
            watcher.NotifyFilter = NotifyFilters.LastAccess | NotifyFilters.LastWrite
               | NotifyFilters.FileName | NotifyFilters.DirectoryName;
            // Only watch text files.
            watcher.Filter = "*.*";

            // Add event handlers.
            watcher.Changed += new FileSystemEventHandler(OnChanged);
            watcher.Created += new FileSystemEventHandler(OnCreated);
            watcher.Deleted += new FileSystemEventHandler(OnChanged);
            watcher.Renamed += new RenamedEventHandler(OnChanged);

            // Begin watching.
            watcher.EnableRaisingEvents = true;

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

        System.Collections.Generic.List<System.IO.FileInfo> result = null;

        public void reloadList()
        {

            var di = new DirectoryInfo(downloadsPath);

            System.Collections.Generic.List<System.IO.FileInfo> newResult = di.GetFiles().OrderByDescending(x => x.LastWriteTime).Take(10).ToList();

            if (result != newResult)
            {


                filesView.Items.Clear();
                result = newResult;
                int index = 0;

                result.Reverse();

                foreach (FileInfo file in result)
                {
                    //filesView.Items.Add(file.Name);

                    Item item2 = new Item();
                    item2.Name = file.Name;
                    item2.Index = index;

                    if (getFileExtension(Convert.ToString(file.Name)).Contains("png") || getFileExtension(Convert.ToString(file.Name)).Contains("jpg") || getFileExtension(Convert.ToString(file.Name)).Contains("jpeg") || getFileExtension(Convert.ToString(file.Name)).Contains("gif") || getFileExtension(Convert.ToString(file.Name)).Contains("bmp"))
                    {
                        item2.Image = BitmapFromUri(new Uri(System.IO.Path.Combine(downloadsPath, file.Name)));
                    }
                    else
                    {
                        int THUMB_SIZE = 50; // 256
                        System.Drawing.Bitmap thumbnail = WindowsThumbnailProvider.GetThumbnail(
                           System.IO.Path.Combine(downloadsPath, file.Name), THUMB_SIZE, THUMB_SIZE, ThumbnailOptions.None);
                        item2.Image = ImageSourceFromBitmap(thumbnail);
                    }


                    filesView.Items.Add(item2);
                    index++;
                }

            }

            


        }

        [DllImport("gdi32.dll", EntryPoint = "DeleteObject")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteObject([In] IntPtr hObject);

        public ImageSource ImageSourceFromBitmap(System.Drawing.Bitmap bmp)
        {
            var handle = bmp.GetHbitmap();
            try
            {
                return Imaging.CreateBitmapSourceFromHBitmap(handle, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            }
            finally { DeleteObject(handle); }
        }

        public void OnChanged(object source, FileSystemEventArgs e)
        {
            var d = Application.Current.Dispatcher;
            if (d.CheckAccess())
                reloadList();
            else
                d.BeginInvoke((Action)reloadList);
            
        }

        public void OnCreated(object source, FileSystemEventArgs e)
        {

            try
            {
                watcher.EnableRaisingEvents = false;
                var d = Application.Current.Dispatcher;
                if (d.CheckAccess())
                    Flash();
                else
                    d.BeginInvoke((Action)Flash);
            }

            finally
            {
                watcher.EnableRaisingEvents = true;
            }
        }

        private void button1_Click(object sender, RoutedEventArgs e)
        {
            Flash();
        }

        public static void Flash()
        {

            FLASHWINFO fw = new FLASHWINFO();

            fw.cbSize = Convert.ToUInt32(Marshal.SizeOf(typeof(FLASHWINFO)));
            fw.hwnd = new WindowInteropHelper(Application.Current.MainWindow).Handle;
            fw.dwFlags = 2;
            fw.uCount = 1;

            FlashWindowEx(ref fw);


        }

        private void window_Deactivated(object sender, EventArgs e)
        {

            window.WindowState = WindowState.Minimized;
        }

        private void window_Activated(object sender, EventArgs e)
        {
            this.Left = (GetMousePosition().X - this.Width);
            if (Environment.OSVersion.Version.Major == 6 && (Environment.OSVersion.Version.Minor == 0) || Environment.OSVersion.Version.Minor == 1)
            {
                this.Top = (GetMousePosition().Y - (610));
            }
            else
            {
                this.Top = (GetMousePosition().Y + this.Height);
            }

            this.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, (Duration)TimeSpan.FromSeconds(.3)));



            /*
            DoubleAnimation animation2 = new DoubleAnimation(0, 512,
                                    (Duration)TimeSpan.FromSeconds(.2));
            this.BeginAnimation(Rectangle.HeightProperty, animation2);
             */
            
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetCursorPos(ref Win32Point pt);

        [StructLayout(LayoutKind.Sequential)]
        internal struct Win32Point
        {
            public Int32 X;
            public Int32 Y;
        };
        public static Point GetMousePosition()
        {
            Win32Point w32Mouse = new Win32Point();
            GetCursorPos(ref w32Mouse);
            return new Point(w32Mouse.X, w32Mouse.Y);
        }

        private void menuBtn_MouseDown(object sender, MouseButtonEventArgs e)
        {
            ContextMenu menu = this.FindResource("menuButton") as ContextMenu;
            menu.PlacementTarget = sender as Button;
            menu.IsOpen = true;
        }

        private void exitItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Application.Current.Shutdown();
            }
            catch
            {
                App.Current.Shutdown();
            }
            finally
            {
                Environment.Exit(0);
            }
        }

        private void minimizeItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                window.WindowState = WindowState.Minimized;
            }
            catch { }
        }

        private void openDownloadsItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(downloadsPath);
            }
            catch { }
        }

        private void DockPanel_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space)
            {
                /*var item = filesView.Items[filesView.SelectedIndex];

                MessageBox.Show(Convert.ToString(item.Name));*/

                try
                {
                    foreach (Item item in filesView.Items)
                    {
                        if (filesView.SelectedIndex == item.Index)
                        {
                            if (getFileExtension(Convert.ToString(item.Name)).Contains("png") || getFileExtension(Convert.ToString(item.Name)).Contains("jpg") || getFileExtension(Convert.ToString(item.Name)).Contains("jpeg") || getFileExtension(Convert.ToString(item.Name)).Contains("gif"))
                            {
                                Preview preview = new Preview(System.IO.Path.Combine(downloadsPath, item.Name));
                                preview.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                                preview.Show();
                            }
                            //System.Diagnostics.Process.Start(System.IO.Path.Combine(downloadsPath, Convert.ToString(item.Name)));
                            //MessageBox.Show(Convert.ToString(item.Name));
                        }
                        //MessageBox.Show(Convert.ToString(item.Name));

                    }
                }
                catch { }
                //var item = filesView.Items[filesView.SelectedIndex];
                //var processId = item.SubItems[1].Text;
                //ListViewItem item = filesView.SelectedItems[filesView.SelectedIndex];
                //MessageBox.Show(Convert.ToString(filesView.SelectedItems[filesView.SelectedIndex].SubItems[1].Text));
                /*Preview preview = new Preview(item.Content);
                preview.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                preview.Show();*/
            }
        }

        public string getFileExtension(string fileName)
        {
            return fileName.Substring(fileName.LastIndexOf(".") + 1);
        }

        private void filesView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            try
            {
                foreach (Item item in filesView.Items)
                {
                    if (filesView.SelectedIndex == item.Index)
                    {
                        System.Diagnostics.Process.Start(System.IO.Path.Combine(downloadsPath, Convert.ToString(item.Name)));
                        
                    }

                }
            }
            catch { }
        }

        private void window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                Application.Current.Shutdown();
            }
            catch
            {
                App.Current.Shutdown();
            }
            finally
            {
                Environment.Exit(0);
            }
        }

        private void helpItem_Click(object sender, RoutedEventArgs e)
        {
            help guide = new help();
            guide.Show();
        }

        private void filesView_DragOver(object sender, DragEventArgs e)
        {
            fileDrag();
        }

        public void fileDrag()
        {
            try
            {
                foreach (Item item in filesView.Items)
                {
                    if (filesView.SelectedIndex == item.Index)
                    {
                        if (File.Exists(System.IO.Path.Combine(downloadsPath, item.Name)))
                        {
                            /*string[] array = { System.IO.Path.Combine(downloadsPath, item.Name) };
                            var data = new DataObject(DataFormats.FileDrop, array);
                            filesView.DoDragDrop(data, DragDropEffects.Copy);*/
                            //var data = new DataObject(DataFormats.FileDrop, System.IO.Path.Combine(downloadsPath, item.Name));
                            //DragDrop.DoDragDrop(System.IO.Path.Combine(downloadsPath, item.Name), data, DragDropEffects.Copy);
                            var data = new DataObject(DataFormats.FileDrop, System.IO.Path.Combine(downloadsPath, item.Name));
                            data.SetData(DataFormats.FileDrop, System.IO.Path.Combine(downloadsPath, item.Name));
                            DragDrop.DoDragDrop(filesView, data, DragDropEffects.Copy);

                        }

                    }

                }
            }
            catch { }
        }

        private void aboutItem_Click(object sender, RoutedEventArgs e)
        {

            System.Diagnostics.Process.Start("https://apps.fhnb.ru/osxInstaller/");
        }

        //public string InstallerPathString = "//root.fhnb.ru/DownloadsOSX/installer/Downloads_Setup.exe";
        //public string InstallerNameString = "Downloads_Setup.exe";

        private void upgradeBtn_Click(object sender, RoutedEventArgs e)
        {
            update UndateWindow = new update(CurrentVersion, this.newVer);
            UndateWindow.Show();
            UndateWindow.Topmost = true;
        }

        public System.Drawing.Point ConvertBack(object value)
        {
            System.Windows.Point wp = (System.Windows.Point)value;
            return new System.Drawing.Point((int)wp.X, (int)wp.Y);
        }

        private void filesView_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Right)
            {
                /*if (filesView.FocusedItem.Bounds.Contains(e.Location))
                {
                    contextMenuStrip1.Show(Cursor.Position);
                }*/
                try
                {
                    foreach (Item item in filesView.Items)
                    {
                        if (filesView.SelectedIndex == item.Index)
                        {
                            ShellContextMenu ctxMnu = new ShellContextMenu();
                            FileInfo[] arrFI = new FileInfo[1];
                            arrFI[0] = new FileInfo(System.IO.Path.Combine(downloadsPath, Convert.ToString(item.Name)));
                            Point p = e.GetPosition(this);
                            double x = p.X;
                            double y = p.Y;
                            ctxMnu.ShowContextMenu(arrFI, ConvertBack(this.PointToScreen(new Point(x, y))));

                            //System.Diagnostics.Process.Start(System.IO.Path.Combine(downloadsPath, Convert.ToString(item.Name)));

                        }

                    }
                }
                catch { }
            }
            else
            {

                fileDrag();

            }

        }
    }

    public class glassClass
    {

        [StructLayout(LayoutKind.Sequential)]
        public struct MARGINS
        {
            public int cxLeftWidth;      // width of left border that retains its size
            public int cxRightWidth;     // width of right border that retains its size
            public int cyTopHeight;      // height of top border that retains its size
            public int cyBottomHeight;   // height of bottom border that retains its size
        };

        [DllImport("DwmApi.dll")]
        public static extern int DwmExtendFrameIntoClientArea(
            IntPtr hwnd,
            ref MARGINS pMarInset);


    }

    public class Item
    {
        public string Name { get; set; }
        public ImageSource Image { get; set; }
        public int Index { get; set; }
    }

    public class TimedWebClient : WebClient
    {
        // Timeout in milliseconds, default = 600,000 msec
        public int Timeout { get; set; }

        public TimedWebClient()
        {
            this.Timeout = 50000;
        }

        protected override WebRequest GetWebRequest(Uri address)
        {
            var objWebRequest = base.GetWebRequest(address);
            objWebRequest.Timeout = this.Timeout;
            return objWebRequest;
        }
    }
}