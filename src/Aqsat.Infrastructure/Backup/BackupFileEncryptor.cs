using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Aqsat.Infrastructure.Backup;

/// <summary>
/// Chunked AES-256-GCM encryption for whole backup files (the offsite/Google Drive leg — see
/// <see cref="GoogleDriveOffsiteUploader"/>). Backups hold the entire customer/payment database, so
/// they are sealed BEFORE leaving the server: what lands in cloud storage is opaque ciphertext and
/// the key never leaves this environment.
///
/// Format (all integers big-endian):
///   header  : magic "AQBKENC1" (8) | file nonce (12) | HKDF salt (16)
///   chunk i : plaintext length (4) | ciphertext (n) | GCM tag (16)
///
/// Per-chunk nonce = file nonce with its last 4 bytes XORed by the chunk index, so every chunk under
/// a given key gets a distinct nonce. AAD = magic | chunk index | final-chunk flag binds each tag to
/// its position and to being/not-being the last chunk — reordering, splicing between two backups, or
/// truncating the tail all fail tag verification instead of decrypting to garbage.
///
/// The subkey is HKDF-SHA256(master key, salt = per-file random, info = domain label), matching the
/// domain-separation discipline of <see cref="Security.AesFieldEncryptor"/>: this key is used for
/// nothing but backup files, and a per-file salt means the same master key never enciphers two
/// backups under identical keystream conditions.
///
/// Key material is a base64 32-byte value under <c>Backup:EncryptionKey</c> — the decrypt tool
/// (src/Aqsat.BackupDecrypt) and RESTORE-RUNBOOK.md need the SAME value, so store it OFF the server
/// (password manager / sealed copy). Losing it makes every offsite backup unrecoverable by design.
/// </summary>
public static class BackupFileEncryptor
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("AQBKENC1");
    private const int NonceSize = 12;
    private const int SaltSize = 16;
    private const int TagSize = 16;
    private const int KeySize = 32;
    private const int LengthPrefixSize = 4;
    private const string HkdfInfo = "Aqsat.BackupFileEncryption.v1";

    /// <summary>Plaintext bytes sealed per GCM invocation. Chosen for multi-GB backups: bounded
    /// memory regardless of database size, and few enough round trips that CPU stays negligible.</summary>
    public const int DefaultChunkSize = 4 * 1024 * 1024;

    /// <summary>Decodes the configured base64 master key, rejecting anything that isn't exactly
    /// 32 bytes — a short key would silently weaken AES-256 to AES-128-or-less.</summary>
    public static byte[] DecodeKey(string? base64Key)
    {
        if (string.IsNullOrWhiteSpace(base64Key))
        {
            throw new InvalidOperationException(
                "Backup:EncryptionKey is not configured. Generate one with: openssl rand -base64 32 — " +
                "and store it OFF the server; without it, offsite backups cannot be decrypted.");
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(base64Key);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("Backup:EncryptionKey is not valid base64.", ex);
        }

        if (key.Length != KeySize)
        {
            throw new InvalidOperationException(
                $"Backup:EncryptionKey must decode to exactly {KeySize} bytes, got {key.Length}. " +
                "Generate one with: openssl rand -base64 32");
        }

        return key;
    }

    /// <summary>Seals <paramref name="source"/> into <paramref name="destination"/>. Both streams are
    /// left open. Pass <paramref name="chunkSize"/> only from tests — production uses
    /// <see cref="DefaultChunkSize"/>.</summary>
    public static async Task EncryptAsync(
        Stream source, Stream destination, byte[] key, CancellationToken ct = default, int chunkSize = DefaultChunkSize)
    {
        ValidateKey(key);
        ArgumentOutOfRangeException.ThrowIfLessThan(chunkSize, 1);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var fileNonce = RandomNumberGenerator.GetBytes(NonceSize);

        await destination.WriteAsync(Magic, ct);
        await destination.WriteAsync(fileNonce, ct);
        await destination.WriteAsync(salt, ct);

        var derived = new byte[KeySize];
        HKDF.DeriveKey(HashAlgorithmName.SHA256, key, derived, salt, Encoding.UTF8.GetBytes(HkdfInfo));
        using var aes = new AesGcm(derived, TagSize);

        var current = new byte[chunkSize];
        var next = new byte[chunkSize];
        var currentLength = await ReadFullAsync(source, current, ct);
        var index = 0L;

        // Read-ahead so the final chunk is only written once we KNOW it's final — the AAD flag is
        // what makes tail truncation detectable on decrypt.
        while (true)
        {
            var nextLength = await ReadFullAsync(source, next, ct);
            var isFinal = nextLength == 0;
            await WriteChunkAsync(destination, aes, fileNonce, current, currentLength, index, isFinal, ct);
            if (isFinal)
            {
                break;
            }

            (current, next) = (next, current);
            currentLength = nextLength;
            index++;
        }
    }

    /// <summary>Reverses <see cref="EncryptAsync"/>. Throws <see cref="CryptographicException"/> on
    /// any tampering, reordering, wrong key, or truncation — never emits partially trusted output.
    /// Both streams are left open.</summary>
    public static async Task DecryptAsync(
        Stream source, Stream destination, byte[] key, CancellationToken ct = default, int chunkSize = DefaultChunkSize)
    {
        ValidateKey(key);
        ArgumentOutOfRangeException.ThrowIfLessThan(chunkSize, 1);

        var header = new byte[Magic.Length + NonceSize + SaltSize];
        await ReadExactAsync(source, header, ct, "header");
        if (!header.AsSpan(0, Magic.Length).SequenceEqual(Magic))
        {
            throw new CryptographicException("Not an Aqsat encrypted backup (bad magic) — wrong file or corrupt.");
        }

        var fileNonce = header.AsSpan(Magic.Length, NonceSize).ToArray();
        var salt = header.AsSpan(Magic.Length + NonceSize, SaltSize).ToArray();
        var derived = new byte[KeySize];
        HKDF.DeriveKey(HashAlgorithmName.SHA256, key, derived, salt, Encoding.UTF8.GetBytes(HkdfInfo));

        using var aes = new AesGcm(derived, TagSize);
        var index = 0L;
        byte[]? pendingLengthPrefix = null;

        while (true)
        {
            // The previous iteration's peek already CONSUMED these 4 bytes from the stream — reusing
            // them as-is instead of reading again (which would eat the next chunk's first 4
            // ciphertext bytes and corrupt the length).
            byte[] chunkLengthBytes;
            int prefixRead;
            if (pendingLengthPrefix is { } peekedPrefix)
            {
                chunkLengthBytes = peekedPrefix;
                pendingLengthPrefix = null;
                prefixRead = LengthPrefixSize;
            }
            else
            {
                chunkLengthBytes = new byte[LengthPrefixSize];
                prefixRead = await ReadUpToAsync(source, chunkLengthBytes, ct);
            }
            if (prefixRead == 0)
            {
                // The encryptor ALWAYS writes at least one (possibly empty) final chunk, so a clean
                // EOF here means the tail was chopped off.
                throw new CryptographicException("Encrypted backup is truncated (no final chunk).");
            }

            if (prefixRead < LengthPrefixSize)
            {
                var rest = new byte[LengthPrefixSize - prefixRead];
                await ReadExactAsync(source, rest, ct, "length prefix");
                rest.CopyTo(chunkLengthBytes, prefixRead);
            }

            var chunkLength = BinaryPrimitives.ReadInt32BigEndian(chunkLengthBytes);
            if (chunkLength < 0 || (long)chunkLength > chunkSize)
            {
                throw new CryptographicException($"Encrypted backup is corrupt (chunk length {chunkLength} outside 0..{chunkSize}).");
            }

            var ciphertext = new byte[chunkLength];
            await ReadExactAsync(source, ciphertext, ct, "ciphertext");
            var tag = new byte[TagSize];
            await ReadExactAsync(source, tag, ct, "tag");

            // Peek the next length prefix to learn whether this chunk was the last one BEFORE the
            // tag is verified — the flag is part of the AAD, so a stripped tail fails right here.
            var peekBuffer = new byte[LengthPrefixSize];
            var peeked = await ReadUpToAsync(source, peekBuffer, ct);
            var isFinal = peeked == 0;
            if (peeked > 0 && peeked < LengthPrefixSize)
            {
                var rest = new byte[LengthPrefixSize - peeked];
                await ReadExactAsync(source, rest, ct, "length prefix");
                rest.CopyTo(peekBuffer, peeked);
            }
            if (!isFinal)
            {
                pendingLengthPrefix = peekBuffer;
            }

            var plaintext = new byte[chunkLength];
            try
            {
                aes.Decrypt(
                    NonceFor(fileNonce, index),
                    ciphertext,
                    tag,
                    plaintext,
                    BuildAad(index, isFinal));
            }
            catch (CryptographicException ex)
            {
                throw new CryptographicException(
                    "Backup decryption failed — wrong key, or a chunk was tampered with, reordered, " +
                    "or spliced in from another backup.",
                    ex);
            }

            await destination.WriteAsync(plaintext, ct);
            if (isFinal)
            {
                return;
            }

            index++;
        }
    }

    /// <summary>Derives the per-chunk nonce: the file nonce with its LAST 4 bytes XORed by the
    /// big-endian chunk index. XOR keeps it a bijection — distinct indices under one file nonce
    /// always produce distinct nonces, which is what GCM's one-shot-per-nonce rule demands.</summary>
    private static byte[] NonceFor(byte[] fileNonce, long index)
    {
        var nonce = (byte[])fileNonce.Clone();
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, index);
        for (var i = 0; i < 4; i++)
        {
            nonce[NonceSize - 4 + i] ^= counter[4 + i];
        }
        return nonce;
    }

    /// <summary>AAD = magic | chunk index | final flag. Binding position means reordered or spliced
    /// chunks fail verification even if each was individually valid in its original file.</summary>
    private static byte[] BuildAad(long index, bool isFinal)
    {
        var aad = new byte[Magic.Length + 8 + 1];
        Magic.CopyTo(aad, 0);
        BinaryPrimitives.WriteInt64BigEndian(aad.AsSpan(Magic.Length, 8), index);
        aad[^1] = isFinal ? (byte)1 : (byte)0;
        return aad;
    }

    private static async Task WriteChunkAsync(
        Stream destination,
        AesGcm aes,
        byte[] fileNonce,
        byte[] plaintext,
        int length,
        long index,
        bool isFinal,
        CancellationToken ct)
    {
        var ciphertext = new byte[length];
        var tag = new byte[TagSize];
        aes.Encrypt(NonceFor(fileNonce, index), plaintext.AsSpan(0, length), ciphertext, tag, BuildAad(index, isFinal));

        var prefix = new byte[LengthPrefixSize];
        BinaryPrimitives.WriteInt32BigEndian(prefix, length);
        await destination.WriteAsync(prefix, ct);
        await destination.WriteAsync(ciphertext, ct);
        await destination.WriteAsync(tag, ct);
    }

    private static void ValidateKey(byte[] key)
    {
        if (key.Length != KeySize)
        {
            throw new ArgumentException($"Encryption key must be exactly {KeySize} bytes, got {key.Length}.", nameof(key));
        }
    }

    /// <summary>Fills the buffer completely or returns the short count at EOF — Stream.ReadAsync is
    /// allowed to return fewer bytes than requested without meaning end-of-stream.</summary>
    private static async Task<int> ReadFullAsync(Stream source, byte[] buffer, CancellationToken ct)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await source.ReadAsync(buffer.AsMemory(offset), ct);
            if (read == 0)
            {
                break;
            }
            offset += read;
        }
        return offset;
    }

    /// <summary>Reads up to buffer.Length bytes; 0 means clean EOF.</summary>
    private static Task<int> ReadUpToAsync(Stream source, byte[] buffer, CancellationToken ct)
        => ReadFullAsync(source, buffer, ct);

    private static async Task ReadExactAsync(Stream source, byte[] buffer, CancellationToken ct, string what)
    {
        if (await ReadFullAsync(source, buffer, ct) < buffer.Length)
        {
            throw new CryptographicException($"Encrypted backup is truncated inside the {what}.");
        }
    }
}