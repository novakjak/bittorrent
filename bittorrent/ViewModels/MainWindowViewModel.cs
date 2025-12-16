using System;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using BencodeNET.Parsing;
using BT = BencodeNET.Torrents;
using bittorrent.Core;

namespace bittorrent.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    public ObservableCollection<TorrentTaskViewModel> Torrents { get; } = new();
    public FileDialogInteraction SelectFiles { get; } = new();

    [RelayCommand]
    public async Task AddTorrentCommand()
    {
        var files = await SelectFiles.Handle();
        var parser = new BencodeParser();
        foreach (var file in files) {
            var metainfo = parser.Parse<BT.Torrent>(file.Path.LocalPath);
            Torrents.Add(new TorrentTaskViewModel(metainfo));
        }
    }
}
