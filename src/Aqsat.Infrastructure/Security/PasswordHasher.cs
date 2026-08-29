using System.Security.Cryptography;
using Aqsat.Application.Common;
using Microsoft.Extensions.Configuration;

namespace Aqsat.Infrastructure.Security;

/// <summary>
/// PBKDF2 (Rfc2898DeriveBytes) — built into the BCL, no new NuGet package, same reasoning as
/// AesFieldEncryptor. Encoded as "{iterations}.{saltBase64}.{hashBase64}" so the iteration count
/// can increase later without invalidating existing hashes. New hashes use OWASP's current floor
/// for PBKDF2-HMAC-SHA256 (600,000 iterations, up from 210k); override via
/// "Security:PasswordIterations" (appsettings.Development.json lowers it purely so the test
/// suite's hundreds of logins don't pay the full cost). Verify reads the iteration count from the
/// stored hash itself, so old and new formats coexist transparently.
/// </summary>
public sealed class PasswordHasher : IPasswordHasher
{
    private const int DefaultIterations = 600_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    private readonly int _iterations;

    // Optional so test/seed code (DevSeeder) can construct one without an IConfiguration.
    public PasswordHasher(IConfiguration? configuration = null)
    {
        _iterations = configuration?.GetValue("Security:PasswordIterations", DefaultIterations) ?? DefaultIterations;
        if (_iterations is < 100_000 or > 10_000_000)
        {
            throw new InvalidOperationException(
                "Security:PasswordIterations must be between 100,000 and 10,000,000.");
        }
    }

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, _iterations, HashAlgorithmName.SHA256, HashSize);
        return $"{_iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public bool Verify(string password, string hash)
    {
        var parts = hash.Split('.');
        if (parts.Length != 3 || !int.TryParse(parts[0], out var iterations) || iterations < 1)
        {
            return false;
        }

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(parts[1]);
            expected = Convert.FromBase64String(parts[2]);
        }
        catch (FormatException)
        {
            // A malformed stored hash (corrupt row, future format) means "cannot verify", not a
            // server error — a login attempt against it must fail closed with false.
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);

        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
