using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Orchestration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Incoders.Hive.Domain.Social.ContentGeneration;
using Incoders.Hive.Domain.Social.SingleAnalyzer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Pipeline;

/// <summary>
/// End-to-end processor for a queued content-generation job. Loads the persisted analysis,
/// runs the format orchestrator with the real analysis (not a stub), composes + renders each
/// succeeded format into a PNG, packages everything into a ZIP, stores it, and persists the
/// generated_packages row so the user can download from the saved-analyses grid later.
///
/// Lives in Application (not the ApiService background-host) so it can be unit-tested with
/// fakes — the worker just resolves and calls this.
/// </summary>
public sealed class ContentGenerationJobProcessor
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IContentGenerationJobRepository _jobs;
    private readonly ISingleContentAnalysisRepository _analyses;
    private readonly IContentGenerationOrchestrator _orchestrator;
    private readonly IVisualCompositionService _composer;
    private readonly IContentRenderer _renderer;
    private readonly IGeneratedPackageBuilder _packageBuilder;
    private readonly IGeneratedPackageStorage _packageStorage;
    private readonly IGeneratedPackageRepository _packages;
    private readonly ILogger<ContentGenerationJobProcessor> _logger;

    public ContentGenerationJobProcessor(
        IContentGenerationJobRepository jobs,
        ISingleContentAnalysisRepository analyses,
        IContentGenerationOrchestrator orchestrator,
        IVisualCompositionService composer,
        IContentRenderer renderer,
        IGeneratedPackageBuilder packageBuilder,
        IGeneratedPackageStorage packageStorage,
        IGeneratedPackageRepository packages,
        ILogger<ContentGenerationJobProcessor>? logger = null)
    {
        _jobs = jobs;
        _analyses = analyses;
        _orchestrator = orchestrator;
        _composer = composer;
        _renderer = renderer;
        _packageBuilder = packageBuilder;
        _packageStorage = packageStorage;
        _packages = packages;
        _logger = logger ?? NullLogger<ContentGenerationJobProcessor>.Instance;
    }

    public async Task ProcessAsync(Guid jobId, CancellationToken ct)
    {
        var job = await _jobs.GetByIdInternalAsync(jobId, ct);
        if (job is null)
        {
            _logger.LogWarning("ContentGeneration: job {JobId} not found — skipping.", jobId);
            return;
        }

        await _jobs.UpdateStatusAsync(job.Id, "Running", null, null, ct);

        try
        {
            await ProcessInternalAsync(job, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await _jobs.UpdateStatusAsync(job.Id, "Failed", "cancelled", "cancelled", ct);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ContentGeneration: unexpected error processing job {JobId}.", job.Id);
            await _jobs.UpdateStatusAsync(job.Id, "Failed", ex.Message, "unexpected-error", ct);
        }
    }

    private async Task ProcessInternalAsync(ContentGenerationJob job, CancellationToken ct)
    {
        // 1. Load the persisted analysis. Without it the format generators run blind.
        var analysisRecord = await _analyses.GetByIdAsync(job.AnalysisId, job.OrganizationId, ct);
        if (analysisRecord is null || string.IsNullOrWhiteSpace(analysisRecord.AnalysisJson))
        {
            _logger.LogWarning(
                "ContentGeneration: analysis {AnalysisId} not found or has no AnalysisJson for job {JobId}.",
                job.AnalysisId, job.Id);
            await _jobs.UpdateStatusAsync(job.Id, "Failed", "analysis_not_available", "analysis-not-completed", ct);
            return;
        }

        SingleAnalyzerAnalysis analysis;
        try
        {
            analysis = JsonSerializer.Deserialize<SingleAnalyzerAnalysis>(analysisRecord.AnalysisJson, JsonOptions)
                ?? throw new InvalidOperationException("Deserialized analysis was null.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "ContentGeneration: failed to deserialize AnalysisJson for analysis {AnalysisId}.",
                job.AnalysisId);
            await _jobs.UpdateStatusAsync(job.Id, "Failed", "analysis_unreadable", "analysis-deserialize-failed", ct);
            return;
        }

        // 2. Run the per-format orchestrator with the REAL analysis (was a stub previously).
        var orchResult = await _orchestrator.RunAsync(
            new OrchestratorRequest(
                OrganizationId: job.OrganizationId,
                ClientId: job.ClientId,
                AnalysisId: job.AnalysisId,
                Analysis: analysis,
                RequestedFormats: job.RequestedFormats,
                CorrelationId: job.CorrelationId),
            ct);

        // 3. Compose + render each succeeded format into a binary asset.
        var assets = new List<RenderedAsset>();
        var inferred = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();

        foreach (var (formatKey, formatResult) in orchResult.PerFormatResults)
        {
            if (formatResult.Status != FormatGenerationStatus.Succeeded || formatResult.Payload is null)
            {
                continue;
            }

            try
            {
                var spec = await _composer.ComposeAsync(formatKey, formatResult.Payload, orchResult.Brand, ct);
                foreach (var f in _composer.GetInferredFields())
                {
                    inferred.Add(f);
                }

                var rendered = await _renderer.RenderAsync(spec, new RenderOptions(), ct);
                if (rendered.IsSuccess)
                {
                    assets.Add(rendered.Value);
                }
                else
                {
                    _logger.LogWarning(
                        "ContentGeneration: render failed for format {FormatKey} (job {JobId}): {Code} - {Detail}",
                        formatKey, job.Id, rendered.Error.Code, rendered.Error.MessageKey);
                    warnings.Add($"render.{formatKey}: {rendered.Error.Code}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "ContentGeneration: compose/render threw for format {FormatKey} (job {JobId}).",
                    formatKey, job.Id);
                warnings.Add($"render.{formatKey}: unexpected-error");
            }
        }

        // 4. Nothing renderable → fail the job. No empty packages.
        if (assets.Count == 0)
        {
            var reducedManifest = SerializeReducedManifest(orchResult);
            await _jobs.UpdateManifestAsync(job.Id, reducedManifest, DateTime.UtcNow, ct);
            await _jobs.UpdateStatusAsync(job.Id, "Failed", "no_assets_produced", "no-renderable-assets", ct);
            return;
        }

        // 5. Build the ZIP, store it, persist the generated_packages row.
        BuiltPackage built;
        try
        {
            built = await _packageBuilder.BuildAsync(
                new BuildPackageInput(assets, inferred.ToList(), warnings, job.OrganizationId, job.Id),
                ct);
        }
        catch (PackagingException ex)
        {
            _logger.LogError(ex,
                "ContentGeneration: package builder rejected job {JobId} (no assets after filter).",
                job.Id);
            await _jobs.UpdateStatusAsync(job.Id, "Failed", "package_build_failed", "package-build-failed", ct);
            return;
        }

        // The builder leaves the underlying stream open at the end position — reset before storing.
        built.Content.Position = 0;
        var storedRef = await _packageStorage.StoreAsync(
            built.Content,
            new StorePackageKey(job.OrganizationId, job.Id),
            ct);

        await _packages.InsertAsync(
            new GeneratedPackage(
                Id: Guid.NewGuid(),
                JobId: job.Id,
                StorageUri: storedRef.Uri,
                SizeBytes: built.SizeBytes,
                ManifestJson: JsonSerializer.Serialize(built.Manifest, JsonOptions),
                ExpiresAt: null,
                CreatedAt: DateTime.UtcNow),
            ct);

        // 6. Persist the FULL manifest on the job (with inferredFields + warnings + size) and
        // decide the final job status. If the orchestrator said Succeeded but a render warning
        // happened, downgrade to Partial — the package exists but is not complete.
        var fullManifestJson = JsonSerializer.Serialize(built.Manifest, JsonOptions);
        await _jobs.UpdateManifestAsync(job.Id, fullManifestJson, DateTime.UtcNow, ct);

        var finalStatus = (orchResult.OverallStatus, warnings.Count) switch
        {
            (OrchestratorStatus.Succeeded, 0) => "Completed",
            (OrchestratorStatus.Succeeded, _) => "Partial",
            (OrchestratorStatus.Partial, _) => "Partial",
            _ => "Partial", // assets.Count > 0 implies at least one format made it through
        };

        await _jobs.UpdateStatusAsync(job.Id, finalStatus, null, null, ct);
    }

    private static string SerializeReducedManifest(OrchestratorResult orchResult) =>
        JsonSerializer.Serialize(new
        {
            overallStatus = orchResult.OverallStatus.ToString(),
            formats = orchResult.PerFormatResults.ToDictionary(
                kv => kv.Key,
                kv => new { status = kv.Value.Status.ToString(), failureReason = kv.Value.FailureReason }),
        }, JsonOptions);
}
