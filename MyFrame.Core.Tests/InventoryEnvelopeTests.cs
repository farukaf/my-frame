using MyFrame.Core;

namespace MyFrame.Core.Tests;

public sealed class InventoryEnvelopeTests
{
    [Fact]
    public void ParsesEnvelopeAndHashesOnlyPayload()
    {
        var session = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var envelope = $"{{\"schemaVersion\":1,\"gameId\":8954,\"source\":\"overwolf-native\",\"kind\":\"inventory\",\"sessionId\":\"{session:D}\",\"eventId\":\"{eventId:D}\",\"sequence\":4,\"receivedAt\":\"2026-09-13T12:00:00Z\",\"providerVersion\":\"native-1\",\"encoding\":\"json-object\",\"completeness\":\"unverified\",\"payload\":\"{{\\\"equipment\\\":[]}}\"}}";
        var result = InventoryEnvelopeParser.Parse(envelope);
        Assert.Equal(session, result.SessionId);
        Assert.Equal("unverified", result.Completeness);
        Assert.Equal(64, result.ContentHash.Length);
    }

    [Fact]
    public void PreservesInstancesUnknownsAndCoverage()
    {
        var projection = InventoryPayloadParser.Parse("{\"equipment\":[{\"instanceId\":\"abc\",\"uniqueName\":\"/Lotus/Test\",\"rank\":30},{\"uniqueName\":\"/Lotus/Unknown\"}],\"stackables\":[{\"typeId\":\"/Lotus/Resource\",\"quantity\":2}],\"futureField\":true}");
        Assert.Equal(2, projection.Equipment.Count);
        Assert.Equal("abc", projection.Equipment[0].InstanceId);
        Assert.Equal(2, projection.Stackables[0].Quantity);
        Assert.Contains(projection.Unknown, item => item.Kind == "futureField");
        Assert.Equal(InventoryFieldState.Known, projection.Coverage["equipment"]);
    }

    [Fact]
    public void MissingArraysAreNotEmptyClaims()
    {
        var projection = InventoryPayloadParser.Parse("{\"other\":1}");
        Assert.Empty(projection.Equipment);
        Assert.Equal(InventoryFieldState.NotObserved, projection.Coverage["equipment"]);
        Assert.Equal(InventoryFieldState.NotObserved, projection.Coverage["stackables"]);
    }

    [Fact]
    public void RejectsUnsupportedGameAndSequence()
    {
        var session = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var envelope = $"{{\"schemaVersion\":1,\"gameId\":1,\"source\":\"overwolf-native\",\"kind\":\"inventory\",\"sessionId\":\"{session:D}\",\"eventId\":\"{eventId:D}\",\"sequence\":0,\"receivedAt\":\"2026-09-13T12:00:00Z\",\"encoding\":\"json-object\",\"payload\":{{}}}}";
        var error = Assert.Throws<InvalidDataException>(() => InventoryEnvelopeParser.Parse(envelope));
        Assert.Equal("INVENTORY_ENVELOPE_UNSUPPORTED", error.Message);
    }
}
