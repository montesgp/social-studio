using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Formats.Carousel;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Formats.Flyer;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Orchestration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Incoders.Hive.Domain.Social.ContentGeneration;
using Incoders.Hive.Domain.Social.SingleAnalyzer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Pipeline;

/// <summary>
/// End-to-end processor for a queued content-generation job.
///
/// Pipeline stages (each checkpoint-persisted in <c>social.content_generation_format_results</c>
/// so a retry never re-spends tokens for work that already succeeded):
/// <list type="number">
///   <item>Load the persisted analysis. Failure here is fatal — the format generators need it.</item>
///   <item>Load existing per-format results for this job. Formats with a stored OutputJson are
///         skipped at the AI stage on retry.</item>
///   <item>Run the orchestrator ONLY on formats that don't yet have an AI payload. Persist each
///         outcome as <c>AiSucceeded</c> (with OutputJson) or <c>AiFailed</c> (with the upstream
///         provider error code, never "unexpected-error").</item>
///   <item>For every format that has a payload (fresh or cached), compose + render and persist
///         the render outcome. Failures carry the actual exception type + message so the user
///         can act on them, not a generic "render failed".</item>
///   <item>Build the ZIP, store it, persist the <c>generated_packages</c> row, update the job
///         manifest with the full builder output, and set the final status.</item>
/// </list>
/// </summary>
public sealed class ContentGenerationJobProcessor
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IContentGenerationJobRepository _jobs;
    private readonly ISingleContentAnalysisRepository _analyses;
    private readonly IContentGenerationOrchestrator _orchestrator;
    private readonly IContentGenerationFormatResultRepository _formatResults;
    private readonly IClientBrandSnapshotLoader _brandLoader;
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
        IContentGenerationFormatResultRepository formatResults,
        IClientBrandSnapshotLoader brandLoader,
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
        _formatResults = formatResults;
        _brandLoader = brandLoader;
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
            // Anything that escapes the per-step handling is surfaced verbatim — the user
            // needs the real failure on their screen, not a generic "unexpected-error".
            _logger.LogError(ex, "ContentGeneration: unhandled error processing job {JobId}.", job.Id);
            await _jobs.UpdateStatusAsync(job.Id, "Failed", FormatException(ex), $"unhandled:{ex.GetType().Name}", ct);
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
            await _jobs.UpdateStatusAsync(job.Id, "Failed", FormatException(ex), "analysis-deserialize-failed", ct);
            return;
        }

        if (job.ClientId is { } jobClientId
            && analysisRecord.ClientId is { } analysisClientId
            && jobClientId != analysisClientId)
        {
            await _jobs.UpdateStatusAsync(job.Id, "Failed", "client_mismatch", "client-mismatch", ct);
            return;
        }

        var effectiveClientId = job.ClientId ?? analysisRecord.ClientId;
        if (effectiveClientId is null || effectiveClientId == Guid.Empty)
        {
            await _jobs.UpdateStatusAsync(job.Id, "Failed", "client_required", "client-required", ct);
            return;
        }

        // 2. Load existing checkpoints — formats with a stored OutputJson skip the AI stage.
        var existingResults = await _formatResults.ListByJobAsync(job.Id, ct);
        var existingByFormat = existingResults.ToDictionary(r => r.FormatKey, StringComparer.OrdinalIgnoreCase);

        var formatsNeedingAi = job.RequestedFormats
            .Where(f => !existingByFormat.TryGetValue(f, out var existing)
                     || string.IsNullOrWhiteSpace(existing.OutputJson))
            .ToList();
        var formatsReusedFromCache = job.RequestedFormats
            .Where(f => existingByFormat.TryGetValue(f, out var existing)
                     && !string.IsNullOrWhiteSpace(existing.OutputJson))
            .ToList();

        if (formatsReusedFromCache.Count > 0)
        {
            _logger.LogInformation(
                "ContentGeneration: reusing cached AI output for {ReusedCount} format(s) on retry of job {JobId}: {ReusedFormats}",
                formatsReusedFromCache.Count, job.Id, string.Join(", ", formatsReusedFromCache));
        }

        // 3. Run the orchestrator only on formats that need fresh AI work. When everything is
        // already cached we still need the brand snapshot for composing, so we load it directly.
        IReadOnlyDictionary<string, FormatGenerationResult<object>> freshResults =
            new Dictionary<string, FormatGenerationResult<object>>();
        BrandSnapshot brand;
        if (formatsNeedingAi.Count > 0)
        {
            var orchResult = await _orchestrator.RunAsync(
                new OrchestratorRequest(
                    OrganizationId: job.OrganizationId,
                    ClientId: effectiveClientId,
                    AnalysisId: job.AnalysisId,
                    Analysis: analysis,
                    RequestedFormats: formatsNeedingAi,
                    CorrelationId: job.CorrelationId,
                    RegenerationObjection: job.RegenerationObjection),
                ct);
            freshResults = orchResult.PerFormatResults;
            brand = orchResult.Brand;

            // Checkpoint every fresh outcome immediately. If the worker dies after this point,
            // the next run picks up here instead of re-spending tokens.
            foreach (var (formatKey, formatResult) in freshResults)
            {
                await PersistAiOutcomeAsync(job.Id, formatKey, formatResult, ct);
            }
        }
        else
        {
            brand = await _brandLoader.LoadAsync(job.OrganizationId, effectiveClientId.Value, ct);
        }

        // 4. Compose + render every format that has a usable payload (fresh OR cached). Persist
        // each render outcome to format_results so a future retry can skip already-succeeded
        // formats wholesale.
        var assets = new List<RenderedAsset>();
        var publicationByFormat = new Dictionary<string, PublicationMetadata>(StringComparer.OrdinalIgnoreCase);
        var inferred = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var renderFailures = new List<string>();
        var aiFailures = new List<string>();

        foreach (var formatKey in job.RequestedFormats)
        {
            var step = ResolveFormatPayload(formatKey, freshResults, existingByFormat);
            if (step.Payload is null)
            {
                // No payload available — record the actual upstream reason (not a generic code)
                // and move on. This is the AI failure path.
                var reason = step.AiErrorCode ?? "ai-output-missing";
                aiFailures.Add($"{formatKey}: {reason}");
                _logger.LogWarning(
                    "ContentGeneration: skipping render for format {FormatKey} — no AI payload available ({Reason}).",
                    formatKey, reason);
                continue;
            }

            try
            {
                var spec = await _composer.ComposeAsync(formatKey, step.Payload, brand, ct);
                foreach (var f in _composer.GetInferredFields())
                {
                    inferred.Add(f);
                }

                var rendered = await _renderer.RenderAsync(spec, new RenderOptions(), ct);
                if (rendered.IsSuccess)
                {
                    assets.Add(rendered.Value);
                    var publication = ResolvePublicationMetadata(formatKey, step.Payload);
                    if (publication is not null)
                    {
                        publicationByFormat[formatKey] = publication;
                    }
                    await PersistRenderSuccessAsync(job.Id, formatKey, step, rendered.Value, ct);
                }
                else
                {
                    var detail = $"{rendered.Error.Code}: {rendered.Error.MessageKey}";
                    renderFailures.Add($"{formatKey}: {detail}");
                    _logger.LogWarning(
                        "ContentGeneration: render failed for format {FormatKey} (job {JobId}): {Detail}",
                        formatKey, job.Id, detail);
                    await PersistRenderFailureAsync(job.Id, formatKey, step, detail, ct);
                }
            }
            catch (Exception ex)
            {
                var detail = FormatException(ex);
                renderFailures.Add($"{formatKey}: {detail}");
                _logger.LogError(ex,
                    "ContentGeneration: compose/render threw for format {FormatKey} (job {JobId}).",
                    formatKey, job.Id);
                await PersistRenderFailureAsync(job.Id, formatKey, step, detail, ct);
            }
        }

        // 5. Nothing renderable → fail the job with the actual reasons, not a generic placeholder.
        if (assets.Count == 0)
        {
            var statusReason = BuildStatusReason(aiFailures, renderFailures);
            var reducedManifest = SerializeReducedManifest(job.RequestedFormats, freshResults, existingByFormat, aiFailures, renderFailures);
            await _jobs.UpdateManifestAsync(job.Id, reducedManifest, DateTime.UtcNow, ct);
            await _jobs.UpdateStatusAsync(job.Id, "Failed", statusReason, "no-renderable-assets", ct);
            return;
        }

        // 6. Build the ZIP, store it, persist the generated_packages row.
        var warningsForManifest = aiFailures.Select(f => $"ai.{f}")
            .Concat(renderFailures.Select(f => $"render.{f}"))
            .ToList();

        BuiltPackage built;
        try
        {
            built = await _packageBuilder.BuildAsync(
            new BuildPackageInput(
                assets,
                inferred.ToList(),
                warningsForManifest,
                job.OrganizationId,
                job.Id,
                publicationByFormat),
            ct);
        }
        catch (PackagingException ex)
        {
            _logger.LogError(ex,
                "ContentGeneration: package builder rejected job {JobId}.", job.Id);
            await _jobs.UpdateStatusAsync(job.Id, "Failed", FormatException(ex), "package-build-failed", ct);
            return;
        }

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

        // 7. Persist the FULL manifest and final status. Any AI or render failure across the run
        // downgrades the job to Partial — the ZIP exists but is missing some formats; the reasons
        // are visible in the warnings.
        var fullManifestJson = JsonSerializer.Serialize(built.Manifest, JsonOptions);
        await _jobs.UpdateManifestAsync(job.Id, fullManifestJson, DateTime.UtcNow, ct);

        var anyFailure = aiFailures.Count + renderFailures.Count > 0;
        var finalStatus = anyFailure ? "Partial" : "Completed";
        var finalReason = anyFailure ? BuildStatusReason(aiFailures, renderFailures) : null;
        await _jobs.UpdateStatusAsync(job.Id, finalStatus, finalReason, null, ct);
    }

    private static PublicationMetadata? ResolvePublicationMetadata(string formatKey, object payload)
    {
        string? description = null;

        if (string.Equals(formatKey, "carousel", StringComparison.OrdinalIgnoreCase))
        {
            description = payload switch
            {
                CarouselContent carousel => carousel.PublicationDescription,
                _ => ReadPublicationDescription(payload)
            };
        }
        else if (string.Equals(formatKey, "flyer", StringComparison.OrdinalIgnoreCase))
        {
            description = payload switch
            {
                FlyerContent flyer => flyer.PublicationDescription,
                _ => ReadPublicationDescription(payload)
            };
        }

        var normalized = description?.Trim();
        return string.IsNullOrWhiteSpace(normalized)
            ? null
            : new PublicationMetadata(normalized);
    }

    private static string? ReadPublicationDescription(JsonElement json)
    {
        if (!json.TryGetProperty("publicationDescription", out var publicationDescription))
        {
            return null;
        }

        return publicationDescription.ValueKind == JsonValueKind.String
            ? publicationDescription.GetString()
            : null;
    }

    private static string? ReadPublicationDescription(object payload)
    {
        if (payload is JsonElement json)
        {
            return ReadPublicationDescription(json);
        }

        var type = payload.GetType();
        var property = type.GetProperty("PublicationDescription")
            ?? type.GetProperty("publicationDescription");
        if (property is null)
        {
            return null;
        }

        return property.GetValue(payload) as string;
    }

    private static FormatStep ResolveFormatPayload(
        string formatKey,
        IReadOnlyDictionary<string, FormatGenerationResult<object>> fresh,
        IReadOnlyDictionary<string, ContentGenerationFormatResult> cached)
    {
        // Fresh-from-orchestrator wins over cached — caller may have re-triggered specifically.
        if (fresh.TryGetValue(formatKey, out var freshResult))
        {
            if (freshResult.Status == FormatGenerationStatus.Succeeded && freshResult.Payload is not null)
            {
                return new FormatStep(
                    Payload: freshResult.Payload,
                    PromptVersion: freshResult.PromptVersion,
                    OutputJson: JsonSerializer.Serialize(freshResult.Payload, JsonOptions),
                    AiErrorCode: null);
            }
            return new FormatStep(
                Payload: null,
                PromptVersion: freshResult.PromptVersion,
                OutputJson: null,
                AiErrorCode: freshResult.FailureReason);
        }

        if (cached.TryGetValue(formatKey, out var cachedResult) && !string.IsNullOrWhiteSpace(cachedResult.OutputJson))
        {
            // Composers accept `object` and the v1 implementations don't read it (brand-only),
            // so a JsonElement is a forward-compatible carrier. When composers start reading
            // payload fields, this can switch to typed deserialization per formatKey.
            JsonElement payload;
            try
            {
                using var doc = JsonDocument.Parse(cachedResult.OutputJson);
                payload = doc.RootElement.Clone();
            }
            catch (Exception ex)
            {
                return new FormatStep(null, cachedResult.PromptVersion, null, $"cached-output-unreadable:{ex.GetType().Name}");
            }

            return new FormatStep(
                Payload: payload,
                PromptVersion: cachedResult.PromptVersion,
                OutputJson: cachedResult.OutputJson,
                AiErrorCode: null);
        }

        return new FormatStep(null, null, null, "no-output-recorded");
    }

    private async Task PersistAiOutcomeAsync(
        Guid jobId,
        string formatKey,
        FormatGenerationResult<object> result,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var record = result.Status == FormatGenerationStatus.Succeeded && result.Payload is not null
            ? new ContentGenerationFormatResult(
                Id: Guid.NewGuid(),
                JobId: jobId,
                FormatKey: formatKey,
                Status: "AiSucceeded",
                OutputJson: JsonSerializer.Serialize(result.Payload, JsonOptions),
                CompositionSpecJson: null,
                RenderedAssetUri: null,
                InferredFieldsJson: result.InferredFields.Count == 0
                    ? null
                    : JsonSerializer.Serialize(result.InferredFields, JsonOptions),
                PromptVersion: result.PromptVersion,
                ErrorCode: null,
                CreatedAt: now,
                UpdatedAt: now)
            : new ContentGenerationFormatResult(
                Id: Guid.NewGuid(),
                JobId: jobId,
                FormatKey: formatKey,
                Status: "AiFailed",
                OutputJson: null,
                CompositionSpecJson: null,
                RenderedAssetUri: null,
                InferredFieldsJson: null,
                PromptVersion: result.PromptVersion,
                ErrorCode: result.FailureReason ?? "ai-failed",
                CreatedAt: now,
                UpdatedAt: now);

        await _formatResults.UpsertAsync(record, ct);
    }

    private async Task PersistRenderSuccessAsync(
        Guid jobId,
        string formatKey,
        FormatStep step,
        RenderedAsset asset,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        await _formatResults.UpsertAsync(new ContentGenerationFormatResult(
            Id: Guid.NewGuid(),
            JobId: jobId,
            FormatKey: formatKey,
            Status: "Rendered",
            OutputJson: step.OutputJson,
            CompositionSpecJson: null,
            // v1 storage: PNG bytes live only inside the final ZIP. We persist a logical marker
            // (filename + content hash) so future versions that store individual assets can
            // recover them and skip re-rendering. ContentHash makes the marker idempotent.
            RenderedAssetUri: $"asset-hash://{asset.ContentHash}",
            InferredFieldsJson: null,
            PromptVersion: step.PromptVersion,
            ErrorCode: null,
            CreatedAt: now,
            UpdatedAt: now), ct);
    }

    private async Task PersistRenderFailureAsync(
        Guid jobId,
        string formatKey,
        FormatStep step,
        string detail,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        await _formatResults.UpsertAsync(new ContentGenerationFormatResult(
            Id: Guid.NewGuid(),
            JobId: jobId,
            FormatKey: formatKey,
            Status: "RenderFailed",
            OutputJson: step.OutputJson, // keep the AI payload so a later retry can re-render
            CompositionSpecJson: null,
            RenderedAssetUri: null,
            InferredFieldsJson: null,
            PromptVersion: step.PromptVersion,
            ErrorCode: detail,
            CreatedAt: now,
            UpdatedAt: now), ct);
    }

    private static string FormatException(Exception ex)
    {
        var msg = ex.Message?.Trim() ?? string.Empty;
        if (msg.Length > 240) msg = msg[..240] + "…";
        return $"{ex.GetType().Name}: {msg}";
    }

    private static string BuildStatusReason(IReadOnlyList<string> aiFailures, IReadOnlyList<string> renderFailures)
    {
        var parts = new List<string>();
        if (aiFailures.Count > 0)
        {
            parts.Add("ai_failed[" + string.Join("; ", aiFailures) + "]");
        }
        if (renderFailures.Count > 0)
        {
            parts.Add("render_failed[" + string.Join("; ", renderFailures) + "]");
        }
        var reason = string.Join(" ", parts);
        if (reason.Length > 480) reason = reason[..480] + "…";
        return reason;
    }

    private static string SerializeReducedManifest(
        IReadOnlyList<string> requestedFormats,
        IReadOnlyDictionary<string, FormatGenerationResult<object>> fresh,
        IReadOnlyDictionary<string, ContentGenerationFormatResult> cached,
        IReadOnlyList<string> aiFailures,
        IReadOnlyList<string> renderFailures) =>
        JsonSerializer.Serialize(new
        {
            overallStatus = "Failed",
            formats = requestedFormats.ToDictionary(
                fmt => fmt,
                fmt =>
                {
                    if (fresh.TryGetValue(fmt, out var f))
                    {
                        return new { status = f.Status.ToString(), failureReason = f.FailureReason };
                    }
                    if (cached.TryGetValue(fmt, out var c))
                    {
                        return new { status = c.Status, failureReason = (string?)c.ErrorCode };
                    }
                    return new { status = "Pending", failureReason = (string?)null };
                }),
            aiFailures,
            renderFailures,
        }, JsonOptions);

    /// <summary>Resolved AI payload + the metadata we need to persist or render it.</summary>
    private sealed record FormatStep(
        object? Payload,
        string? PromptVersion,
        string? OutputJson,
        string? AiErrorCode);
}
