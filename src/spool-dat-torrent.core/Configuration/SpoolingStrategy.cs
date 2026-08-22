using System;
using System.Collections.Generic;
using System.Text;

namespace SpoolDatTorrent.Core.Configuration
{
    public enum SpoolingStrategy
    {
        MoveFiles,
        Pause,
        RateLimit
    }
}
