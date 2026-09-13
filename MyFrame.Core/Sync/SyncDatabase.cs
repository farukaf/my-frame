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
            """);
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT OR IGNORE INTO schema_migrations(version, applied_at) VALUES ($version, $at);";
        command.Parameters.AddWithValue("$version", SchemaVersion);
        command.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<SyncPublicationResult> PublishAsync(SyncBatch batch, CancellationToken cancellationToken = default)
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
            await transaction.CommitAsync(cancellationToken);
            return new SyncPublicationResult(runId, revisionId, false, batch.RecordCount);
        }
        finally { _writer.Release(); }
    }

    public async Task<SyncStatus?> GetStatusAsync(string sourceId, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(SqliteOpenMode.ReadOnly, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT r.revision_id, r.content_hash, run.state, run.finished_at, run.records_accepted, run.records_rejected
            FROM sources s LEFT JOIN source_revisions r ON r.source_id=s.source_id AND r.state='active'
            LEFT JOIN sync_runs run ON run.run_id=(SELECT run_id FROM sync_runs WHERE source_id=s.source_id ORDER BY started_at DESC LIMIT 1)
            WHERE s.source_id=$source LIMIT 1;
            """;
        command.Parameters.AddWithValue("$source", sourceId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new SyncStatus(sourceId, reader.IsDBNull(0) ? null : reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.IsDBNull(3) ? null : DateTimeOffset.Parse(reader.GetString(3)), reader.IsDBNull(4) ? 0 : reader.GetInt64(4), reader.IsDBNull(5) ? 0 : reader.GetInt64(5));
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
    private static async Task CommandAsync(SqliteConnection c, SqliteTransaction t, string sql, CancellationToken token, params (string Name, object Value)[] args) { await using var command = c.CreateCommand(); command.Transaction = t; command.CommandText = sql; foreach (var (name, value) in args) command.Parameters.AddWithValue(name, value); await command.ExecuteNonQueryAsync(token); }
    private static void Validate(SyncBatch batch) { if (string.IsNullOrWhiteSpace(batch.SourceId) || string.IsNullOrWhiteSpace(batch.ContentHash) || string.IsNullOrWhiteSpace(batch.PayloadJson) || batch.RecordCount < 0) throw new ArgumentException("Sync batch is incomplete."); }
}
