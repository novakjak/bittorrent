using System;
using System.Linq;
using System.Collections.Generic;
using BencodeNET.Torrents;

namespace bittorrent.Core;

public static class TorrentUtils
{
	public static long GetPieceLength(Torrent torrent, int piece)
	{
		switch (torrent.FileMode)
		{
			case TorrentFileMode.Single: {
				var file = torrent.File;
				if (piece == torrent.NumberOfPieces - 1)
				{
					return file.FileSize % torrent.PieceSize;
				}
				return torrent.PieceSize;
			}
			case TorrentFileMode.Multi: {
				var files = torrent.Files;
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
		}
		throw new ArgumentException("Torrent is without files");
	}
}
