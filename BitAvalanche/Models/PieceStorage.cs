using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using BencodeNET.Torrents;

using Data = BitAvalanche.Core.Data;

namespace BitAvalanche;

public sealed class PieceStorage
{
    public Torrent Torrent { get; }

    private readonly TorrentFileMode _fileMode;
    private FileStream? _file;
    private readonly List<FileStream>? _files = new();

    public PieceStorage(Torrent torrent)
    {
        Torrent = torrent;
        _fileMode = torrent.FileMode;
        if (Torrent.File is not null)
            CreateFiles(Torrent.File);
        else
            CreateFiles(Torrent.Files);
    }

    public async Task StorePieceAsync(Data.Piece piece)
    {
        if (Torrent.File is not null)
            await StorePieceAsync(Torrent.File, piece);
        else
            await StorePieceAsync(Torrent.Files, piece);
    }

    public async Task<Data.Piece> GetPieceAsync(int pieceIdx)
    {
        if (Torrent.File is not null)
            return await GetPieceAsync(Torrent.File, pieceIdx);
        else
            return await GetPieceAsync(Torrent.Files, pieceIdx);
    }
    public long GetPieceLength(int piece) => GetPieceLength(Torrent, piece);
    public static long GetPieceLength(Torrent torrent, int piece)
    {
        if (torrent.File is not null)
            return GetPieceLength(torrent, torrent.File, piece);
        else if (torrent.Files is not null)
            return GetPieceLength(torrent, torrent.Files, piece);
        return 0;
    }

    private (int, int) GetFileAndOffsetFromPieceIdx(int pieceIdx)
    {
        if (_fileMode != TorrentFileMode.Multi)
        {
            throw new Exception("Can only calculate file idx on multi file torrents");
        }
        int i;
        for (i = 0; i < _files!.Count; i++)
        {
            var count = GetPieceCount(Torrent.Files[i].FileSize);
            if (pieceIdx < count)
            {
                break;
            }
            pieceIdx -= (int)count;
        }
        return (i, pieceIdx);
    }
    private long GetPieceCount(long fileLength)
    {
        var count = fileLength / Torrent.PieceSize;
        if (fileLength % Torrent.PieceSize > 0)
        {
            count++;
        }
        return count;
    }

    private static long GetPieceLength(Torrent torrent, SingleFileInfo file, int piece)
    {
        if (piece == torrent.NumberOfPieces - 1)
        {
            return file.FileSize % torrent.PieceSize;
        }
        return torrent.PieceSize;
    }
    private void CreateFiles(SingleFileInfo fileInfo)
    {
        FileStream file;
        try
        {
            file = File.Open(fileInfo.FileName, FileMode.OpenOrCreate);
            if (file.Length < fileInfo.FileSize)
            {
                file.SetLength(fileInfo.FileSize);
            }
        }
        catch (Exception e)
        {
            Logger.Warn(e.Message);
            return;
        }
        _file = file;
    }
    private async Task StorePieceAsync(SingleFileInfo file, Data.Piece piece)
    {
        _file!.Seek(piece.Idx * (int)Torrent.PieceSize, SeekOrigin.Begin);
        await _file!.WriteAsync(piece.Data, 0, piece.Data.Length);
    }
    private async Task<Data.Piece> GetPieceAsync(SingleFileInfo fileInfo, int pieceIdx)
    {
        var len = GetPieceLength(pieceIdx);
        var piece = new Data.Piece(pieceIdx, new byte[len]);
        await _file!.ReadExactlyAsync(
            piece.Data,
            pieceIdx * (int)Torrent.PieceSize,
            piece.Data.Length
        );
        return piece;
    }

    private static long GetPieceLength(Torrent torrent, MultiFileInfoList files, int piece)
    {
        if (files.Count == 0)
            return 0;

        MultiFileInfo? file = null;
        long filePieces = 0;

        var enumerator = files.GetEnumerator();
        while (enumerator.MoveNext())
        {
            file = enumerator.Current;
            filePieces = file.FileSize / torrent.PieceSize;
            if (file.FileSize % torrent.PieceSize > 0)
                filePieces++;
            if (filePieces > piece)
                break;
            piece -= (int)filePieces;
        }

        if (piece == filePieces! - 1)
        {
            return file!.FileSize % torrent.PieceSize;
        }
        return torrent.PieceSize;
    }
    private void CreateFiles(MultiFileInfoList files)
    {
        foreach (var fileInfo in Torrent.Files)
        {
            var file = File.Open(fileInfo.FileName, FileMode.OpenOrCreate);
            if (file.Length < fileInfo.FileSize)
            {
                file.SetLength(fileInfo.FileSize);
            }
            _files!.Add(file);
        }
    }
    private async Task StorePieceAsync(MultiFileInfoList files, Data.Piece piece)
    {
        var (fileIdx, off) = GetFileAndOffsetFromPieceIdx(piece.Idx);
        var file = _files![fileIdx];
        file.Seek(piece.Idx * (int)Torrent.PieceSize, SeekOrigin.Begin);
        await file.WriteAsync(piece.Data, 0, piece.Data.Length);
    }
    private async Task<Data.Piece> GetPieceAsync(MultiFileInfoList files, int pieceIdx)
    {
        var (fileIdx, off) = GetFileAndOffsetFromPieceIdx(pieceIdx);
        var file = _files![fileIdx];
        var len = GetPieceLength(pieceIdx);
        var piece = new Data.Piece(pieceIdx, new byte[len]);
        await file.ReadExactlyAsync(
            piece.Data,
            off * (int)Torrent.PieceSize,
            piece.Data.Length
        );
        return piece;
    }
}
