using SocialStudio.Domain.Common;

namespace SocialStudio.Application.Abstractions.ContentAnalysis;

/// <summary>
/// Pre-flight gate for the content-analysis workflow. Verifies that EVERY external API the flow
/// needs (extraction provider + AI provider) is reachable BEFORE any expensive work starts.
/// If a required provider is down, the workflow must NOT begin — it fails fast with a clear error.
/// </summary>
public interface IContentAnalysisPreflight
{
    /// <summary>
    /// Probes the required providers for the organization. Returns success only when all are healthy;
    /// otherwise a failure whose error identifies which capability (extraction/AI) is unavailable.
    /// </summary>
    Task<Result> EnsureReadyAsync(
        Guid organizationId,
        string extractionProviderKey,
        string aiProviderKey,
        CancellationToken cancellationToken = default);
}
