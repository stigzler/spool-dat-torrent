using System;
using System.Collections.Generic;
using System.Text;

namespace SpoolDatTorrent.Core.Models
{
    public enum StreamLifecycleStatus
    {
        Active,
        Paused,
        Completed,
        Error
    }
}
