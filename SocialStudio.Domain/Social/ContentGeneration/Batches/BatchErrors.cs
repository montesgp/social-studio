using SocialStudio.Domain.Common;

namespace SocialStudio.Domain.Social.ContentGeneration.Batches;

/// <summary>
/// Stable error codes for batch composition. Kept beside the value object rather than in Application
/// because the rules they name are domain rules, not transport concerns.
/// </summary>
public static class BatchErrors
{
    public static readonly Error EmptyComposition = Error.Validation(
        "batch.composition.empty",
        "batch.composition.empty");

    public static readonly Error NonPositiveCount = Error.Validation(
        "batch.composition.non_positive_count",
        "batch.composition.non_positive_count");

    public static Error UnknownFormat(string formatKey) => Error.Validation(
        "batch.composition.unknown_format",
        $"batch.composition.unknown_format:{formatKey}");

    public static Error NotDeliverable(string formatKey) => Error.Validation(
        "batch.composition.not_deliverable",
        $"batch.composition.not_deliverable:{formatKey}");

    public static Error TooManyPieces(int maxPieces) => Error.Validation(
        "batch.composition.too_many_pieces",
        $"batch.composition.too_many_pieces:{maxPieces}");
}
