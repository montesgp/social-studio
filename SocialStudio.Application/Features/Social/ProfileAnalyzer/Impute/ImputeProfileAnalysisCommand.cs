using SocialStudio.Application.Features.Social.ProfileAnalyzer.Analyze;
using SocialStudio.Domain.Common;
using MediatR;

namespace SocialStudio.Application.Features.Social.ProfileAnalyzer.Impute;

/// <summary>
/// Imputes AI-detected fields from a completed ProfileAnalysis into the linked client's
/// BrandManual, ContentPlan, and/or GeneralStrategy (PA-20, PA-21, PA-22).
///
/// Rules:
///   - Only valid for status == Analyzed (PA-22); returns NotAnalyzed error otherwise.
///   - Requires ClientId != null; returns ClientRequired for URL-path analyses (PA-20).
///   - Each impute target is applied via an existing write command with load-merge-write
///     semantics, preserving unselected fields (PA-21 idempotency).
///   - Only the targets in <see cref="Targets"/> are touched; all others are unchanged.
/// </summary>
public sealed record ImputeProfileAnalysisCommand(
    Guid AnalysisId,
    Guid OrganizationId,
    IReadOnlyList<ProfileImputeTarget> Targets) : IRequest<Result<Unit>>;
