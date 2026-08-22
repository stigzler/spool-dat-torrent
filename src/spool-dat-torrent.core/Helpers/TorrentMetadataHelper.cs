using BencodeNET.Parsing;
using BencodeNET.Torrents;
using System;
using System.Collections.Generic;
using System.Text;

namespace SpoolDatTorrent.Core.Helpers
{
    public class TorrentMetadataHelper
    {
        public static string GetInfoHash(string torrentFilePath)
        {
            if (!File.Exists(torrentFilePath))
            {
                throw new FileNotFoundException("Torrent file not found.", torrentFilePath);
            }

            var parser = new BencodeParser();
            var torrent = parser.Parse<Torrent>(torrentFilePath);

            // Returns the 40-character hex string hash expected by qBittorrent
            return torrent.GetInfoHash().ToLowerInvariant();
        }
    }
}
