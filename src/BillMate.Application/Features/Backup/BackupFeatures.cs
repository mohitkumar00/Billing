using System.Security.Cryptography;
using System.Text;
using BillMate.Application.Common.Exceptions;
using BillMate.Application.Common.Interfaces;
using BillMate.Application.Common.Security;
using BillMate.Domain.Entities;
using BillMate.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BillMate.Application.Features.Backup;

public static class BackupConstants
{
    public const string DriveTokenSettingKey = "gdrive_oauth_refresh_token";
}

public sealed record BackupLogDto(
    Guid Id,
    BackupTarget BackupTarget,
    string FileName,
    string? RemoteFileId,
    long FileSizeBytes,
    BackupStatus Status,
    string? ErrorMessage,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt);

public sealed record DriveAuthUrlDto(string AuthUrl, string State, string CodeVerifier);

[RequirePermission("backup.manage")]
public sealed record RunBackupCommand(BackupTarget Target = BackupTarget.GoogleDrive) : IRequest<Guid>;

[RequirePermission("backup.manage")]
public sealed record GetBackupHistoryQuery(int Limit = 30) : IQuery<IReadOnlyList<BackupLogDto>>;

[RequirePermission("backup.manage")]
public sealed record GetDriveAuthUrlQuery(string RedirectUri) : IQuery<DriveAuthUrlDto>;

[RequirePermission("backup.manage")]
public sealed record CompleteDriveAuthCommand(string Code, string CodeVerifier, string RedirectUri) : ICommand<Unit>;

[RequirePermission("backup.manage")]
public sealed record ListDriveBackupsQuery : IQuery<IReadOnlyList<DriveBackupFileDto>>;

[RequirePermission("backup.manage")]
public sealed record RestoreFromDriveCommand(string RemoteFileId) : IRequest<Unit>;

internal sealed class BackupHandlers :
    IRequestHandler<RunBackupCommand, Guid>,
    IRequestHandler<GetBackupHistoryQuery, IReadOnlyList<BackupLogDto>>,
    IRequestHandler<GetDriveAuthUrlQuery, DriveAuthUrlDto>,
    IRequestHandler<CompleteDriveAuthCommand, Unit>,
    IRequestHandler<ListDriveBackupsQuery, IReadOnlyList<DriveBackupFileDto>>,
    IRequestHandler<RestoreFromDriveCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IBackupOrchestrator _orchestrator;
    private readonly IBackupStorageClient _driveClient;

    public BackupHandlers(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        IBackupOrchestrator orchestrator,
        IBackupStorageClient driveClient)
    {
        _db = db;
        _currentUser = currentUser;
        _orchestrator = orchestrator;
        _driveClient = driveClient;
    }

    public async Task<Guid> Handle(RunBackupCommand request, CancellationToken ct)
    {
        var businessId = _currentUser.BusinessId ?? throw new UnauthorizedAppException("AUTH_003", "Missing business context.");
        return await _orchestrator.RunBackupAsync(businessId, request.Target, ct);
    }

    public async Task<IReadOnlyList<BackupLogDto>> Handle(GetBackupHistoryQuery request, CancellationToken ct)
    {
        var businessId = _currentUser.BusinessId ?? throw new UnauthorizedAppException("AUTH_003", "Missing business context.");
        return await _db.BackupLogs.AsNoTracking()
            .Where(b => b.BusinessId == businessId)
            .OrderByDescending(b => b.StartedAt)
            .Take(Math.Clamp(request.Limit, 1, 100))
            .Select(b => new BackupLogDto(
                b.Id, b.BackupTarget, b.FileName, b.RemoteFileId, b.FileSizeBytes, b.Status, b.ErrorMessage, b.StartedAt, b.CompletedAt))
            .ToListAsync(ct);
    }

    public Task<DriveAuthUrlDto> Handle(GetDriveAuthUrlQuery request, CancellationToken ct)
    {
        var verifierBytes = RandomNumberGenerator.GetBytes(32);
        var codeVerifier = Base64UrlEncode(verifierBytes);
        var challengeBytes = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        var codeChallenge = Base64UrlEncode(challengeBytes);
        var state = Guid.NewGuid().ToString("N");

        var url = _driveClient.BuildAuthorizationUrl(state, codeChallenge, request.RedirectUri);
        return Task.FromResult(new DriveAuthUrlDto(url, state, codeVerifier));
    }

    public async Task<Unit> Handle(CompleteDriveAuthCommand request, CancellationToken ct)
    {
        var businessId = _currentUser.BusinessId ?? throw new UnauthorizedAppException("AUTH_003", "Missing business context.");
        var refreshToken = await _driveClient.ExchangeAuthorizationCodeAsync(request.Code, request.CodeVerifier, request.RedirectUri, ct);

        var existing = await _db.AppSettings.FirstOrDefaultAsync(s => s.BusinessId == businessId && s.SettingKey == BackupConstants.DriveTokenSettingKey, ct);
        var bytes = Encoding.UTF8.GetBytes(refreshToken);

        if (existing is null)
        {
            _db.AppSettings.Add(AppSetting.Create(businessId, BackupConstants.DriveTokenSettingKey, null, bytes, isSecret: true));
        }
        else
        {
            existing.UpdateValue(null, bytes);
        }

        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }

    public async Task<IReadOnlyList<DriveBackupFileDto>> Handle(ListDriveBackupsQuery request, CancellationToken ct)
    {
        var businessId = _currentUser.BusinessId ?? throw new UnauthorizedAppException("AUTH_003", "Missing business context.");
        var business = await _db.Businesses.AsNoTracking().FirstOrDefaultAsync(b => b.Id == businessId, ct)
            ?? throw new NotFoundException("BUS_001", "Business not found.");
        var setting = await _db.AppSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.BusinessId == businessId && s.SettingKey == BackupConstants.DriveTokenSettingKey, ct);

        if (setting?.EncryptedBlob is null || setting.EncryptedBlob.Length == 0)
            throw new AppException("BKP_002", "Google Drive is not connected for this business.", 400);

        var token = Encoding.UTF8.GetString(setting.EncryptedBlob);
        return await _driveClient.ListBackupsAsync(token, business.Name, ct);
    }

    public async Task<Unit> Handle(RestoreFromDriveCommand request, CancellationToken ct)
    {
        var businessId = _currentUser.BusinessId ?? throw new UnauthorizedAppException("AUTH_003", "Missing business context.");
        await _orchestrator.RestoreFromDriveAsync(businessId, request.RemoteFileId, ct);
        return Unit.Value;
    }

    private static string Base64UrlEncode(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
