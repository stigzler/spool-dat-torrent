using System;
using System.Collections.Generic;
using System.Text;

namespace SpoolDatTorrent.Core.Helpers
{
    public static class LongHelper
    {
        public static double ToGigabytes(this long bytes)
        {
            return Math.Round(bytes / 1024.0 / 1024.0 / 1024.0, 1);
        }
    }
}
