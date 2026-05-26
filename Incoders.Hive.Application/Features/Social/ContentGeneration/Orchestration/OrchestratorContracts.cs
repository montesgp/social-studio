using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
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
    Guid CorrelationId);

/// <summary>
/// Result from the content package orchestrator.
/// Contains the aggregated status and per-format results.
/// </summary>
public sealed record OrchestratorResult(
    OrchestratorStatus OverallStatus,
    IReadOnlyDictionary<string, FormatGenerationResult<object>> PerFormatResults);
