using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Aqsat.Infrastructure.Backup;

/// <summary>
/// Offsite copy of the daily backup to Google Drive — the "get it off the host" leg
/// docs/RESTORE-RUNBOOK.md says this repo does not automate. It does now.
///
/// Pipeline per run: seal the .bak with <see cref="BackupFileEncryptor"/> (ciphertext only ever
/// leaves the machine — the encryption key stays in this environment's config), then a Drive v3
/// resumable upload of the .enc into a dedicated folder, then a best-effort remote retention sweep
/// mirroring the local <c>Backup:RetentionDays</c> window so the Drive folder can't grow forever.
///
/// Auth is a Google service-account JWT (RS256) traded for an OAuth access token with the narrow
/// <c>drive.file</c> scope — the app can only touch files IT created, not the whole Drive. The
/// service-account JSON is read from <c>Backup:Offsite:ServiceAccountJson</c> (inline JSON or a file
/// path). Configuration being absent/empty makes <see cref="UploadAsync"/> return false without
/// failing the backup job — offsite is opt-in per environment.
///
/// Failure semantics: a Drive outage must not lose the LOCAL backup (already written and retention-
/// swept by the time this runs), but it must be loud — the exception propagates to Hangfire so the
/// job shows failed and retries. A silently missing offsite copy is exactly the failure mode this
/// class exists to prevent.
/// </summary>
public sealed class GoogleDriveOffsiteUploader : IOffsiteBackupUploader
{
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string DriveScope = "https://www.googleapis.com/auth/drive.file";

    // Drive's resumable protocol wants every chunk except the last to be a multiple of 256 KiB;
    // 8 MiB satisfies that and keeps memory flat regardless of backup size.
    private const int UploadChunkSize = 8 * 1024 * 1024;

    private readonly HttpClient _http;
    private readonly IConfiguration _configuration;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<GoogleDriveOffsiteUploader> _logger;

    // Token is good for an hour and one job run does auth + one upload + one sweep, so caching per
    // instance is enough; the job creates a fresh instance per run anyway (scoped DI).
    private string? _cachedAccessToken;
    private DateTimeOffset _accessTokenExpiresAt;

    public GoogleDriveOffsiteUploader(
        HttpClient http,
        IConfiguration configuration,
        TimeProvider timeProvider,
        ILogger<GoogleDriveOffsiteUploader> logger)
    {
        _http = http;
        _configuration = configuration;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>Encrypts <paramref name="backupFilePath"/> to a sibling .enc, uploads it, deletes
    /// the local .enc, then prunes remote files older than the retention window. Returns false
    /// (no-op) when offsite isn't configured; throws when configured but failing.</summary>
    public async Task<bool> UploadAsync(string backupFilePath, CancellationToken ct = default)
    {
        var folderId = _configuration["Backup:Offsite:FolderId"];
        if (string.IsNullOrWhiteSpace(folderId))
        {
            _logger.LogInformation(
                "Offsite backup upload skipped — Backup:Offsite:FolderId is not set. " +
                "Set it (plus Backup:Offsite:ServiceAccountJson and Backup:EncryptionKey) to push backups to Google Drive.");
            return false;
        }

        if (!File.Exists(backupFilePath))
        {
            throw new FileNotFoundException("Backup file to upload does not exist.", backupFilePath);
        }

        var serviceAccountJson = ResolveServiceAccountJson();
        var key = BackupFileEncryptor.DecodeKey(_configuration["Backup:EncryptionKey"]);

        var encryptedPath = backupFilePath + ".enc";
        try
        {
            _logger.LogInformation("Sealing backup {Backup} for offsite upload", backupFilePath);
            await using (var source = File.OpenRead(backupFilePath))
            await using (var destination = File.Create(encryptedPath))
            {
                await BackupFileEncryptor.EncryptAsync(source, destination, key, ct);
            }

            var encryptedSize = new FileInfo(encryptedPath).Length;
            var accessToken = await GetAccessTokenAsync(serviceAccountJson, ct);
            var remoteName = Path.GetFileName(encryptedPath);
            await UploadResumableAsync(accessToken, folderId, remoteName, encryptedPath, encryptedSize, ct);
            _logger.LogInformation(
                "Offsite upload complete: {RemoteName} ({Bytes} bytes ciphertext) -> Google Drive folder {FolderId}",
                remoteName, encryptedSize, folderId);
        }
        finally
        {
            // The sealed temp is useless locally either way (the .bak is the restore input); never
            // leave ciphertext lying around taking 2x the backup's disk.
            TryDelete(encryptedPath);
        }

        await PruneRemoteAsync(folderId, ct);
        return true;
    }

    private string ResolveServiceAccountJson()
    {
        var configured = _configuration["Backup:Offsite:ServiceAccountJson"];
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                "Backup:Offsite:FolderId is set but Backup:Offsite:ServiceAccountJson is empty — " +
                "point it at a service-account JSON file path (or paste the JSON inline) whose " +
                "client_email has been shared into the target Drive folder as an editor.");
        }

        // Inline JSON vs file path: a path must not start with '{'. Supporting both keeps docker
        // (mount the JSON as a secret file) and plain appsettings (inline) equally workable.
        return configured.TrimStart().StartsWith('{')
            ? configured
            : File.ReadAllText(configured.Trim());
    }

    private async Task<string> GetAccessTokenAsync(string serviceAccountJson, CancellationToken ct)
    {
        if (_cachedAccessToken is not null && _timeProvider.GetUtcNow() < _accessTokenExpiresAt)
        {
            return _cachedAccessToken;
        }

        using var document = JsonDocument.Parse(serviceAccountJson);
        var root = document.RootElement;
        var clientEmail = root.GetProperty("client_email").GetString()
            ?? throw new InvalidOperationException("Service-account JSON has no client_email.");
        var privateKeyPem = root.GetProperty("private_key").GetString()
            ?? throw new InvalidOperationException("Service-account JSON has no private_key.");

        // The token endpoint comes from the JSON itself (Google can rotate it); fall back to the
        // well-known constant only if the file predates including it.
        var tokenUri = root.TryGetProperty("token_uri", out var uriElement) ? uriElement.GetString() : null;
        if (string.IsNullOrWhiteSpace(tokenUri))
        {
            tokenUri = TokenEndpoint;
        }

        var now = _timeProvider.GetUtcNow();
        var jwt = CreateServiceAccountJwt(clientEmail, privateKeyPem, tokenUri, now);

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
            ["assertion"] = jwt,
        });
        using var response = await _http.PostAsync(tokenUri, content, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            // Body carries Google's OAuth error text (invalid_grant = clock skew / revoked key …)
            // — exactly what the operator needs in the Hangfire failure log.
            throw new HttpRequestException(
                $"Google token exchange failed: {(int)response.StatusCode} {response.ReasonPhrase} — {body}");
        }

        using var tokenDocument = JsonDocument.Parse(body);
        var accessToken = tokenDocument.RootElement.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("Token response has no access_token.");
        var expiresIn = tokenDocument.RootElement.TryGetProperty("expires_in", out var expiryElement)
            ? expiryElement.GetInt32()
            : 3600;

        _cachedAccessToken = accessToken;
        _accessTokenExpiresAt = now.AddSeconds(Math.Max(60, expiresIn - 60));
        return accessToken;
    }

    /// <summary>Builds the RFC 7523 JWT itself rather than via a library helper, so the iat/exp
    /// timestamps come from the injected TimeProvider (testable clock, and the classic cause of
    /// "invalid_grant" skew bugs stays visible).</summary>
    private string CreateServiceAccountJwt(string clientEmail, string privateKeyPem, string tokenUri, DateTimeOffset now)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKeyPem);

        var handler = new JwtSecurityTokenHandler();
        var token = handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = clientEmail,
            Audience = tokenUri,
            IssuedAt = now.UtcDateTime,
            Expires = now.UtcDateTime.AddHours(1),
            SigningCredentials = new SigningCredentials(
                new RsaSecurityKey(rsa) { KeyId = "aqsat-backup" },
                SecurityAlgorithms.RsaSha256),
            // Explicit claim: CreateToken does not map "scope" for us.
            Claims = new Dictionary<string, object> { ["scope"] = DriveScope },
        });
        return handler.WriteToken(token);
    }

    /// <summary>Drive v3 resumable upload: first a session request carrying only metadata (gets back
    /// a session URI), then chunked PUTs of the ciphertext. Chunking means a dropped connection
    /// resumes at the failed offset instead of re-sending gigabytes, and bounds memory to one
    /// chunk. 308 Permanent Redirect is Drive's "give me the next chunk" — NOT an error.</summary>
    private async Task UploadResumableAsync(string accessToken, string folderId, string remoteName, string encryptedPath, long totalSize, CancellationToken ct)
    {
        var sessionUri = await StartUploadSessionAsync(accessToken, folderId, remoteName, totalSize, ct);

        await using var stream = File.OpenRead(encryptedPath);
        var buffer = new byte[UploadChunkSize];
        long offset = 0;
        int read;

        while ((read = await ReadChunkAsync(stream, buffer, ct)) > 0)
        {
            var isLast = offset + read >= totalSize;
            using var request = new HttpRequestMessage(HttpMethod.Put, sessionUri)
            {
                Content = new ByteArrayContent(buffer, 0, read),
            };
            request.Content.Headers.ContentLength = read;
            request.Content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(
                offset, offset + read - 1, totalSize);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await _http.SendAsync(request, ct);
            var statusCode = (int)response.StatusCode;
            // 200/201 = whole file committed (last chunk), 308 = chunk accepted, keep going.
            if (statusCode is not (200 or 201 or 308))
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                throw new HttpRequestException(
                    $"Google Drive upload chunk at offset {offset} failed: {statusCode} {response.ReasonPhrase} — {body}");
            }

            if (isLast && statusCode is not (200 or 201))
            {
                throw new HttpRequestException(
                    $"Google Drive reported {statusCode} after the final chunk of {totalSize} bytes — upload not committed.");
            }

            offset += read;
        }

        if (offset != totalSize)
        {
            throw new IOException($"Upload ended at {offset} of {totalSize} bytes — local .enc changed mid-upload?");
        }
    }

    private async Task<Uri> StartUploadSessionAsync(string accessToken, string folderId, string remoteName, long totalSize, CancellationToken ct)
    {
        // Metadata-only first request. Name carries the ".enc" suffix so a human browsing the Drive
        // folder can't mistake it for a restorable .bak.
        var metadata = JsonSerializer.Serialize(new
        {
            name = remoteName,
            parents = new[] { folderId },
        });
        using var request = new HttpRequestMessage(HttpMethod.Post,
            "https://www.googleapis.com/upload/drive/v3/files?uploadType=resumable")
        {
            Content = new StringContent(metadata, System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("X-Upload-Content-Length", totalSize.ToString());
        request.Headers.Add("X-Upload-Content-Type", "application/octet-stream");

        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"Google Drive upload session creation failed: {(int)response.StatusCode} {response.ReasonPhrase} — {body}");
        }

        return response.Headers.Location
            ?? throw new HttpRequestException("Google Drive returned no resumable session URI.");
    }

    /// <summary>Mirrors the local retention window onto Drive: files in the folder older than
    /// Backup:RetentionDays are moved to trash (recoverable for 30 days — a belt-and-braces second
    /// chance on top of the window). Best-effort by design: failing to prune must never fail an
    /// upload that just succeeded, so errors log as warnings.</summary>
    private async Task PruneRemoteAsync(string folderId, CancellationToken ct)
    {
        var retentionDays = _configuration.GetValue("Backup:RetentionDays", 14);
        try
        {
            var accessToken = await GetAccessTokenAsync(ResolveServiceAccountJson(), ct);
            var cutoff = _timeProvider.GetUtcNow().AddDays(-retentionDays);

            // createdTime is the only clock we can trust remotely (our local mtime doesn't travel).
            var query = Uri.EscapeDataString(
                $"'{folderId}' in parents and trashed = false and name contains '.enc'");
            using var listRequest = new HttpRequestMessage(HttpMethod.Get,
                $"https://www.googleapis.com/drive/v3/files?q={query}&fields=files(id,name,createdTime)&pageSize=1000");
            listRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

            using var listResponse = await _http.SendAsync(listRequest, ct);
            if (!listResponse.IsSuccessStatusCode)
            {
                var body = await listResponse.Content.ReadAsStringAsync(ct);
                throw new HttpRequestException(
                    $"Google Drive listing for retention prune failed: {(int)listResponse.StatusCode} — {body}");
            }

            using var document = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync(ct));
            foreach (var file in document.RootElement.GetProperty("files").EnumerateArray())
            {
                if (!DateTimeOffset.TryParse(
                        file.GetProperty("createdTime").GetString(),
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.AdjustToUniversal,
                        out var created))
                {
                    continue;
                }

                if (created >= cutoff)
                {
                    continue;
                }

                var fileId = file.GetProperty("id").GetString()!;
                using var trashRequest = new HttpRequestMessage(HttpMethod.Patch,
                    $"https://www.googleapis.com/drive/v3/files/{fileId}");
                trashRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
                trashRequest.Content = new StringContent("{\"trashed\":true}", System.Text.Encoding.UTF8, "application/json");
                using var trashResponse = await _http.SendAsync(trashRequest, ct);
                if (trashResponse.IsSuccessStatusCode)
                {
                    _logger.LogInformation(
                        "Pruned expired offsite backup {Name} (older than {RetentionDays} days)",
                        file.GetProperty("name").GetString(), retentionDays);
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException or JsonException)
        {
            _logger.LogWarning(ex,
                "Offsite backup retention prune failed — the upload itself succeeded; check Drive folder {FolderId}",
                folderId);
        }
    }

    private static async Task<int> ReadChunkAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), ct);
            if (read == 0)
            {
                break;
            }
            offset += read;
        }
        return offset;
    }

    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not delete temporary encrypted file {Path}", path);
        }
    }
}