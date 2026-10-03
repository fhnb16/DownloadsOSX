using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace downloadsOSX
{
    /// <summary>
    /// Логика взаимодействия для update.xaml
    /// </summary>
    public partial class update : Window
    {

        public string InstallerInfoString = "root.fhnb.ru/apps.json";
        public string InstallerPathString = "";
        public string InstallerPathString1 = "root.fhnb.ru/installer/dist/";
        public string InstallerPathString2 = "/Omicron Repository.exe";
        public string InstallerNameString = "Omicron Repository.exe";
        public string newVersion = "0.0";
        public string UpdaterLatest = "0.0";
        public static string TempPath = "Installer";

        public update(string CurrentVersion, string NewVer)
        {
            InitializeComponent();
            label_versions.Text = "Current: " + CurrentVersion + ", New Version: " + NewVer;
            newVersion = NewVer;
            TempPath = Path.Combine(Path.GetTempPath(), TempPath);

            try
            {
                UpdaterLatest = (string)JsonConvert.DeserializeObject<JToken>(new TimedWebClient { Timeout = 10000 }.DownloadString("https://" + InstallerInfoString))["LatestVersion"];
            }
            catch
            {
                try
                {
                    UpdaterLatest = (string)JsonConvert.DeserializeObject<JToken>(new TimedWebClient { Timeout = 5000 }.DownloadString("http://" + InstallerInfoString))["LatestVersion"];
                }
                catch
                {
                    UpdaterLatest = "0.0";
                }
            }
            if (UpdaterLatest != "0.0")
            {
                InstallerPathString = InstallerPathString1 + UpdaterLatest + InstallerPathString2;
            }
            else
            {
                this.Close();
            }

            //MessageBox.Show(InstallerPathString);
            /*updateBtn.Foreground = (SolidColorBrush)new BrushConverter().ConvertFromString("#7FFFFFFF");
            skipBtn.Foreground = (SolidColorBrush)new BrushConverter().ConvertFromString("#6BFFFFFF");
            updateBtn.Background = (SolidColorBrush)new BrushConverter().ConvertFromString("#7F292929");*/
        }

        private void UpdateBtn_MouseDown(object sender, MouseButtonEventArgs e)
        {

        }

        private void SkipBtn_MouseUp(object sender, MouseButtonEventArgs e)
        {

        }

        private void UpdateBtn_MouseUp(object sender, MouseButtonEventArgs e)
        {

        }

        private void SkipBtn_MouseEnter(object sender, MouseEventArgs e)
        {
            //skipBtn.Foreground = (SolidColorBrush)new BrushConverter().ConvertFromString("#A3FFFFFF");
        }

        private void SkipBtn_MouseLeave(object sender, MouseEventArgs e)
        {
            //skipBtn.Foreground = (SolidColorBrush)new BrushConverter().ConvertFromString("#6BFFFFFF");
        }

        private void UpdateBtn_MouseEnter(object sender, MouseEventArgs e)
        {
            /*updateBtn.Foreground = (SolidColorBrush)new BrushConverter().ConvertFromString("#FFFFFFFF");
            updateBtn.Background = (SolidColorBrush)new BrushConverter().ConvertFromString("#FF292929");*/
        }

        private void UpdateBtn_MouseLeave(object sender, MouseEventArgs e)
        {
            /*updateBtn.Foreground = (SolidColorBrush)new BrushConverter().ConvertFromString("#7FFFFFFF");
            updateBtn.Background = (SolidColorBrush)new BrushConverter().ConvertFromString("#7F292929");*/
        }

        private void UpdateBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                /*if (!System.IO.File.Exists(System.IO.Path.Combine(AppContext.BaseDirectory, InstallerNameString)))
                {
                    using (var client = new System.Net.WebClient())
                    {
                        client.DownloadFile(InstallerPathString, System.IO.Path.Combine(AppContext.BaseDirectory, InstallerNameString));
                    }
                    
                    Process.Start(System.IO.Path.Combine(AppContext.BaseDirectory, InstallerNameString));

                }
                else
                {
                    Process.Start(System.IO.Path.Combine(AppContext.BaseDirectory, InstallerNameString));
                }*/

                if (Directory.Exists(TempPath))
                {
                    Directory.CreateDirectory(TempPath);
                }

                using (var client = new System.Net.WebClient())
                {
                    try
                    {
                        client.DownloadFile("https://" + InstallerPathString, System.IO.Path.Combine(TempPath, InstallerNameString));
                    }
                    catch
                    {
                        client.DownloadFile("http://" + InstallerPathString, System.IO.Path.Combine(TempPath, InstallerNameString));
                    }
                }

                string fullPath = System.IO.Path.Combine(TempPath, InstallerNameString);
                string appID = MainWindow.AppID;
                string appLoc = System.IO.Path.GetDirectoryName(fullPath);
                string appExec = System.IO.Path.GetFileName(fullPath);

                Process prs = new Process();
                //
                prs.StartInfo.WorkingDirectory = appLoc;
                prs.StartInfo.FileName = fullPath;

                // Set as administrator.
                prs.StartInfo.UseShellExecute = true;
                //prs.StartInfo.Verb = "runas";

                prs.StartInfo.Arguments = appID;  // Assuming that this is "MSSQL$SQLEXPRESS Stop";
                prs.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                //
                prs.Start();

                Environment.Exit(0);
            }
            catch (Exception ex) { MessageBox.Show(ex.ToString()); return; }

        }
    }
}
