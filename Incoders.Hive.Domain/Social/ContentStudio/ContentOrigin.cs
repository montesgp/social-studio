namespace Incoders.Hive.Domain.Social.ContentStudio;

/// <summary>
/// Source-agnostic, soft reference from a <see cref="ContentItem"/> back to the generator output
/// it was derived from. This is the ONLY link between ContentStudio and a generator — all values
/// are primitives/Guids, so ContentStudio depends on no generator domain type and a new
/// <see cref="GeneratorKind"/> integrates without changing the aggregates.
/// </summary>
public sealed record ContentOrigin(
    GeneratorKind GeneratorKind,
    Guid SourceRunId,
    Guid? SourcePackageId,
    Guid FormatResultId,
    string FormatKey);
