using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;

namespace Incoders.Hive.Application.Features.Social.ClientAestheticProfileAdmin.GetProfile;

/// <summary>
/// client-analysis-data admin panel: full read model for the "what was captured" view.
/// <see cref="Profile"/> is the complete decoded <see cref="Shared.ClientAestheticProfile"/> — every
/// field the extractor can produce, including nullable/empty ones so the admin sees gaps clearly.
/// <see cref="Profile"/> is only populated when the latest analysis reached <c>Analyzed</c>; for any
/// other status the caller still learns <see cref="Status"/> so they know an analysis is in flight,
/// failed, or missing entirely.
/// </summary>
public sealed record ClientAestheticProfileAdminResponse(
    bool HasAnalysis,
    Guid? AnalysisId,
    string? Status,
    DateTime? CompletedAtUtc,
    string? ProfileUrlOrHandle,
    ClientAestheticProfile? Profile)
{
    public static readonly ClientAestheticProfileAdminResponse NoAnalysis =
        new(HasAnalysis: false, AnalysisId: null, Status: null, CompletedAtUtc: null, ProfileUrlOrHandle: null, Profile: null);
}
