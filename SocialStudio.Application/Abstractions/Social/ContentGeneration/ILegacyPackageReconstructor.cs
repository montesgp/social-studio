namespace SocialStudio.Application.Abstractions.Social.ContentGeneration;

/// <summary>
/// One-time, idempotent reconstruction of legacy single-analyzer packages to the
/// current format taxonomy (story → story-single, feed-post → feed-single,
/// carousel → feed-carousel; flyer was previously folded into story).
///
/// "Integral" reconstruction: renames the stored objects in the bucket AND updates
/// the DB rows (format_results.format_key, format_assets.storage_path / filename)
/// so storage and DB stay consistent. Safe to run repeatedly — already-canonical
/// rows are skipped.
/// </summary>
public interface ILegacyPackageReconstructor
{
    Task<LegacyPackageReconstructionReport> ReconstructAsync(CancellationToken ct);
}

/// <summary>Outcome of a reconstruction pass, for logging/observability.</summary>
public sealed record LegacyPackageReconstructionReport(
    int ResultsReKeyed,
    int AssetsRenamed,
    int StaleResultsDeleted)
{
    /// <summary>True when nothing needed reconstruction (everything already canonical).</summary>
    public bool NoOp => ResultsReKeyed == 0 && AssetsRenamed == 0 && StaleResultsDeleted == 0;
}
