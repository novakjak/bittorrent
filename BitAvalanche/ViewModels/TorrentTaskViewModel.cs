using System.Diagnostics;
using System.Threading.Tasks;

using BitAvalanche.Models;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using BT = BencodeNET.Torrents;

namespace BitAvalanche.ViewModels;

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

    public void Start() => Task.Start();
    public async void Stop() => await Task.Stop();
    public void ShowLocationOnDisk()
    {
        var path = Task.Path;
        using var p = new Process();
        p.StartInfo.FileName = path;
        p.StartInfo.UseShellExecute = true;
        p.Start();
    }

    public TorrentTask Task { get; private set; }

    public TorrentTaskViewModel(BT.Torrent t, string saveLocation)
    {
        Name = t.DisplayName;
        InfoHash = t.OriginalInfoHashBytes;
        Task = new TorrentTask(t, saveLocation);
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
