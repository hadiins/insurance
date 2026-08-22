# Restore Runbook

**A backup nobody has ever restored is not a backup** (docs/TASKS.md Task 20's own check). This
procedure is tested — `tests/Aqsat.UnitTests/Deployment/DatabaseBackupJobTests.cs` runs the exact
`BACKUP DATABASE` / `RESTORE DATABASE` statements below against a real SQL Server engine on every
`dotnet test` run, restoring into a throwaway database and asserting the row counts match.

## How backups are made

`DatabaseBackupJob` (`src/Aqsat.Infrastructure/Jobs/DatabaseBackupJob.cs`) runs daily at 03:00 via
Hangfire, inside the `api` container:

1. `BACKUP DATABASE [Aqsat] TO DISK = '/var/opt/mssql/backup/Aqsat-{yyyyMMdd-HHmmss}.bak' WITH CHECKSUM, INIT;`
2. Deletes files older than `Backup:RetentionDays` (default 14) from the same directory.

The backup file is written by the **sqlserver** container to a Docker volume
(`sqlserver-backup`) that is also mounted into the **api** container, so the retention sweep — plain
`System.IO` file deletion — can see the same files.

**Get backups off the host.** A volume on the same VM is not a disaster-recovery plan — it survives
a bad deploy, not a lost VM. Copy `.bak` files to separate storage (object storage in an Iranian
provider, per CLAUDE.md's data-residency rule) on whatever cadence your incident tolerance requires;
this repo does not automate that leg.

## Restoring — step by step

Run these against the `sqlserver` container (`docker compose -f docker-compose.prod.yml exec sqlserver /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$SA_PASSWORD"`), or any `sqlcmd`/SSMS/Azure Data Studio session pointed at it.

### 1. Find the backup file

```bash
docker compose -f docker-compose.prod.yml exec sqlserver ls -la /var/opt/mssql/backup
```

### 2. Discover the logical file names inside the backup

Every backup's data/log logical file names must be known before `RESTORE` can remap them — do not
assume they match a fresh install's names if the database has ever been renamed or restored before.

```sql
RESTORE FILELISTONLY FROM DISK = N'/var/opt/mssql/backup/Aqsat-20260821-030000.bak';
```

Note the `LogicalName` values where `Type = 'D'` (data) and `Type = 'L'` (log) — call them
`<data-logical>` and `<log-logical>` below.

### 3. Restore into a NEW database first — never straight over the live one

Restoring directly on top of `Aqsat` with no verification step is how a bad backup turns a single
outage into a permanent data loss. Always restore to a scratch name, verify, then cut over.

```sql
RESTORE DATABASE [AqsatRestoreCheck]
FROM DISK = N'/var/opt/mssql/backup/Aqsat-20260821-030000.bak'
WITH
    MOVE '<data-logical>' TO '/var/opt/mssql/backup/AqsatRestoreCheck.mdf',
    MOVE '<log-logical>' TO '/var/opt/mssql/backup/AqsatRestoreCheck.ldf',
    REPLACE;
```

### 4. Confirm the data

```sql
USE [AqsatRestoreCheck];
SELECT COUNT(*) FROM Organizations;
SELECT COUNT(*) FROM Policies;
SELECT TOP 5 PolicyNumber, IssueDate FROM Policies ORDER BY IssueDate DESC;
```

Compare row counts against what you expect from the point in time the backup was taken (the
`{yyyyMMdd-HHmmss}` in the filename). If they look right, proceed; if not, try an earlier backup
before touching the live database.

### 5. Cut over

```sql
-- Take the live database offline so nothing writes to it mid-swap.
ALTER DATABASE [Aqsat] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
DROP DATABASE [Aqsat];
ALTER DATABASE [AqsatRestoreCheck] MODIFY NAME = [Aqsat];
```

Then restart the `api` container (`docker compose -f docker-compose.prod.yml restart api`) so its
connection pool picks up a fresh connection to the renamed database.

### 6. Clean up

Delete the `.mdf`/`.ldf` files used for verification if you restored to a temporary name/path
outside the final data volume, and remove the scratch database if step 5 wasn't taken (verification
only, no cutover).

## What this does NOT cover

- **Point-in-time recovery** — these are full backups only, no transaction-log shipping. Recovery
  granularity is "as of the last daily backup," which is what CLAUDE.md's settlement-countdown
  business risk actually needs protecting against (losing days of installment/payment history), not
  minute-level RPO. Revisit if that changes.
- **Cross-region/offsite copies** — see "Get backups off the host" above.
