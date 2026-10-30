using SocialStudio.Domain.Social.ContentFormats;
using SocialStudio.Domain.Social.ContentGeneration.Batches;

namespace SocialStudio.Tests.Domain.Social.ContentGeneration.Batches;

/// <summary>
/// RED tests for <see cref="BatchComposition"/> — the "what is in the cart" value object.
///
/// The composition is the ONLY place that knows a batch of N pieces exists. Everything downstream
/// still runs one piece per job, because the existing pipeline keys a format result by
/// (JobId, FormatKey) and cannot hold two carousels in one job.
/// </summary>
public sealed class BatchCompositionTests
{
    private const int MaxPieces = 20;

    private static string AnyDeliverableKey =>
        FormatCatalog.All().First(d => !d.IsAuxiliary).FormatKey;

    private static string OtherDeliverableKey =>
        FormatCatalog.All().Where(d => !d.IsAuxiliary).Skip(1).First().FormatKey;

    [Fact]
    public void Create_KeepsRequestedCountPerFormat()
    {
        var result = BatchComposition.Create(
            [new CompositionEntry(AnyDeliverableKey, 2), new CompositionEntry(OtherDeliverableKey, 4)],
            MaxPieces);

        Assert.True(result.IsSuccess);
        Assert.Equal(6, result.Value.TotalPieces);
    }

    [Fact]
    public void Expand_YieldsOneFormatKeyPerPiece()
    {
        var composition = BatchComposition.Create(
            [new CompositionEntry(AnyDeliverableKey, 2), new CompositionEntry(OtherDeliverableKey, 1)],
            MaxPieces).Value;

        var pieces = composition.Expand();

        // One entry per piece is the whole point: each becomes its own job downstream.
        Assert.Equal(3, pieces.Count);
        Assert.Equal(2, pieces.Count(k => k == AnyDeliverableKey));
        Assert.Equal(1, pieces.Count(k => k == OtherDeliverableKey));
    }

    [Fact]
    public void Expand_IsDeterministic()
    {
        var entries = new[]
        {
            new CompositionEntry(AnyDeliverableKey, 2),
            new CompositionEntry(OtherDeliverableKey, 3)
        };

        var first = BatchComposition.Create(entries, MaxPieces).Value.Expand();
        var second = BatchComposition.Create(entries, MaxPieces).Value.Expand();

        Assert.Equal(first, second);
    }

    [Fact]
    public void Create_RejectsEmptyCart()
    {
        var result = BatchComposition.Create([], MaxPieces);

        Assert.True(result.IsFailure);
        Assert.Equal(BatchErrors.EmptyComposition.Code, result.Error.Code);
    }

    [Fact]
    public void Create_RejectsNonPositiveCount()
    {
        var result = BatchComposition.Create([new CompositionEntry(AnyDeliverableKey, 0)], MaxPieces);

        Assert.True(result.IsFailure);
        Assert.Equal(BatchErrors.NonPositiveCount.Code, result.Error.Code);
    }

    [Fact]
    public void Create_RejectsUnknownFormatKey()
    {
        var result = BatchComposition.Create([new CompositionEntry("not-a-format", 1)], MaxPieces);

        Assert.True(result.IsFailure);
        Assert.Equal(BatchErrors.UnknownFormat("not-a-format").Code, result.Error.Code);
    }

    [Fact]
    public void Create_RejectsLegacyCarouselAlias()
    {
        // "carousel" is deliberately rejected on write paths; the cart must emit surface-scoped keys.
        var result = BatchComposition.Create([new CompositionEntry("carousel", 1)], MaxPieces);

        Assert.True(result.IsFailure);
        Assert.Equal(BatchErrors.UnknownFormat("carousel").Code, result.Error.Code);
    }

    [Fact]
    public void Create_RejectsAuxiliaryFormat()
    {
        var auxiliary = FormatCatalog.All().First(d => d.IsAuxiliary).FormatKey;

        var result = BatchComposition.Create([new CompositionEntry(auxiliary, 1)], MaxPieces);

        Assert.True(result.IsFailure);
        Assert.Equal(BatchErrors.NotDeliverable(auxiliary).Code, result.Error.Code);
    }

    [Fact]
    public void Create_MergesDuplicateFormatEntries()
    {
        // Dragging the same tile twice must add up, never silently collapse to one piece —
        // that collapse is exactly the bug the existing single-job path has.
        var result = BatchComposition.Create(
            [new CompositionEntry(AnyDeliverableKey, 2), new CompositionEntry(AnyDeliverableKey, 3)],
            MaxPieces);

        Assert.True(result.IsSuccess);
        Assert.Equal(5, result.Value.TotalPieces);
        Assert.Single(result.Value.Entries);
    }

    [Fact]
    public void Create_RejectsBatchOverTheConfiguredCap()
    {
        var result = BatchComposition.Create([new CompositionEntry(AnyDeliverableKey, MaxPieces + 1)], MaxPieces);

        Assert.True(result.IsFailure);
        Assert.Equal(BatchErrors.TooManyPieces(MaxPieces).Code, result.Error.Code);
    }

    [Fact]
    public void Create_AcceptsExactlyTheCap()
    {
        var result = BatchComposition.Create([new CompositionEntry(AnyDeliverableKey, MaxPieces)], MaxPieces);

        Assert.True(result.IsSuccess);
        Assert.Equal(MaxPieces, result.Value.TotalPieces);
    }
}
