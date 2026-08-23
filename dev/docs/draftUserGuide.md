# User Guide

## BitTorrent Clients

Presently only workign for 1 - qbittorrent

### QBitTorrent
Please use V5 and upwards - due to breaking changes in the V4 - V5 move.


## Config File

`spool_settings.json` holds the default settings for the app. Notes on these settings:

### ClientDownloadsMapping

Leave this entire section blank if SpoolDatTorrent and your BitTorrent client are running on the same computer and see the exact same drive letters or folders (e.g., both apps see C:\Torrents\Completed).

You only need to complete this section for Docker or remote server setups where the two apps use different internal paths to look at the exact same physical files.