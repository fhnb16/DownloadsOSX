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
using Microsoft.Win32;

namespace downloadsOSX
{
    /// <summary>
    /// Логика взаимодействия для help.xaml
    /// </summary>
    public partial class help : Window
    {
        public help()
        {
            InitializeComponent();

            this.Title = this.Title + " v." + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(2);
        }


        private void Window_Deactivated(object sender, EventArgs e)
        {

        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {

            this.Focus();
            this.Activate();
            buttonRegistryCheck();
        }

        private void buttonRegistryCheck()
        {

            if (isOnRegistryStartup("DownloaderOSX"))
            {
                button1.Content = (string)FindResource("autoStartBtnOK");
            }
            else
            {
                button1.Content = (string)FindResource("autoStartBtn");

            }
        }

        private bool isOnRegistryStartup(string key)
        {
            RegistryKey RegistryKey = Registry.CurrentUser.OpenSubKey
                     ("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
            if (RegistryKey.GetValue(key) != null)
            {
                return true;

            }
            return false;

        }

        private void button1_Click(object sender, RoutedEventArgs e)
        {

            if (isOnRegistryStartup("DownloaderOSX"))
            {
                Microsoft.Win32.RegistryKey reg;
                reg = Microsoft.Win32.Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run\\");
                try
                {
                    reg.DeleteValue("DownloaderOSX");
                    reg.Close();

                }
                catch (Exception ex) { MessageBox.Show(ex.Message); }
                finally
                {
                    buttonRegistryCheck();
                }
            }
            else
            {
                const string name = "DownloaderOSX";
                string ExePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                Microsoft.Win32.RegistryKey reg;
                reg = Microsoft.Win32.Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run\\");
                try
                {

                    reg.SetValue(name, ExePath);

                    reg.Close();

                }
                catch (Exception ex) { MessageBox.Show(ex.Message); }
                finally
                {
                    buttonRegistryCheck();
                }
            }
        }
    }
}
