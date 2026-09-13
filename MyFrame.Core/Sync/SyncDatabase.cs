using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace MyFrame.Core.Sync;

public sealed class SyncDatabase : IAsyncDisposable
{
    private const int SchemaVersion = 2;
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
        await EnsureSchemaCompatibilityAsync(connection, cancellationToken);
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
            CREATE TABLE IF NOT EXISTS inventory_upgrades(revision_id TEXT NOT NULL REFERENCES inventory_revisions(revision_id), ordinal INTEGER NOT NULL, owner_instance_id TEXT, source_field TEXT NOT NULL, upgrade_id TEXT, rank INTEGER, raw_json TEXT NOT NULL, PRIMARY KEY(revision_id, ordinal));
            CREATE TABLE IF NOT EXISTS worldstate_revisions(revision_id TEXT PRIMARY KEY REFERENCES source_revisions(revision_id), source_timestamp TEXT, retrieved_at TEXT NOT NULL, is_current INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS worldstate_bounties(revision_id TEXT NOT NULL REFERENCES worldstate_revisions(revision_id), bounty_id TEXT NOT NULL, syndicate TEXT, activation TEXT, expiry TEXT, PRIMARY KEY(revision_id, bounty_id));
            CREATE TABLE IF NOT EXISTS worldstate_jobs(revision_id TEXT NOT NULL, bounty_id TEXT NOT NULL, job_id TEXT NOT NULL, type TEXT, unique_name TEXT, minimum_mastery_rank INTEGER, standing_stages_json TEXT NOT NULL, PRIMARY KEY(revision_id, bounty_id, job_id));
            CREATE TABLE IF NOT EXISTS worldstate_rewards(revision_id TEXT NOT NULL, bounty_id TEXT NOT NULL, job_id TEXT NOT NULL, ordinal INTEGER NOT NULL, item TEXT NOT NULL, chance REAL, count INTEGER, rarity TEXT, PRIMARY KEY(revision_id, bounty_id, job_id, ordinal));
            CREATE TABLE IF NOT EXISTS worldstate_cycles(revision_id TEXT NOT NULL REFERENCES worldstate_revisions(revision_id), name TEXT NOT NULL, state TEXT, activation TEXT, expiry TEXT, PRIMARY KEY(revision_id, name));
            """);
        await EnsureColumnAsync(connection, "public_export_items", "raw_json", "TEXT NOT NULL DEFAULT '{}'");
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT OR IGNORE INTO schema_migrations(version, applied_at) VALUES ($version, $at);";
        command.Parameters.AddWithValue("$version", SchemaVersion);
        command.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsureSchemaCompatibilityAsync(SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name='schema_migrations' LIMIT 1;";
        if (await command.ExecuteScalarAsync(cancellationToken) is null) return;
        command.CommandText = "SELECT MAX(version) FROM schema_migrations;";
        var value = await command.ExecuteScalarAsync(cancellationToken);
        if (value is not null && value is not DBNull && Convert.ToInt32(value) > SchemaVersion)
            throw new NotSupportedException("SYNC_SCHEMA_NEWER");
    }

    public async Task<SyncPublicationResult> PublishAsync(SyncBatch batch, CancellationToken cancellationToken = default)
        => await PublishInternalAsync(batch, null, null, null, cancellationToken);

    public async Task<SyncPublicationResult> PublishCatalogAsync(SyncBatch batch, IReadOnlyList<PublicExportRecord> records, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(records);
        if (records.Count != batch.RecordCount) throw new ArgumentException("Catalog record count does not match batch.", nameof(records));
        if (records.Select(record => record.UniqueName).Distinct(StringComparer.Ordinal).Count() != records.Count) throw new ArgumentException("Catalog contains duplicate unique names.", nameof(records));
        return await PublishInternalAsync(batch, records, null, null, cancellationToken);
    }

    public async Task<SyncPublicationResult> PublishInventoryAsync(InventoryEnvelope envelope, InventoryProjection projection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(projection);
        if (envelope.Completeness is not ("verified" or "unverified")) throw new ArgumentException("Inventory completeness is invalid.", nameof(envelope));
        var batch = new SyncBatch("overwolf-inventory", envelope.ContentHash, envelope.PayloadJson, projection.Equipment.Count + projection.Stackables.Count, "overwolf-native-1");
        return await PublishInternalAsync(batch, null, (envelope, projection), null, cancellationToken);
    }

    public async Task<SyncPublicationResult> PublishWorldStateAsync(WorldStateSnapshot snapshot, SyncBatch batch, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!string.Equals(batch.SourceId, "worldstate-pc", StringComparison.Ordinal) || !string.Equals(batch.ContentHash, snapshot.ContentHash, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("World State batch does not match snapshot.", nameof(batch));
        return await PublishInternalAsync(batch, null, null, (snapshot, batch), cancellationToken);
    }

    public async Task<int> PruneRetainedAsync(int maximumRevisionsPerSource = 3,
        CancellationToken cancellationToken = default)
    {
        if (maximumRevisionsPerSource is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(maximumRevisionsPerSource),
                "maximumRevisionsPerSource must be between 1 and 100.");

        await _writer.WaitAsync(cancellationToken);
        try
        {
            await InitializeAsync(cancellationToken);
            await using var connection = await OpenAsync(SqliteOpenMode.ReadWrite, cancellationToken);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            var candidates = new List<(string RevisionId, string SourceId)>();
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "SELECT revision_id, source_id FROM source_revisions WHERE state='retained' ORDER BY source_id, retrieved_at DESC, revision_id DESC;";
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                var retainedBySource = new Dictionary<string, int>(StringComparer.Ordinal);
                while (await reader.ReadAsync(cancellationToken))
                {
                    var source = reader.GetString(1);
                    var retained = retainedBySource.GetValueOrDefault(source);
                    if (retained >= maximumRevisionsPerSource - 1)
                        candidates.Add((reader.GetString(0), source));
                    else
                        retainedBySource[source] = retained + 1;
                }
            }

            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var table in new[] { "public_export_items", "inventory_equipment", "inventory_stackables", "inventory_unknown", "inventory_upgrades", "inventory_revisions", "worldstate_rewards", "worldstate_jobs", "worldstate_bounties", "worldstate_cycles", "worldstate_revisions" })
                    await CommandAsync(connection, transaction, $"DELETE FROM {table} WHERE revision_id=$revision;", cancellationToken, ("$revision", candidate.RevisionId));
                await CommandAsync(connection, transaction, "DELETE FROM source_revisions WHERE revision_id=$revision AND state='retained';", cancellationToken, ("$revision", candidate.RevisionId));
            }

            await transaction.CommitAsync(cancellationToken);
            return candidates.Count;
        }
        finally { _writer.Release(); }
    }

    public async Task<IReadOnlyList<PublicExportRecord>> GetPublicExportItemsAsync(string sourceId, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path)) return [];
        await using var connection = await OpenAsync(SqliteOpenMode.ReadOnly, cancellationToken);
        var hasRawJson = await HasColumnAsync(connection, "public_export_items", "raw_json", cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = hasRawJson ? """
            SELECT i.unique_name, i.name, i.category, i.description, i.canonical_name, i.raw_json
            FROM public_export_items i JOIN source_revisions r ON r.revision_id=i.revision_id
            WHERE r.source_id=$source AND r.state='active' ORDER BY i.unique_name;
            """ : """
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
            records.Add(new PublicExportRecord(uniqueName, name, reader.IsDBNull(2) ? null : reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3), new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["en"] = name ?? uniqueName }, hasRawJson && !reader.IsDBNull(5) ? reader.GetString(5) : null));
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

    public async Task<IReadOnlyDictionary<string, InventoryFieldState>> GetInventoryCoverageAsync(
        CancellationToken cancellationToken = default)
        => await GetSourceCoverageAsync("overwolf-inventory", cancellationToken);

    public async Task<IReadOnlyDictionary<string, InventoryFieldState>> GetSourceCoverageAsync(
        string sourceId, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path)) return new Dictionary<string, InventoryFieldState>(StringComparer.Ordinal);
        await using var connection = await OpenAsync(SqliteOpenMode.ReadOnly, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT field_path, state FROM coverage WHERE source_id=$source ORDER BY field_path;";
        command.Parameters.AddWithValue("$source", sourceId);
        var result = new Dictionary<string, InventoryFieldState>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            if (Enum.TryParse<InventoryFieldState>(reader.GetString(1), ignoreCase: false, out var state))
                result[reader.GetString(0)] = state;
        return result;
    }

    public async Task<IReadOnlyList<InventoryStackableRecord>> GetInventoryStackablesAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path)) return [];
        await using var connection = await OpenAsync(SqliteOpenMode.ReadOnly, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.type_id, s.quantity, s.quantity_state, s.raw_json
            FROM inventory_stackables s JOIN inventory_revisions ir ON ir.revision_id=s.revision_id
            JOIN source_revisions r ON r.revision_id=ir.revision_id
            WHERE r.source_id='overwolf-inventory' AND r.state='active' ORDER BY s.ordinal;
            """;
        var records = new List<InventoryStackableRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            records.Add(new(reader.IsDBNull(0) ? null : reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetInt32(1), (InventoryFieldState)reader.GetInt32(2), reader.GetString(3)));
        return records;
    }

    public async Task<IReadOnlyList<InventoryUpgradeRecord>> GetInventoryUpgradesAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path)) return [];
        await using var connection = await OpenAsync(SqliteOpenMode.ReadOnly, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT u.owner_instance_id, u.source_field, u.upgrade_id, u.rank, u.raw_json
            FROM inventory_upgrades u JOIN inventory_revisions ir ON ir.revision_id=u.revision_id
            JOIN source_revisions r ON r.revision_id=ir.revision_id
            WHERE r.source_id='overwolf-inventory' AND r.state='active' ORDER BY u.ordinal;
            """;
        var records = new List<InventoryUpgradeRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            records.Add(new(reader.IsDBNull(0) ? null : reader.GetString(0), reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetInt32(3), reader.GetString(4)));
        return records;
    }

    public async Task<IReadOnlyList<WorldStateBounty>> GetCurrentWorldStateBountiesAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path)) return [];
        await using var connection = await OpenAsync(SqliteOpenMode.ReadOnly, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT b.revision_id, b.bounty_id, b.syndicate, b.activation, b.expiry
            FROM worldstate_bounties b JOIN worldstate_revisions wr ON wr.revision_id=b.revision_id
            JOIN source_revisions r ON r.revision_id=wr.revision_id
            WHERE r.source_id='worldstate-pc' AND r.state='active' AND (b.activation IS NULL OR b.activation <= $now) AND (b.expiry IS NULL OR b.expiry > $now)
            ORDER BY b.bounty_id;
            """;
        command.Parameters.AddWithValue("$now", now.ToString("O"));
        var result = new List<WorldStateBounty>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var revisionId = reader.GetString(0);
            var bountyId = reader.GetString(1);
            var jobs = new List<WorldStateJob>();
            await using var jobsCommand = connection.CreateCommand();
            jobsCommand.CommandText = "SELECT job_id, type, unique_name, minimum_mastery_rank, standing_stages_json FROM worldstate_jobs WHERE revision_id=$revision AND bounty_id=$bounty ORDER BY job_id;";
            jobsCommand.Parameters.AddWithValue("$revision", revisionId);
            jobsCommand.Parameters.AddWithValue("$bounty", bountyId);
            await using var jobsReader = await jobsCommand.ExecuteReaderAsync(cancellationToken);
            while (await jobsReader.ReadAsync(cancellationToken))
            {
                var jobId = jobsReader.GetString(0);
                var stages = JsonSerializer.Deserialize<int[]>(jobsReader.GetString(4)) ?? [];
                var rewards = new List<WorldStateReward>();
                await using var rewardsCommand = connection.CreateCommand();
                rewardsCommand.CommandText = "SELECT item, chance, count, rarity FROM worldstate_rewards WHERE revision_id=$revision AND bounty_id=$bounty AND job_id=$job ORDER BY ordinal;";
                rewardsCommand.Parameters.AddWithValue("$revision", revisionId);
                rewardsCommand.Parameters.AddWithValue("$bounty", bountyId);
                rewardsCommand.Parameters.AddWithValue("$job", jobId);
                await using var rewardsReader = await rewardsCommand.ExecuteReaderAsync(cancellationToken);
                while (await rewardsReader.ReadAsync(cancellationToken))
                    rewards.Add(new(rewardsReader.GetString(0), rewardsReader.IsDBNull(1) ? null : Convert.ToDecimal(rewardsReader.GetValue(1)),
                        rewardsReader.IsDBNull(2) ? null : rewardsReader.GetInt32(2), rewardsReader.IsDBNull(3) ? null : rewardsReader.GetString(3)));
                jobs.Add(new(jobId, jobsReader.IsDBNull(1) ? null : jobsReader.GetString(1),
                    jobsReader.IsDBNull(2) ? null : jobsReader.GetString(2), jobsReader.IsDBNull(3) ? null : jobsReader.GetInt32(3), stages, rewards));
            }
            result.Add(new(bountyId, reader.IsDBNull(2) ? null : reader.GetString(2), ParseDate(reader, 3), ParseDate(reader, 4), jobs));
        }
        return result;
    }

    public async Task<IReadOnlyList<WorldStateCycle>> GetCurrentWorldStateCyclesAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path)) return [];
        await using var connection = await OpenAsync(SqliteOpenMode.ReadOnly, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.name, c.state, c.activation, c.expiry
            FROM worldstate_cycles c JOIN worldstate_revisions wr ON wr.revision_id=c.revision_id
            JOIN source_revisions r ON r.revision_id=wr.revision_id
            WHERE r.source_id='worldstate-pc' AND r.state='active'
            ORDER BY c.name;
            """;
        var result = new List<WorldStateCycle>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1),
                ParseDate(reader, 2), ParseDate(reader, 3)));
        return result;
    }

    private async Task<SyncPublicationResult> PublishInternalAsync(SyncBatch batch, IReadOnlyList<PublicExportRecord>? records, (InventoryEnvelope Envelope, InventoryProjection Projection)? inventory, (WorldStateSnapshot Snapshot, SyncBatch Batch)? worldState = null, CancellationToken cancellationToken = default)
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
                    await CommandAsync(connection, transaction, "INSERT INTO public_export_items(revision_id, unique_name, name, category, description, canonical_name, raw_json) VALUES ($revision, $unique, $name, $category, $description, $canonical, $raw);", cancellationToken, ("$revision", revisionId), ("$unique", record.UniqueName), ("$name", (object?)record.Name ?? DBNull.Value), ("$category", (object?)record.Category ?? DBNull.Value), ("$description", (object?)record.Description ?? DBNull.Value), ("$canonical", PublicExportIdentity.Canonicalize(record.Name ?? record.UniqueName)), ("$raw", (object?)record.RawJson ?? "{}"));
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
                for (var index = 0; index < (data.Projection.Upgrades?.Count ?? 0); index++)
                {
                    var upgrade = data.Projection.Upgrades![index];
                    await CommandAsync(connection, transaction, "INSERT INTO inventory_upgrades(revision_id, ordinal, owner_instance_id, source_field, upgrade_id, rank, raw_json) VALUES ($revision, $ordinal, $owner, $source, $id, $rank, $raw);", cancellationToken, ("$revision", revisionId), ("$ordinal", index), ("$owner", (object?)upgrade.OwnerInstanceId ?? DBNull.Value), ("$source", upgrade.SourceField), ("$id", (object?)upgrade.UpgradeId ?? DBNull.Value), ("$rank", (object?)upgrade.Rank ?? DBNull.Value), ("$raw", upgrade.RawJson));
                }
                await CommandAsync(connection, transaction, "DELETE FROM coverage WHERE source_id='overwolf-inventory';", cancellationToken);
                foreach (var field in data.Projection.Coverage)
                    await CommandAsync(connection, transaction, "INSERT INTO coverage(source_id, field_path, state, observed_at, detail) VALUES ('overwolf-inventory', $field, $state, $at, NULL) ON CONFLICT(source_id, field_path) DO UPDATE SET state=excluded.state, observed_at=excluded.observed_at, detail=excluded.detail;", cancellationToken, ("$field", field.Key), ("$state", field.Value.ToString()), ("$at", now));
            }
            if (worldState is { } world)
            {
                await CommandAsync(connection, transaction, "INSERT INTO worldstate_revisions(revision_id, source_timestamp, retrieved_at, is_current) VALUES ($revision, $sourceTimestamp, $retrieved, 1);", cancellationToken, ("$revision", revisionId), ("$sourceTimestamp", (object?)world.Snapshot.SourceTimestamp?.ToString("O") ?? DBNull.Value), ("$retrieved", world.Snapshot.RetrievedAt.ToString("O")));
                await CommandAsync(connection, transaction, "DELETE FROM coverage WHERE source_id='worldstate-pc';", cancellationToken);
                foreach (var field in world.Snapshot.Coverage)
                    await CommandAsync(connection, transaction, "INSERT INTO coverage(source_id, field_path, state, observed_at, detail) VALUES ('worldstate-pc', $field, $state, $at, NULL) ON CONFLICT(source_id, field_path) DO UPDATE SET state=excluded.state, observed_at=excluded.observed_at, detail=excluded.detail;", cancellationToken, ("$field", field.Key), ("$state", field.Value.ToString()), ("$at", now));
                foreach (var bounty in world.Snapshot.Bounties)
                {
                    await CommandAsync(connection, transaction, "INSERT INTO worldstate_bounties(revision_id, bounty_id, syndicate, activation, expiry) VALUES ($revision, $id, $syndicate, $activation, $expiry);", cancellationToken, ("$revision", revisionId), ("$id", bounty.Id), ("$syndicate", (object?)bounty.Syndicate ?? DBNull.Value), ("$activation", (object?)bounty.Activation?.ToString("O") ?? DBNull.Value), ("$expiry", (object?)bounty.Expiry?.ToString("O") ?? DBNull.Value));
                    foreach (var job in bounty.Jobs)
                    {
                        await CommandAsync(connection, transaction, "INSERT INTO worldstate_jobs(revision_id, bounty_id, job_id, type, unique_name, minimum_mastery_rank, standing_stages_json) VALUES ($revision, $bounty, $job, $type, $unique, $mr, $stages);", cancellationToken, ("$revision", revisionId), ("$bounty", bounty.Id), ("$job", job.Id), ("$type", (object?)job.Type ?? DBNull.Value), ("$unique", (object?)job.UniqueName ?? DBNull.Value), ("$mr", (object?)job.MinimumMasteryRank ?? DBNull.Value), ("$stages", JsonSerializer.Serialize(job.StandingStages)));
                        for (var index = 0; index < job.Rewards.Count; index++)
                        {
                            var reward = job.Rewards[index];
                            await CommandAsync(connection, transaction, "INSERT INTO worldstate_rewards(revision_id, bounty_id, job_id, ordinal, item, chance, count, rarity) VALUES ($revision, $bounty, $job, $ordinal, $item, $chance, $count, $rarity);", cancellationToken, ("$revision", revisionId), ("$bounty", bounty.Id), ("$job", job.Id), ("$ordinal", index), ("$item", reward.Item), ("$chance", (object?)reward.Chance ?? DBNull.Value), ("$count", (object?)reward.Count ?? DBNull.Value), ("$rarity", (object?)reward.Rarity ?? DBNull.Value));
                        }
                    }
                }
                foreach (var cycle in world.Snapshot.Cycles)
                    await CommandAsync(connection, transaction, "INSERT INTO worldstate_cycles(revision_id, name, state, activation, expiry) VALUES ($revision, $name, $state, $activation, $expiry);", cancellationToken, ("$revision", revisionId), ("$name", cycle.Name), ("$state", (object?)cycle.State ?? DBNull.Value), ("$activation", (object?)cycle.Activation?.ToString("O") ?? DBNull.Value), ("$expiry", (object?)cycle.Expiry?.ToString("O") ?? DBNull.Value));
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

    public async Task<IReadOnlyList<SyncRunSummary>> GetRecentRunsAsync(
        string? sourceId = null, int limit = 20, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path)) return [];
        limit = Math.Clamp(limit, 1, 100);
        await using var connection = await OpenAsync(SqliteOpenMode.ReadOnly, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT run_id, source_id, state, started_at, finished_at, records_received,
                   records_accepted, records_rejected, error_code
            FROM sync_runs
            WHERE ($source IS NULL OR source_id=$source)
            ORDER BY started_at DESC LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$source", (object?)sourceId ?? DBNull.Value);
        command.Parameters.AddWithValue("$limit", limit);
        var result = new List<SyncRunSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2),
                DateTimeOffset.Parse(reader.GetString(3)), reader.IsDBNull(4) ? null : DateTimeOffset.Parse(reader.GetString(4)),
                reader.GetInt64(5), reader.GetInt64(6), reader.GetInt64(7), reader.IsDBNull(8) ? null : reader.GetString(8)));
        return result;
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

    public async Task RestoreAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath)) throw new ArgumentException("Restore path is required.", nameof(sourcePath));
        var source = Path.GetFullPath(sourcePath);
        if (string.Equals(source, _path, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Restore source must be different from the active database.", nameof(sourcePath));
        if (!File.Exists(source) || (File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
            throw new FileNotFoundException("Restore source was not found or is a link.", source);

        await _writer.WaitAsync(cancellationToken);
        var temporary = Path.Combine(Path.GetDirectoryName(_path)!, $".{Path.GetFileName(_path)}.restore-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var backupSource = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = source, Mode = SqliteOpenMode.ReadOnly, Cache = SqliteCacheMode.Shared, Pooling = false
            }.ToString()))
            await using (var backupTarget = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = temporary, Mode = SqliteOpenMode.ReadWriteCreate, Cache = SqliteCacheMode.Shared, Pooling = false
            }.ToString()))
            {
                await backupSource.OpenAsync(cancellationToken);
                await backupTarget.OpenAsync(cancellationToken);
                backupSource.BackupDatabase(backupTarget);
            }
            File.Move(temporary, _path, true);
            foreach (var sidecar in new[] { _path + "-wal", _path + "-shm" })
                if (File.Exists(sidecar)) File.Delete(sidecar);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            _writer.Release();
        }
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
    private static async Task EnsureColumnAsync(SqliteConnection connection, string table, string column, string definition, CancellationToken cancellationToken = default)
    {
        await using var check = connection.CreateCommand();
        check.CommandText = $"PRAGMA table_info([{table}]);";
        await using var reader = await check.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase)) return;
        await reader.DisposeAsync();
        await using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE [{table}] ADD COLUMN [{column}] {definition};";
        await alter.ExecuteNonQueryAsync(cancellationToken);
    }
    private static async Task<bool> HasColumnAsync(SqliteConnection connection, string table, string column, CancellationToken cancellationToken = default)
    {
        await using var check = connection.CreateCommand();
        check.CommandText = $"PRAGMA table_info([{table}]);";
        await using var reader = await check.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
    private static DateTimeOffset? ParseDate(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : DateTimeOffset.Parse(reader.GetString(ordinal));
    private static void Validate(SyncBatch batch) { if (string.IsNullOrWhiteSpace(batch.SourceId) || string.IsNullOrWhiteSpace(batch.ContentHash) || string.IsNullOrWhiteSpace(batch.PayloadJson) || batch.RecordCount < 0) throw new ArgumentException("Sync batch is incomplete."); }
}
