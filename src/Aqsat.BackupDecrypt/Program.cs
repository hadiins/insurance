using System.Security.Cryptography;
using Aqsat.Infrastructure.Backup;

// aqsat-backup-decrypt — restores an offsite .enc backup to a usable .bak.
//
// Usage:
//   aqsat-backup-decrypt <key-base64> <input.enc> <output.bak>
//   aqsat-backup-decrypt --key-file <path> <input.enc> <output.bak>     (key file = raw base64 text)
//
// The key is Backup:EncryptionKey from the SERVER's configuration (generate: openssl rand -base64 32).
// It must be stored off the server — this tool is the only thing that can undo the encryption, and
// it runs on whatever machine you happen to be restoring from, not on the api container.
//
// Exit codes: 0 = decrypted and verified, 1 = usage, 2 = decryption failed (wrong key / tampered /
// truncated — the AES-GCM tags are checked per chunk, so a partial or edited file never "works").

if (args.Length is < 3 or > 4)
{
    Console.Error.WriteLine("Usage: aqsat-backup-decrypt <key-base64> <input.enc> <output.bak>");
    Console.Error.WriteLine("   or: aqsat-backup-decrypt --key-file <path> <input.enc> <output.bak>");
    return 1;
}

string keyBase64;
string inputPath;
string outputPath;
if (args[0] == "--key-file")
{
    if (args.Length != 4)
    {
        Console.Error.WriteLine("Usage: aqsat-backup-decrypt --key-file <path> <input.enc> <output.bak>");
        return 1;
    }
    keyBase64 = (await File.ReadAllTextAsync(args[1])).Trim();
    inputPath = args[2];
    outputPath = args[3];
}
else
{
    keyBase64 = args[0];
    inputPath = args[1];
    outputPath = args[2];
}

byte[] key;
try
{
    key = BackupFileEncryptor.DecodeKey(keyBase64);
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine($"Key error: {ex.Message}");
    return 1;
}

if (!File.Exists(inputPath))
{
    Console.Error.WriteLine($"Input not found: {inputPath}");
    return 1;
}

try
{
    await using var source = File.OpenRead(inputPath);
    await using var destination = File.Create(outputPath);
    await BackupFileEncryptor.DecryptAsync(source, destination, key);
    await destination.FlushAsync();

    // A successful GCM verification of EVERY chunk already proves integrity; this second pass only
    // reports the size so the operator can eyeball it against the restore runbook's expectations.
    var bytes = new FileInfo(outputPath).Length;
    Console.WriteLine($"Decrypted {inputPath} -> {outputPath} ({bytes} bytes).");
    Console.WriteLine("Next: follow docs/RESTORE-RUNBOOK.md from step 2 (RESTORE FILELISTONLY).");
    return 0;
}
catch (CryptographicException ex)
{
    Console.Error.WriteLine($"Decryption failed: {ex.Message}");
    Console.Error.WriteLine("Wrong key, or the file was tampered with/truncated. Nothing was written beyond a partial output.");
    try
    {
        File.Delete(outputPath);
    }
    catch (Exception deleteEx) when (deleteEx is IOException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine($"(Also could not clean up partial output: {deleteEx.Message})");
    }
    return 2;
}