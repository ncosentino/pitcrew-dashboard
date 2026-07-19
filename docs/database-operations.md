# Database operations

The dashboard image includes
`/app/tools/database/PitCrew.Dashboard.DatabaseTool`. It uses the same embedded
SQLite library as the application; no `sqlite3` operating-system package is
installed.

The examples assume:

```powershell
$compose = @(
    '--env-file', '.env.hosted',
    '--file', 'docker-compose.hosted.yml'
)
```

## Online backup

Backups can run while the dashboard is online. SQLite may briefly block writers
while `BackupDatabase` copies the current transactionally consistent snapshot.

```powershell
$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
docker compose @compose exec dashboard `
    /app/tools/database/PitCrew.Dashboard.DatabaseTool `
    backup `
    --database /var/lib/pitcrew-dashboard/pitcrew-dashboard.db `
    --output "/var/lib/pitcrew-dashboard/backups/pitcrew-$timestamp.db"
```

The tool writes to a temporary file, runs `PRAGMA quick_check` and
`PRAGMA foreign_key_check`, then renames the verified file into place.

## Verify a backup

```powershell
docker compose @compose exec dashboard `
    /app/tools/database/PitCrew.Dashboard.DatabaseTool `
    verify `
    --input /var/lib/pitcrew-dashboard/backups/pitcrew-YYYYMMDD-HHMMSS.db
```

Verification runs the full `PRAGMA integrity_check` plus
`PRAGMA foreign_key_check`.

## Restore

Restore is not an online operation. Stop the dashboard so no application
connection or pooled SQLite handle can reference the file:

```powershell
docker compose @compose stop dashboard
```

Run the tool in a one-off container using the same persistent volume:

```powershell
docker compose @compose run --rm --no-deps `
    --entrypoint /app/tools/database/PitCrew.Dashboard.DatabaseTool `
    dashboard `
    restore `
    --input /var/lib/pitcrew-dashboard/backups/pitcrew-YYYYMMDD-HHMMSS.db `
    --database /var/lib/pitcrew-dashboard/pitcrew-dashboard.db
```

The tool:

1. Fully verifies the source backup.
2. Copies it into a temporary file beside the live database.
3. Fully verifies the staged copy.
4. Moves any stopped database WAL and shared-memory sidecars beside the rollback
   copy.
5. Atomically replaces the live file.
6. Retains the previous database as a timestamped `.pre-restore-*.bak` rollback
   file, with matching sidecars when they existed.

Restart the dashboard:

```powershell
docker compose @compose start dashboard
```

Startup reapplies any migrations newer than the restored backup.

## Storage constraints

- Keep the database, staging file, and rollback file on the same filesystem so
  replacement remains atomic.
- Do not place a WAL database on NFS or SMB storage.
- Back up the dashboard volume separately from Caddy certificate storage.
- Treat the dashboard volume as sensitive because it also contains
  data-protection keys used to decrypt authentication cookies.
