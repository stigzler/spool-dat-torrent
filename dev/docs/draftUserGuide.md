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

## Spool job settings

### Overriding the destination path

When you specify a target folder, files are placed directly into it, with the torrent's shared root folder stripped away — so psx/Wipeout 3.zip rather than psx/Redump/Sony - PlayStation/Wipeout 3.zip. Any subfolders that exist below that shared root (for example Aftermarket/) are preserved. If the torrent's files don't share a common root folder, the app falls back to mirroring the torrent's full folder structure inside your target.