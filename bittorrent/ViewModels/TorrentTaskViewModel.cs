using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using BT = BencodeNET.Torrents;
using bittorrent.Models;

namespace bittorrent.ViewModels;

public partial class TorrentTaskViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private byte[] _infoHash = new byte[0];

    [ObservableProperty]
    private double _percentComplete = 0.0;

    [ObservableProperty]
    private int _peerCount = 0;

    [ObservableProperty]
    private int _downloaded = 0;

    [ObservableProperty]
    private int _uploaded = 0;

    public TorrentTask Task { get; private set; }

    public TorrentTaskViewModel(BT.Torrent t)
    {
        Name = t.DisplayName;
        InfoHash = t.OriginalInfoHashBytes;
        Task = new TorrentTask(t);
        Task.DownloadedPiece += HandleDownloadedPiece;
        Task.PeerCountChanged += HandlePeerCountChanged;
        Task.Start();
    }

    private void HandleDownloadedPiece(object? sender, (int pieceIdx, double completion) args)
    {
        PercentComplete = args.completion;
        Downloaded = Task.Downloaded;
    }
    private void HandlePeerCountChanged(object? sender, int count)
        => PeerCount = count;
}
