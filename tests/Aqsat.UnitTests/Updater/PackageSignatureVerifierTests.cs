using System.Security.Cryptography;
using System.Text;
using Aqsat.Updater;
using Aqsat.Updater.Security;
using Microsoft.Extensions.Configuration;

namespace Aqsat.UnitTests.Updater;

/// <summary>
/// Task 21's own check (docs/TASKS.md): send a package with an invalid signature — it must be
/// rejected. docs/UPDATE-SYSTEM.md rule 3: "a package without a valid signature is rejected — this
/// is the only thing that stops installing a tampered release."
/// </summary>
public class PackageSignatureVerifierTests
{
    [Fact]
    public void A_manifest_signed_by_the_matching_private_key_verifies()
    {
        using var rsa = RSA.Create(2048);
        var verifier = BuildVerifier(rsa);
        var manifest = Sign(rsa, new UpdateManifest("1.5.0", "registry.example.ir/aqsat-api:1.5.0", "abc123", ""));

        Assert.True(verifier.Verify(manifest));
    }

    [Fact]
    public void A_manifest_signed_by_a_different_key_is_rejected()
    {
        using var trustedKey = RSA.Create(2048);
        using var attackerKey = RSA.Create(2048);
        var verifier = BuildVerifier(trustedKey);

        // The attacker signs a package with their own key — the verifier only trusts one public
        // key (configured server-side, never taken from the request), so this must fail.
        var manifest = Sign(attackerKey, new UpdateManifest("1.5.0", "registry.example.ir/aqsat-api:1.5.0", "abc123", ""));

        Assert.False(verifier.Verify(manifest));
    }

    [Fact]
    public void A_manifest_whose_fields_were_tampered_with_after_signing_is_rejected()
    {
        using var rsa = RSA.Create(2048);
        var verifier = BuildVerifier(rsa);
        var signed = Sign(rsa, new UpdateManifest("1.5.0", "registry.example.ir/aqsat-api:1.5.0", "abc123", ""));

        // Signature is untouched, but the image tag was swapped after signing — exactly what an
        // attacker who intercepted a legitimate manifest and tried to redirect it would do.
        var tampered = signed with { ImageTag = "registry.example.ir/aqsat-api:malicious" };

        Assert.False(verifier.Verify(tampered));
    }

    [Fact]
    public void A_manifest_with_garbage_for_a_signature_is_rejected_not_thrown()
    {
        using var rsa = RSA.Create(2048);
        var verifier = BuildVerifier(rsa);
        var manifest = new UpdateManifest("1.5.0", "registry.example.ir/aqsat-api:1.5.0", "abc123", "not-valid-base64!!!");

        Assert.False(verifier.Verify(manifest));
    }

    private static IPackageSignatureVerifier BuildVerifier(RSA trustedKey)
    {
        var publicKeyPem = trustedKey.ExportSubjectPublicKeyInfoPem();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Updater:SigningPublicKeyPem"] = publicKeyPem })
            .Build();
        return new PackageSignatureVerifier(configuration);
    }

    private static UpdateManifest Sign(RSA signingKey, UpdateManifest unsigned)
    {
        var payloadBytes = Encoding.UTF8.GetBytes(unsigned.SignedPayload());
        var signatureBytes = signingKey.SignData(payloadBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        return unsigned with { SignatureBase64 = Convert.ToBase64String(signatureBytes) };
    }
}
