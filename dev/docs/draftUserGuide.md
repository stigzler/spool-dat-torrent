# User Guide

## Installation

### Docker

Example Compose:

```yml

services:
  qbittorrent:
    image: lscr.io/linuxserver/qbittorrent:latest
    container_name: qbittorrent
    environment:
      - PUID=1000
      - PGID=1000
      - TZ=Etc/UTC
# WebUI API is enabled by default on 8080
    volumes:
      - ./config/qbittorrent:/config
# Scratch area for active downloads (shared with spool-dat-torrent)
      - /mnt/scratch/qbittorrent:/downloads
    ports:
      - "8080:8080"
      - "6881:6881"
      - "6881:6881/udp"
    restart: unless-stopped

  spooldattorrent:
    image: spooldattorrent:latest
    container_name: spooldattorrent
    environment:
      - TZ=Etc/UTC
# Where SDT reads/writes spool_settings.json
      - SPOOL_CONFIG_DIR=/app/config
# Where SDT caches the .torrent/.dat source files per stream
      - SPOOL_CACHE_DIR=/app/cache
    volumes:
# SDT's own config + cached source files (must persist!)
      - ./config/spooldattorrent:/app/config
      - ./cache/spooldattorrent:/app/cache

# The SAME scratch path qBittorrent writes downloads to, mapped to SDT's
# virtual path (see ClientDownloadsMapping)
      - /mnt/scratch/qbittorrent:/app/scratch

# Final destination for moved roms (the spooling target)
      - /mnt/pool/roms/spooled:/app/pool
    ports:
      - "8090:8080"   # SDT web UI
    depends_on:
      - qbittorrent
    restart: unless-stopped
    ```

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