using System.Text.Json;
using MyFrame.Core;

namespace MyFrame.Core.Tests;

public sealed class CollectorCaptureStatusProbeTests
{
    [Fact]
    public async Task MissingDirectoryIsReportedWithoutCreatingIt()
    {
        var path = Path.Combine(Path.GetTempPath(), "my-frame-probe-" + Guid.NewGuid().ToString("N"));
        var result = await CollectorCaptureStatusProbe.ReadAsync(path);

        Assert.Equal("missing", result.State);
        Assert.False(result.DirectoryExists);
        Assert.IsType<bool>(result.OverwolfRunning);
        Assert.IsType<bool>(result.WarframeRunning);
        Assert.False(Directory.Exists(path));
    }

    [Fact]
    public async Task ReportsHeartbeatAndInvalidMarkerWithoutPayloadDetails()
    {
        using var folder = new TemporaryFolder();
        await File.WriteAllTextAsync(Path.Combine(folder.Path, "collector-status.json"), JsonSerializer.Serialize(new
        {
            schemaVersion = 1, kind = "my-frame-collector", state = "started",
            timestampUtc = DateTimeOffset.UtcNow
        }));
        await File.WriteAllTextAsync(Path.Combine(folder.Path, "bad.ready.json"), "{}");

        var result = await CollectorCaptureStatusProbe.ReadAsync(folder.Path);

        Assert.Equal("heartbeat-only", result.State);
        Assert.Equal("started", result.HeartbeatState);
        Assert.True(result.HeartbeatFresh);
        Assert.Equal(1, result.ReadyMarkers);
        Assert.Equal(0, result.ValidMarkers);
        Assert.Equal(1, result.InvalidMarkers);
        Assert.Contains("CAPTURE_FORMAT_INVALID", result.InvalidByCode.Keys);
    }

    [Fact]
    public async Task DoesNotTreatExpiredHeartbeatAsActive()
    {
        using var folder = new TemporaryFolder();
        await File.WriteAllTextAsync(Path.Combine(folder.Path, "collector-status.json"), JsonSerializer.Serialize(new
        {
            schemaVersion = 1, kind = "my-frame-collector", state = "started",
            timestampUtc = DateTimeOffset.UtcNow.AddHours(-2)
        }));

        var result = await CollectorCaptureStatusProbe.ReadAsync(folder.Path);

        Assert.Equal("idle", result.State);
        Assert.False(result.HeartbeatFresh);
    }

    private sealed class TemporaryFolder : IDisposable
    {
        public TemporaryFolder()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "my-frame-probe-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }
        public string Path { get; }
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
    }
}
