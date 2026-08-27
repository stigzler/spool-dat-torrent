# SpoolDatTorrent

SpoolDatTorrent (SDT) dynamically monitors and adjusts what files are being downloaded by a bitTorrent client to just those specified by a dat file. This is useful where you have large torrents (say 3.5TB / 4000 files) where it is not possible to manually select what files you require.

Some specific types of file sets have tools that produce a custom `.dat` file to 'slim-down' that set (eg. the excellent [Retool](https://github.com/unexpectedpanda/retool)). Traditionally, people would have to download all of the files then use these `.dat` files to filter that set to their requirements. This turns that on its head and downloads *only* the files that are specified by the dat. This saves on time, disk-space, bandwidth and grey hairs.

SDT also downloads the files in batches (the 'Spool Cap'). This means that if your filtered set is still too large for your download disk
(e.g. 2.5TBs to download onto a 2TB disk) then you can set the spool cap to 1TB. SDT will set up a batch of downloads totaling 1TB and send them over to your bitTorrent client to download. It basically dynamically alters the files in the torrent to download. Once these are downloaded, it moves those files to another destination of your choosing (a larger 32TB pooled disk space, for example) and then move onto the next 'batch'

Given how this works, there's one ask of you:

Make sure you also 🌱

## Screenshots

![Screenshot 2026 08 27 190115](dev/docs/images/Screenshot%202026-08-27%20190115.png)

![Screenshot 2026 08 27 190149](dev/docs/images/Screenshot%202026-08-27%20190149.png)

![Screenshot 2026 08 27 190300](dev/docs/images/Screenshot%202026-08-27%20190300.png)

![Screenshot 2026 08 27 192154](dev/docs/images/Screenshot%202026-08-27%20192154.png)

## Two front-ends

- **Web UI** — a browser-based admin, runs in Docker.
- **CLI** — a terminal app (Spectre.Console) for scripting/headless use.

## Prerequisites

- **A BitTorrent client with a Web API.** Only **qBittorrent** is supported today (v5.0+ required). Enable its Web UI and note the host URL, username/password, or API key.
- **Docker** (for the Web UI only — the CLI runs natively on Windows/Linux).

## Usage — CLI

The CLI executable is `SpoolDatTorrent` (`SpoolDatTorrent.exe` on Windows). Run it with no arguments to see help, or `SpoolDatTorrent <command> -h` for command-specific help.

### Commands

| Command | Arguments | Description |
|---|---|---|
| `list` | `-s\|--status <Active\|Paused\|Completed\|Error>` (optional) | List all servers + streams. |
| `spool` | — | Start the spooling daemon for all active streams. |
| `add` | see below | Add a stream and start spooling it. |
| `pause` | `<id\|path\|magnet\|hash>` | Pause a stream. |
| `resume` | `<id\|path\|magnet\|hash>` | Resume a paused stream. |
| `retry` | `<id\|path\|magnet\|hash>` | Re-activate an errored stream once fixed. |
| `cancel` | `<id\|path\|magnet\|hash>` | Cancel a single stream. |
| `cancel-all` | — | Cancel **all** streams (removes every torrent + clears all streams). |
| `add-server` | — | Create a new server profile (placeholder — edit it afterwards). |
| `delete-server` | `<name>` | Delete a server profile. |
| `config` | — | Open `config.json` in your default text editor. |

### `add` — parameters & switches

| Switch | Required | Description |
|---|---|---|
| `-t/--torrent <PATH_OR_HASH>` | ✅ | Path to a `.torrent` file, a magnet link, or an info-hash. |
| `-d/--dat <DAT_PATH>` | ✅ | Path to the source DAT file. |
| `-n/--name <NAME>` | | Friendly display name (logs/UI only). |
| `--target <PATH>` | | Destination folder override (files go straight in, torrent root stripped). |
| `-c/--cap <GB>` | | Override the spooling cap (GB) for this run. |
| `--strategy <STRATEGY>` | | `MoveFiles` (default), `Pause`, or `RateLimit`. |
| `-f/--filter <PATTERN>` | | File matching pattern, e.g. `*USA*`. |
| `--client-host <URL>` | | Override the client host URL. |
| `--client-key <KEY>` | | Override the client API key. |
| `-s/--server <NAME>` | | Server profile to use (defaults to the configured default). |
| `--fresh` | | Start fresh: clear saved state + remove the torrent from the client (keeps already-moved files). |

### Examples

```bash
# 1. Create a server profile (creates a placeholder you then edit)
SpoolDatTorrent add-server

# 2. Open config.json to set the host / credentials / cap
SpoolDatTorrent config

# 3. List servers + streams
SpoolDatTorrent list

# 4. Add a stream (torrent file + DAT) and start spooling
SpoolDatTorrent add -t "C:\torrents\tony-spacestation.torrent" -d "C:\dats\tony-spacestation-1g1r.dat"

# 5. Add from a magnet, targeting a specific server and destination
SpoolDatTorrent add -t "magnet:?xt=urn:btih:..." -d "C:\dats\tony-spacestation-1g1r.dat" -s MyServer --target "D:\files\tony-spacestation"

# 6. Pause / resume / cancel a stream (by id, path, magnet, or hash)
SpoolDatTorrent pause 1
SpoolDatTorrent resume 1
SpoolDatTorrent cancel 1
```

### Example `config.json`

```json
{
  "DefaultServerProfile": "DefaultQBit",
  "TorrentServers": {
    "DefaultQBit": {
      "ClientType": "QBittorrent",
      "Host": "http://localhost:8080",
      "Username": "admin",
      "Password": "",
      "ApiKey": "qbt_4htwNXNhURVOJNYyGrwxV",
      "SpoolingCapGb": 500,
      "ClientDownloadsMapping": {
        "ClientVirtualPrefix": "",        "ClientVirtualPrefix": "",
        "AppVirtualPrefix": ""        "AppVirtualPrefix": ""
      }
    }
  },
  "DefaultSpoolingTarget": "/staging-dir",
  "PollIntervalSeconds": 2,
  "SettlingTimeSeconds": 5,
  "CacheDirectory": "",
  "ServerRetryCount": 3,
  "SpoolingCapSafetyMarginPercent": 5,
}
```

**Key options:**

- `DefaultServerProfile` — which server a stream uses when it doesn't specify one.
- `TorrentServers` — one entry per BitTorrent client.
- `SpoolingCapGb` is the max batch size;
- `DefaultSpoolingTarget` — where completed files are moved by default.

below).
- `PollIntervalSeconds` — how often the engine polls the client.
- `SettlingTimeSeconds` — wait after pausing before moving files (lets the client finish writing).
- `ServerRetryCount` — consecutive failures before a stream is marked errored.
- `SpoolingCapSafetyMarginPercent` — headroom reserved for BitTorrent "boundary piece" overhead (5-10% normally good).
---

## Usage — Web UI

### Installation (Docker Compose)

```yaml
services:
  spooldattorrent:
    image: ghcr.io/stigzler/spool-dat-torrent:latest
    container_name: spooldattorrent
    environment:
      - SDT_ADMIN_PASSWORD=admin
      - SDT_SECRET_KEY=nmUgKjZjUws649H1aZC5
      - SDT_DEBUG_LOG=0
      - TZ=Europe/London
    ports:
      - 6502:6502
    volumes:
      - /home/[user]/appdata/spooldattorrent:/app/data
      - /mnt/scratch/Downloads/qbittorrent/complete:/downloads/complete
      - /mnt/scratch/Downloads/qbittorrent/incomplete:/downloads/incomplete
      - /mnt/pool/Media/Bin/spool-dat-torrent-in:/staging-dir
      - /mnt/pool/Media/Games/roms:/library-dir     # Optional alternate destination
    networks:
      - media_net
    restart: unless-stopped
networks:
  media_net:
    external: true
```

Then browse to `http://<host>:6502` and log in with the `SDT_ADMIN_PASSWORD` you set

### Understanding the `/downloads/complete` and `/downloads/incomplete` mounts

These two mounts are **how SDT sees the files qBittorrent has downloaded.**

qBittorrent saves its downloads into two folders:

- **`/downloads/incomplete`** — files that are still downloading.
- **`/downloads/complete`** — files that have finished downloading.

SDT needs to *read* those files to move them to your destination. In Docker, a container can only see folders you explicitly mount into it. So the compose file mounts the **same host folders** that qBittorrent uses into the SDT container, at the **same paths** qBittorrent reports them.

Here's the same idea shown side-by-side with a typical qBittorrent compose:

```yaml
# qBittorrent container
services:
  qbittorrent:
    image: lscr.io/linuxserver/qbittorrent:latest
    volumes:
      - /mnt/scratch/Downloads/qbittorrent/complete:/downloads/complete
      - /mnt/scratch/Downloads/qbittorrent/incomplete:/downloads/incomplete
```

```yaml
# SpoolDatTorrent container (same host folders, same container paths)
services:
  spooldattorrent:
    volumes:
      - /mnt/scratch/Downloads/qbittorrent/complete:/downloads/complete
      - /mnt/scratch/Downloads/qbittorrent/incomplete:/downloads/incomplete
```

**The rule:** the part **before** the `:` is the real folder on your host machine; the part **after** the `:` is where it appears *inside* the container. Because both containers mount the same host folder at the same container path, SDT can find qBittorrent's files with no extra configuration.

> If your qBittorrent and SDT containers see the same disk at **different** paths, you'll need the `ClientDownloadsMapping` setting on the server profile instead (see "Add / edit a server" below).

### The `staging-dir` and `library-dir` mounts

- **`/staging-dir`** — the **default** destination. Completed files are moved here (into a subfolder named after the torrent). This is where you'd point a post-processing tool (e.g. a ROM manager) to pick files up.
- **`/library-dir`** — an **optional alternate** destination for files that need no further processing. Leave this mount out entirely if you don't need it.

> ⚠️ **Do not rename the container-side paths.** The part after the `:` must stay exactly `/staging-dir` and `/library-dir` — these are the fixed names SDT looks for. You can change the host-side path (before the `:`) to anything you like.

### Global settings

![Screenshot 2026 08 27 175918](dev/docs/images/Screenshot%202026-08-27%20175918.png)

Key items:

- **Default Server Profile** — the client used when a stream doesn't specify one.
- **Poll Interval** — how often the engine polls the client. For set and forget, this can be high - eg. 30s. For real time monitoring for large torrents set to around 5s.
- **Settling Time** — wait after pausing before moving files. Again, the larger the .torrent, the higher amount of time needed. If you're getting move errors after a batch has been downloaded, increase this number. 5 works well for a 3.7TB torrent with 4000 files.
- **Server Retry Count** — failures before a stream is marked errored.
- **Safety Margin (%)** — headroom for boundary-piece overhead. Short version: bittorrent sometimes necessitates downloading parts of other files to complete the desired file. Therefore, 1.2GB may be needed for a 1GB file batch.
- **Staging / Library host path** — display-only; shows your real host path instead of the container path in the Stream cards.

### Add / edit a server

![Screenshot 2026 08 27 180827](dev/docs/images/Screenshot%202026-08-27%20180827.png)

Go to **Servers** → **Add Server**, then edit it:

- **Host** — the bitTorrent Client's Web UI URL (e.g. `http://localhost:8080`).
- **Username / Password** — or an **API Key** (recommended over un + pw; qBittorrent only needs one or the other).
- **Spooling Cap (GB)** — max batch size for this client. For example, if you have a 2TB download disk, you might set this to 1500 (~1.5TB). Do leave *some* headroom.
- **Docker path mapping** — only needed if qBittorrent and SDT see the same disk at different paths. Leave both blank if you mounted the same paths in both containers.

### Add a stream

![Screenshot 2026 08 27 181708](dev/docs/images/Screenshot%202026-08-27%20181708.png)

Go to **Streams** → **Add Stream**. The minimum is a **torrent** (upload a `.torrent` file, or paste a magnet/info-hash) and a **DAT file** (upload `.dat`/`.xml`/`.lst`).

Optional settings:

- **Server Profile** — which client to use (defaults to the configured default).
- **Strategy** — `MoveFiles` (default), `Pause`, or `RateLimit`. Presently latter two aren't implemented.
- **Name** — friendly display name. Defaults to torrent name if left blank.
- **Destination root** — `staging-dir` or `library-dir`, plus an optional subfolder. If blank, the full .torrent filepath is used (even if has empty parent folders before the files). If specified, SDT will attempt to remove empty parent folders and place files directly in the specified path.
- **File Filter** — e.g. `*USA*`. Filter by specified criteria.
- **Settling Time** — per-stream override of the global default.
- **Priority / De-priority Terms** — filename substrings to download first/last (e.g. `(USA)` first, `(Japan)` last). Note: these slow performance. These are useful where certain tags in filenames are more available than others, meaning you're not waiting on poorly seeded files. Eg. `(USA)` and `(World)` may be better seeded than `(Japan)`.

### Cancelling streams

You can cancel a stream from the Streams page (or `cancel` / `cancel-all` in the CLI).

> **Three important takeaways:**
>

> 1. **Cancelling does NOT remove any ROMs from the destination folder.** Files already moved stay put.

> 2. **It DOES remove the torrent from your BitTorrent client** (and its scratch files).
> 3. Re-adding the same torrent later (even with a different DAT) will **resume** spooling that torrent to the configured destination/options.
