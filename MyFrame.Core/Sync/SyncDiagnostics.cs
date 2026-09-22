using System.Text.Json;

namespace MyFrame.Core.Sync;

public sealed record SyncDiagnosticSource(string SourceId, string State, string Detail,
    string Revision, string LastRun, string ParserVersion, string Coverage);
public sealed record SyncDiagnosticAttempt(string SourceId, string State, string StartedAt, string Detail);

public static class SyncDiagnosticsSerializer
{
    public static string Serialize(IReadOnlyList<SyncDiagnosticSource> sources,
        IReadOnlyList<SyncDiagnosticAttempt> attempts, DateTimeOffset generatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(attempts);
        var payload = new
        {
            schemaVersion = 1,
            generatedAtUtc,
            sources,
            attempts
        };
        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }
}
