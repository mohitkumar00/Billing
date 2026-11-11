using BillMate.Application.Common.Interfaces;
using BillMate.Domain.Enums;
using BillMate.Infrastructure.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace BillMate.Api.Jobs;

public sealed class NightlyBackupJob
{
    private readonly AppDbContext _db;
    private readonly IBackupOrchestrator _orchestrator;

    public NightlyBackupJob(AppDbContext db, IBackupOrchestrator orchestrator)
    {
        _db = db;
        _orchestrator = orchestrator;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var businesses = await _db.Businesses.AsNoTracking().Select(b => b.Id).ToListAsync(ct);
        foreach (var businessId in businesses)
        {
            await _orchestrator.RunBackupAsync(businessId, BackupTarget.LocalDisk, ct);
        }
    }
}
