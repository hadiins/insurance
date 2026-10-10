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
a bad deploy, not a lost VM. Since 2026-10 this leg IS automated: after every daily backup,
`DatabaseBackupJob` seals the `.bak` with AES-256-GCM (`BackupFileEncryptor`, key =
`Backup:EncryptionKey`) and uploads the ciphertext-only `.enc` to a Google Drive folder
(`GoogleDriveOffsiteUploader`, resumable upload, service-account auth), then prunes remote files
older than the same `Backup:RetentionDays` window. What lands in Drive is opaque ciphertext —
Google (or anyone with the folder link) sees nothing but random bytes; the key never leaves this
environment. **The whole thing is opt-in**: with `Backup:Offsite:FolderId` unset the uploader
no-ops and local backups work exactly as before. Setup lives in `.env.example`
(`BACKUP_ENCRYPTION_KEY` / `BACKUP_OFFSITE_FOLDER_ID` /
`BACKUP_OFFSITE_SERVICE_ACCOUNT_JSON`).

Data-residency note (CLAUDE.md): customer/national-ID data must stay hosted in Iran. An
encrypted blob on a foreign cloud is ciphertext, not plaintext — but it is still YOUR call as
the owner whether that satisfies the rule for your deployment; if it doesn't, leave the leg
disabled and copy `.bak` files to Iranian object storage on your own cadence instead. Key
discipline is non-negotiable either way: the encryption key must be stored OFF the server
(password manager / sealed copy). Losing it makes every offsite backup unrecoverable — by
design.

## Restoring — step by step

Run these against the `sqlserver` container (`docker compose -f docker-compose.prod.yml exec sqlserver /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$SA_PASSWORD"`), or any `sqlcmd`/SSMS/Azure Data Studio session pointed at it.

### 1b. Restoring from an OFFSITE (.enc) copy — when the VM/volume is gone

The Drive folder holds `<dbname>-<timestamp>.bak.enc` files — AES-256-GCM ciphertext, NOT usable
`.bak`s. On ANY machine with .NET 8 (not on the api container):

```bash
# 1. Download the newest .enc from the Drive folder.
# 2. Decrypt with the OFF-server copy of the key (generate-time value of BACKUP_ENCRYPTION_KEY):
dotnet run --project src/Aqsat.BackupDecrypt -- <key-base64> <downloaded.enc> Aqsat-restored.bak
#    -- or:  ... -- --key-file /path/to/key.txt <downloaded.enc> Aqsat-restored.bak
# 3. Exit code 0 = GCM tags verified on EVERY chunk (tampered/truncated files fail loudly,
#    exit 2 — never a silently-short restore). Copy Aqsat-restored.bak to the new server's
#    /var/opt/mssql/backup/ and continue from step 2 below with that filename.
```

The decrypt tool compiles its single dependency (`BackupFileEncryptor.cs`) by source-link, so it
builds even when the rest of the tree won't — which is exactly when you're restoring. Wrong key =
`CryptographicException`, exit 2; the partial output is deleted.

### 1. Find the backup file (local `.bak`)

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

## If a restore is interrupted

An instance restart — or a client killed mid-command — during `RESTORE DATABASE` leaves the target
database in the `RESTORING` state: unusable, and `DROP DATABASE` / `ALTER DATABASE` refuse it
("a database is in the Restoring state", "currently in use") for as long as the session running the
restore is still alive. A session left in `KILLED/ROLLBACK` after the client died never yields on its
own, so the fix is an instance restart, after which SQL Server discards the incompletely restored
database and the scratch name disappears from `sys.databases`.

Check for one with:

```sql
SELECT session_id, status, command FROM sys.dm_exec_requests WHERE command LIKE '%RESTORE%';
SELECT name, state_desc FROM sys.databases WHERE state_desc = 'RESTORING';
```

## What this does NOT cover

- **Point-in-time recovery** — these are full backups only, no transaction-log shipping. Recovery
  granularity is "as of the last daily backup," which is what CLAUDE.md's settlement-countdown
  business risk actually needs protecting against (losing days of installment/payment history), not
  minute-level RPO. Revisit if that changes.
- **Cross-region/offsite copies** — see "Get backups off the host" above.
