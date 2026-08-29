using System.Security.Cryptography;
using System.Text;
using Aqsat.Application.Common;
using Microsoft.Extensions.Configuration;

namespace Aqsat.Infrastructure.Security;

/// <summary>
/// AES-GCM field-level encryption for data-at-rest fields like Customer.NationalId. The key comes
/// from configuration ("Encryption:NationalIdKey", base64) — a dev-only value lives in
/// appsettings.Development.json; production must supply it via environment/secret store.
/// </summary>
public sealed class AesFieldEncryptor : IFieldEncryptor
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[] _key;
    private readonly byte[] _hmacKey;

    public AesFieldEncryptor(IConfiguration configuration)
    {
        var base64Key = configuration["Encryption:NationalIdKey"]
            ?? throw new InvalidOperationException("Encryption:NationalIdKey is not configured.");
        _key = Convert.FromBase64String(base64Key);

        // Domain-separated subkey: Hash() must not reuse the AES key directly, and an UNKEYED hash
        // of a 10-digit national ID is trivially reversible by precomputation from any DB or backup
        // read — which silently defeated the whole point of encrypting NationalId at rest. Deriving
        // a distinct HMAC key means offline reversal requires this same server-side secret.
        _hmacKey = SHA256.HashData(Encoding.UTF8.GetBytes("Aqsat.NationalIdHash.v2").Concat(_key).ToArray());
    }

    public byte[] Encrypt(string plaintext)
    {
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plainBytes, cipherBytes, tag);

        // Layout: nonce | tag | ciphertext
        var result = new byte[NonceSize + TagSize + cipherBytes.Length];
        Buffer.BlockCopy(nonce, 0, result, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, result, NonceSize, TagSize);
        Buffer.BlockCopy(cipherBytes, 0, result, NonceSize + TagSize, cipherBytes.Length);
        return result;
    }

    public string Decrypt(byte[] ciphertext)
    {
        var nonce = ciphertext.AsSpan(0, NonceSize);
        var tag = ciphertext.AsSpan(NonceSize, TagSize);
        var cipherBytes = ciphertext.AsSpan(NonceSize + TagSize);
        var plainBytes = new byte[cipherBytes.Length];

        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, cipherBytes, tag, plainBytes);

        return Encoding.UTF8.GetString(plainBytes);
    }

    /// <summary>Keyed hash (HMAC-SHA256) for equality lookup without decrypting. Deliberately NOT
    /// a plain SHA-256 — see the _hmacKey derivation comment in the constructor.</summary>
    public byte[] Hash(string plaintext)
    {
        using var hmac = new HMACSHA256(_hmacKey);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(plaintext));
    }
}
