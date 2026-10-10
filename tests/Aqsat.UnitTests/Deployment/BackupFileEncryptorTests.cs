using System.Security.Cryptography;
using Aqsat.Infrastructure.Backup;

namespace Aqsat.UnitTests.Deployment;

/// <summary>
/// The offsite leg only helps if the ciphertext can be turned back into a .bak — and only hurts if
/// it CAN'T (wrong key handling), or if a damaged/tampered file silently decrypts to garbage.
/// These prove the properties that matter: round-trip fidelity across chunk boundaries,
/// authentication of every chunk (position + final flag in the AAD), and loud failure on wrong key.
/// </summary>
public class BackupFileEncryptorTests
{
    private static byte[] NewKey() => RandomNumberGenerator.GetBytes(32);

    private static async Task<(byte[] Ciphertext, byte[] Key)> SealAsync(byte[] plaintext, int chunkSize)
    {
        var key = NewKey();
        await using var input = new MemoryStream(plaintext);
        await using var output = new MemoryStream();
        await BackupFileEncryptor.EncryptAsync(input, output, key, chunkSize: chunkSize);
        return (output.ToArray(), key);
    }

    private static async Task<byte[]> UnsealAsync(byte[] ciphertext, byte[] key, int chunkSize)
    {
        await using var input = new MemoryStream(ciphertext);
        await using var output = new MemoryStream();
        await BackupFileEncryptor.DecryptAsync(input, output, key, chunkSize: chunkSize);
        return output.ToArray();
    }

    [Theory]
    [InlineData(0)]                  // empty DB edge — still writes a final chunk
    [InlineData(1)]
    [InlineData(63)]
    [InlineData(64)]                 // exactly one chunk
    [InlineData(65)]                 // one byte over — forces the read-ahead path
    [InlineData(1024 * 1024 + 7)]    // multi-chunk with ragged tail
    public async Task Round_trip_returns_the_original_bytes(int length)
    {
        var plaintext = RandomNumberGenerator.GetBytes(length);
        var (ciphertext, key) = await SealAsync(plaintext, chunkSize: 64);
        var restored = await UnsealAsync(ciphertext, key, chunkSize: 64);
        Assert.Equal(plaintext, restored);
    }

    [Fact]
    public async Task Round_trip_at_the_default_chunk_size_handles_a_large_payload()
    {
        // Bigger than DefaultChunkSize so at least two real 4 MiB chunks plus a partial one go
        // through the production path, not just the small-chunk test path.
        var plaintext = RandomNumberGenerator.GetBytes(BackupFileEncryptor.DefaultChunkSize + 12345);
        var (ciphertext, key) = await SealAsync(plaintext, chunkSize: BackupFileEncryptor.DefaultChunkSize);
        var restored = await UnsealAsync(ciphertext, key, chunkSize: BackupFileEncryptor.DefaultChunkSize);
        Assert.Equal(plaintext, restored);
    }

    [Fact]
    public async Task Different_encryptions_of_the_same_plaintext_produce_different_ciphertext()
    {
        // Per-file random salt + nonce must actually vary: identical output would mean nonce reuse.
        var plaintext = RandomNumberGenerator.GetBytes(4096);
        var (first, key) = await SealAsync(plaintext, chunkSize: 64);
        var (second, _) = await SealAsync(plaintext, chunkSize: 64);
        Assert.NotEqual(first, second);
        // …and both must still decrypt with their own key.
        Assert.Equal(plaintext, await UnsealAsync(first, key, chunkSize: 64));
    }

    [Fact]
    public async Task Wrong_key_fails_loudly_instead_of_returning_garbage()
    {
        var plaintext = RandomNumberGenerator.GetBytes(300);
        var (ciphertext, _) = await SealAsync(plaintext, chunkSize: 64);
        await Assert.ThrowsAsync<CryptographicException>(
            () => UnsealAsync(ciphertext, key: NewKey(), chunkSize: 64));
    }

    [Theory]
    [InlineData(0)]      // first byte of the magic — wrong-file detection
    [InlineData(36)]     // first byte of the first chunk's length prefix
    [InlineData(45)]     // inside the first chunk's ciphertext
    [InlineData(-1)]     // last byte — the final chunk's tag
    public async Task Flipping_any_byte_fails_verification(int byteIndex)
    {
        var plaintext = RandomNumberGenerator.GetBytes(300);
        var (ciphertext, key) = await SealAsync(plaintext, chunkSize: 64);

        var target = byteIndex >= 0 ? byteIndex : ciphertext.Length + byteIndex;
        ciphertext[target] ^= 0xFF;

        await Assert.ThrowsAsync<CryptographicException>(
            () => UnsealAsync(ciphertext, key, chunkSize: 64));
    }

    [Fact]
    public async Task Stripping_the_tail_is_detected_not_decrypted_short()
    {
        // Three chunks; chop the final frame off. The classic failure of naive schemes is quietly
        // returning a "successful" SHORT file here — the restore would then be silently missing
        // the end of the data. The final-chunk AAD flag makes this a hard error instead.
        var plaintext = RandomNumberGenerator.GetBytes(192); // exactly 3 full 64-byte chunks
        var (ciphertext, key) = await SealAsync(plaintext, chunkSize: 64);

        var frames = ParseFrames(ciphertext);
        Assert.Equal(3, frames.Count);
        var withoutTail = Reassemble(frames.Take(2));

        await Assert.ThrowsAsync<CryptographicException>(
            () => UnsealAsync(withoutTail, key, chunkSize: 64));
    }

    [Fact]
    public async Task Reordered_chunks_fail_because_position_is_authenticated()
    {
        var plaintext = RandomNumberGenerator.GetBytes(192); // 3 equal 64-byte chunks
        var (ciphertext, key) = await SealAsync(plaintext, chunkSize: 64);

        var frames = ParseFrames(ciphertext);
        Assert.Equal(3, frames.Count);

        // Swap chunk 0 and chunk 1 — each carries a valid tag from its original position, so only
        // the position-bound AAD can catch this.
        (frames[0], frames[1]) = (frames[1], frames[0]);

        await Assert.ThrowsAsync<CryptographicException>(
            () => UnsealAsync(Reassemble(frames), key, chunkSize: 64));
    }

    [Fact]
    public void DecodeKey_rejects_missing_empty_and_wrong_length_keys()
    {
        Assert.Throws<InvalidOperationException>(() => BackupFileEncryptor.DecodeKey(null));
        Assert.Throws<InvalidOperationException>(() => BackupFileEncryptor.DecodeKey(""));
        Assert.Throws<InvalidOperationException>(() => BackupFileEncryptor.DecodeKey("not base64 !!"));
        Assert.Throws<InvalidOperationException>(() => BackupFileEncryptor.DecodeKey(Convert.ToBase64String(new byte[16])));
        Assert.Equal(32, BackupFileEncryptor.DecodeKey(Convert.ToBase64String(new byte[32])).Length);
    }

    // header = magic(8) + file nonce(12) + salt(16); each frame = length(4, big-endian) + ct(n) + tag(16).
    private const int HeaderSize = 8 + 12 + 16;

    private static List<byte[]> ParseFrames(byte[] ciphertext)
    {
        var frames = new List<byte[]>();
        var offset = HeaderSize;
        while (offset < ciphertext.Length)
        {
            var length = (ciphertext[offset] << 24) | (ciphertext[offset + 1] << 16)
                       | (ciphertext[offset + 2] << 8) | ciphertext[offset + 3];
            var frameLength = 4 + length + 16;
            frames.Add(ciphertext.AsSpan(offset, frameLength).ToArray());
            offset += frameLength;
        }
        return frames;
    }

    private static byte[] Reassemble(IEnumerable<byte[]> frames)
    {
        using var output = new MemoryStream();
        foreach (var frame in frames)
        {
            output.Write(frame);
        }
        return output.ToArray();
    }
}