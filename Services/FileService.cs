using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using BencodeNET.Parsing;
using BencodeNET.Torrents;

namespace bittorrent.Services;

public class FileService
{
    private readonly IStorageProvider _provider;

    public async Task<IEnumerable<IStorageFile>> OpenPicker()
    {
        var files = await _provider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Text File",
            AllowMultiple = true,
        });
        return files;
    }

    public async Task<IEnumerable<Torrent>> OpenTorrentPicker()
    {
        var files = await OpenPicker();
        var parser = new BencodeParser();
        return files
            .Select(file =>
            {
                try
                {
                    using (var f = File.Open(file.Path.LocalPath.ToString(), FileMode.Open))
                    {
                        return parser.Parse<Torrent>(f);
                    }
                }
                catch
                {
                    return null;
                }
            })
            .Where(t => t is not null)
            .Select(t => t!)
            .ToList();
    }

    public FileService(IStorageProvider? provider)
    {
        if (provider is null)
        {
            throw new NullReferenceException("Missing StorageProvider.");
        }
        _provider = provider;
    }
}
