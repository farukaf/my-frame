using System.Text.Json;
using MyFrame.Core.Sync;

namespace MyFrame.Core.Tests;

public sealed class SyncDiagnosticsTests
{
    [Fact]
    public void SerializationContainsOnlySanitizedOperationalFields()
    {
        var json = SyncDiagnosticsSerializer.Serialize(
            [new SyncDiagnosticSource("public-export", "published", "Accepted 3", "rev-1", "today", "parser-1", "Coverage: components=Known")],
            [new SyncDiagnosticAttempt("public-export", "published", "today", "Accepted 3")],
            DateTimeOffset.Parse("2026-09-14T12:00:00Z"));

        using var document = JsonDocument.Parse(json);
        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Contains("public-export", json, StringComparison.Ordinal);
        Assert.DoesNotContain("payload", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("C:\\Users", json, StringComparison.OrdinalIgnoreCase);
    }
}
