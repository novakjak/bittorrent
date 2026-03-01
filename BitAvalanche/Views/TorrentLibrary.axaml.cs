using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

using BitAvalanche.ViewModels;

namespace BitAvalanche.Views;

public partial class TorrentLibrary : Window
{
    public TorrentLibrary()
    {
        InitializeComponent();
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

    public async void AddTorrent(object? sender, RoutedEventArgs args)
    {
        var files = await FileDialogHandler();
        if (files.Count() == 0)
            return;
        foreach (var file in files)
        {
            var vm = new AddTorrentDialogViewModel();
            var window = new AddTorrentDialog()
            {
                DataContext = vm
            };
            bool closed = false;
            vm.CloseRequested += (_, _) => closed = true;
            vm.MetaInfoFile = file;
            if (closed)
                continue;
            var confirmed = await window.ShowDialog<bool>(this);
            var ctx = (TorrentLibraryViewModel)DataContext!;
            if (vm.MetaInfo is null)
                continue;
            ctx.AddTorrent(vm.MetaInfo, vm.SaveLocation.Path.AbsolutePath);
        }
    }
}
