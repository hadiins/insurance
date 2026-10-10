using System.Security.Cryptography;
using System.Text.Json;
using Aqsat.Infrastructure.Backup;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aqsat.UnitTests.Deployment;

/// <summary>
/// The offsite leg is the only part of the backup strategy that talks to the outside world, so
/// it's the only part that can silently stop working (expired key, revoked share, Drive outage)
/// while local .baks keep piling up and looking healthy. These pin the contract with a fake
/// Drive (no network, no credentials): opt-in no-op when unconfigured, ciphertext-only upload
/// that decrypts back to the original .bak, loud failure on auth errors, and best-effort remote
/// pruning that can never fail a successful upload.
/// </summary>
public class GoogleDriveOffsiteUploaderTests
{
    private static string NewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static string NewServiceAccountJson() => JsonSerializer.Serialize(new
    {
        client_email = "aqsat-backup@test.iam.gserviceaccount.com",
        private_key = RSA.Create(2048).ExportPkcs8PrivateKeyPem(),
        token_uri = "https://oauth2.googleapis.com/token",
    });

    private static IConfiguration Config(string? folderId, string? serviceAccountJson = null, string? key = null) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Backup:Directory"] = Path.GetTempPath(),
                ["Backup:RetentionDays"] = "14",
                ["Backup:EncryptionKey"] = key ?? NewKey(),
                ["Backup:Offsite:FolderId"] = folderId,
                ["Backup:Offsite:ServiceAccountJson"] = serviceAccountJson,
            })
            .Build();

    private static GoogleDriveOffsiteUploader Uploader(
        FakeDriveHandler fake, IConfiguration config, out HttpClient http)
    {
        http = new HttpClient(fake);
        return new GoogleDriveOffsiteUploader(
            http, config, TimeProvider.System, NullLogger<GoogleDriveOffsiteUploader>.Instance);
    }

    private static string WriteTempBak(byte[] content)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"aqsat-offsite-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "Aqsat-20261008-030000.bak");
        File.WriteAllBytes(path, content);
        return path;
    }

    private static async Task<byte[]> DecryptAsync(byte[] ciphertext, string keyBase64)
    {
        await using var input = new MemoryStream(ciphertext);
        await using var output = new MemoryStream();
        await BackupFileEncryptor.DecryptAsync(
            input, output, BackupFileEncryptor.DecodeKey(keyBase64));
        return output.ToArray();
    }

    [Fact]
    public async Task Noop_when_folder_id_missing_returns_false_and_makes_no_http_calls()
    {
        var fake = new FakeDriveHandler();
        var uploader = Uploader(fake, Config(folderId: null), out var http);
        using var _ = http;

        var bak = WriteTempBak(RandomNumberGenerator.GetBytes(1024));
        try
        {
            Assert.False(await uploader.UploadAsync(bak));
            Assert.Empty(fake.Requests);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(bak)!, recursive: true);
        }
    }

    [Fact]
    public async Task Upload_seals_ciphertext_only_and_round_trips_through_fake_drive()
    {
        var fake = new FakeDriveHandler();
        var key = NewKey();
        var uploader = Uploader(
            fake, Config(folderId: "folder123", serviceAccountJson: NewServiceAccountJson(), key: key),
            out var http);
        using var _ = http;

        var original = RandomNumberGenerator.GetBytes(3000);
        var bak = WriteTempBak(original);
        try
        {
            Assert.True(await uploader.UploadAsync(bak));

            // What crossed the wire decrypts back to the exact .bak with the same key …
            var uploaded = fake.UploadedBytes;
            Assert.NotEmpty(uploaded);
            Assert.Equal(original, await DecryptAsync(uploaded, key));
            // … and is NOT the plaintext (the Drive folder must never see a usable .bak).
            Assert.NotEqual(original, uploaded);

            // Session metadata names the .enc inside the configured folder.
            using var metadata = JsonDocument.Parse(fake.SessionMetadataJson);
            Assert.EndsWith(".enc", metadata.RootElement.GetProperty("name").GetString());
            Assert.Equal("folder123", metadata.RootElement.GetProperty("parents")[0].GetString());

            // Local hygiene: the .bak survives (it's the restore input), the sealed temp doesn't.
            Assert.True(File.Exists(bak));
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(bak)!, "*.enc"));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(bak)!, recursive: true);
        }
    }

    [Fact]
    public async Task Auth_failure_is_loud_not_a_silent_missing_copy()
    {
        var fake = new FakeDriveHandler { TokenFails = true };
        var uploader = Uploader(
            fake, Config(folderId: "folder123", serviceAccountJson: NewServiceAccountJson()),
            out var http);
        using var _ = http;

        var bak = WriteTempBak(RandomNumberGenerator.GetBytes(512));
        try
        {
            // Must surface (Hangfire red row + retry), never a quiet false.
            await Assert.ThrowsAsync<HttpRequestException>(() => uploader.UploadAsync(bak));
            // Even on failure the sealed temp is cleaned up, and the .bak is untouched.
            Assert.True(File.Exists(bak));
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(bak)!, "*.enc"));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(bak)!, recursive: true);
        }
    }

    [Fact]
    public async Task Prune_failure_does_not_fail_a_successful_upload()
    {
        var fake = new FakeDriveHandler { ListFails = true };
        var uploader = Uploader(
            fake, Config(folderId: "folder123", serviceAccountJson: NewServiceAccountJson()),
            out var http);
        using var _ = http;

        var bak = WriteTempBak(RandomNumberGenerator.GetBytes(512));
        try
        {
            Assert.True(await uploader.UploadAsync(bak));
            Assert.NotEmpty(fake.UploadedBytes);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(bak)!, recursive: true);
        }
    }

    [Fact]
    public async Task Prune_trashes_only_remote_files_older_than_the_retention_window()
    {
        var fake = new FakeDriveHandler
        {
            ListJson = JsonSerializer.Serialize(new
            {
                files = new[]
                {
                    new { id = "old-id", name = "Aqsat-20200101-030000.bak.enc", createdTime = DateTimeOffset.UtcNow.AddDays(-30).ToString("o") },
                    new { id = "fresh-id", name = "Aqsat-20261007-030000.bak.enc", createdTime = DateTimeOffset.UtcNow.AddDays(-1).ToString("o") },
                },
            }),
        };
        var uploader = Uploader(
            fake, Config(folderId: "folder123", serviceAccountJson: NewServiceAccountJson()),
            out var http);
        using var _ = http;

        var bak = WriteTempBak(RandomNumberGenerator.GetBytes(512));
        try
        {
            Assert.True(await uploader.UploadAsync(bak));
            Assert.Equal(new[] { "old-id" }, fake.TrashedIds);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(bak)!, recursive: true);
        }
    }

    /// <summary>Minimal in-memory Google: token endpoint, resumable session + chunk PUTs, file
    /// listing, and trash — recording what it saw instead of storing anything.</summary>
    private sealed class FakeDriveHandler : HttpMessageHandler
    {
        public bool TokenFails { get; init; }
        public bool ListFails { get; init; }
        public string ListJson { get; init; } = """{"files":[]}""";

        public readonly List<string> Requests = new();
        public string SessionMetadataJson { get; private set; } = "";
        public byte[] UploadedBytes { get; private set; } = [];
        public readonly List<string> TrashedIds = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.AbsoluteUri;
            Requests.Add($"{request.Method} {url}");

            if (url.Contains("oauth2.googleapis.com/token"))
            {
                if (TokenFails)
                {
                    return new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest)
                    {
                        Content = new StringContent("""{"error":"invalid_grant"}"""),
                    };
                }

                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"access_token":"fake-token","expires_in":3600}"""),
                };
            }

            if (request.Method == HttpMethod.Post && url.Contains("/upload/drive/v3/files"))
            {
                SessionMetadataJson = await request.Content!.ReadAsStringAsync(ct);
                var session = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"id":"fake-file-id"}"""),
                };
                session.Headers.Location = new Uri("https://upload.example/session/1");
                return session;
            }

            if (request.Method == HttpMethod.Put)
            {
                var chunk = await request.Content!.ReadAsByteArrayAsync(ct);
                UploadedBytes = [.. UploadedBytes, .. chunk];
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"id":"fake-file-id"}"""),
                };
            }

            if (request.Method == HttpMethod.Get && url.Contains("/drive/v3/files"))
            {
                if (ListFails)
                {
                    return new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError)
                    {
                        Content = new StringContent("drive is having a bad day"),
                    };
                }

                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(ListJson),
                };
            }

            if (request.Method == HttpMethod.Patch)
            {
                TrashedIds.Add(request.RequestUri!.AbsolutePath.Split('/').Last());
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("{}"),
                };
            }

            throw new InvalidOperationException($"Unexpected request: {request.Method} {url}");
        }
    }
}
