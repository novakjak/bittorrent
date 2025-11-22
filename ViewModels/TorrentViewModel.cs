using CommunityToolkit.Mvvm.ComponentModel;
using BencodeNET.Torrents;

namespace bittorrent.ViewModels;

public partial class TorrentViewModel : ViewModelBase {
    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    public double _percentComplete = 0.0;

    public TorrentViewModel(Torrent t)
    {
        _name = t.DisplayName;
    }
}
