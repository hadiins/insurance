using System.Diagnostics;
using System.Text.Json;
using Aqsat.Updater;
using Aqsat.Updater.Security;
using Microsoft.Extensions.Configuration;

namespace Aqsat.UnitTests.Release;

/// <summary>
/// Task 23's own check (docs/TASKS.md): publish a version, apply it, end to end. The one step that
/// crosses a tool boundary — scripts/release/sign-package.sh (bash + OpenSSL) producing a signature
/// src/Aqsat.Updater/Security/PackageSignatureVerifier.cs (.NET, RSASignaturePadding.Pss) has to
/// accept — is exactly the kind of thing that "looks right" in each half separately and silently
/// breaks on padding or salt-length mismatches. This runs the actual script (not a reimplementation
/// of it) and feeds its output straight into the real verifier.
///
/// Skips itself if `bash`/`openssl` aren't on PATH rather than failing the whole suite — this is an
/// environment-availability gate, not a product bug, and CI images that lack a POSIX shell entirely
/// shouldn't block on it.
/// </summary>
public class ReleaseSigningPipelineTests
{
    [Fact]
    public async Task A_signature_produced_by_the_real_release_script_verifies_against_the_real_dotnet_verifier()
    {
        var repoRoot = FindRepoRoot();
        var scriptPath = Path.Combine(repoRoot, "scripts", "release", "sign-package.sh");
        if (!File.Exists(scriptPath) || !IsOnPath("bash") || !IsOnPath("openssl"))
        {
            // Environment-availability gate, not a product bug — a CI image with no POSIX shell at
            // all shouldn't fail the whole suite over it.
            return;
        }

        var workDir = Path.Combine(Path.GetTempPath(), $"aqsat-release-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);
        try
        {
            var privateKeyPath = Path.Combine(workDir, "release-signing-key.pem");
            var publicKeyPath = Path.Combine(workDir, "release-signing-key.pub.pem");
            await RunAsync("openssl", $"genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out \"{privateKeyPath}\"", workDir);
            await RunAsync("openssl", $"pkey -in \"{privateKeyPath}\" -pubout -out \"{publicKeyPath}\"", workDir);

            const string version = "1.5.0";
            const string imageTag = "registry.example.ir/aqsat-api:1.5.0";
            const string sha = "sha256:abc123def456";

            var scriptOutput = await RunAsync("bash", $"\"{scriptPath}\" \"{version}\" \"{imageTag}\" \"{sha}\" \"{privateKeyPath}\"", workDir);
            var manifestJson = JsonDocument.Parse(scriptOutput);
            var signatureBase64 = manifestJson.RootElement.GetProperty("signatureBase64").GetString()!;

            var publicKeyPem = await File.ReadAllTextAsync(publicKeyPath);
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Updater:SigningPublicKeyPem"] = publicKeyPem })
                .Build();
            var verifier = new PackageSignatureVerifier(configuration);

            var manifest = new UpdateManifest(version, imageTag, sha, signatureBase64);
            Assert.True(verifier.Verify(manifest));

            // And the usual tamper check still holds through the real pipeline, not just the
            // in-process .NET-signs/.NET-verifies path PackageSignatureVerifierTests covers.
            Assert.False(verifier.Verify(manifest with { ImageTag = "registry.example.ir/aqsat-api:malicious" }));
        }
        finally
        {
            Directory.Delete(workDir, recursive: true);
        }
    }

    private static async Task<string> RunAsync(string fileName, string arguments, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo(fileName, arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(startInfo)!;
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{fileName} {arguments} exited {process.ExitCode}\n{stderr}");
        }

        return stdout;
    }

    private static bool IsOnPath(string command)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("bash", $"-lc \"command -v {command}\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            })!;
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aqsat.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the repo root (Aqsat.sln) from the test output directory.");
    }
}
