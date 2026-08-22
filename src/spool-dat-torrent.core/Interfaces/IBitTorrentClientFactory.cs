using System;
using System.Collections.Generic;
using System.Text;

namespace SpoolDatTorrent.Core.Interfaces
{
    public interface IBitTorrentClientFactory
    {
        IBitTorrentClient GetClient(string profileName);
    }
}
