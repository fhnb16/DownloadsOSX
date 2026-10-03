using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Windows;
using System.Globalization;
using System.Net;

namespace downloadsOSX
{
    /// <summary>
    /// Логика взаимодействия для App.xaml
    /// </summary>
    public partial class App : Application
    {

        private void App_Startup(object sender, StartupEventArgs e)
        {
            if (Environment.OSVersion.Version.Major < 6)
            {
                MessageBox.Show("OS Not Supported");
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

            SetLanguageDictionary();

            /*upgrade u = new upgrade();
            u.upgradeCheck(false, (string)FindResource("newUpdatesFounded"), (string)FindResource("oldVersion"), (string)FindResource("newVersion"), (string)FindResource("upgradeTitle"), (string)FindResource("noupdates"));*/

            MainWindow mainWindow = new MainWindow();
            mainWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            mainWindow.Show();
        }

        private void SetLanguageDictionary()
        {

            string lang = "en-US";

            switch (CultureInfo.InstalledUICulture.Name)
            {
                case "en-US": lang = "en-US";
                    break;
                case "en-GB": lang = "en-US";
                    break;
                case "ru-RU": lang = "ru-RU";
                    break;
                case "be-BY": lang = "ru-RU";
                    break;
                case "uk-UA": lang = "uk-UA";
                    break;
                case "ru-BY": lang = "ru-RU";
                    break;
                case "ru-UA": lang = "ru-RU";
                    break;
                default:      lang = "en-US";
                    break;
            }

            ResourceDictionary dict = new ResourceDictionary();

            switch (lang)
            {
                case "en-US":
                    dict.Source = new Uri("..\\Resources\\lang.xaml",
                                  UriKind.Relative);
                    break;
                case "ru-RU":
                    dict.Source = new Uri("..\\Resources\\lang.ru-RU.xaml",
                                       UriKind.Relative);
                    break;
                case "uk-UA":
                    dict.Source = new Uri("..\\Resources\\lang.uk-UA.xaml",
                                       UriKind.Relative);
                    break;
                default:
                    dict.Source = new Uri("..\\Resources\\lang.xaml",
                                      UriKind.Relative);
                    break;
            }

            this.Resources.MergedDictionaries.Add(dict);
        } 
    }
}
