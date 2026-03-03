using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using BencodeNET.Parsing;
using BT = BencodeNET.Torrents;

namespace BitAvalanche.ViewModels;

public partial class AddTorrentDialogViewModel : ViewModelBase
{
    [ObservableProperty]
    private IStorageFile? _metaInfoFile;

    [ObservableProperty]
    private BT.Torrent? _metaInfo;

    [ObservableProperty]
    private IStorageFolder? _saveLocation;

    public ObservableCollection<TorrentFileViewModel> Files { get; private set; } = new();

    public event EventHandler? CloseRequested;

    async partial void OnMetaInfoFileChanged(IStorageFile? oldValue, IStorageFile? newValue)
    {
        if (newValue is null)
            return;
        SaveLocation = await newValue.GetParentAsync();
        try
        {
            var parser = new BencodeParser();
            var dataStream = await newValue.OpenReadAsync();
            MetaInfo = parser.Parse<BT.Torrent>(dataStream);
        }
        catch
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
            return;
        }
        UpdateFiles();
    }

    private void UpdateFiles()
    {
        if (MetaInfo is null)
        {
            Files.Clear();
            return;
        }
        if (MetaInfo.FileMode == BT.TorrentFileMode.Single)
        {
            Files.Add(new TorrentFileViewModel(MetaInfo.File!.FileName));
        }
        else
        {
            var files = CreateFileTree(MetaInfo.Files!);
            foreach (var file in files)
                Files.Add(file);
        }
    }

    private IEnumerable<TorrentFileViewModel> CreateFileTree(BT.MultiFileInfoList files)
    {
        var root = new TorrentFileViewModel("Root", new());
        foreach (var file in files)
        {
            var (dirsMissing, dir) = FindNode(file.Path, root);
            for (TorrentFileViewModel currentDir = dir; dirsMissing > 0; dirsMissing--)
            {
                var newDir = new TorrentFileViewModel(file.Path[file.Path.Count() - dirsMissing]);
                currentDir.AddChild(newDir);
                currentDir = newDir;
            }
        }
        return root.Children as IEnumerable<TorrentFileViewModel> ?? new List<TorrentFileViewModel>();
    }

    private (int dirsMissing, TorrentFileViewModel highestNodeFound) FindNode(IEnumerable<string> pathComponents, TorrentFileViewModel root)
    {
        if (pathComponents.Count() == 0)
            return (0, root);
        var pathComponent = pathComponents.First();
        var dir = root.Children?.FirstOrDefault(n => n?.Name == pathComponent, null);
        if (dir is null)
            return (pathComponents.Count(), root);
        return FindNode(pathComponents.Skip(1), dir);
    }
}
