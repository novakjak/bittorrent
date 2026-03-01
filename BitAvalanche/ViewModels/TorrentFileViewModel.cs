using System;
using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using BitAvalanche.ViewModels;

public partial class TorrentFileViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _name = String.Empty;

    public ObservableCollection<TorrentFileViewModel>? Children { get; private set; }

    public TorrentFileViewModel(string name)
    {
        Name = name;
    }

    public TorrentFileViewModel(string name, ObservableCollection<TorrentFileViewModel> children)
    {
        Name = name;
        Children = children;
    }

    public void AddChild(TorrentFileViewModel child)
    {
        Children ??= new();
        Children.Add(child);
    }
}
