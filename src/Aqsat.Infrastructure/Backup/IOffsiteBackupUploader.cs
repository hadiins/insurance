namespace Aqsat.Infrastructure.Backup;

/// <summary>
/// The offsite leg of the backup strategy (README "Backup & restore"): pushes the daily .bak
/// somewhere that survives losing the VM. Implemented by <see cref="GoogleDriveOffsiteUploader"/>.
/// Return value: true = an upload happened, false = offsite isn't configured (normal for dev).
/// </summary>
public interface IOffsiteBackupUploader
{
    Task<bool> UploadAsync(string backupFilePath, CancellationToken ct = default);
}