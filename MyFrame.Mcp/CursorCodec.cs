using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MyFrame.Mcp;

public sealed class CursorCodec
{
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
    private readonly JsonSerializerOptions _json;

    public CursorCodec(JsonSerializerOptions json) => _json = json;

    public string Encode(string tool, string snapshotId, string query, int offset)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new CursorPayload(tool, snapshotId, query, offset), _json);
        using var hmac = new HMACSHA256(_key);
        var signature = hmac.ComputeHash(payload);
        return Base64Url(payload) + "." + Base64Url(signature);
    }

    public CursorPayload Decode(string cursor, string tool, string query)
    {
        try
        {
            var parts = cursor.Split('.');
            if (parts.Length != 2) throw new FormatException();
            var payload = FromBase64Url(parts[0]);
            var actual = FromBase64Url(parts[1]);
            if (Base64Url(payload) != parts[0] || Base64Url(actual) != parts[1]) throw new FormatException();
            using var hmac = new HMACSHA256(_key);
            if (!CryptographicOperations.FixedTimeEquals(hmac.ComputeHash(payload), actual)) throw new FormatException();
            var decoded = JsonSerializer.Deserialize<CursorPayload>(payload, _json) ?? throw new FormatException();
            if (decoded.Tool != tool || decoded.Query != query || decoded.Offset < 0) throw new FormatException();
            return decoded;
        }
        catch (Exception error) when (error is FormatException or JsonException)
        {
            throw new QueryProblemException("INVALID_CURSOR",
                "The cursor is invalid for this query. Restart from the first page.");
        }
    }

    private static string Base64Url(byte[] value) => Convert.ToBase64String(value)
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        return Convert.FromBase64String(padded);
    }
}

public sealed record CursorPayload(string Tool, string SnapshotId, string Query, int Offset);

public sealed class QueryProblemException(string code, string message, bool retryable = false)
    : Exception(message)
{
    public string Code { get; } = code;
    public bool Retryable { get; } = retryable;
}
