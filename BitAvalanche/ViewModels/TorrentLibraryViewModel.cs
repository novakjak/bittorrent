using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

using BitAvalanche.Core;
using BitAvalanche.Models;

using CommunityToolkit.Mvvm.Input;

using BT = BencodeNET.Torrents;

namespace BitAvalanche.ViewModels;

public partial class TorrentLibraryViewModel : ViewModelBase
{
    public ObservableCollection<TorrentTaskViewModel> Torrents { get; } = new();

    private readonly ConnectionListener _listener = new(Config.Get().DefaultPort);

    public TorrentLibraryViewModel()
    {
        _listener.Start();
    }

    public void AddTorrent(BT.Torrent metainfo, string saveLocation)
        => Torrents.Add(new TorrentTaskViewModel(metainfo, saveLocation));

    [RelayCommand]
    public async Task RemoveTorrent(TorrentTaskViewModel torrent)
        => Torrents.Remove(torrent);

    private void HandleNewPeer(object? sender, (INetworkClient client, Peer peer, byte[] infoHash) args)
    {
        var taskVM = Torrents.First(t => t.InfoHash == args.infoHash);
        if (taskVM is null)
            return;
        taskVM.Task.AddPeer(args.client, args.peer);
        Logger.Info($"Peer {args.peer} connected to client");
    }
}
