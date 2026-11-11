using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using BillMate.Application.Common.Exceptions;
using BillMate.Application.Common.Interfaces;
using BillMate.Application.Features.Backup;
using BillMate.Domain.Entities;
using BillMate.Domain.Enums;
using BillMate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace BillMate.Infrastructure.Backup;

public sealed class PgToolsRunner : IPgToolsRunner
{
    private readonly string _pgBinDirectory;

    public PgToolsRunner(IConfiguration configuration)
    {
        _pgBinDirectory = configuration["Postgres:BinDirectory"] ?? string.Empty;
    }

    public Task DumpCustomFormatAsync(string connectionString, string outputDumpPath, CancellationToken ct = default)
    {
        var csb = new NpgsqlConnectionStringBuilder(connectionString);
        var args = $"-h \"{csb.Host}\" -p {csb.Port} -U \"{csb.Username}\" -F c --no-owner -f \"{outputDumpPath}\" \"{csb.Database}\"";
        return RunPgToolAsync("pg_dump", args, csb.Password, ct);
    }

    public Task VerifyDumpAsync(string dumpPath, CancellationToken ct = default) =>
        RunPgToolAsync("pg_restore", $"--list \"{dumpPath}\"", password: null, ct);

    public async Task RecreateDatabaseAsync(string connectionString, CancellationToken ct = default)
    {
        var csb = new NpgsqlConnectionStringBuilder(connectionString);
        var targetDb = csb.Database ?? "billmatedb";
        var adminCsb = new NpgsqlConnectionStringBuilder(connectionString) { Database = "postgres" };

        await using var conn = new NpgsqlConnection(adminCsb.ConnectionString);
        await conn.OpenAsync(ct);

        await using (var killCmd = new NpgsqlCommand(
            $"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = @db AND pid <> pg_backend_pid();", conn))
        {
            killCmd.Parameters.AddWithValue("db", targetDb);
            await killCmd.ExecuteNonQueryAsync(ct);
        }

        var safeIdent = targetDb.Replace("\"", "\"\"");
        await using (var dropCmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{safeIdent}\";", conn))
            await dropCmd.ExecuteNonQueryAsync(ct);

        await using (var createCmd = new NpgsqlCommand($"CREATE DATABASE \"{safeIdent}\";", conn))
            await createCmd.ExecuteNonQueryAsync(ct);
    }

    public Task RestoreDumpAsync(string connectionString, string dumpPath, CancellationToken ct = default)
    {
        var csb = new NpgsqlConnectionStringBuilder(connectionString);
        var args = $"-h \"{csb.Host}\" -p {csb.Port} -U \"{csb.Username}\" -d \"{csb.Database}\" --no-owner --clean --if-exists \"{dumpPath}\"";
        return RunPgToolAsync("pg_restore", args, csb.Password, ct);
    }

    private async Task RunPgToolAsync(string toolName, string arguments, string? password, CancellationToken ct)
    {
        var exe = string.IsNullOrWhiteSpace(_pgBinDirectory) ? toolName : Path.Combine(_pgBinDirectory, toolName);
        var psi = new ProcessStartInfo(exe, arguments)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (!string.IsNullOrEmpty(password))
            psi.Environment["PGPASSWORD"] = password;

        using var proc = Process.Start(psi) ?? throw new AppException("BKP_004", $"Failed to start {toolName}.");
        var stderr = await proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);

        if (proc.ExitCode != 0)
            throw new AppException("BKP_005", $"{toolName} exited with code {proc.ExitCode}: {stderr}");
    }
}

public sealed class BackupOrchestrator : IBackupOrchestrator
{
    private readonly AppDbContext _db;
    private readonly IBackupStorageClient _driveClient;
    private readonly IPgToolsRunner _pgTools;
    private readonly IConfiguration _configuration;
    private readonly ILogger<BackupOrchestrator> _logger;

    public BackupOrchestrator(
        AppDbContext db,
        IBackupStorageClient driveClient,
        IPgToolsRunner pgTools,
        IConfiguration configuration,
        ILogger<BackupOrchestrator> logger)
    {
        _db = db;
        _driveClient = driveClient;
        _pgTools = pgTools;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<Guid> RunBackupAsync(Guid businessId, BackupTarget target, CancellationToken ct = default)
    {
        var business = await _db.Businesses.AsNoTracking().FirstOrDefaultAsync(b => b.Id == businessId, ct)
            ?? throw new NotFoundException("BUS_001", "Business not found.");

        var timestamp = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(5.5)).ToString("yyyyMMdd_HHmmss");
        var gzFileName = $"billmate_{timestamp}.dump.gz";
        var log = BackupLog.Start(businessId, target, gzFileName);
        _db.BackupLogs.Add(log);
        await _db.SaveChangesAsync(ct);

        var tempDump = Path.Combine(Path.GetTempPath(), $"billmate_{Guid.NewGuid():N}.dump");
        var tempGz = $"{tempDump}.gz";

        try
        {
            var connStr = _configuration.GetConnectionString("DefaultConnection")
                ?? throw new AppException("BKP_006", "Database connection string missing.");

            await _pgTools.DumpCustomFormatAsync(connStr, tempDump, ct);
            await _pgTools.VerifyDumpAsync(tempDump, ct);

            await CompressGzipAsync(tempDump, tempGz, ct);
            var sizeBytes = new FileInfo(tempGz).Length;
            var sha256 = await ComputeSha256HexAsync(tempGz, ct);
            log.MarkVerified(sizeBytes, sha256);

            if (target == BackupTarget.GoogleDrive)
            {
                var setting = await _db.AppSettings.AsNoTracking()
                    .FirstOrDefaultAsync(s => s.BusinessId == businessId && s.SettingKey == BackupConstants.DriveTokenSettingKey, ct)
                    ?? throw new AppException("BKP_002", "Google Drive is not connected.", 400);

                var refreshToken = Encoding.UTF8.GetString(setting.EncryptedBlob!);
                var remoteId = await _driveClient.UploadResumableAsync(refreshToken, business.Name, tempGz, gzFileName, ct);
                log.MarkUploaded(remoteId);
                await _driveClient.PruneOldBackupsAsync(refreshToken, business.Name, keepDays: 30, minKeepCount: 5, ct);
            }
            else
            {
                var localDir = _configuration["Backup:LocalDirectory"]
                    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "BillMate", "Backups");
                Directory.CreateDirectory(localDir);
                var dest = Path.Combine(localDir, gzFileName);
                File.Copy(tempGz, dest, overwrite: true);
                log.MarkUploaded(dest);
            }

            await _db.SaveChangesAsync(ct);
            return log.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Backup failed for Business={BusinessId}, Target={Target}", businessId, target);
            log.MarkFailed(ex.Message);
            await _db.SaveChangesAsync(ct);
            throw;
        }
        finally
        {
            if (File.Exists(tempDump)) File.Delete(tempDump);
            if (File.Exists(tempGz)) File.Delete(tempGz);
        }
    }

    public async Task RestoreFromDriveAsync(Guid businessId, string remoteFileId, CancellationToken ct = default)
    {
        var setting = await _db.AppSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.BusinessId == businessId && s.SettingKey == BackupConstants.DriveTokenSettingKey, ct)
            ?? throw new AppException("BKP_002", "Google Drive is not connected.", 400);

        var refreshToken = Encoding.UTF8.GetString(setting.EncryptedBlob!);
        var tempGz = Path.Combine(Path.GetTempPath(), $"restore_{Guid.NewGuid():N}.dump.gz");
        var tempDump = Path.ChangeExtension(tempGz, null);

        try
        {
            await _driveClient.DownloadBackupAsync(refreshToken, remoteFileId, tempGz, ct);
            await DecompressGzipAsync(tempGz, tempDump, ct);
            await _pgTools.VerifyDumpAsync(tempDump, ct);

            var connStr = _configuration.GetConnectionString("DefaultConnection")
                ?? throw new AppException("BKP_006", "Database connection string missing.");

            await _pgTools.RecreateDatabaseAsync(connStr, ct);
            await _pgTools.RestoreDumpAsync(connStr, tempDump, ct);
        }
        finally
        {
            if (File.Exists(tempGz)) File.Delete(tempGz);
            if (File.Exists(tempDump)) File.Delete(tempDump);
        }
    }

    private static async Task CompressGzipAsync(string sourcePath, string destGzPath, CancellationToken ct)
    {
        await using var input = File.OpenRead(sourcePath);
        await using var output = File.Create(destGzPath);
        await using var gzip = new GZipStream(output, CompressionLevel.Optimal);
        await input.CopyToAsync(gzip, ct);
    }

    private static async Task DecompressGzipAsync(string sourceGzPath, string destPath, CancellationToken ct)
    {
        await using var input = File.OpenRead(sourceGzPath);
        await using var gzip = new GZipStream(input, CompressionMode.Decompress);
        await using var output = File.Create(destPath);
        await gzip.CopyToAsync(output, ct);
    }

    private static async Task<string> ComputeSha256HexAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
