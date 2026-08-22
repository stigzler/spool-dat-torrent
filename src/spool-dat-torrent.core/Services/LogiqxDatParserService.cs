using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using SpoolDatTorrent.Core.Interfaces;

namespace SpoolDatTorrent.Core.Services
{
    public class LogiqxDatParserService : IDatParserService
    {
        public async Task<HashSet<string>> GetGameNamesFromFileAsync(string filePath, CancellationToken cancellationToken = default)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("DAT file not found", filePath);
            }

            using var stream = File.OpenRead(filePath);
            return await GetGameNamesFromStreamAsync(stream, cancellationToken);
        }

        public async Task<HashSet<string>> GetGameNamesFromStreamAsync(Stream stream, CancellationToken cancellationToken = default)
        {
            // Use OrdinalIgnoreCase so "Game" and "game" match perfectly
            var gameNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var doc = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);

            foreach (var game in doc.Descendants("game"))
            {
                var name = (string?)game.Attribute("name");
                if (!string.IsNullOrWhiteSpace(name))
                {
                    gameNames.Add(name);
                }
            }

            return gameNames;
        }
    }
}