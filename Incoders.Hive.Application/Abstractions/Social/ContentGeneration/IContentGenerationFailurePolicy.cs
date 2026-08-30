namespace Incoders.Hive.Application.Abstractions.Social.ContentGeneration;

/// <summary>
/// Read-side policy port for cost-control decisions around content-generation retries.
/// Application uses this to avoid blindly re-spending AI credits when the same
/// analysis/client/format has already failed repeatedly.
/// </summary>
public interface IContentGenerationFailurePolicy
{
    /// <summary>
    /// Counts terminal failed attempts per requested format for one analysis/client scope.
    /// Implementations count persisted per-format failures across jobs, not only the current job.
    /// </summary>
    Task<IReadOnlyDictionary<string, int>> CountFailedAttemptsByFormatAsync(
        Guid organizationId,
        Guid analysisId,
        Guid clientId,
        IReadOnlyCollection<string> formatKeys,
        CancellationToken cancellationToken);
}
