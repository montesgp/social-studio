using Incoders.Hive.Domain.Social.ContentGeneration;
using Incoders.Hive.Domain.Social.SingleAnalyzer;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;

/// <summary>
/// Immutable value object passed into every format generator.
/// Carries the analysis snapshot, brand context, and correlation metadata
/// needed to build a prompt and populate the usage ledger context.
///
/// AiProviderKey + AiModel are pre-resolved by the orchestrator via IDefaultAiSelector
/// (same selector the single-analyzer uses) so every format hits the SAME provider that
/// just analyzed the content — no per-format provider discovery, no per-format failure
/// when the org has only a single AI integration.
/// </summary>
public sealed record GenerationContext(
    Guid OrganizationId,
    Guid? ClientId,
    Guid AnalysisId,
    SingleAnalyzerAnalysis Analysis,
    BrandSnapshot BrandSnapshot,
    Guid CorrelationId,
    string AiProviderKey,
    string? AiModel,
    string? RegenerationObjection = null,
    /// <summary>
    /// WU-4 (CAG-CG-07): aesthetic profile for CASE C (ClientAesthetic) jobs.
    /// Null for all other policies — generators append no aesthetic guidance
    /// (ConcisionConstraint is still emitted unchanged).
    /// </summary>
    ClientAestheticProfile? AestheticProfile = null,
    /// <summary>
    /// WU-2 (CAC-CG-01): explicit user-requested carousel slide count, when provided by the caller.
    /// When non-null, this value wins over any profile-derived TypicalSlideCount (OQ-3).
    /// Null → effective count falls back to profile TypicalSlideCount clamped [3..10], then current default.
    /// </summary>
    int? RequestedSlideCount = null);
