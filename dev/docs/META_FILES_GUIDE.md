# Project Meta Files — What They Do & When They Matter

This document explains the "meta" files in the repo that can be easy to lose track of: what each one does, whether it's relevant to **local development** (running the web UI / CLI on your Windows machine) or to **deployment** (Docker / GitHub), and what happens if you get it wrong.

---

## Quick reference table

| File | Local dev? | Deployment? | Purpose |
|---|---|---|---|
| `Directory.Build.props` | **Yes** | **Yes** | Single source of truth for version (applies to all projects) |
| `Dockerfile` | No | **Yes** | Builds the container image |
| `.dockerignore` | No | **Yes** | Tells Docker what *not* to copy into the image |
| `.gitignore` | **Yes** | No | Tells Git what *not* to track |
| `.github/workflows/docker-publish.yml` | No | **Yes** | CI: auto-builds + pushes the image on push |
| `src/spool-dat-torrent.web/appsettings.json` | **Yes** | **Yes** | ASP.NET Core logging config (shipped in image) |
| `src/spool-dat-torrent.web/appsettings.Development.json` | **Yes** | No | Dev-only logging overrides |
| `src/spool-dat-torrent.web/Properties/launchSettings.json` | **Yes** | No | Local run profiles (ports, env vars) |

---

## 1. `Directory.Build.props` — shared version for all projects

**Location:** repo root (`Directory.Build.props`)

**Relevant to:** both local dev **and** deployment.

**What it does:** MSBuild automatically imports this file into **every** `.csproj` in the repo (and subdirectories). It sets **one shared `<Version>`** for all projects (`SpoolDatTorrent.Core`, `SpoolDatTorrent.Web`, `SpoolDatTorrent.CLI`, and any future desktop app).

Current version: **0.8.6**

**Why it matters:**
- **Single source of truth** — you bump the version once here, and all assemblies get the same version.
- **NuGet/Docker tags** — if you decide to publish packages or tag images with the version, this is where you read it from.
- **MSBuild convention** — `Directory.Build.props` is a standard MSBuild mechanism; it auto-applies to all projects in the tree.

**If you get it wrong:** every project has a different (or missing) version, making it hard to track which build is which in logs/containers/NuGet.

---

## 2. `Dockerfile` — builds the container image

**Relevant to:** deployment only. Not used for local dev.

**What it does:**
- **Stage 1 (build):** uses the .NET 10 SDK to `restore` + `publish` the web project to `/app/publish`.
- **Stage 2 (final):** uses the lightweight ASP.NET runtime, sets `WORKDIR /app`, and:
  - `EXPOSE 6502` + `ENV ASPNETCORE_HTTP_PORTS=6502` → the container listens on **port 6502** (this is why your compose maps `6502:6502`).
  - `ENV SPOOL_CONFIG_DIR=/app/data` → **this is why you don't need to set `SPOOL_CONFIG_DIR` in compose** — it's baked into the image. Config, DB, cache, and the **log file** all go to `/app/data`.
  - `RUN mkdir -p /app/data /app/dats` → creates the data dir.
  - `ENTRYPOINT ["dotnet", "SpoolDatTorrent.Web.dll"]` → runs the web host.

**If you get it wrong:** the container won't listen on the port your compose maps, or config/log won't persist (if `SPOOL_CONFIG_DIR` were removed).

---

## 3. `.dockerignore` — what Docker excludes from the build context

**Relevant to:** deployment only.

**What it does:** tells `docker build` which files/folders to **exclude** from the build context (the files sent to the Docker daemon). It excludes:
- `.git`, `.github`, `.gitignore` — version control cruft.
- `**/bin`, `**/obj` — build output (never needed in the image).
- `**/*.db`, `**/*.db-shm`, `**/*.db-wal` — local dev databases (never ship these).
- `**/config.json` — local config with secrets; the container generates its own on first boot.
- `.vs`, `.idea`, `*.user`, `*.suo`, `Thumbs.db`, `.DS_Store` — IDE/OS cruft.
- `dev` — docs/dev-only.

**If you get it wrong:** the image gets bloated (bin/obj, DBs, secrets) or, worse, a local `config.json` with real credentials gets baked into the image.

---

## 4. `.gitignore` — what Git excludes from version control

**Relevant to:** local development (and any Git workflow).

**What it does:** tells Git which files to **not track**. It's the standard Visual Studio ignore set: `bin/`, `obj/`, `.vs/`, `*.user`, `*.suo`, build logs, test results, etc. (plus the project-specific entries in `.dockerignore`).

**If you get it wrong:** build artifacts and local state get committed to the repo, bloating it and potentially leaking local paths/secrets.

---

## 5. `.github/workflows/docker-publish.yml` — CI/CD pipeline

**Relevant to:** deployment only. Not used for local dev.

**What it does:** a GitHub Actions workflow that runs on **push to `main`/`master`** (or manual `workflow_dispatch`). It:
1. Checks out the repo.
2. Sets up Docker Buildx.
3. Logs in to **GitHub Container Registry (ghcr.io)**.
4. Builds the image from `./Dockerfile` and pushes it to `ghcr.io/stigzler/spool-dat-torrent:latest`.

**This is how your `ghcr.io/stigzler/spool-dat-torrent:latest` image gets built and published** — you don't build it manually. When you push to `main`/`master`, the new image is published automatically.

**If you get it wrong:** the image isn't rebuilt/pushed, so your server keeps pulling the old `latest`.

---

## 6. `appsettings.json` — ASP.NET Core logging config (shipped in image)

**Relevant to:** both local dev **and** deployment (it's copied into the image).

**What it does:** controls the **built-in ASP.NET Core `ILogger`** (the `info:` lines in the console — *not* your custom `Logger`). Current settings:
```json
"LogLevel": {
  "Default": "Information",
  "Microsoft.AspNetCore": "Warning",
  "System.Net.Http.HttpClient": "Warning",
  "Microsoft.EntityFrameworkCore.Database.Command": "Warning"
}
```
- `System.Net.Http.HttpClient: Warning` → suppresses the per-request `Sending HTTP request POST ...` spam.
- `Microsoft.EntityFrameworkCore.Database.Command: Warning` → suppresses the per-query `Executed DbCommand ... SELECT ...` spam.
- Real warnings/errors from those categories still show.

**If you get it wrong:** you get the noisy `info:` spam in the console/docker logs (or, if set too high, you lose real error visibility).

---

## 7. `appsettings.Development.json` — dev-only logging overrides

**Relevant to:** local development only. **Not** shipped in the production image (it's only loaded when `ASPNETCORE_ENVIRONMENT=Development`).

**What it does:** mirrors the same logging rules as `appsettings.json` but only applies in the `Development` environment (which your `launchSettings.json` sets). It exists so you can have different logging in dev vs prod without touching the shared file.

**If you get it wrong:** dev logging behaves differently from prod, which can hide issues that only appear in one environment.

---

## 8. `launchSettings.json` — local run profiles

**Relevant to:** local development only. **Not** used in the container.

**What it does:** defines how the app runs when you press **F5** in Visual Studio (or `dotnet run`). It has two profiles:
- **`http`** → `http://localhost:5111`
- **`https`** → `https://localhost:7143;http://localhost:5111`

Each sets env vars for local dev:
- `ASPNETCORE_ENVIRONMENT=Development`
- `SDT_SECRET_KEY` — secret encryption key
- `SDT_ADMIN_PASSWORD` — web login password
- `SDT_DEBUG_LOG` — `0`/`1` to toggle debug logging

**If you get it wrong:** the app runs on the wrong port, or local env vars (password, debug flag) are wrong. **It has zero effect on the container** — the Dockerfile and compose control that.

---

## Summary: which files matter for what

- **Local dev (web UI / CLI on Windows):** `Directory.Build.props`, `launchSettings.json`, `appsettings.json`, `appsettings.Development.json`, `.gitignore`.
- **Deployment (Docker / server):** `Directory.Build.props`, `Dockerfile`, `.dockerignore`, `.github/workflows/docker-publish.yml`, `appsettings.json` (shipped in image).
- **Both:** `Directory.Build.props` (version for all projects), `appsettings.json` (logging config).

**Key takeaway:** the container's behavior is governed by the **Dockerfile** (port, `SPOOL_CONFIG_DIR`) and your **compose file** (env vars, mounts) — not by `launchSettings.json`. And the image is published automatically by the **GitHub Actions workflow** when you push to `main`/`master`.
