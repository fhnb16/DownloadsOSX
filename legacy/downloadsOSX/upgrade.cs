using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Net;
using System.Globalization;

namespace downloadsOSX
{
    class upgrade
    {
        string actualUrl;

        string version;

        public void upgradeCheck(bool ck, string founded, string oldv, string newv, string ut, string nup)
        {
            string appName = "downloadsOSX";
            string updaterName = "updater.exe";
            actualUrl = "https://root.fhnb.ru/DownloadsOSX/actualVersion.txt";
            string configUrl = "https://root.fhnb.ru/DownloadsOSX/config.ini";
            string installerUrl = "https://root.fhnb.ru/installer/get/dist/configure.php?config=";

            version = Convert.ToString(System.Reflection.Assembly.GetEntryAssembly().GetName().Version).Substring(0, Convert.ToString(System.Reflection.Assembly.GetEntryAssembly().GetName().Version).IndexOf(".") + 2);

            if (checkUpdates(ck, nup))
            {
                string newVer = null;
                using (var client = new WebClient())
                {
                    newVer = client.DownloadString(actualUrl);
                }
                MessageBoxResult result = MessageBox.Show(founded + Environment.NewLine + oldv + ": " + version + ", " + newv + ": " + newVer, ut, MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        System.IO.Directory.CreateDirectory(System.IO.Path.GetTempPath() + "\\" + appName);

                        string folderTemp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), appName);

                        string configFileUrl = configUrl;

                        using (var client = new WebClient())
                        {
                            client.DownloadFile(installerUrl + configFileUrl + "????????????????", System.IO.Path.Combine(folderTemp, updaterName));

                            System.Diagnostics.Process.Start(System.IO.Path.Combine(folderTemp, updaterName));
                        }

                        try
                        {
                            System.IO.Directory.Delete(folderTemp, true);
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
                    catch
                    {
                        MessageBox.Show("Error");
                    }

                }
            }
        }

        private bool checkUpdates(bool chk, string noupt)
        {
            try
            {
                string currVer = version;
                string newVer = null;
                using (var client = new WebClient())
                {
                    newVer = client.DownloadString(actualUrl);
                }
                if (currVer != newVer)
                {
                    return true;
                }
                else
                {
                    if (chk)
                    {
                        MessageBox.Show(noupt);
                    }
                    return false;
                }
            }
            catch
            {
                //MessageBox.Show("Error: "+ex);
                return false;

            }

        }
    }
}
