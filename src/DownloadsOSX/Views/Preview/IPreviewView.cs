using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using DownloadsOSX.Models;

namespace DownloadsOSX.Views.Preview
{
    /// <summary>One kind of quick-look content (image, media, text, shell handler, info card).</summary>
    internal interface IPreviewView
    {
        FrameworkElement Element { get; }

        /// <summary>Loads the item. Returns false if this view can't show it (the window then falls back).</summary>
        Task<bool> LoadAsync(FileEntry entry, CancellationToken ct);

        /// <summary>Preferred content size in DIPs, known after a successful load.</summary>
        Size NaturalSize { get; }

        /// <summary>Extra header info, e.g. "1920 × 1080" or "3:45".</summary>
        string Info { get; }

        /// <summary>Gives the view a chance to handle a key before the window does.</summary>
        bool HandleKey(KeyEventArgs e);

        /// <summary>Stops playback / releases files.</summary>
        void Unload();
    }
}
