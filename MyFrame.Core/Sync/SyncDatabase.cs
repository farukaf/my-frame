using Microsoft.Data.Sqlite;

namespace MyFrame.Core.Sync;

public sealed class SyncDatabase : IAsyncDisposable
{
    private const int SchemaVersion = 1;
    private readonly string _path;
    private readonly SemaphoreSlim _writer = new(1, 1);

    public SyncDatabase(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Database path is required.", nameof(path));
        _path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(SqliteOpenMode.ReadWriteCreate, cancellationToken);
        await ExecuteAsync(connection, "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;");
        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS schema_migrations(version INTEGER PRIMARY KEY, applied_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS sources(source_id TEXT PRIMARY KEY, kind TEXT NOT NULL, display_name TEXT NOT NULL, enabled INTEGER NOT NULL DEFAULT 1, created_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS sync_runs(run_id TEXT PRIMARY KEY, source_id TEXT NOT NULL REFERENCES sources(source_id), state TEXT NOT NULL, started_at TEXT NOT NULL, finished_at TEXT, records_received INTEGER NOT NULL, records_accepted INTEGER NOT NULL, records_rejected INTEGER NOT NULL, error_code TEXT);
            CREATE TABLE IF NOT EXISTS source_revisions(revision_id TEXT PRIMARY KEY, source_id TEXT NOT NULL REFERENCES sources(source_id), content_hash TEXT NOT NULL, payload_json TEXT NOT NULL, parser_version TEXT NOT NULL, record_count INTEGER NOT NULL, state TEXT NOT NULL, retrieved_at TEXT NOT NULL, published_at TEXT);
            CREATE UNIQUE INDEX IF NOT EXISTS ux_source_revision_hash ON source_revisions(source_id, content_hash);
            CREATE TABLE IF NOT EXISTS coverage(source_id TEXT NOT NULL REFERENCES sources(source_id), field_path TEXT NOT NULL, state TEXT NOT NULL, observed_at TEXT NOT NULL, detail TEXT, PRIMARY KEY(source_id, field_path));
            CREATE TABLE IF NOT EXISTS staging_records(run_id TEXT NOT NULL REFERENCES sync_runs(run_id), ordinal INTEGER NOT NULL, payload_hash TEXT NOT NULL, payload_json TEXT NOT NULL, PRIMARY KEY(run_id, ordinal));
            CREATE TABLE IF NOT EXISTS public_export_items(revision_id TEXT NOT NULL REFERENCES source_revisions(revision_id), unique_name TEXT NOT NULL, name TEXT, category TEXT, description TEXT, canonical_name TEXT NOT NULL, PRIMARY KEY(revision_id, unique_name));
            CREATE INDEX IF NOT EXISTS ix_public_export_items_name ON public_export_items(canonical_name);
            CREATE TABLE IF NOT EXISTS inventory_revisions(revision_id TEXT PRIMARY KEY REFERENCES source_revisions(revision_id), session_id TEXT NOT NULL, event_id TEXT NOT NULL, sequence INTEGER NOT NULL, completeness TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS inventory_equipment(revision_id TEXT NOT NULL REFERENCES inventory_revisions(revision_id), instance_id TEXT NOT NULL, type_id TEXT, rank INTEGER, config_json TEXT, rank_state INTEGER NOT NULL, config_state INTEGER NOT NULL, raw_json TEXT NOT NULL, PRIMARY KEY(revision_id, instance_id));
            CREATE TABLE IF NOT EXISTS inventory_stackables(revision_id TEXT NOT NULL REFERENCES inventory_revisions(revision_id), ordinal INTEGER NOT NULL, type_id TEXT, quantity INTEGER, quantity_state INTEGER NOT NULL, raw_json TEXT NOT NULL, PRIMARY KEY(revision_id, ordinal));
            CREATE TABLE IF NOT EXISTS inventory_unknown(revision_id TEXT NOT NULL REFERENCES inventory_revisions(revision_id), ordinal INTEGER NOT NULL, kind TEXT NOT NULL, reason_code TEXT NOT NULL, raw_json TEXT NOT NULL, PRIMARY KEY(revision_id, ordinal));
            """);
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT OR IGNORE INTO schema_migrations(version, applied_at) VALUES ($version, $at);";
        command.Parameters.AddWithValue("$version", SchemaVersion);
        command.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<SyncPublicationResult> PublishAsync(SyncBatch batch, CancellationToken cancellationToken = default)
        => await PublishInternalAsync(batch, null, null, cancellationToken);

    public async Task<SyncPublicationResult> PublishCatalogAsync(SyncBatch batch, IReadOnlyList<PublicExportRecord> records, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(records);
        if (records.Count != batch.RecordCount) throw new ArgumentException("Catalog record count does not match batch.", nameof(records));
        if (records.Select(record => record.UniqueName).Distinct(StringComparer.Ordinal).Count() != records.Count) throw new ArgumentException("Catalog contains duplicate unique names.", nameof(records));
        return await PublishInternalAsync(batch, records, null, cancellationToken);
    }

    public async Task<SyncPublicationResult> PublishInventoryAsync(InventoryEnvelope envelope, InventoryProjection projection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(projection);
        if (envelope.Completeness is not ("verified" or "unverified")) throw new ArgumentException("Inventory completeness is invalid.", nameof(envelope));
        var batch = new SyncBatch("overwolf-inventory", envelope.ContentHash, envelope.PayloadJson, projection.Equipment.Count + projection.Stackables.Count, "overwolf-native-1");
        return await PublishInternalAsync(batch, null, (envelope, projection), cancellationToken);
    }

    public async Task<IReadOnlyList<PublicExportRecord>> GetPublicExportItemsAsync(string sourceId, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path)) return [];
        await using var connection = await OpenAsync(SqliteOpenMode.ReadOnly, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT i.unique_name, i.name, i.category, i.description, i.canonical_name
            FROM public_export_items i JOIN source_revisions r ON r.revision_id=i.revision_id
            WHERE r.source_id=$source AND r.state='active' ORDER BY i.unique_name;
            """;
        command.Parameters.AddWithValue("$source", sourceId);
        var records = new List<PublicExportRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var uniqueName = reader.GetString(0);
            var name = reader.IsDBNull(1) ? null : reader.GetString(1);
            records.Add(new PublicExportRecord(uniqueName, name, reader.IsDBNull(2) ? null : reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3), new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["en"] = name ?? uniqueName }));
        }
        return records;
    }

    public async Task<IReadOnlyList<InventoryEquipmentRecord>> GetInventoryEquipmentAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path)) return [];
        await using var connection = await OpenAsync(SqliteOpenMode.ReadOnly, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT e.instance_id, e.type_id, e.rank, e.config_json, e.rank_state, e.config_state, e.raw_json
            FROM inventory_equipment e JOIN inventory_revisions ir ON ir.revision_id=e.revision_id
            JOIN source_revisions r ON r.revision_id=ir.revision_id
            WHERE r.source_id='overwolf-inventory' AND r.state='active' ORDER BY e.instance_id;
            """;
        var records = new List<InventoryEquipmentRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            records.Add(new(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetInt32(2), reader.IsDBNull(3) ? null : reader.GetString(3), (InventoryFieldState)reader.GetInt32(4), (InventoryFieldState)reader.GetInt32(5), reader.GetString(6)));
        return records;
    }

    private async Task<SyncPublicationResult> PublishInternalAsync(SyncBatch batch, IReadOnlyList<PublicExportRecord>? records, (InventoryEnvelope Envelope, InventoryProjection Projection)? inventory, CancellationToken cancellationToken = default)
    {
        Validate(batch);
        await _writer.WaitAsync(cancellationToken);
        try
        {
            await InitializeAsync(cancellationToken);
            await using var connection = await OpenAsync(SqliteOpenMode.ReadWrite, cancellationToken);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            var now = DateTimeOffset.UtcNow.ToString("O");
            var runId = Guid.NewGuid().ToString("N");
            var revisionId = Guid.NewGuid().ToString("N");
            await CommandAsync(connection, transaction, "INSERT OR IGNORE INTO sources(source_id, kind, display_name, created_at) VALUES ($id, 'sync', $id, $at);", cancellationToken, ("$id", batch.SourceId), ("$at", now));
            await using (var existing = connection.CreateCommand())
            {
                existing.Transaction = transaction;
                existing.CommandText = "SELECT revision_id, record_count FROM source_revisions WHERE source_id=$source AND content_hash=$hash LIMIT 1;";
                existing.Parameters.AddWithValue("$source", batch.SourceId);
                existing.Parameters.AddWithValue("$hash", batch.ContentHash);
                await using var reader = await existing.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken))
                {
                    await transaction.CommitAsync(cancellationToken);
                    return new SyncPublicationResult(runId, reader.GetString(0), true, reader.GetInt32(1));
                }
            }
            await CommandAsync(connection, transaction, "INSERT INTO sync_runs(run_id, source_id, state, started_at, finished_at, records_received, records_accepted, records_rejected) VALUES ($run, $source, 'published', $at, $at, $count, $count, 0);", cancellationToken, ("$run", runId), ("$source", batch.SourceId), ("$at", now), ("$count", batch.RecordCount));
            await CommandAsync(connection, transaction, "INSERT INTO staging_records(run_id, ordinal, payload_hash, payload_json) VALUES ($run, 0, $hash, $payload);", cancellationToken, ("$run", runId), ("$hash", batch.ContentHash), ("$payload", batch.PayloadJson));
            await CommandAsync(connection, transaction, "UPDATE source_revisions SET state='retained' WHERE source_id=$source AND state='active';", cancellationToken, ("$source", batch.SourceId));
            await CommandAsync(connection, transaction, "INSERT INTO source_revisions(revision_id, source_id, content_hash, payload_json, parser_version, record_count, state, retrieved_at, published_at) VALUES ($revision, $source, $hash, $payload, $parser, $count, 'active', $at, $at);", cancellationToken, ("$revision", revisionId), ("$source", batch.SourceId), ("$hash", batch.ContentHash), ("$payload", batch.PayloadJson), ("$parser", batch.ParserVersion), ("$count", batch.RecordCount), ("$at", now));
            if (records is not null)
                foreach (var record in records)
                    await CommandAsync(connection, transaction, "INSERT INTO public_export_items(revision_id, unique_name, name, category, description, canonical_name) VALUES ($revision, $unique, $name, $category, $description, $canonical);", cancellationToken, ("$revision", revisionId), ("$unique", record.UniqueName), ("$name", (object?)record.Name ?? DBNull.Value), ("$category", (object?)record.Category ?? DBNull.Value), ("$description", (object?)record.Description ?? DBNull.Value), ("$canonical", PublicExportIdentity.Canonicalize(record.Name ?? record.UniqueName)));
            if (inventory is { } data)
            {
                await CommandAsync(connection, transaction, "INSERT INTO inventory_revisions(revision_id, session_id, event_id, sequence, completeness) VALUES ($revision, $session, $event, $sequence, $completeness);", cancellationToken, ("$revision", revisionId), ("$session", data.Envelope.SessionId.ToString("D")), ("$event", data.Envelope.EventId.ToString("D")), ("$sequence", data.Envelope.Sequence), ("$completeness", data.Envelope.Completeness));
                foreach (var equipment in data.Projection.Equipment)
                    await CommandAsync(connection, transaction, "INSERT INTO inventory_equipment(revision_id, instance_id, type_id, rank, config_json, rank_state, config_state, raw_json) VALUES ($revision, $instance, $type, $rank, $config, $rankState, $configState, $raw);", cancellationToken, ("$revision", revisionId), ("$instance", equipment.InstanceId), ("$type", (object?)equipment.TypeId ?? DBNull.Value), ("$rank", (object?)equipment.Rank ?? DBNull.Value), ("$config", (object?)equipment.ConfigJson ?? DBNull.Value), ("$rankState", (int)equipment.RankState), ("$configState", (int)equipment.ConfigState), ("$raw", equipment.RawJson));
                for (var index = 0; index < data.Projection.Stackables.Count; index++)
                {
                    var stackable = data.Projection.Stackables[index];
                    await CommandAsync(connection, transaction, "INSERT INTO inventory_stackables(revision_id, ordinal, type_id, quantity, quantity_state, raw_json) VALUES ($revision, $ordinal, $type, $quantity, $state, $raw);", cancellationToken, ("$revision", revisionId), ("$ordinal", index), ("$type", (object?)stackable.TypeId ?? DBNull.Value), ("$quantity", (object?)stackable.Quantity ?? DBNull.Value), ("$state", (int)stackable.QuantityState), ("$raw", stackable.RawJson));
                }
                for (var index = 0; index < data.Projection.Unknown.Count; index++)
                {
                    var unknown = data.Projection.Unknown[index];
                    await CommandAsync(connection, transaction, "INSERT INTO inventory_unknown(revision_id, ordinal, kind, reason_code, raw_json) VALUES ($revision, $ordinal, $kind, $reason, $raw);", cancellationToken, ("$revision", revisionId), ("$ordinal", index), ("$kind", unknown.Kind), ("$reason", unknown.ReasonCode), ("$raw", unknown.RawJson));
                }
            }
            await transaction.CommitAsync(cancellationToken);
            return new SyncPublicationResult(runId, revisionId, false, batch.RecordCount);
        }
        finally { _writer.Release(); }
    }

    public async Task<SyncStatus?> GetStatusAsync(string sourceId, CancellationToken cancellationToken = default)
    {
        // Status reads must not create or migrate the database. This keeps the
        // MCP/UI read path genuinely read-only and lets a missing store report
        // "not initialized" instead of mutating state.
        if (!File.Exists(_path)) return null;
        await using var connection = await OpenAsync(SqliteOpenMode.ReadOnly, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT r.revision_id, r.content_hash, run.state, run.finished_at, run.records_accepted, run.records_rejected, run.error_code
            FROM sources s LEFT JOIN source_revisions r ON r.source_id=s.source_id AND r.state='active'
            LEFT JOIN sync_runs run ON run.run_id=(SELECT run_id FROM sync_runs WHERE source_id=s.source_id ORDER BY started_at DESC LIMIT 1)
            WHERE s.source_id=$source LIMIT 1;
            """;
        command.Parameters.AddWithValue("$source", sourceId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new SyncStatus(sourceId, reader.IsDBNull(0) ? null : reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.IsDBNull(3) ? null : DateTimeOffset.Parse(reader.GetString(3)), reader.IsDBNull(4) ? 0 : reader.GetInt64(4), reader.IsDBNull(5) ? 0 : reader.GetInt64(5), reader.IsDBNull(6) ? null : reader.GetString(6));
    }

    public async Task RecordFailureAsync(string sourceId, string errorCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceId) || string.IsNullOrWhiteSpace(errorCode)) throw new ArgumentException("Source and error code are required.");
        await _writer.WaitAsync(cancellationToken);
        try
        {
            await InitializeAsync(cancellationToken);
            await using var connection = await OpenAsync(SqliteOpenMode.ReadWrite, cancellationToken);
            var now = DateTimeOffset.UtcNow.ToString("O");
            await CommandAsync(connection, null, "INSERT OR IGNORE INTO sources(source_id, kind, display_name, created_at) VALUES ($id, 'sync', $id, $at);", cancellationToken, ("$id", sourceId), ("$at", now));
            await CommandAsync(connection, null, "INSERT INTO sync_runs(run_id, source_id, state, started_at, finished_at, records_received, records_accepted, records_rejected, error_code) VALUES ($run, $source, 'failed', $at, $at, 0, 0, 0, $error);", cancellationToken, ("$run", Guid.NewGuid().ToString("N")), ("$source", sourceId), ("$at", now), ("$error", errorCode));
        }
        finally { _writer.Release(); }
    }

    public async Task BackupAsync(string destinationPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(destinationPath)) throw new ArgumentException("Backup path is required.", nameof(destinationPath));
        var destination = Path.GetFullPath(destinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await _writer.WaitAsync(cancellationToken);
        try
        {
            await InitializeAsync(cancellationToken);
            await using var source = await OpenAsync(SqliteOpenMode.ReadOnly, cancellationToken);
            await using var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destination, Mode = SqliteOpenMode.ReadWriteCreate, Cache = SqliteCacheMode.Shared, Pooling = false }.ToString());
            await target.OpenAsync(cancellationToken);
            source.BackupDatabase(target);
        }
        finally { _writer.Release(); }
    }

    public async ValueTask DisposeAsync() { _writer.Dispose(); await Task.CompletedTask; }

    private async Task<SqliteConnection> OpenAsync(SqliteOpenMode mode, CancellationToken token)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _path, Mode = mode, Cache = SqliteCacheMode.Shared, Pooling = false }.ToString());
        await connection.OpenAsync(token);
        await ExecuteAsync(connection, "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;", token);
        return connection;
    }
    private static async Task ExecuteAsync(SqliteConnection connection, string sql, CancellationToken token = default) { await using var command = connection.CreateCommand(); command.CommandText = sql; await command.ExecuteNonQueryAsync(token); }
    private static async Task CommandAsync(SqliteConnection c, SqliteTransaction? t, string sql, CancellationToken token, params (string Name, object Value)[] args) { await using var command = c.CreateCommand(); if (t is not null) command.Transaction = t; command.CommandText = sql; foreach (var (name, value) in args) command.Parameters.AddWithValue(name, value); await command.ExecuteNonQueryAsync(token); }
    private static void Validate(SyncBatch batch) { if (string.IsNullOrWhiteSpace(batch.SourceId) || string.IsNullOrWhiteSpace(batch.ContentHash) || string.IsNullOrWhiteSpace(batch.PayloadJson) || batch.RecordCount < 0) throw new ArgumentException("Sync batch is incomplete."); }
}
