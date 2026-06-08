using Incoders.Hive.Application.Features.Social.ContentGeneration.Pipeline;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Pipeline;

/// <summary>
/// Guards the <c>composition_spec_json</c> serialization contract: a single-asset format stores ONE
/// envelope (legacy object shape), a carousel stores one envelope PER slide (array shape), and the
/// parser round-trips both — including rows written before multi-slide support existed.
/// </summary>
public sealed class CompositionSpecJsonTests
{
    private static CompositionSpecEnvelope Envelope(string markup) => new(
        SchemaVersion: CompositionSpecEnvelope.CurrentSchemaVersion,
        Markup: markup,
        Canvas: new CanvasDimensions(1080, 1920, 96),
        SlotGeometry: [],
        BackgroundImageRef: null);

    [Fact]
    public void Serialize_SingleEnvelope_RoundTripsAsOne()
    {
        var json = CompositionSpecJson.Serialize([Envelope("<div>one</div>")]);

        var parsed = CompositionSpecJson.ParseList(json);

        Assert.Single(parsed);
        Assert.Equal("<div>one</div>", parsed[0].Markup);
    }

    [Fact]
    public void Serialize_ManyEnvelopes_RoundTripsAsListInOrder()
    {
        var json = CompositionSpecJson.Serialize(
            [Envelope("<div>1</div>"), Envelope("<div>2</div>"), Envelope("<div>3</div>")]);

        var parsed = CompositionSpecJson.ParseList(json);

        Assert.Equal(3, parsed.Count);
        Assert.Equal("<div>1</div>", parsed[0].Markup);
        Assert.Equal("<div>3</div>", parsed[2].Markup);
    }

    [Fact]
    public void ParseList_LegacySingleObjectShape_ReturnsOneEnvelope()
    {
        // A row written before multi-slide support: a single JSON object (not an array).
        const string legacy =
            "{\"schemaVersion\":1,\"markup\":\"<div>legacy</div>\"," +
            "\"canvas\":{\"width\":1080,\"height\":1920,\"dpi\":96},\"slotGeometry\":[]}";

        var parsed = CompositionSpecJson.ParseList(legacy);

        Assert.Single(parsed);
        Assert.Equal("<div>legacy</div>", parsed[0].Markup);
    }

    [Fact]
    public void Serialize_Empty_ReturnsNull()
    {
        Assert.Null(CompositionSpecJson.Serialize([]));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    public void ParseList_NullBlankOrInvalid_ReturnsEmpty(string? json)
    {
        Assert.Empty(CompositionSpecJson.ParseList(json));
    }
}
