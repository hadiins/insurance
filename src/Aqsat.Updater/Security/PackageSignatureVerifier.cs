using System.Security.Cryptography;
using System.Text;

namespace Aqsat.Updater.Security;

/// <summary>
/// docs/UPDATE-SYSTEM.md rule 3, verbatim: "a package without a valid signature is rejected — this
/// is the only thing that stops an installing a tampered release." RSA-PSS/SHA-256 over the
/// manifest's canonical payload, verified against a public key baked into this service's own
/// config (never accepted as part of the request — the whole point is that the caller cannot
/// choose which key verifies their package).
/// </summary>
public interface IPackageSignatureVerifier
{
    bool Verify(UpdateManifest manifest);
}

public sealed class PackageSignatureVerifier : IPackageSignatureVerifier
{
    private readonly RSA _publicKey;

    public PackageSignatureVerifier(IConfiguration configuration)
    {
        var publicKeyPem = configuration["Updater:SigningPublicKeyPem"];
        if (string.IsNullOrWhiteSpace(publicKeyPem))
        {
            throw new InvalidOperationException(
                "Updater:SigningPublicKeyPem is not configured — this service cannot verify any package without it, and must not start pretending it can.");
        }

        _publicKey = RSA.Create();
        _publicKey.ImportFromPem(publicKeyPem);
    }

    public bool Verify(UpdateManifest manifest)
    {
        byte[] signatureBytes;
        try
        {
            signatureBytes = Convert.FromBase64String(manifest.SignatureBase64);
        }
        catch (FormatException)
        {
            // Not valid base64 at all — reject the same as any other invalid signature, not a
            // separate error path an attacker could use to distinguish "malformed" from "wrong".
            return false;
        }

        var payloadBytes = Encoding.UTF8.GetBytes(manifest.SignedPayload());
        return _publicKey.VerifyData(payloadBytes, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
    }
}
