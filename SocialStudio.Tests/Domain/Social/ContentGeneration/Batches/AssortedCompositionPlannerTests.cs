using SocialStudio.Domain.Social.ContentFormats;
using SocialStudio.Domain.Social.ContentGeneration.Batches;

namespace SocialStudio.Tests.Domain.Social.ContentGeneration.Batches;

/// <summary>
/// RED tests for <see cref="AssortedCompositionPlanner"/> — the "surprise me x N" tile.
///
/// The operator asks for a total and the system decides the mix. That mix must be shown back before
/// anything is generated: a batch that silently decides how to spend N AI runs is a black box the
/// operator cannot budget for. Hence: deterministic, inspectable, and derived from the catalog rather
/// than a hardcoded format list.
/// </summary>
public sealed class AssortedCompositionPlannerTests
{
    private const int MaxPieces = 20;

    private static IReadOnlyList<string> Deliverables =>
        FormatCatalog.All().Where(d => !d.IsAuxiliary).Select(d => d.FormatKey).ToList();

    [Fact]
    public void Plan_SpreadsEvenlyWhenTotalDividesExactly()
    {
        var formatCount = Deliverables.Count;
        var total = formatCount * 2;

        var composition = AssortedCompositionPlanner.Plan(total, Deliverables, MaxPieces).Value;

        Assert.Equal(total, composition.TotalPieces);
        Assert.All(composition.Entries, entry => Assert.Equal(2, entry.Count));
    }

    [Fact]
    public void Plan_DistributesTheRemainderOneByOne()
    {
        var formatCount = Deliverables.Count;
        var total = formatCount * 2 + 2; // two formats get one extra

        var composition = AssortedCompositionPlanner.Plan(total, Deliverables, MaxPieces).Value;

        Assert.Equal(total, composition.TotalPieces);
        Assert.Equal(2, composition.Entries.Count(e => e.Count == 3));
        Assert.Equal(formatCount - 2, composition.Entries.Count(e => e.Count == 2));
    }

    [Fact]
    public void Plan_NeverSpreadsWiderThanOnePiece()
    {
        // "Equitativo" means no format may end up two ahead of another.
        for (var total = 1; total <= MaxPieces; total++)
        {
            var composition = AssortedCompositionPlanner.Plan(total, Deliverables, MaxPieces).Value;
            var counts = composition.Entries.Select(e => e.Count).ToList();

            Assert.Equal(total, composition.TotalPieces);
            Assert.True(
                counts.Max() - counts.Min() <= 1,
                $"total={total} produced an uneven spread: {string.Join(",", counts)}");
        }
    }

    [Fact]
    public void Plan_OmitsFormatsThatWouldGetZeroPieces()
    {
        var composition = AssortedCompositionPlanner.Plan(2, Deliverables, MaxPieces).Value;

        Assert.Equal(2, composition.Entries.Count);
        Assert.All(composition.Entries, entry => Assert.True(entry.Count > 0));
    }

    [Fact]
    public void Plan_IsDeterministic()
    {
        var first = AssortedCompositionPlanner.Plan(7, Deliverables, MaxPieces).Value.Expand();
        var second = AssortedCompositionPlanner.Plan(7, Deliverables, MaxPieces).Value.Expand();

        Assert.Equal(first, second);
    }

    [Fact]
    public void Plan_HonoursARestrictedFormatPool()
    {
        // The pool is a parameter, not a constant, so a future "only feed formats" or client-strategy
        // rule needs no change here.
        var pool = Deliverables.Take(2).ToList();

        var composition = AssortedCompositionPlanner.Plan(4, pool, MaxPieces).Value;

        Assert.Equal(4, composition.TotalPieces);
        Assert.All(composition.Entries, entry => Assert.Contains(entry.FormatKey, pool));
    }

    [Fact]
    public void Plan_RejectsNonPositiveTotal()
    {
        var result = AssortedCompositionPlanner.Plan(0, Deliverables, MaxPieces);

        Assert.True(result.IsFailure);
        Assert.Equal(BatchErrors.NonPositiveCount.Code, result.Error.Code);
    }

    [Fact]
    public void Plan_RejectsTotalOverTheConfiguredCap()
    {
        var result = AssortedCompositionPlanner.Plan(MaxPieces + 1, Deliverables, MaxPieces);

        Assert.True(result.IsFailure);
        Assert.Equal(BatchErrors.TooManyPieces(MaxPieces).Code, result.Error.Code);
    }

    [Fact]
    public void Plan_RejectsEmptyFormatPool()
    {
        var result = AssortedCompositionPlanner.Plan(4, [], MaxPieces);

        Assert.True(result.IsFailure);
        Assert.Equal(BatchErrors.EmptyComposition.Code, result.Error.Code);
    }
}
