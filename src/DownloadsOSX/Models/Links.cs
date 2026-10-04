using DownloadsOSX.Services;

namespace DownloadsOSX.Models
{
    /// <summary>External links shown in the app (public so XAML can bind to them for tooltips).</summary>
    public static class Links
    {
        public static string GitHub => "https://github.com/" + UpdateService.GitHubRepo;
        public const string Developer = "https://fhnb.ru";
    }
}
