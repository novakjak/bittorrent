using System;
using System.Linq;
using System.IO;
using System.Net.Sockets;
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
using bittorrent.Models;

namespace bittorrent.ViewModels;

public partial class TorrentLibraryViewModel : ViewModelBase
{
    public ObservableCollection<TorrentTaskViewModel> Torrents { get; } = new();
    public FileDialogInteraction SelectFiles { get; } = new();

    private ConnectionListener _listener = new(8085);

    public TorrentLibraryViewModel()
    {
        _listener.Start();
    }

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

    private void HandleNewPeer(object? sender, (INetworkClient client, Peer peer, byte[] infoHash) args)
    {
        var taskVM = Torrents.First(t => t.InfoHash == args.infoHash);
        if (taskVM is null)
            return;
        taskVM.Task.AddPeer(args.client, args.peer);
        Console.WriteLine("===== peer connected to us =====");
    }
}
