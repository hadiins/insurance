namespace Aqsat.Updater;

/// <summary>
/// docs/UPDATE-SYSTEM.md §1/§4 — the only shape of input this service ever accepts. No arbitrary
/// commands, no arbitrary files: a version tag, the image it points to, and a signature over both.
/// Task 22's panel is the thing that will eventually populate this from a signed UpdatePackage
/// catalog row rather than a raw request body; this service doesn't care where it came from, only
/// that the signature checks out.
/// </summary>
public sealed record UpdateManifest(string Version, string ImageTag, string Sha256, string SignatureBase64)
{
    /// <summary>Exactly what the publisher signed — canonical, order-fixed, so signer and verifier
    /// can never disagree on what bytes were signed.</summary>
    public string SignedPayload() => $"{Version}|{ImageTag}|{Sha256}";
}
