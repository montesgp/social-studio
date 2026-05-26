using Incoders.Hive.Application.Abstractions.Observability;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
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
    private readonly ProviderCallContext _providerCallContext;
    private readonly ILogger<ContentPackageOrchestrator> _logger;

    public ContentPackageOrchestrator(
        IEnumerable<IFormatContentGenerator> generators,
        IClientBrandSnapshotLoader brandLoader,
        ProviderCallContext providerCallContext,
        ILogger<ContentPackageOrchestrator>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(generators);
        ArgumentNullException.ThrowIfNull(brandLoader);
        ArgumentNullException.ThrowIfNull(providerCallContext);

        _registry = new FormatContentGeneratorRegistry(generators);
        _brandLoader = brandLoader;
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

        var genCtx = new GenerationContext(
            OrganizationId: request.OrganizationId,
            ClientId: request.ClientId,
            AnalysisId: request.AnalysisId,
            Analysis: request.Analysis,
            BrandSnapshot: brandSnapshot,
            CorrelationId: request.CorrelationId);

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

        return new OrchestratorResult(overallStatus, perFormatResults);
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
}
