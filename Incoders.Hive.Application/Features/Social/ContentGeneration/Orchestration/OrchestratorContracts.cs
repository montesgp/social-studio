using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Incoders.Hive.Domain.Social.ContentGeneration;
using Incoders.Hive.Domain.Social.SingleAnalyzer;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Orchestration;

/// <summary>Overall status of a content package generation run.</summary>
public enum OrchestratorStatus
{
    Succeeded,
    Partial,
    Failed
}

/// <summary>
/// Input contract for the content package orchestrator.
/// Carries the analysis, requested formats, and correlation metadata.
/// </summary>
public sealed record OrchestratorRequest(
    Guid OrganizationId,
    Guid? ClientId,
    Guid AnalysisId,
    SingleAnalyzerAnalysis Analysis,
    IReadOnlyList<string> RequestedFormats,
    Guid CorrelationId,
    string? RegenerationObjection = null,
    /// <summary>
    /// WU-4 (CAG-CG-07): aesthetic profile for CASE C (ClientAesthetic) jobs.
    /// Null for all other policies — generators degrade gracefully (ConcisionConstraint only).
    /// </summary>
    ClientAestheticProfile? AestheticProfile = null);

/// <summary>
/// Result from the content package orchestrator.
/// Carries the frozen brand snapshot too so the caller can reuse it for downstream
/// composition + rendering without making a second round-trip to the brand loader.
/// </summary>
public sealed record OrchestratorResult(
    OrchestratorStatus OverallStatus,
    IReadOnlyDictionary<string, FormatGenerationResult<object>> PerFormatResults,
    BrandSnapshot Brand);
