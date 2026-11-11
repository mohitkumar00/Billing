using System.Text.Json;
using BillMate.Application.Common.Exceptions;
using BillMate.Application.Common.Interfaces;
using BillMate.Application.Features.Invoices;
using BillMate.Domain.Entities;
using BillMate.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BillMate.Application.Features.Sync;

public sealed record OfflineSyncOperationInput(
    Guid EntityId,
    string OperationType,
    string PayloadJson,
    DateTimeOffset ClientCreatedAt);

public sealed record SyncBatchResult(int ProcessedCount, int ConflictCount, int FailedCount);

public sealed record ProcessOfflineSyncBatchCommand(Guid DeviceId, IReadOnlyList<OfflineSyncOperationInput> Operations) : IRequest<SyncBatchResult>;

internal sealed class SyncBatchHandler : IRequestHandler<ProcessOfflineSyncBatchCommand, SyncBatchResult>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ISender _sender;

    public SyncBatchHandler(IApplicationDbContext db, ICurrentUserService currentUser, ISender sender)
    {
        _db = db;
        _currentUser = currentUser;
        _sender = sender;
    }

    public async Task<SyncBatchResult> Handle(ProcessOfflineSyncBatchCommand request, CancellationToken ct)
    {
        var businessId = _currentUser.BusinessId ?? throw new UnauthorizedAppException("AUTH_003", "Missing business context.");
        int processed = 0, conflict = 0, failed = 0;

        foreach (var op in request.Operations.OrderBy(o => o.ClientCreatedAt))
        {
            var queueItem = SyncQueueItem.Enqueue(businessId, request.DeviceId, op.OperationType, op.EntityId, op.PayloadJson, op.ClientCreatedAt);
            _db.SyncQueue.Add(queueItem);

            try
            {
                if (queueItem.OperationType == "CREATE_INVOICE")
                {
                    var cmd = JsonSerializer.Deserialize<CreateInvoiceCommand>(op.PayloadJson, JsonOptions)
                        ?? throw new AppException("SYNC_002", "Invalid invoice sync payload.");
                    await _sender.Send(cmd, ct);
                    queueItem.MarkProcessed();
                    processed++;
                }
                else
                {
                    queueItem.MarkFailed($"Unsupported sync operation '{queueItem.OperationType}'.");
                    failed++;
                }
            }
            catch (InsufficientStockException ex)
            {
                queueItem.MarkConflict(ex.Message);
                conflict++;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                queueItem.MarkConflict($"Concurrency conflict: {ex.Message}");
                conflict++;
            }
            catch (Exception ex)
            {
                queueItem.MarkFailed(ex.Message);
                failed++;
            }

            await _db.SaveChangesAsync(ct);
        }

        return new SyncBatchResult(processed, conflict, failed);
    }
}
