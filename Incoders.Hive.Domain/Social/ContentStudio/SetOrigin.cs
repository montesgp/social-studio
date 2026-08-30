namespace Incoders.Hive.Domain.Social.ContentStudio;

/// <summary>
/// Soft origin of a <see cref="ContentSet"/>. For a <see cref="SetKind.GenerationRun"/> set the
/// generator kind and source run id are set; later set kinds use the campaign id or period.
/// </summary>
public sealed record SetOrigin(
    GeneratorKind? GeneratorKind,
    Guid? SourceRunId,
    Guid? CampaignId,
    PeriodRef? Period)
{
    /// <summary>Origin for a set built from a single generation run.</summary>
    public static SetOrigin ForRun(GeneratorKind generatorKind, Guid sourceRunId) =>
        new(generatorKind, sourceRunId, null, null);
}
