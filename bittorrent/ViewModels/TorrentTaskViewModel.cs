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
    private double _percentComplete = 0.0;

    private TorrentTask _task;

    public TorrentTaskViewModel(BT.Torrent t)
    {
        Name = t.DisplayName;
        _task = new TorrentTask(t);
        _task.DownloadedPiece += HandleDownloadedPiece;
        _task.Start();
    }

    public void HandleDownloadedPiece(object? sender, (int pieceIdx, double completion) args)
    {
        PercentComplete = args.completion;
    }
}
