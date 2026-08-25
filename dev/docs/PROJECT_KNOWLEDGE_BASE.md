# SpoolDatTorrent — Project Knowledge Base

> **Purpose of this document:** This is the authoritative "bring-me-up-to-speed" reference for any fresh AI/developer context. It captures the project's goals, architecture, technical decisions, idiosyncrasies, current state, and outstanding work. Read this first before making any changes.

---

## 1. What the project is

SpoolDatTorrent (SDT) automates downloading **1G1R (one game, one region) ROM sets** from huge torrents without downloading the whole torrent.

**The problem it solves:** ROM torrents are enormous (e.g. a PlayStation set on Myrient is ~3.6 TB) and contain many redundant versions of the same game (different regions/versions). A user only wants the 1G1R subset defined by a DAT file (e.g. produced by Retool).

**How it works:**

1. User provides a `.torrent` (or magnet) + a 1G1R DAT file.
2. SDT talks to a BitTorrent client's API (currently **qBittorrent only**) and manipulates **per-file priorities** to download only the DAT-matched files.
3. Downloads happen in **batches** bounded by a configurable storage cap (e.g. "download up to 800 GB at a time").
4. When a batch completes, SDT **moves** the files to a final destination, frees the scratch space, and starts the next batch.

**Target platforms:** CLI (primary, working), Docker/Linux service (planned), desktop app (future). A Blazor web UI project exists but is currently just the default template + MudBlazor scaffolding.

---

## 2. Solution structure

Solution file: `spool-dat-torrent.slnx` (the new XML solution format).

| Project | Path | Role |
|---|---|---|
| `spool-dat-torrent.core` | `src/spool-dat-torrent.core/` | **All business logic.** Class library, host-agnostic. |
| `spool-dat-torrent.cli` | `src/spool-dat-torrent.cli/` | Spectre.Console CLI front-end. |
| `spool-dat-torrent.web` | `src/spool-dat-torrent.web/` | Blazor (Interactive Server) + MudBlazor. **Scaffolding only.** |
| `SpoolDatTorrent.Core.Tests` | `tests/SpoolDatTorrent.Core.Tests/` | xUnit tests. |
| `dev` | `dev/dev.shproj` | Shared project (docs, wireframes). |

**Target framework:** `.NET 10` (net10.0) across all projects.

**Key packages:**

- `BencodeNET` 5.0.0 (parse `.torrent` files / info-hash)
- `Microsoft.EntityFrameworkCore.Sqlite` 10.0.11 (persistence)
- `Microsoft.Extensions.Http` / `Hosting.Abstractions` 10.0.0
- `Spectre.Console` 0.57.2 + `Spectre.Console.Cli` 0.55.0 (CLI)
- `MudBlazor` 9.8.0 (web UI, unused so far)

> ⚠️ **Version mismatch note:** `Spectre.Console` (0.57.2) and `Spectre.Console.Cli` (0.55.0) are **not aligned**. This has caused subtle rendering/behavior differences. Consider aligning them.

---

## 3. Core architecture

### 3.1 The engine — `SpoolingEngine` (`Services/SpoolingEngine.cs`)

The heart of the app. It is a `BackgroundService` (so it can run continuously in Docker), but the CLI calls its public method directly in a loop.

**Public entry point:** `EvaluateAllStreamsAsync(CancellationToken)` — one "poll cycle".

**Flow of one cycle:**

1. Load **all** streams from the DB.
2. On the **first** cycle of a fresh engine instance, re-activate any `Error` streams to `Active` (so a fixed server is retried after a restart). Guarded by `_hasReactivatedOnStart`.
3. Filter to `Active` streams only.
4. Group by server profile (empty `ServerProfileId` → `DefaultServerProfile`).
5. For each server: compute `capPerStream = (SpoolingCapGb * 1GB) / streamCount`, then apply the safety margin.
6. Authenticate the client, then call `ProcessStreamAsync` per stream.
7. Report all streams to the progress reporter.

**`ProcessStreamAsync` — the state machine.** Categorizes each DAT-matched file into one of four lists, then acts:

- `alreadyMoved` — file exists at destination with correct size.
- `readyToMove` — `Progress >= 1.0 && Priority > 0` (downloaded, not yet moved).
- `downloading` — `Priority > 0 && Progress > 0` (in progress).
- `pending` — everything else (not on disk, not downloading). **Includes priority-0 files** (demoted after being moved, or skipped by the cap) so they get re-allocated.

**States (in order):**

1. **WAIT** — if `downloading.Any()`, log and return (do nothing until batch finishes).
2. **DRAIN** — if `readyToMove.Any()`: pause torrent → copy each completed file to destination → **delete the whole torrent** (`deleteFiles=true`) → **re-add the same `.torrent`** (same info-hash → same swarm) → re-allocate next batch.
3. **ALLOCATE** — if `pending.Any()`: set file priorities (1 = download, 0 = skip) up to the cap, then resume.
4. **COMPLETE** — if all three lists empty: set `Status = Completed`.

### 3.2 The critical design decision: delete-and-readd

**qBittorrent has NO API to remove an individual file from a torrent.** The file list is immutable (fixed by `.torrent` metadata).

The original approach (download → copy → set priority 0 → delete the scratch file) caused `file_open ... cannot find the file` errors because libtorrent still tracked the deleted file for boundary pieces/rechecks.

**The fix:** after moving a batch's files, **delete the entire torrent** (`deleteFiles=true`) and **re-add the same `.torrent`** (same info-hash → same swarm), then set priorities for the next batch. This lets libtorrent rebuild boundary pieces into `.parts` files for skipped files.

> ⚠️ **Do NOT subset the torrent** (create a new `.torrent` with only the wanted files) — that changes the info-hash and breaks the swarm. The user tried this with BencodeNET and it didn't work.

### 3.3 Boundary pieces & the safety margin

BitTorrent downloads in **pieces**. A piece can span two files. When a selected file shares a piece with a skipped file, libtorrent downloads the whole piece and writes the skipped portion to a transient `.parts` file — real disk usage not counted in the selected files' sizes.

`SpoolingCapSafetyMarginPercent` (default 5%) reserves headroom for this. The effective cap is `cap * (1 - margin/100)`.

### 3.4 The "checking" phase gotcha

qBittorrent's `delete` is **asynchronous**. If you re-add before the files are actually gone from disk, qBittorrent hash-checks the stale files (the slow "checking" phase).

**Fixes in place:**

- `DeleteTorrentAsync` polls (up to 60s) until the torrent is actually gone.
- `WaitForScratchFilesDeletedAsync` waits until the downloaded files are gone from disk before re-adding.
- `AddTorrentAsync` treats HTTP 409 (torrent already exists) as a no-op.

> ⚠️ **Do NOT use `skip_checking=true`** — it lies to qBittorrent (claims files exist when they don't), causing "Missing Files" and 0-byte copies. This was tried and reverted.

### 3.5 The `_movedFileCache` (self-healing)

`IsAlreadyMoved` uses an in-memory `ConcurrentDictionary<string, HashSet<int>>` keyed by torrent identifier → file indices, to avoid re-statting the destination every cycle.

**Critical fix:** the cache fast path must **verify the destination file still exists on disk**. If the user deletes destination files (e.g. to re-download), the cache must drop the index and return `false`. This was a real regression — see "Known bugs fixed" below.

---

## 4. Data model

### 4.1 `TorrentStreamItem` (the "stream" = one torrent+DAT job)

| Field | Type | Notes |
|---|---|---|
| `Id` | int | PK. **Reuses lowest free ID** (not auto-increment) — see `AddStreamCommand`. |
| `TorrentIdentifier` | string | The info-hash (40-char hex). Natural lookup key. |
| `Name` | string | Display name. Defaults to torrent filename (`.torrent` stripped). |
| `DatFilePath` | string | Original DAT path. |
| `OriginalTorrentPath` | string? | Original `.torrent` path (if added from file). |
| `OriginalMagnet` | string? | Magnet link (if added from magnet). |
| `CachedTorrentPath` | string? | Cached copy of `.torrent` (see §5). |
| `CachedDatPath` | string? | Cached copy of `.dat`. |
| `SpoolingTargetOverride` | string? | Per-stream destination override. |
| `Strategy` | `SpoolingStrategy` | `MoveFiles` (default), `Pause`, `RateLimit`. |
| `FileFilter` | string | `*.*` default. |
| `Status` | `StreamLifecycleStatus` | `Active`, `Paused`, `Completed`, `Error`. |
| `CreatedUtc` | DateTime | |
| `Files` | `List<TorrentFileItem>` | **Vestigial** — the engine never writes to it. |
| `ServerProfileId` | string | Which server profile. Empty → default. |
| `MovedCount` | int | Persisted progress (files moved). |
| `TotalCount` | int | Persisted progress (total desired). |

### 4.2 `StreamLifecycleStatus`

`Active`, `Paused`, `Completed`, `Error`.

- Only `Active` streams are spooled.
- `Error` streams are **re-activated to `Active` on engine restart** (fresh instance).
- `Paused` and `Completed` are **not** auto-reactivated.

### 4.3 `TorrentFileItem` (vestigial)

Exists in the schema but the engine **never writes to it**. Per-file state is re-derived from qBittorrent + the filesystem each cycle. This is intentional (stateless-by-design for resume).

### 4.4 `SpoolDbContext` (`Data/SpoolDbContext.cs`)

SQLite via EF Core. `Streams` and `Files` DbSets. `EnsureCreatedAsync` (no migrations).

> ⚠️ **Schema changes require deleting the DB** — `EnsureCreatedAsync` won't add columns to an existing DB. The DB file is `spooldattorrent.db` (renamed from `cli_test.db`).

---

## 5. File caching (`.torrent` + `.dat`)

When a stream is added, SDT copies the `.torrent` (if a local file) and `.dat` into a **disk cache**, so the stream survives the user later deleting the originals (common over TB-scale, many-session spooling).

- **Cache location:** `GlobalSpoolSettings.CacheDirectory` → `SPOOL_CACHE_DIR` env var → `cache/` folder beside the settings file. Resolved by `SettingsManager.GetCacheDirectory`.
- **Layout:** `{cacheDir}/{info-hash}/{filename}`.
- **"Locked-in" design:** once added, the stream works off the cached files. **We do NOT restore to the original location** (deliberate — avoids path-resolution bugs).
- **On re-add:** cached files are **overwritten** (so updated sources take effect).
- **On cancel:** cached files + the per-stream folder are deleted.
- **Engine reads cached paths first**, falling back to originals (for pre-cache streams).

Implemented in `Commands/StreamFileCache.cs`.

---

## 6. Configuration

### 6.1 `GlobalSpoolSettings` (`Configuration/GlobalSpoolSettings.cs`)

| Field | Default | Notes |
|---|---|---|
| `DefaultServerProfile` | `"LocalQBit"` | Named profile, not "first in list". |
| `TorrentServers` | dict | Keyed by profile name. |
| `DefaultSpoolingTarget` | `""` | Final destination root. |
| `PollIntervalSeconds` | `2` | Engine poll cadence (was 15, lowered). |
| `SettlingTimeSeconds` | `30` | |
| `CacheDirectory` | `""` | See §5. |
| `ServerRetryCount` | `3` | Consecutive failures before marking `Error`. |
| `SpoolingCapSafetyMarginPercent` | `5.0` | Boundary-piece headroom. |

### 6.2 `TorrentServerProfile`

`ClientType` (default `"qBittorrent"`), `Host`, `Username`, `Password`, `ApiKey`, `SpoolingCapGb` (default 500), `ClientDownloadsMapping`.

### 6.3 `ClientDownloadsMapping`

Translates the client's reported download paths to SDT's virtual paths (for Docker/remote where the two apps see different paths to the same physical files). `ClientVirtualPrefix` (e.g. `/downloads`) → `AppVirtualPrefix` (e.g. `/app/scratch`). Leave blank when both apps see the same paths.

### 6.4 `SettingsManager` (`Configuration/SettingsManager.cs`)

- `GetSettingsPath()` → `config.json` (renamed from `spool_settings.json`). Env var `SPOOL_CONFIG_DIR`.
- `GetCacheDirectory()` → §5.
- `EnsureDefaultSettingsExist()` — creates default config on first run.
- `AddServerProfile()` / `DeleteServerProfile()` — add/remove server profiles (persist to JSON).

---

## 7. BitTorrent client abstraction

### 7.1 `IBitTorrentClient` (`Interfaces/IBitTorrentClient.cs`)

Full interface: `AuthenticateAsync`, `GetActiveFootprintBytesAsync`, `PauseTorrentAsync`, `ResumeTorrentAsync`, `DeleteTorrentAsync`, `SetDownloadLimitAsync`, `MoveFilesAsync`, `AddTorrentAsync`, `GetPieceSizeAsync`, `RecheckTorrentAsync`, `GetFilesAsync`, `SetFilePrioritiesAsync`, `GetTorrentSavePathAsync`, `GetTorrentNameAsync`, `TorrentExistsAsync`, `GetAllTorrentHashesAsync`.

### 7.2 `QBitClient` (`Services/QBitClient.cs`)

The only implementation. Talks to qBittorrent WebUI API v2 (`/api/v2/...`).

**Auth:** API key (Bearer header) if `ApiKey` set, else cookie-based login.

**Key endpoints used:**

- `/api/v2/torrents/info` — save path, name, piece size, all hashes.
- `/api/v2/torrents/files` — file list (index, name, size, progress, priority).
- `/api/v2/torrents/filePrio` — set file priorities (`id` = `|`-separated indices, `priority` 0/1/6/7).
- `/api/v2/torrents/add` — add torrent (multipart; `stopped` field for paused; 409 = already exists).
- `/api/v2/torrents/delete` — delete (with `deleteFiles`).
- `/api/v2/torrents/stop` / `start` — pause/resume.

> ⚠️ **qBittorrent 5.0+ required** (breaking changes from v4). The `paused` vs `stopped` field: the code uses `stopped` (the user found `paused` didn't work for their setup).

### 7.3 `BitTorrentClientFactory` (`Services/BitTorrentClientFactory.cs`)

`GetClient(profileName)` — falls back to default profile, then to the first configured profile if the name is missing. Always returns `QBitClient` (no Deluge support yet, despite `ClientType` field).

---

## 8. DAT parsing

`IDatParserService` → `LogiqxDatParserService` (`Services/LogiqxDatParserService.cs`).

Parses Logiqx XML DAT files, returns a `HashSet<string>` of game names (case-insensitive). Matching is done by `Path.GetFileNameWithoutExtension(file.Name)` against the game names.

---

## 9. Progress reporting (host-agnostic)

`ISpoolingProgressReporter` (`Interfaces/ISpoolingProgressReporter.cs`) — the seam between the engine and any UI:

- `ReportStreams(IReadOnlyList<StreamProgressInfo>)`
- `ReportStatus(string)`

The engine emits structured events through this. The CLI implements it with `SpectreProgressReporter` (`cli/Services/SpectreProgressReporter.cs`). The web/desktop apps will provide their own implementations.

**DTOs:** `StreamProgressInfo` (name, torrent id, stream id, status, moved/total, files), `FileProgressInfo` (name, progress, stream id, size bytes), `StreamDetails` (list view), `ServerProfileDetails`.

---

## 10. CLI (Spectre.Console)

### 10.1 Commands (registered in `cli/Program.cs`)

| Command | Args | Notes |
|---|---|---|
| `list` | `-s/--status` optional | Lists servers + streams. |
| `spool` | none | Starts the monitor (lists first). |
| `add` | `-t/--torrent`, `-d/--dat` required; `-n/--name`, `--target`, `-c/--cap`, `--strategy`, `-f/--filter`, `--client-host`, `--client-key`, `-s/--server`, `--fresh` | Adds a stream + starts monitor. |
| `pause` | `<stream-id or path/magnet/hash>` | Sets status `Paused`. |
| `resume` | `<stream-id or path/magnet/hash>` | Sets status `Active`. |
| `retry` | `<stream-id or path/magnet/hash>` | Re-activates an `Error` stream. |
| `cancel` | `<stream-id or path/magnet/hash>` | Cancels one stream. |
| `cancel-all` | none | Cancels all streams. |
| `add-server` | none | Creates a placeholder server profile. |
| `delete-server` | `<name>` | Deletes a server profile (refuses if referenced). |
| `config` | none | Opens config.json in default editor. |

**Executable name:** `SpoolDatTorrent.exe` (AssemblyName `SpoolDatTorrent`). Application name in help is `SpoolDatTorrent`.

### 10.2 The monitor (`cli/Commands/SpoolMonitor.cs`)

Runs the engine in a background task (poll cadence = `PollIntervalSeconds`), and renders a **Spectre `AnsiConsole.Progress()`** live display refreshing every 1 second.

**Display layout (top → bottom):**

1. Job rows — `(streamId) name — moved/total files processed` with progress bar.
2. Status line — indeterminate spinner, latest status message.
3. File rows — `(streamId) filename (size)` with progress bar.

**Idiosyncrasies:**

- Spectre renders tasks in **add order** (fixed), so the status task is created lazily after the first job task.
- Long text is **truncated** (`Truncate` helper, 40 chars for names, 70 for status) to prevent line-wrap. Uses `"..."` (three dots, not the Unicode ellipsis).
- Ctrl+C is handled via `Console.CancelKeyPress` + `try/catch OperationCanceledException` to exit cleanly and restore the cursor (`AnsiConsole.Cursor.Show()` in `finally`).

### 10.3 `CliServiceProvider` (`cli/Commands/CliServiceProvider.cs`)

Shared DI builder. Registers `SpoolDbContext` (SQLite `spooldattorrent.db`), `IBitTorrentClientFactory`, `IDatParserService`, `ISpoolingProgressReporter` (Spectre), `SpoolingEngine`. Applies CLI overrides via `PostConfigure` before building.

---

## 11. Core commands (host-agnostic business logic)

These live in `core/Commands/` and are reusable by CLI, web, desktop:

- `AddStreamCommand` — add/update a stream (reuses lowest free ID, caches files, resolves default server).
- `CancelStreamCommand` — cancel one (by hash or ID), deletes cached files.
- `CancelAllStreamsCommand` — cancel all, deletes cached files.
- `ListStreamsCommand` — list streams (with `EnsureCreatedAsync`).
- `ListServerProfilesCommand` — list server profiles.
- `RetryStreamCommand` — set `Error` → `Active` (by hash or ID).
- `SetStreamStatusCommand` — set status (Paused/Active) (by hash or ID).
- `StreamFileCache` — file caching helper (§5).

> ⚠️ **Known refactor debt:** Some CLI command classes still contain business logic that should move to Core (e.g. `AddStreamCommand` CLI still does server resolution + `--fresh` handling inline). The user wants this refactored so web/desktop can reuse the same logic. **Only `add`, `cancel`, `cancel-all` were explicitly in scope for the file-cache refactor; the broader command refactor is outstanding.**

---

## 12. Destination path resolution

`GetDestinationRoot(stream, torrentName)`:

- If `SpoolingTargetOverride` set → use it directly (files go straight in, no torrent-name subfolder).
- Else → `DefaultSpoolingTarget / torrentName`.

`GetPrefixToStrip` + `GetCommonRootDirectory` + `StripPrefix`:

- With explicit target: strip the **common root directory** shared by all files (so `psx/Wipeout 3.zip` not `psx/Redump/Sony - PlayStation/Wipeout 3.zip`), preserving subfolders below that root.
- Without target: strip only the torrent name (first segment) to avoid duplication.
- If no common root (files scattered), fall back to mirroring the full torrent structure.

---

## 13. Known bugs fixed (important context)

1. **`file_open` errors** — fixed by delete-and-readd (§3.2).
2. **Slow "checking" phase** — fixed by waiting for files to be deleted before re-add (§3.4).
3. **`skip_checking` disaster** — reverted; caused "Missing Files" (§3.4).
4. **Resume of completed stream after deleting destination files** — two-part fix:
   - Categorization: priority-0 files now go to `pending` (not ignored).
   - `_movedFileCache` fast path now verifies the file still exists on disk (self-healing).
5. **Retry counter resetting to 0** — the reset was after auth, not after the whole server group; a bad API key (403 on a later call) oscillated 0→1 forever. Fixed by moving reset after all streams processed.
6. **`list` on vanilla install** — `no such table: Streams`; fixed by adding `EnsureCreatedAsync` to `ListStreamsCommand`.
7. **Stream ID showing 0** — placeholder `StreamProgressInfo` didn't set `StreamId`; fixed.
8. **Server column blank on add** — Core now resolves default server on create.

---

## 14. Outstanding tasks / future work

1. **Refactor CLI commands → Core** (the big one). Move business logic out of `cli/Commands/*` into `core/Commands/*` so web/desktop can reuse. Currently `add`, `cancel`, `cancel-all` partially done; others outstanding.
2. **Web UI (Blazor + MudBlazor)** — currently scaffolding only. Needs: register `SpoolingEngine` as a hosted `BackgroundService`, a `BlazorProgressReporter` (singleton store), and pages for streams/clients/settings. Wireframe exists at `dev/docs/wireframes/MainPageAndStreams.md`.
3. **Docker** — needs a Dockerfile + compose (sample in `dev/docs/draftUserGuide.md`). Must volume-mount config, cache, scratch, and pool.
4. **Deluge (or other) client support** — `BitTorrentClientFactory` always returns `QBitClient`; `ClientType` field is unused.
5. **Align Spectre.Console versions** (0.57.2 vs 0.55.0).
6. **`TorrentFileItem` table** — vestigial; decide whether to use it or remove it.
7. **`SpoolingStrategy`** — `Pause` and `RateLimit` are defined but not implemented (only `MoveFiles` is used).
8. **Auto-retry for web UI** — errored streams only re-activate on engine restart; a permanent web service needs periodic auto-retry or manual retry (manual `retry` command exists).
9. **Green "completed" message** — user asked for color; not yet done (needs a color-aware `LogStatus`).

---

## 15. Key files quick-reference

| File | What it is |
|---|---|
| `core/Services/SpoolingEngine.cs` | The engine (state machine, ~890 lines). |
| `core/Services/QBitClient.cs` | qBittorrent API client. |
| `core/Services/BitTorrentClientFactory.cs` | Client factory. |
| `core/Services/LogiqxDatParserService.cs` | DAT parser. |
| `core/Commands/*.cs` | Host-agnostic commands. |
| `core/Configuration/*.cs` | Settings + profiles. |
| `core/Models/*.cs` | Entities + enums. |
| `core/DTOs/*.cs` | Data transfer objects. |
| `core/Interfaces/*.cs` | Abstractions. |
| `core/Data/SpoolDbContext.cs` | EF Core SQLite context. |
| `core/Helpers/*.cs` | Logger, TorrentMetadataHelper, LongHelper. |
| `cli/Program.cs` | Command registration. |
| `cli/Commands/SpoolMonitor.cs` | Live display + engine loop. |
| `cli/Commands/CliServiceProvider.cs` | DI setup. |
| `cli/Services/SpectreProgressReporter.cs` | Spectre reporter. |
| `dev/docs/draftUserGuide.md` | User guide (in progress). |
| `dev/docs/wireframes/MainPageAndStreams.md` | Web UI wireframe. |

---

## 16. Conventions & gotchas

- **Info-hash** is the canonical stream identifier (40-char hex). `TorrentMetadataHelper.ResolveInfoHash` handles `.torrent` path, magnet (base32/hex), or raw hash.
- **Stream IDs reuse the lowest free value** (not auto-increment) — `AddStreamCommand.GetLowestFreeStreamIdAsync`.
- **EF Core Migrations** (not `EnsureCreatedAsync`) — schema changes require a new migration via `dotnet ef migrations add`. Both web (`Program.cs`) and CLI (`CliServiceProvider`) call `Database.Migrate()` on startup, so existing DBs upgrade automatically. See the "Database migrations" section below.
- **Config file** is `config.json` (not `spool_settings.json`).
- **DB file** is `spooldattorrent.db` (not `cli_test.db`).
- **Logging** via `Logger.Log` (static, writes to `SpoolDatTorrent.log`). `LogStatus` in the engine routes to the reporter (or console if no reporter).
- **The engine is stateless-by-design** for resume: it re-derives per-file state from qBittorrent + filesystem each cycle, not from the DB.
- **Only `Active` streams are spooled.** `Error` re-activates on restart; `Paused`/`Completed` do not.

## 16.5 Database migrations

We use **EF Core Migrations** for the SQLite database (`spooldattorrent.db`), not `EnsureCreatedAsync`. This lets existing databases upgrade **seamlessly** when the schema changes — no manual steps, no data loss.

- **Adding a new migration** (do this whenever the model changes — new/renamed/removed column, table, or relation):

  ```bash
  dotnet ef migrations add <MigrationName> --project src/spool-dat-torrent.core
  ```

  Migration files are generated under `src/spool-dat-torrent.core/Data/Migrations/`.

- **Migrations are applied automatically at startup** by both hosts:
  - Web: `Program.cs` calls `db.Database.Migrate()`.
  - CLI: `CliServiceProvider.Build()` calls `db.Database.Migrate()`.
  You do **not** run `dotnet ef database update` — the app does it.

- **Design-time factory:** `SpoolDbContextFactory` (in `Core/Data/`) lets `dotnet ef` instantiate the context from the class library (which has no composition root).

- **Tooling:** `dotnet-ef` global tool (10.0.11) is installed; `Microsoft.EntityFrameworkCore.Design` is referenced in the Core csproj.

- **Gotchas:**
  - Commit the migration file(s) together with the code that changes the model — the migration is part of the release.
  - Do **not** delete `spooldattorrent.db` to "fix" a schema mismatch — that loses data. Add a migration instead.
  - To undo an unapplied migration during dev: `dotnet ef migrations remove --project src/spool-dat-torrent.core`.

## 17. WebUI Development

## Web UI — Milestone 1 (just completed)

## Goal

First working slice of the Blazor web UI: layout + Settings page + single-admin login.

## What was built

### Core

- `GlobalSpoolSettings.AdminPassword` — plaintext single-admin password.
- `SettingsManager.LoadSettings()` / `SaveSettings()` / `CreateDefaultSettings()` — non-exiting settings load/save (the old `EnsureDefaultSettingsExist` calls `Environment.Exit`, unsafe for a web host).

### Web (`spool-dat-torrent.web`)

- `Program.cs` — loads shared `config.json` as a singleton, registers Core services (`SpoolDbContext`, `IBitTorrentClientFactory`, `IDatParserService`), adds **cookie auth** (not Identity), defines `POST /auth/login` + `GET /auth/logout`.
- `MainLayout.razor` — sidebar nav (Streams, Clients, Spool/Pause, About, Settings) + logout button.
- `Settings.razor` — edits global settings, saves to `config.json`. `DefaultServerProfile` is a `MudSelect` dropdown. Every field has `HelperText`.
- `Login.razor` + `EmptyLayout.razor` + `RedirectToLogin.razor` — login page + auth guard (`AuthorizeRouteView` in `Routes.razor`).
- Stub pages — `Streams`, `Clients`, `Spool`, `About`.

## Key decisions

- **No MVVM toolkit** — plain C# fields + `@bind` + `StateHasChanged` (Blazor has no binding engine, so `CommunityToolkit.Mvvm` buys nothing).
- **"server" = "client"** (BitTorrent client) — used interchangeably.
- **Server profiles moved OUT of Settings** — their add/remove/edit will live in the **Clients** page (still a stub).
- **Login is a plain HTML `<form method="post">`** (not MudBlazor), posting to `/auth/login`.

## Bugs fixed this session (important context)

1. `_Imports.razor` was missing `@using MudBlazor` → all `@bind-Value` failed with `RZ9991`.
2. Logout 404 → added a GET handler (now `GET /auth/logout`).
3. Login `AmbiguousMatchException` → `@page "/login"` collided with `MapPost("/login")`; moved endpoints to `/auth/*`.

## Outstanding web UI work (next milestones)

- **Clients page** — server profile CRUD, with two rules: block deleting the *last* profile (must keep ≥1), and on deleting the *default* profile, warn + reassign default to first remaining. (Core's `DeleteServerProfile` already reassigns default; the "keep ≥1" guard is new.)
- **Streams page** — list + live progress (implemented: cards, add/cancel/remove, live polling via `InMemoryProgressStore`). Further polish (global Spool/Pause control) pending.
- **Spool/Pause page**.
- **Register `SpoolingEngine` as a hosted `BackgroundService`** — DONE: `SpoolEngineHostedService` runs the engine loop at `PollIntervalSeconds`, writing snapshots to `InMemoryProgressStore`.
- **`BlazorProgressReporter`** — replaced by `InMemoryProgressStore` (singleton implementing `ISpoolingProgressReporter`) in Core.

## Stretch goals (deferred, not yet implemented)

- **DAT hash verification on move** — the DAT files (No-Intro / Redump / TOSEC, all Logiqx XML) carry `size`, `crc`, `md5`, and `sha1` per `<rom>`. The engine currently selects files by **name match** and verifies only **size** (plus BitTorrent's own piece-level SHA-1). A stretch goal is to extend `IDatParserService` to return a richer model (`name → { size, crc, md5, sha1 }`) and verify the **SHA-1** of each moved file against the DAT *during* the copy (stream source → hash → destination in one pass, so no extra I/O). This confirms the file is the *correct dump*, not just a name-matched file. Make it a user setting (default on). Deferred because it touches the engine copy path.

## Current branch

`webUI`