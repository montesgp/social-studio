using Incoders.Hive.Domain.Social.ContentGeneration;
using Incoders.Hive.Domain.Social.SingleAnalyzer;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;

/// <summary>
/// Immutable value object passed into every format generator.
/// Carries the analysis snapshot, brand context, and correlation metadata
/// needed to build a prompt and populate the usage ledger context.
/// </summary>
public sealed record GenerationContext(
    Guid OrganizationId,
    Guid? ClientId,
    Guid AnalysisId,
    SingleAnalyzerAnalysis Analysis,
    BrandSnapshot BrandSnapshot,
    Guid CorrelationId);
