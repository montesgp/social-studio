using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Application.Abstractions.Observability;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Orchestration;

/// <summary>
/// Orchestrates per-format content generation, aggregating individual results into an
/// <see cref="OrchestratorResult"/> with an overall status.
/// </summary>
public sealed class ContentPackageOrchestrator : IContentGenerationOrchestrator
{
    private readonly FormatContentGeneratorRegistry _registry;
    private readonly IClientBrandSnapshotLoader _brandLoader;
    private readonly IWorkflowConfigurationResolver? _workflowResolver;
    private readonly ProviderCallContext _providerCallContext;
    private readonly ILogger<ContentPackageOrchestrator> _logger;

    public ContentPackageOrchestrator(
        IEnumerable<IFormatContentGenerator> generators,
        IClientBrandSnapshotLoader brandLoader,
        ProviderCallContext providerCallContext,
        IWorkflowConfigurationResolver? workflowResolver = null,
        ILogger<ContentPackageOrchestrator>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(generators);
        ArgumentNullException.ThrowIfNull(brandLoader);
        ArgumentNullException.ThrowIfNull(providerCallContext);

        _registry = new FormatContentGeneratorRegistry(generators);
        _brandLoader = brandLoader;
        _workflowResolver = workflowResolver;
        _providerCallContext = providerCallContext;
        _logger = logger ?? NullLogger<ContentPackageOrchestrator>.Instance;
    }

    /// <inheritdoc />
    public async Task<OrchestratorResult> RunAsync(
        OrchestratorRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var perFormatResults = new Dictionary<string, FormatGenerationResult<object>>();

        // Build the generation context — brand snapshot is loaded at orchestrator level
        // so all format generators share the same frozen brand context for this run.
        var brandSnapshot = await _brandLoader.LoadAsync(
            request.OrganizationId,
            request.ClientId ?? Guid.Empty,
            cancellationToken);

        // Resolve the generation AI route ONCE for the whole run (single "generate-content" stage —
        // all formats share the SAME resolved agent; no per-format provider differentiation). The
        // workflow resolver returns only Connected integrations ordered by priority, so priority-1 is
        // used and a backup is picked automatically when the primary integration is not connected.
        // If the org has no configured AI route, fail every requested format with AiRouteNotConfigured.
        var selectedAi = await ResolveGenerationAiAsync(request.OrganizationId, cancellationToken);
        if (selectedAi.IsFailure)
        {
            _logger.LogWarning(
                "Content generation: no configured AI route for org {OrganizationId} ({ErrorCode}). " +
                "Marking all {FormatCount} requested formats as failed.",
                request.OrganizationId, selectedAi.Error.Code, request.RequestedFormats.Count);

            foreach (var formatKey in request.RequestedFormats)
            {
                perFormatResults[formatKey] = FormatGenerationResult<object>.Failed(
                    "unknown.v0", selectedAi.Error.Code);
            }
            return new OrchestratorResult(OrchestratorStatus.Failed, perFormatResults, brandSnapshot);
        }

        var genCtx = new GenerationContext(
            OrganizationId: request.OrganizationId,
            ClientId: request.ClientId,
            AnalysisId: request.AnalysisId,
            Analysis: request.Analysis,
            BrandSnapshot: brandSnapshot,
            CorrelationId: request.CorrelationId,
            AiProviderKey: selectedAi.Value.ProviderKey,
            AiModel: selectedAi.Value.Model,
            RegenerationObjection: request.RegenerationObjection,
            // WU-4 (CAG-CG-07): thread aesthetic profile into the generation context so
            // each format generator can emit advisory guidance when a profile is present.
            AestheticProfile: request.AestheticProfile);

        foreach (var formatKey in request.RequestedFormats)
        {
            var generator = _registry.TryResolve(formatKey);
            if (generator is null)
            {
                _logger.LogWarning(
                    "No generator registered for format key '{FormatKey}' — marking as failed.",
                    formatKey);

                perFormatResults[formatKey] = FormatGenerationResult<object>.Failed(
                    "unknown.v0", "no-generator-registered");
                continue;
            }

            try
            {
                var formatResult = await generator.GenerateBoxedAsync(genCtx, cancellationToken);
                perFormatResults[formatKey] = formatResult;

                _logger.LogDebug(
                    "Format '{FormatKey}' completed with status {Status} for analysis {AnalysisId}.",
                    formatKey, formatResult.Status, request.AnalysisId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                _logger.LogError(ex,
                    "Format '{FormatKey}' threw unexpectedly for analysis {AnalysisId}.",
                    formatKey, request.AnalysisId);

                perFormatResults[formatKey] = FormatGenerationResult<object>.Failed(
                    generator.PromptVersion, "unexpected-error");
            }
        }

        var overallStatus = AggregateStatus(perFormatResults.Values);

        return new OrchestratorResult(overallStatus, perFormatResults, brandSnapshot);
    }

    private static OrchestratorStatus AggregateStatus(
        IEnumerable<FormatGenerationResult<object>> results)
    {
        var list = results.ToList();
        if (list.Count == 0) return OrchestratorStatus.Failed;

        var succeededCount = list.Count(r => r.Status == FormatGenerationStatus.Succeeded);
        var failedCount = list.Count(r => r.Status == FormatGenerationStatus.Failed);

        if (failedCount == 0) return OrchestratorStatus.Succeeded;
        if (succeededCount == 0) return OrchestratorStatus.Failed;
        return OrchestratorStatus.Partial;
    }

    private async Task<Result<GenerationAiRoute>> ResolveGenerationAiAsync(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        // AI selection comes SOLELY from the org's priority-ordered workflow config (no Default-AI fallback).
        if (_workflowResolver is null)
        {
            return Result.Failure<GenerationAiRoute>(IntegrationErrors.AiRouteNotConfigured);
        }

        var route = await _workflowResolver.ResolveStepAsync(
            new WorkflowStepResolutionRequest(
                organizationId,
                "single-analyzer",
                "content-analysis",
                "generate-content",
                "ai-generation",
                null),
            cancellationToken);

        if (route.IsFailure)
        {
            return Result.Failure<GenerationAiRoute>(route.Error);
        }

        // Priority-1 among the connected routes is the default. The whole package uses this single agent.
        var candidate = route.Value
            .Where(item => item.IsEnabled)
            .OrderBy(item => item.Priority)
            .FirstOrDefault();

        if (candidate is null)
        {
            return Result.Failure<GenerationAiRoute>(IntegrationErrors.AiRouteNotConfigured);
        }

        _logger.LogInformation(
            "WorkflowStepResolved OrganizationId={OrganizationId} WorkflowKey={WorkflowKey} AnalysisType={AnalysisType} StepKey={StepKey} ProviderKey={ProviderKey} ModelId={ModelId} Priority={Priority} IsFallback={IsFallback}",
            organizationId,
            "single-analyzer",
            "content-analysis",
            "generate-content",
            candidate.ProviderKey,
            candidate.ModelId ?? "(provider default)",
            candidate.Priority,
            candidate.Priority > 1);

        return Result.Success(new GenerationAiRoute(candidate.ProviderKey, candidate.ModelId));
    }

    private sealed record GenerationAiRoute(string ProviderKey, string? Model);
}
