using Incoders.Hive.Application.Abstractions.ContentAnalysis;
using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Application.Features.ContentAnalysis;

/// <summary>
/// Resolves a registered <see cref="IContentAnalysisProfile"/> by flow key.
/// All registered profiles are injected via DI as <c>IEnumerable&lt;IContentAnalysisProfile&gt;</c>.
/// </summary>
public sealed class ContentAnalysisProfileProvider : IContentAnalysisProfileProvider
{
    private readonly IReadOnlyDictionary<string, IContentAnalysisProfile> _profiles;

    public ContentAnalysisProfileProvider(IEnumerable<IContentAnalysisProfile> profiles)
    {
        _profiles = profiles.ToDictionary(
            p => p.FlowKey,
            p => p,
            StringComparer.OrdinalIgnoreCase);
    }

    public Result<IContentAnalysisProfile> ForFlow(string flowKey)
    {
        if (_profiles.TryGetValue(flowKey, out var profile))
        {
            return Result.Success(profile);
        }

        return Result.Failure<IContentAnalysisProfile>(ContentAnalysisErrors.ProfileNotFound);
    }
}

internal static class ContentAnalysisErrors
{
    public static readonly Error ProfileNotFound = Error.NotFound(
        "ContentAnalysis.ProfileNotFound",
        "content_analysis.profile_not_found");
}
