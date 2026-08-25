# Development Strategy

## Shipping a new feature / build

When you change the codebase and prepare a build or new release, follow this checklist. It keeps the database schema and the shared Core logic consistent across all hosts (web, CLI, Docker).

> **Rule of thumb:** anything that touches the **database schema** or shared **Core** logic affects both the web app and the CLI, so verify both.

### 1. Update the database schema (if the model changed)

If you added/removed a column, table, or relation on any entity (e.g. `TorrentStreamItem`, `TorrentFileItem`, or anything in `SpoolDbContext`), the database must be migrated.

We use **EF Core Migrations** (not `EnsureCreatedAsync`). Each schema change needs a new migration file:

```bash
dotnet ef migrations add <MigrationName> --project src/spool-dat-torrent.core
```

- `<MigrationName>` is a short descriptive name, e.g. `AddOriginalDatPath`.
- The migration file is generated under `src/spool-dat-torrent.core/Data/Migrations/`.
- Existing databases are upgraded **automatically and seamlessly** on the next app launch — no manual steps, no data loss. Both the web host (`Program.cs`) and the CLI (`CliServiceProvider`) call `Database.Migrate()` on startup.

> ⚠️ **Only `dotnet ef migrations add` is required.** You do **not** need to run `dotnet ef database update` — the app applies pending migrations itself on startup.

### 2. Verify both hosts build

The schema/Core change must compile in every consumer:

```bash
dotnet build
```

Fix any errors before committing.

### 3. Smoke-test

- Run the web app and confirm the relevant page loads against a migrated (or fresh) database.
- If relevant, run the matching CLI command (`spool list`, `add`, etc.) to confirm the CLI path works too.

### 4. Commit

Commit the code **and** the new migration file(s) under `src/spool-dat-torrent.core/Data/Migrations/` together. The migration is part of the release; without it the schema and code won't match.

---

## Notes

- **Brand-new database:** if there is no `spooldattorrent.db` yet, the initial `InitialCreate` migration creates the full schema on first launch.
- **Do not delete the DB to "fix" a schema mismatch** unless you're happy to lose data — migrations exist specifically so that isn't necessary.
- **Undo an unapplied migration** during development: `dotnet ef migrations remove --project src/spool-dat-torrent.core`.
