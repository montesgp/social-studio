using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ContentFormats;

namespace Incoders.Hive.Domain.Social.ContentGeneration.Batches;

/// <summary>One requested format and how many pieces of it the batch should produce.</summary>
public sealed record CompositionEntry(string FormatKey, int Count);

/// <summary>
/// What the operator put in the cart: a validated set of formats with a piece count each.
///
/// WHY THIS EXISTS AS ITS OWN CONCEPT: the generation pipeline below is, by construction, one result
/// per format per job — the format result row is upserted on (JobId, FormatKey) and the job status
/// projection indexes results into a dictionary keyed by format. Two carousels cannot coexist in one
/// job without breaking both. So "quantity" cannot be a field on the job; it lives here, one layer up,
/// and <see cref="Expand"/> turns it into one job per piece. The proven pipeline stays untouched.
///
/// The cap is a parameter rather than a constant so it can move with configuration (and, later, with
/// what a given plan pays for) without editing this type.
/// </summary>
public sealed class BatchComposition
{
    private BatchComposition(IReadOnlyList<CompositionEntry> entries)
    {
        Entries = entries;
    }

    /// <summary>Requested formats with their counts, in catalog declaration order.</summary>
    public IReadOnlyList<CompositionEntry> Entries { get; }

    public int TotalPieces => Entries.Sum(entry => entry.Count);

    /// <summary>
    /// Validates a requested cart. Duplicate entries for the same format are SUMMED, never collapsed:
    /// dragging the same tile twice must mean two pieces.
    /// </summary>
    public static Result<BatchComposition> Create(
        IReadOnlyList<CompositionEntry> requested,
        int maxPieces)
    {
        ArgumentNullException.ThrowIfNull(requested);

        if (requested.Count == 0)
        {
            return Result.Failure<BatchComposition>(BatchErrors.EmptyComposition);
        }

        var merged = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in requested)
        {
            var formatKey = entry.FormatKey?.Trim().ToLowerInvariant() ?? string.Empty;

            if (entry.Count <= 0)
            {
                return Result.Failure<BatchComposition>(BatchErrors.NonPositiveCount);
            }

            // TryGet is canonical-only, so legacy aliases such as "carousel" are rejected here exactly
            // as they are on the existing write path. The cart must send surface-scoped keys.
            var definition = FormatCatalog.TryGet(formatKey);

            if (definition is null)
            {
                return Result.Failure<BatchComposition>(BatchErrors.UnknownFormat(formatKey));
            }

            // Auxiliaries (content-idea, caption, hook, …) are ingredients of a piece, not a piece the
            // operator can order by the unit.
            if (definition.IsAuxiliary)
            {
                return Result.Failure<BatchComposition>(BatchErrors.NotDeliverable(formatKey));
            }

            merged[definition.FormatKey] = merged.GetValueOrDefault(definition.FormatKey) + entry.Count;
        }

        var total = merged.Values.Sum();

        if (total > maxPieces)
        {
            return Result.Failure<BatchComposition>(BatchErrors.TooManyPieces(maxPieces));
        }

        // Catalog order, not insertion order, so the same cart always yields the same plan regardless
        // of the sequence the operator dragged the tiles in.
        var ordered = FormatCatalog.All()
            .Where(definition => merged.ContainsKey(definition.FormatKey))
            .Select(definition => new CompositionEntry(definition.FormatKey, merged[definition.FormatKey]))
            .ToList();

        return Result.Success(new BatchComposition(ordered));
    }

    /// <summary>
    /// Flattens the composition into one format key per piece — each becomes its own generation job.
    /// Interleaved rather than grouped, so a batch that fails halfway still leaves the operator with a
    /// spread of formats in the bank instead of four carousels and nothing else.
    /// </summary>
    public IReadOnlyList<string> Expand()
    {
        var pieces = new List<string>(TotalPieces);
        var remaining = Entries.Select(entry => entry.Count).ToArray();
        var exhausted = false;

        while (!exhausted)
        {
            exhausted = true;

            for (var index = 0; index < Entries.Count; index++)
            {
                if (remaining[index] <= 0)
                {
                    continue;
                }

                pieces.Add(Entries[index].FormatKey);
                remaining[index]--;
                exhausted = false;
            }
        }

        return pieces;
    }
}
