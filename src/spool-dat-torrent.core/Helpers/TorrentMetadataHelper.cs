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

        /// <summary>
        /// Resolve an info-hash from a .torrent file path, a magnet link, or a raw hash.
        /// </summary>
        public static string ResolveInfoHash(string torrentPathOrHashOrMagnet)
        {
            // Raw 40-char hex hash
            if (torrentPathOrHashOrMagnet.Length == 40 &&
                torrentPathOrHashOrMagnet.All(Uri.IsHexDigit))
            {
                return torrentPathOrHashOrMagnet.ToLowerInvariant();
            }

            // Magnet link: extract the btih (base32 or hex) from the xt=urn:btih: parameter
            if (torrentPathOrHashOrMagnet.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
            {
                var match = System.Text.RegularExpressions.Regex.Match(
                    torrentPathOrHashOrMagnet,
                    @"xt=urn:btih:([A-Za-z0-9]+)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                if (match.Success)
                {
                    var btih = match.Groups[1].Value;
                    // Base32 hashes are 32 chars; hex are 40. Convert base32 -> hex.
                    if (btih.Length == 32)
                    {
                        return Base32ToHex(btih);
                    }
                    return btih.ToLowerInvariant();
                }

                throw new ArgumentException("Magnet link does not contain a valid btih info-hash.");
            }

            // Otherwise treat as a .torrent file path
            return GetInfoHash(torrentPathOrHashOrMagnet);
        }

        private static string Base32ToHex(string base32)
        {
            const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
            var bits = new System.Collections.Generic.List<bool>();

            foreach (var c in base32.ToUpperInvariant())
            {
                int value = alphabet.IndexOf(c);
                if (value < 0) throw new ArgumentException($"Invalid base32 character '{c}' in info-hash.");
                for (int i = 4; i >= 0; i--)
                {
                    bits.Add((value & (1 << i)) != 0);
                }
            }

            // Trim to a multiple of 8 bits
            int byteCount = bits.Count / 8;
            var bytes = new byte[byteCount];
            for (int i = 0; i < byteCount; i++)
            {
                byte b = 0;
                for (int j = 0; j < 8; j++)
                {
                    if (bits[i * 8 + j]) b |= (byte)(1 << (7 - j));
                }
                bytes[i] = b;
            }

            return Convert.ToHexString(bytes).ToLowerInvariant();
        }
    }
}
