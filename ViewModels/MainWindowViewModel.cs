using System;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using bittorrent.Services;

namespace bittorrent.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    public ObservableCollection<TorrentViewModel> Torrents { get; } = new ObservableCollection<TorrentViewModel>();

    [RelayCommand]
    public async Task AddTorrentCommand()
    {
        if (Application.Current is not App app)
        {
            throw new Exception("Incorrect app initialized");
        }
        var service = new FileService(app.MainWindow?.StorageProvider);
        var ms = await service.OpenTorrentPicker();
        foreach (var m in ms) {
            Torrents.Add(new TorrentViewModel(m));
        }
    }
}
