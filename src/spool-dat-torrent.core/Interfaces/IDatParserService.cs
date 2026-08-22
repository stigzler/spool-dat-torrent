using SpoolDatTorrent.Core.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace SpoolDatTorrent.Core.Interfaces
{
    public interface IDatParserService
    {
        Task<HashSet<string>> GetGameNamesFromStreamAsync(Stream stream, CancellationToken cancellationToken = default);
        Task<HashSet<string>> GetGameNamesFromFileAsync(string filePath, CancellationToken cancellationToken = default);
    }
}
