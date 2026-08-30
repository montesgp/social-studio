using Incoders.Hive.Domain.Social.Clients;

namespace Incoders.Hive.Tests.Domain.Social.Clients;

/// <summary>
/// Per-client cap on how many pieces (format results) a single generation job may build. Exists so
/// an operator can run a cheap 2-piece test instead of always paying for the full format set, whose
/// visual formats each drive per-slide AI image renders.
/// Mirrors the <c>MaxPostsToAnalyze</c> override pattern.
/// </summary>
public sealed class ClientMaxPiecesPerGenerationTests
{
    private static readonly DateTime Now = new(2026, 8, 6, 12, 0, 0, DateTimeKind.Utc);

    private static Client NewClient()
    {
        var created = Client.Create(ClientId.New(), Guid.NewGuid(), "Acme", null, Now);
        Assert.True(created.IsSuccess);
        return created.Value;
    }

    [Fact]
    public void DefaultsToNull_MeaningNoCap()
    {
        var client = NewClient();

        Assert.Null(client.MaxPiecesPerGeneration);
    }

    [Fact]
    public void SetMaxPiecesPerGeneration_AcceptsAValueInRange()
    {
        var client = NewClient();

        var result = client.SetMaxPiecesPerGeneration(2, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, client.MaxPiecesPerGeneration);
    }

    [Fact]
    public void SetMaxPiecesPerGeneration_NullClearsTheCap()
    {
        var client = NewClient();
        Assert.True(client.SetMaxPiecesPerGeneration(2, Now).IsSuccess);

        var result = client.SetMaxPiecesPerGeneration(null, Now);

        Assert.True(result.IsSuccess);
        Assert.Null(client.MaxPiecesPerGeneration);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(Client.MaxPiecesPerGenerationMax + 1)]
    public void SetMaxPiecesPerGeneration_RejectsOutOfRange(int value)
    {
        var client = NewClient();

        var result = client.SetMaxPiecesPerGeneration(value, Now);

        Assert.True(result.IsFailure);
        Assert.Equal(ClientErrors.MaxPiecesPerGenerationOutOfRange, result.Error);
    }

    [Fact]
    public void SetMaxPiecesPerGeneration_TouchesUpdatedAt()
    {
        var client = NewClient();
        var later = Now.AddMinutes(5);

        Assert.True(client.SetMaxPiecesPerGeneration(3, later).IsSuccess);

        Assert.Equal(later, client.UpdatedAtUtc);
    }

    [Fact]
    public void ApplyPieceCap_WithNoCap_KeepsEveryRequestedFormat()
    {
        var client = NewClient();

        var capped = client.ApplyPieceCap(["reel-script", "feed-single", "feed-carousel"]);

        Assert.Equal(["reel-script", "feed-single", "feed-carousel"], capped);
    }

    [Fact]
    public void ApplyPieceCap_TrimsToTheCap_PreservingRequestOrder()
    {
        // Order is the caller's priority order, so the cheap formats an operator puts first are the
        // ones that survive a test-sized cap.
        var client = NewClient();
        Assert.True(client.SetMaxPiecesPerGeneration(2, Now).IsSuccess);

        var capped = client.ApplyPieceCap(["reel-script", "feed-single", "feed-carousel", "story-single"]);

        Assert.Equal(["reel-script", "feed-single"], capped);
    }

    [Fact]
    public void ApplyPieceCap_WithFewerRequestedThanTheCap_IsANoOp()
    {
        var client = NewClient();
        Assert.True(client.SetMaxPiecesPerGeneration(5, Now).IsSuccess);

        var capped = client.ApplyPieceCap(["reel-script"]);

        Assert.Equal(["reel-script"], capped);
    }

    [Fact]
    public void ApplyPieceCap_WithNoRequestedFormats_ReturnsEmpty()
    {
        var client = NewClient();
        Assert.True(client.SetMaxPiecesPerGeneration(2, Now).IsSuccess);

        Assert.Empty(client.ApplyPieceCap([]));
    }
}
