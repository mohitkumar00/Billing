using System.Net.Http.Json;
using System.Text.Json.Serialization;
using BillMate.Application.Common.Exceptions;
using BillMate.Application.Common.Interfaces;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Upload;
using Microsoft.Extensions.Configuration;
using DriveFile = Google.Apis.Drive.v3.Data.File;

namespace BillMate.Infrastructure.Backup;

public sealed class GoogleDriveOptions
{
    public const string SectionName = "GoogleDrive";
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string RootFolderName { get; set; } = "BillMate Backups";
}

public sealed class GoogleDriveBackupClient : IBackupStorageClient
{
    private const string Scope = DriveService.ScopeConstants.DriveFile;
    private readonly GoogleDriveOptions _options;
    private readonly HttpClient _http = new();

    public GoogleDriveBackupClient(IConfiguration configuration)
    {
        var sec = configuration.GetSection(GoogleDriveOptions.SectionName);
        _options = new GoogleDriveOptions
        {
            ClientId = sec[nameof(GoogleDriveOptions.ClientId)] ?? string.Empty,
            ClientSecret = sec[nameof(GoogleDriveOptions.ClientSecret)] ?? string.Empty,
            RootFolderName = sec[nameof(GoogleDriveOptions.RootFolderName)] ?? "BillMate Backups"
        };
    }

    public string BuildAuthorizationUrl(string state, string codeChallenge, string redirectUri)
    {
        EnsureConfigured();
        return "https://accounts.google.com/o/oauth2/v2/auth"
             + $"?client_id={Uri.EscapeDataString(_options.ClientId)}"
             + $"&redirect_uri={Uri.EscapeDataString(redirectUri)}"
             + "&response_type=code"
             + $"&scope={Uri.EscapeDataString(Scope)}"
             + "&access_type=offline&prompt=consent"
             + $"&state={Uri.EscapeDataString(state)}"
             + $"&code_challenge={Uri.EscapeDataString(codeChallenge)}"
             + "&code_challenge_method=S256";
    }

    public async Task<string> ExchangeAuthorizationCodeAsync(string code, string codeVerifier, string redirectUri, CancellationToken ct = default)
    {
        EnsureConfigured();
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret,
            ["redirect_uri"] = redirectUri,
            ["grant_type"] = "authorization_code",
            ["code_verifier"] = codeVerifier
        });

        var resp = await _http.PostAsync("https://oauth2.googleapis.com/token", form, ct);
        if (!resp.IsSuccessStatusCode)
            throw new AppException("BKP_001", "Google OAuth token exchange failed.", 502);

        var token = await resp.Content.ReadFromJsonAsync<OAuthTokenResponse>(cancellationToken: ct);
        if (string.IsNullOrWhiteSpace(token?.RefreshToken))
            throw new AppException("BKP_001", "Google did not return a refresh token. Ensure consent prompt is accepted.", 502);

        return token.RefreshToken;
    }

    public async Task<string> UploadResumableAsync(string refreshToken, string businessFolderName, string localFilePath, string fileName, CancellationToken ct = default)
    {
        var service = CreateDriveService(refreshToken);
        var folderId = await EnsureFolderHierarchyAsync(service, businessFolderName, ct);

        var fileMetadata = new DriveFile
        {
            Name = fileName,
            Parents = [folderId]
        };

        await using var stream = File.OpenRead(localFilePath);
        var request = service.Files.Create(fileMetadata, stream, "application/gzip");
        request.Fields = "id";
        request.ChunkSize = ResumableUpload.MinimumChunkSize * 4; // 1 MB chunks for shop Wi-Fi resilience

        var progress = await request.UploadAsync(ct);
        if (progress.Status != UploadStatus.Completed)
            throw new AppException("BKP_001", $"Google Drive upload failed: {progress.Exception?.Message}", 502);

        return request.ResponseBody.Id;
    }

    public async Task<IReadOnlyList<DriveBackupFileDto>> ListBackupsAsync(string refreshToken, string businessFolderName, CancellationToken ct = default)
    {
        var service = CreateDriveService(refreshToken);
        var folderId = await EnsureFolderHierarchyAsync(service, businessFolderName, ct);

        var listReq = service.Files.List();
        listReq.Q = $"'{folderId}' in parents and trashed = false";
        listReq.Fields = "files(id, name, size, createdTime)";
        listReq.OrderBy = "createdTime desc";
        listReq.PageSize = 50;

        var res = await listReq.ExecuteAsync(ct);
        return res.Files.Select(f => new DriveBackupFileDto(
            f.Id,
            f.Name,
            f.Size ?? 0L,
            f.CreatedTimeDateTimeOffset ?? DateTimeOffset.UtcNow)).ToArray();
    }

    public async Task DownloadBackupAsync(string refreshToken, string remoteFileId, string destinationPath, CancellationToken ct = default)
    {
        var service = CreateDriveService(refreshToken);
        await using var output = File.Create(destinationPath);
        var status = await service.Files.Get(remoteFileId).DownloadAsync(output, ct);
        if (status.Status != Google.Apis.Download.DownloadStatus.Completed)
            throw new AppException("BKP_001", $"Google Drive download failed: {status.Exception?.Message}", 502);
    }

    public async Task<int> PruneOldBackupsAsync(string refreshToken, string businessFolderName, int keepDays = 30, int minKeepCount = 5, CancellationToken ct = default)
    {
        var files = await ListBackupsAsync(refreshToken, businessFolderName, ct);
        var cutoff = DateTimeOffset.UtcNow.AddDays(-keepDays);
        var service = CreateDriveService(refreshToken);
        var deleted = 0;

        for (var i = minKeepCount; i < files.Count; i++)
        {
            if (files[i].CreatedAtUtc < cutoff)
            {
                await service.Files.Delete(files[i].FileId).ExecuteAsync(ct);
                deleted++;
            }
        }
        return deleted;
    }

    private DriveService CreateDriveService(string refreshToken)
    {
        EnsureConfigured();
        var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets
            {
                ClientId = _options.ClientId,
                ClientSecret = _options.ClientSecret
            },
            Scopes = [Scope]
        });

        var credential = new UserCredential(flow, "billmate-user", new TokenResponse { RefreshToken = refreshToken });
        return new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "BillMate"
        });
    }

    private async Task<string> EnsureFolderHierarchyAsync(DriveService service, string businessFolderName, CancellationToken ct)
    {
        var rootId = await FindOrCreateFolderAsync(service, _options.RootFolderName, parentId: null, ct);
        return await FindOrCreateFolderAsync(service, businessFolderName, parentId: rootId, ct);
    }

    private static async Task<string> FindOrCreateFolderAsync(DriveService service, string name, string? parentId, CancellationToken ct)
    {
        var safeName = name.Replace("'", "\\'");
        var q = $"mimeType = 'application/vnd.google-apps.folder' and name = '{safeName}' and trashed = false";
        if (!string.IsNullOrEmpty(parentId))
            q += $" and '{parentId}' in parents";

        var list = service.Files.List();
        list.Q = q;
        list.Fields = "files(id)";
        var found = await list.ExecuteAsync(ct);
        if (found.Files.Count > 0) return found.Files[0].Id;

        var folder = new DriveFile
        {
            Name = name,
            MimeType = "application/vnd.google-apps.folder",
            Parents = string.IsNullOrEmpty(parentId) ? null : [parentId]
        };
        var created = await service.Files.Create(folder).ExecuteAsync(ct);
        return created.Id;
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret))
            throw new AppException("BKP_003", "GoogleDrive:ClientId and ClientSecret are not configured.", 400);
    }

    private sealed class OAuthTokenResponse
    {
        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }
    }
}
