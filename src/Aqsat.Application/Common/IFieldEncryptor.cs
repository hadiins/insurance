namespace Aqsat.Application.Common;

/// <summary>Encrypts sensitive fields at rest (e.g. NationalId — CLAUDE.md rule 12).</summary>
public interface IFieldEncryptor
{
    byte[] Encrypt(string plaintext);
    string Decrypt(byte[] ciphertext);

    /// <summary>SHA-256 of the plaintext, for lookup without decrypting.</summary>
    byte[] Hash(string plaintext);
}
