namespace Aqsat.Updater.Docker;

/// <summary>
/// docs/RELEASE-RUNBOOK.md §2 — the release pipeline signs the REGISTRY manifest digest (what
/// `docker push` reports in RepoDigests, and what `docker inspect --format='{{index .RepoDigests 0}}'`
/// extracts), NOT the image config ID (`docker inspect .Id` — a different hash entirely). A pull-side
/// digest gate that compares the config ID against the signed RepoDigest therefore fails EVERY update
/// with "digest does not match" even when the image is exactly the signed one. This helper extracts
/// the same RepoDigest value the publisher signed. Pure and public so unit tests can pin the
/// behavior without a Docker daemon (see tests/Aqsat.UnitTests/Updater/ImageDigestTests.cs).
/// </summary>
public static class ImageDigest
{
    /// <summary>Picks the RepoDigest of the just-pulled image. Prefers the entry for the exact
    /// repository that was pulled (an image tag can carry digests from several registries it was
    /// pushed to / retagged from), falls back to the only entry there is, and only then to the
    /// image config ID — which cannot match a signed manifest digest, so UpdateOrchestrator's gate
    /// fails closed, exactly as it should for an image whose origin it cannot establish.</summary>
    public static string OfPulledImage(string repository, IEnumerable<string>? repoDigests, string imageId)
    {
        var digests = repoDigests?.ToList() ?? [];
        var entry = digests.FirstOrDefault(d => d.StartsWith(repository + "@", StringComparison.OrdinalIgnoreCase))
                    ?? digests.FirstOrDefault();

        if (entry is null)
        {
            return imageId;
        }

        // RepoDigest entries look like "127.0.0.1:5000/aqsat-api@sha256:abc…" — take everything
        // after the '@' (repository names may contain ':' for a host:port, never '@').
        var digest = entry[(entry.LastIndexOf('@') + 1)..];
        return digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest : $"sha256:{digest}";
    }
}