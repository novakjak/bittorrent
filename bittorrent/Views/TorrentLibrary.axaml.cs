using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

using bittorrent.ViewModels;

namespace bittorrent.Views;

public partial class TorrentLibrary : Window
{
    private IDisposable? _fileDialogInteractionDisposable = null;

    public TorrentLibrary()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        _fileDialogInteractionDisposable?.Dispose();

        if (DataContext is TorrentLibraryViewModel vm)
        {
            _fileDialogInteractionDisposable = vm.SelectFiles.RegisterHandler(FileDialogHandler);
        }

        base.OnDataContextChanged(e);
    }

    private async Task<IEnumerable<IStorageFile>> FileDialogHandler()
    {
        var files = await TopLevel.GetTopLevel(this)!
            .StorageProvider
            .OpenFilePickerAsync(new FilePickerOpenOptions
            {
                AllowMultiple = true,
                Title = "Select .torrent files",
            });
        return files;
    }
}
