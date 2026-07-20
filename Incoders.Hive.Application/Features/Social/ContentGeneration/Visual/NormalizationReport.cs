namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Visual;

/// <summary>
/// Outcome of a <see cref="TypographyNormalizer.Normalize"/> pass.
/// </summary>
/// <param name="RewrittenCount">Number of <c>font-family</c> declarations rewritten.</param>
/// <param name="RewrittenFamilies">
/// The original generic-only stack value for each declaration that was rewritten, in encounter
/// order — useful for observability without re-parsing the markup diff.
/// </param>
public sealed record NormalizationReport(int RewrittenCount, IReadOnlyList<string> RewrittenFamilies)
{
    public static readonly NormalizationReport Empty = new(0, Array.Empty<string>());
}
