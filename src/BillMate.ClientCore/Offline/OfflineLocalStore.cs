using System.Text.Json;
using BillMate.Application.Features.Invoices;
using BillMate.Application.Features.Products;
using Microsoft.Data.Sqlite;

namespace BillMate.ClientCore.Offline;

public sealed record PendingOfflineOperation(
    Guid Id,
    string OperationType,
    Guid EntityId,
    string PayloadJson,
    DateTimeOffset ClientCreatedAtUtc,
    int RetryCount,
    string? LastError);

/// <summary>
/// SQLite WAL-mode local store for offline POS catalog lookup and queued invoice persistence.
/// </summary>
public sealed class OfflineLocalStore : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private readonly SqliteConnection _conn;
    private bool _initialized;

    public OfflineLocalStore(string databasePath)
    {
        var dir = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        _conn = new SqliteConnection($"Data Source={databasePath}");
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_initialized) return;
        await _conn.OpenAsync(ct);

        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS cached_products (
                id TEXT PRIMARY KEY,
                sku TEXT NOT NULL,
                barcode TEXT,
                name TEXT NOT NULL,
                json_payload TEXT NOT NULL,
                updated_utc TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_cached_products_search ON cached_products(name, sku, barcode);

            CREATE TABLE IF NOT EXISTS pending_sync_queue (
                id TEXT PRIMARY KEY,
                operation_type TEXT NOT NULL,
                entity_id TEXT NOT NULL,
                payload_json TEXT NOT NULL,
                client_created_utc TEXT NOT NULL,
                retry_count INTEGER NOT NULL DEFAULT 0,
                last_error TEXT
            );
            """;
        await cmd.ExecuteNonQueryAsync(ct);
        _initialized = true;
    }

    public async Task UpsertProductsAsync(IEnumerable<ProductDto> products, CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        await using var tx = (SqliteTransaction)await _conn.BeginTransactionAsync(ct);

        foreach (var p in products)
        {
            await using var cmd = _conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO cached_products (id, sku, barcode, name, json_payload, updated_utc)
                VALUES ($id, $sku, $barcode, $name, $json, $utc)
                ON CONFLICT(id) DO UPDATE SET
                    sku = excluded.sku,
                    barcode = excluded.barcode,
                    name = excluded.name,
                    json_payload = excluded.json_payload,
                    updated_utc = excluded.updated_utc;
                """;
            cmd.Parameters.AddWithValue("$id", p.Id.ToString());
            cmd.Parameters.AddWithValue("$sku", p.Sku);
            cmd.Parameters.AddWithValue("$barcode", (object?)p.Barcode ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$name", p.Name);
            cmd.Parameters.AddWithValue("$json", JsonSerializer.Serialize(p, JsonOpts));
            cmd.Parameters.AddWithValue("$utc", DateTimeOffset.UtcNow.ToString("O"));
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<ProductDto>> SearchCachedProductsAsync(string? query, int limit = 50, CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        await using var cmd = _conn.CreateCommand();
        if (string.IsNullOrWhiteSpace(query))
        {
            cmd.CommandText = "SELECT json_payload FROM cached_products ORDER BY name LIMIT $limit;";
        }
        else
        {
            cmd.CommandText = """
                SELECT json_payload FROM cached_products
                WHERE LOWER(name) LIKE $term OR LOWER(sku) LIKE $term OR LOWER(COALESCE(barcode,'')) LIKE $term
                ORDER BY name LIMIT $limit;
                """;
            cmd.Parameters.AddWithValue("$term", $"%{query.Trim().ToLowerInvariant()}%");
        }
        cmd.Parameters.AddWithValue("$limit", limit);

        var list = new List<ProductDto>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var dto = JsonSerializer.Deserialize<ProductDto>(reader.GetString(0), JsonOpts);
            if (dto is not null) list.Add(dto);
        }
        return list;
    }

    public async Task EnqueueOfflineInvoiceAsync(CreateInvoiceCommand command, CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO pending_sync_queue (id, operation_type, entity_id, payload_json, client_created_utc)
            VALUES ($id, 'CREATE_INVOICE', $entityId, $payload, $utc);
            """;
        cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
        cmd.Parameters.AddWithValue("$entityId", command.IdempotencyKey.ToString());
        cmd.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(command, JsonOpts));
        cmd.Parameters.AddWithValue("$utc", DateTimeOffset.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<PendingOfflineOperation>> GetPendingOperationsAsync(CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, operation_type, entity_id, payload_json, client_created_utc, retry_count, last_error
            FROM pending_sync_queue
            ORDER BY client_created_utc ASC;
            """;

        var list = new List<PendingOfflineOperation>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new PendingOfflineOperation(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                Guid.Parse(reader.GetString(2)),
                reader.GetString(3),
                DateTimeOffset.Parse(reader.GetString(4)),
                reader.GetInt32(5),
                reader.IsDBNull(6) ? null : reader.GetString(6)));
        }
        return list;
    }

    public async Task RemovePendingOperationAsync(Guid queueId, CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM pending_sync_queue WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", queueId.ToString());
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task MarkRetryFailureAsync(Guid queueId, string error, CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE pending_sync_queue SET retry_count = retry_count + 1, last_error = $err WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", queueId.ToString());
        cmd.Parameters.AddWithValue("$err", error);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async ValueTask DisposeAsync()
    {
        await _conn.CloseAsync();
        SqliteConnection.ClearPool(_conn);
        await _conn.DisposeAsync();
    }
}
