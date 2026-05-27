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
    string? AiModel);
