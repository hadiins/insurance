using System.Security.Cryptography;
using System.Text;
using Aqsat.Application.Platform;
using Microsoft.Extensions.Configuration;

namespace Aqsat.Infrastructure.Platform;

/// <summary>Same algorithm, same padding, same configuration key as
/// src/Aqsat.Updater/Security/PackageSignatureVerifier.cs — kept in sync by hand, not by sharing
/// code, per the isolation principle docs/UPDATE-SYSTEM.md §1 establishes.</summary>
public sealed class PackageSignatureVerifier(IConfiguration configuration) : IPackageSignatureVerifier
{
    private RSA? _publicKey;

    public bool Verify(string version, string imageTag, string sha256, string signatureBase64)
    {
        byte[] signatureBytes;
        try
        {
            signatureBytes = Convert.FromBase64String(signatureBase64);
        }
        catch (FormatException)
        {
            return false;
        }

        var payload = $"{version}|{imageTag}|{sha256}";
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        return GetPublicKey().VerifyData(payloadBytes, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
    }

    /// <summary>Parsing the PEM happens on first actual use, not construction. This class is
    /// injected into PlatformUpdatesController's constructor, and MVC resolves every constructor
    /// dependency for EVERY action on that controller regardless of which one is called — an eager
    /// throw here for an unset Updater:SigningPublicKeyPem would 500 every other endpoint on the
    /// controller (status, otp, history, ...) too, not just /register, which is the only one that
    /// actually needs this.</summary>
    private RSA GetPublicKey()
    {
        if (_publicKey is not null)
        {
            return _publicKey;
        }

        var publicKeyPem = configuration["Updater:SigningPublicKeyPem"];
        if (string.IsNullOrWhiteSpace(publicKeyPem))
        {
            throw new InvalidOperationException("Updater:SigningPublicKeyPem is not configured — package registration is unavailable until it is.");
        }

        var key = RSA.Create();
        key.ImportFromPem(publicKeyPem);
        _publicKey = key;
        return _publicKey;
    }
}
