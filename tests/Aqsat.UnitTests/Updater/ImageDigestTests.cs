using Aqsat.Updater.Docker;

namespace Aqsat.UnitTests.Updater;

/// <summary>
/// The digest gate (UpdateOrchestrator + DockerContainerOrchestrator.PullImageAsync) must compare
/// the REGISTRY manifest digest the release pipeline signs (docs/RELEASE-RUNBOOK.md §2's
/// RepoDigests extraction) against the same value from the just-pulled image — comparing the image
/// config ID instead would refuse every single update. ImageDigest.OfPulledImage pins that
/// extraction; these tests run without a Docker daemon.
/// </summary>
public class ImageDigestTests
{
    private const string Repo = "127.0.0.1:5000/aqsat-api";
    private const string OtherRepo = "registry.example.ir/aqsat-api";

    [Fact]
    public void PrefersTheEntryMatchingThePulledRepository()
    {
        var digest = ImageDigest.OfPulledImage(Repo,
        [
            $"{OtherRepo}@sha256:{new string('a', 64)}",
            $"{Repo}@sha256:{new string('b', 64)}",
        ], $"sha256:{new string('c', 64)}");

        Assert.Equal($"sha256:{new string('b', 64)}", digest);
    }

    [Fact]
    public void FallsBackToTheOnlyEntryWhenRepositoryDiffers()
    {
        var digest = ImageDigest.OfPulledImage(Repo, [$"{OtherRepo}@sha256:{new string('a', 64)}"], "irrelevant");

        Assert.Equal($"sha256:{new string('a', 64)}", digest);
    }

    [Fact]
    public void ExtractsTheDigestAfterTheAtSignEvenForHostPortRepositories()
    {
        // The repository part contains ':' (host:port) but never '@' — the split must take
        // everything after the LAST '@', not get confused by the port colon.
        var digest = ImageDigest.OfPulledImage(Repo, [$"{Repo}@{new string('d', 64)}"], "irrelevant");

        Assert.Equal($"sha256:{new string('d', 64)}", digest);
    }

    [Fact]
    public void AddsTheSha256PrefixWhenTheRegistryOmitsIt()
    {
        var digest = ImageDigest.OfPulledImage(Repo, [$"{Repo}@{new string('e', 64)}"], "irrelevant");

        Assert.Equal($"sha256:{new string('e', 64)}", digest);
    }

    [Fact]
    public void ReturnsTheImageIdWhenNoRepoDigestsExist()
    {
        // docker load / purely local images carry no RepoDigests. Falling back to the config ID
        // makes the digest gate fail closed against a signed manifest digest — correct behavior
        // for an image whose registry origin cannot be established.
        var imageId = $"sha256:{new string('f', 64)}";

        var digest = ImageDigest.OfPulledImage(Repo, [], imageId);
        Assert.Equal(imageId, digest);

        var digestNull = ImageDigest.OfPulledImage(Repo, null, imageId);
        Assert.Equal(imageId, digestNull);
    }
}