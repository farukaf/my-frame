using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace MyFrame.Core;

/// <summary>
/// Persists user settings in the shared SQLite database and imports the legacy JSON document on
/// first use. The legacy file is intentionally retained as a rollback source.
/// </summary>
public sealed class SqliteSettingsStore : IMyFrameSettingsWriter
{
    private readonly string _databasePath;
    private readonly string? _legacyPath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    private Task? _initialized;

    public SqliteSettingsStore(string databasePath, string? legacyPath = null)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
            throw new ArgumentException("Database path is required.", nameof(databasePath));
        _databasePath = Path.GetFullPath(databasePath);
        _legacyPath = legacyPath;
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
    }

    public async Task<MyFrameSettingsDocument?> LoadAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload_json FROM myframe_settings WHERE id = 1;";
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (value is not string json) return null;
        return Normalize(JsonSerializer.Deserialize<MyFrameSettingsDocument>(json, _json));
    }

    public async Task SaveAsync(MyFrameSettingsDocument settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.StorageVersion != MyFrameSettingsDocument.CurrentStorageVersion)
            throw new ArgumentOutOfRangeException(nameof(settings), "Unsupported settings version.");
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO myframe_settings(id, payload_json, updated_at)
            VALUES (1, $payload, $updated)
            ON CONFLICT(id) DO UPDATE SET payload_json = excluded.payload_json, updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(settings, _json));
        command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static MyFrameSettingsDocument? Normalize(MyFrameSettingsDocument? value) => value is null
        ? null
        : value with
        {
            DucatsPerPlatinum = Math.Clamp(value.DucatsPerPlatinum, 1, 50),
            UnvaultedPrimeSetsToReserve = Math.Clamp(value.UnvaultedPrimeSetsToReserve, 0, 10)
        };

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        Task initialization;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { initialization = _initialized ??= InitializeCoreAsync(); }
        finally { _gate.Release(); }
        await initialization.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task InitializeCoreAsync()
    {
        await using var connection = await OpenAsync(CancellationToken.None).ConfigureAwait(false);
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "CREATE TABLE IF NOT EXISTS myframe_settings(id INTEGER PRIMARY KEY CHECK(id = 1), payload_json TEXT NOT NULL, updated_at TEXT NOT NULL);";
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        if (string.IsNullOrWhiteSpace(_legacyPath) || !File.Exists(_legacyPath)) return;
        await using var count = connection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM myframe_settings WHERE id = 1;";
        if (Convert.ToInt32(await count.ExecuteScalarAsync()) != 0) return;
        MyFrameSettingsDocument? settings;
        try { settings = JsonSerializer.Deserialize<MyFrameSettingsDocument>(await File.ReadAllTextAsync(_legacyPath), _json); }
        catch (Exception error) when (error is IOException or JsonException) { return; }
        if (settings is null || settings.StorageVersion != MyFrameSettingsDocument.CurrentStorageVersion) return;
        await using var insert = connection.CreateCommand();
        insert.CommandText = "INSERT INTO myframe_settings(id, payload_json, updated_at) VALUES (1, $payload, $updated);";
        insert.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(settings, _json));
        insert.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
        await insert.ExecuteNonQueryAsync();
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false
        }.ToString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=5000;";
        await pragma.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }
}
