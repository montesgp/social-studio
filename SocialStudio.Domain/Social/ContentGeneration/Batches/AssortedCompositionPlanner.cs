using SocialStudio.Domain.Common;

namespace SocialStudio.Domain.Social.ContentGeneration.Batches;

/// <summary>
/// The "surprise me x N" tile: the operator names a total and the system decides the mix.
///
/// Equitable means no format ends up more than one piece ahead of another. The remainder is handed
/// out in pool order rather than at random, so the same request always produces the same plan — the
/// mix is shown back before generation starts, and a plan the operator cannot predict or reproduce is
/// not a plan they can budget for.
///
/// The eligible pool is a PARAMETER, not a constant. Restricting a batch to one surface, or letting a
/// client's strategy weight the pool, needs a different caller — not a change here.
/// </summary>
public static class AssortedCompositionPlanner
{
    public static Result<BatchComposition> Plan(
        int totalPieces,
        IReadOnlyList<string> eligibleFormatKeys,
        int maxPieces)
    {
        ArgumentNullException.ThrowIfNull(eligibleFormatKeys);

        if (totalPieces <= 0)
        {
            return Result.Failure<BatchComposition>(BatchErrors.NonPositiveCount);
        }

        if (totalPieces > maxPieces)
        {
            return Result.Failure<BatchComposition>(BatchErrors.TooManyPieces(maxPieces));
        }

        if (eligibleFormatKeys.Count == 0)
        {
            return Result.Failure<BatchComposition>(BatchErrors.EmptyComposition);
        }

        var poolSize = eligibleFormatKeys.Count;
        var baseShare = totalPieces / poolSize;
        var remainder = totalPieces % poolSize;

        var entries = new List<CompositionEntry>(poolSize);

        for (var index = 0; index < poolSize; index++)
        {
            // The first `remainder` formats in pool order absorb the leftover pieces.
            var count = baseShare + (index < remainder ? 1 : 0);

            // A total smaller than the pool leaves formats at zero; they are simply not in the cart.
            if (count == 0)
            {
                continue;
            }

            entries.Add(new CompositionEntry(eligibleFormatKeys[index], count));
        }

        // Routed through Create so the assorted path inherits the exact same validation as a
        // hand-built cart — unknown keys, auxiliaries and the cap are checked in one place only.
        return BatchComposition.Create(entries, maxPieces);
    }
}
