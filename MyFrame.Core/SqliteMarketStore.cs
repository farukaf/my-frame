using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace MyFrame.Core;

/// <summary>
/// Stores market quotes, the last account/order response and the market item index in the
/// shared SQLite database. Legacy JSON files are imported once, without being deleted, so an
/// upgrade can be rolled back safely while all new writes use one transactional store.
/// </summary>
public sealed class SqliteMarketStore : IPriceCache, IReadOnlyPriceCache, IMarketStateStore,
    IMarketItemIndexStore
{
    private readonly string _databasePath;
    private readonly string? _legacyPricesPath;
    private readonly string? _legacyStatePath;
    private readonly string? _legacyItemsPath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    private Task? _initialized;

    public SqliteMarketStore(string databasePath, string? legacyPricesPath = null,
        string? legacyStatePath = null, string? legacyItemsPath = null)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
            throw new ArgumentException("Database path is required.", nameof(databasePath));
        _databasePath = Path.GetFullPath(databasePath);
        _legacyPricesPath = legacyPricesPath;
        _legacyStatePath = legacyStatePath;
        _legacyItemsPath = legacyItemsPath;
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
    }

    public async Task<MarketQuote?> GetAsync(string slug, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload_json FROM market_quotes WHERE slug = $slug;";
        command.Parameters.AddWithValue("$slug", slug);
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is string json ? JsonSerializer.Deserialize<MarketQuote>(json, _json) : null;
    }

    public async Task SetAsync(MarketQuote quote, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(quote);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO market_quotes(slug, payload_json, updated_at)
            VALUES ($slug, $payload, $updated)
            ON CONFLICT(slug) DO UPDATE SET payload_json = excluded.payload_json, updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$slug", quote.Slug);
        command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(quote, _json));
        command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<string, MarketQuote>> LoadAllAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT slug, payload_json FROM market_quotes;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new Dictionary<string, MarketQuote>(StringComparer.Ordinal);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var quote = JsonSerializer.Deserialize<MarketQuote>(reader.GetString(1), _json);
            if (quote is not null) result[reader.GetString(0)] = quote;
        }
        return result;
    }

    async Task<MarketState?> IMarketStateStore.LoadAsync(CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        return await LoadSingletonAsync<MarketState>("market_state", cancellationToken).ConfigureAwait(false);
    }

    async Task IMarketStateStore.SaveAsync(MarketState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await SaveSingletonAsync("market_state", state, cancellationToken).ConfigureAwait(false);
    }

    async Task<MarketItemIndex?> IMarketItemIndexStore.LoadAsync(CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        return await LoadSingletonAsync<MarketItemIndex>("market_item_index", cancellationToken).ConfigureAwait(false);
    }

    async Task IMarketItemIndexStore.SaveAsync(MarketItemIndex index, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(index);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await SaveSingletonAsync("market_item_index", index, cancellationToken).ConfigureAwait(false);
    }

    private async Task<T?> LoadSingletonAsync<T>(string table, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT payload_json FROM {table} WHERE id = 1;";
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is string json ? JsonSerializer.Deserialize<T>(json, _json) : default;
    }

    private async Task SaveSingletonAsync<T>(string table, T value, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO {table}(id, payload_json, updated_at)
            VALUES (1, $payload, $updated)
            ON CONFLICT(id) DO UPDATE SET payload_json = excluded.payload_json, updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(value, _json));
        command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

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
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS market_quotes(slug TEXT PRIMARY KEY, payload_json TEXT NOT NULL, updated_at TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS market_state(id INTEGER PRIMARY KEY CHECK(id = 1), payload_json TEXT NOT NULL, updated_at TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS market_item_index(id INTEGER PRIMARY KEY CHECK(id = 1), payload_json TEXT NOT NULL, updated_at TEXT NOT NULL);
                """;
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        await ImportLegacyQuotesAsync(connection).ConfigureAwait(false);
        await ImportLegacySingletonAsync<MarketState>(connection, "market_state", _legacyStatePath).ConfigureAwait(false);
        await ImportLegacySingletonAsync<MarketItemIndex>(connection, "market_item_index", _legacyItemsPath).ConfigureAwait(false);
    }

    private async Task ImportLegacyQuotesAsync(SqliteConnection connection)
    {
        if (string.IsNullOrWhiteSpace(_legacyPricesPath) || !File.Exists(_legacyPricesPath)) return;
        await using var count = connection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM market_quotes;";
        if (Convert.ToInt32(await count.ExecuteScalarAsync()) != 0) return;
        Dictionary<string, MarketQuote>? values;
        try
        {
            values = JsonSerializer.Deserialize<Dictionary<string, MarketQuote>>(
                await File.ReadAllTextAsync(_legacyPricesPath), _json);
        }
        catch (Exception error) when (error is IOException or JsonException) { return; }
        if (values is null || values.Count == 0) return;
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
        foreach (var quote in values.Values)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT OR IGNORE INTO market_quotes(slug, payload_json, updated_at) VALUES ($slug, $payload, $updated);";
            insert.Parameters.AddWithValue("$slug", quote.Slug);
            insert.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(quote, _json));
            insert.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
            await insert.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();
    }

    private async Task ImportLegacySingletonAsync<T>(SqliteConnection connection, string table, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        await using var count = connection.CreateCommand();
        count.CommandText = $"SELECT COUNT(*) FROM {table} WHERE id = 1;";
        if (Convert.ToInt32(await count.ExecuteScalarAsync()) != 0) return;
        T? value;
        try { value = JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(path), _json); }
        catch (Exception error) when (error is IOException or JsonException) { return; }
        if (value is null) return;
        await using var insert = connection.CreateCommand();
        insert.CommandText = $"INSERT INTO {table}(id, payload_json, updated_at) VALUES (1, $payload, $updated);";
        insert.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(value, _json));
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
