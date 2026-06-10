using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Observability;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Formats.Carousel;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Formats.Flyer;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Orchestration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Incoders.Hive.Domain.Common;
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
///         the render outcome. Multi-asset formats (carousel) fan out N specs via ComposeMany.</item>
///   <item>Upload each rendered asset to Supabase Storage via IGeneratedAssetStorage; persist
///         one child asset row per stored object. Storage failures mark that format failed.</item>
///   <item>Write manifest.json to storage (org/client/analysis context + formats + timestamps).</item>
///   <item>Build the ZIP (backward-compat frozen path), store it, persist the generated_packages row.</item>
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
    private readonly IGeneratedAssetStorage? _assetStorage;
    private readonly IContentGenerationFormatAssetRepository? _assetRepository;
    private readonly IVisualDesigner? _designer;
    private readonly IVisualGuardrailValidator? _guardrails;
    private readonly ProviderCallContext _providerCallContext;
    private readonly ILogger<ContentGenerationJobProcessor> _logger;

    /// <summary>
    /// Designed-raster formats: flyer + carousel slides.
    /// Text formats (hook, caption, cta-variation, reel-script, content-idea) bypass the designer.
    /// </summary>
    private static readonly HashSet<string> DesignedRasterFormats =
        new(StringComparer.OrdinalIgnoreCase) { "flyer", "carousel" };

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
        IGeneratedAssetStorage? assetStorage = null,
        IContentGenerationFormatAssetRepository? assetRepository = null,
        IVisualDesigner? designer = null,
        IVisualGuardrailValidator? guardrails = null,
        ProviderCallContext? providerCallContext = null,
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
        _assetStorage = assetStorage;
        _assetRepository = assetRepository;
        _designer = designer;
        _guardrails = guardrails;
        // The designer makes an AI call from the render loop — OUTSIDE the orchestrator's
        // provider-call scope. Without an ambient ProviderCallContext the instrumentation
        // decorator throws "ProviderCallContext.Current is null", failing every flyer/carousel.
        // In production DI injects the scoped instance the decorator reads; the fallback only
        // keeps unit construction simple.
        _providerCallContext = providerCallContext ?? new ProviderCallContext();
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
            // Per-job timeout or host shutdown. The terminal write MUST use a fresh token —
            // writing with the already-cancelled ct would cancel SaveChanges too, leaving the
            // job stuck in "Running" forever (the exact "pill never changes" symptom).
            await _jobs.UpdateStatusAsync(job.Id, "Failed", "cancelled", "cancelled", CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            // Anything that escapes the per-step handling is surfaced verbatim — the user
            // needs the real failure on their screen, not a generic "unexpected-error".
            // Use CancellationToken.None so this terminal write always lands even if ct is faulted.
            _logger.LogError(ex, "ContentGeneration: unhandled error processing job {JobId}.", job.Id);
            await _jobs.UpdateStatusAsync(job.Id, "Failed", FormatException(ex), $"unhandled:{ex.GetType().Name}", CancellationToken.None);
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

        // 3. Run the orchestrator only on formats that need fresh AI work.
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

            foreach (var (formatKey, formatResult) in freshResults)
            {
                var persisted = await PersistAiOutcomeAsync(job.Id, formatKey, formatResult, ct);
                existingByFormat[formatKey] = persisted;
            }
        }
        else
        {
            brand = await _brandLoader.LoadAsync(job.OrganizationId, effectiveClientId.Value, ct);
        }

        // 4. Compose + render every format. Multi-asset formats fan out via ComposeManyAsync.
        //    The returned specs are then each rendered and stored individually.
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
                var reason = step.AiErrorCode ?? "ai-output-missing";
                aiFailures.Add($"{formatKey}: {reason}");
                _logger.LogWarning(
                    "ContentGeneration: skipping render for format {FormatKey} — no AI payload available ({Reason}).",
                    formatKey, reason);
                continue;
            }

            foreach (var f in step.InferredFields)
            {
                inferred.Add(f);
            }

            try
            {
                // Fan-out: carousel returns N specs, single-asset formats return 1 spec.
                var specs = await _composer.ComposeManyAsync(formatKey, step.Payload, brand, ct);
                foreach (var f in _composer.GetInferredFields())
                {
                    inferred.Add(f);
                }

                var formatRendered = false;
                var formatStorageFailed = false;
                var formatAssetRows = new List<FormatAssetRow>();
                Guid formatResultId;

                // WU-1 (B3 D1+D3): one editable composition envelope (validated markup + canvas +
                // per-slot geometry + background data-URI) is captured PER successfully rendered
                // slide and persisted in composition_spec_json. Single-asset formats (flyer) yield
                // one; carousel yields one per slide so every slide stays editable on export.
                var capturedEnvelopes = new List<CompositionSpecEnvelope>();

                // We need the format result ID to associate asset rows.
                // Generate a stable ID now (it will be upserted into the DB after render).
                formatResultId = existingByFormat.TryGetValue(formatKey, out var existingResult)
                    ? existingResult.Id
                    : Guid.NewGuid();

                for (var specIndex = 0; specIndex < specs.Count; specIndex++)
                {
                    var spec = specs[specIndex];

                    // Per-slide capture — built into an envelope at the end of a successful iteration.
                    string? capturedMarkup = null;
                    CompositionDimensions? capturedDimensions = null;
                    IReadOnlyList<SlotGeometryEntry> capturedGeometry = [];
                    string? capturedBackgroundDataUri = null;

                    // Designer + guardrail stage for designed-raster formats (flyer, carousel).
                    // Text/markdown formats bypass entirely (spec.OutputKind != Raster means they
                    // are already text/markdown — the designer only applies to raster specs from
                    // designed formats).
                    //
                    // IsActive gate: when the registered IVisualDesigner is a passthrough/no-op
                    // (IsActive=false, e.g. B1a's NoOpVisualDesigner), this entire block is skipped
                    // and processing falls through to the legacy renderer below — preserving
                    // pre-B1a rendering behavior exactly. Only a real, active designer (B1b's
                    // AiVisualDesigner, IsActive=true) triggers the designer+guardrail chain.
                    if (_designer is not null
                        && _designer.IsActive
                        && _guardrails is not null
                        && DesignedRasterFormats.Contains(formatKey)
                        && spec.OutputKind == CompositionOutputKind.Raster)
                    {
                        // Tier-aware background selection.
                        var backgroundTemplates = brand.Brand?.BackgroundTemplates
                            ?? Array.Empty<BrandBackgroundTemplateSnapshot>();
                        var selection = Domain.Social.ContentGeneration.BackgroundSelector.Select(
                            backgroundTemplates, formatKey);

                        // Design stage. OrganizationId is threaded per-job so the designer's AI
                        // provider call resolves credentials for the correct tenant.
                        // A ProviderCallContext scope MUST be active around this call: the
                        // instrumentation decorator reads ProviderCallContext.Current to write the
                        // usage-ledger event and throws if it is null. The content generators open
                        // their own scope inside the orchestrator; the designer runs here, in the
                        // render loop, so the scope must be opened explicitly.
                        Result<VisualDesignOutput> designed;
                        using (_providerCallContext.Begin(
                            job.OrganizationId,
                            effectiveClientId,
                            workflowKey: "content-generation",
                            pipelineStepKey: $"designer:{formatKey}",
                            promptVersion: "visual-designer/v1",
                            correlationId: job.CorrelationId))
                        {
                            designed = await _designer.ComposeAsync(
                                job.OrganizationId, spec, brand.Brand!, selection, ct);
                        }
                        if (!designed.IsSuccess)
                        {
                            var designDetail = $"{designed.Error.Code}: {designed.Error.MessageKey}";
                            renderFailures.Add($"{formatKey}: designer-failed:{designDetail}");
                            _logger.LogWarning(
                                "ContentGeneration: designer failed for format {FormatKey} spec {SpecIndex} (job {JobId}): {Detail}",
                                formatKey, specIndex, job.Id, designDetail);
                            await PersistRenderFailureAsync(job.Id, formatKey, step, designDetail, ct);
                            break; // Fail the whole format
                        }

                        // Guardrail stage.
                        var guarded = _guardrails.Validate(designed.Value, brand.Brand!, spec);
                        if (!guarded.IsSuccess)
                        {
                            var guardDetail = $"{guarded.Error.Code}: {guarded.Error.MessageKey}";
                            renderFailures.Add($"{formatKey}: guardrail-failed:{guardDetail}");
                            _logger.LogWarning(
                                "ContentGeneration: guardrail failed for format {FormatKey} spec {SpecIndex} (job {JobId}): {Detail}",
                                formatKey, specIndex, job.Id, guardDetail);
                            await PersistRenderFailureAsync(job.Id, formatKey, step, guardDetail, ct);
                            break; // Renderer NOT called — fail the format
                        }

                        // Guardrails passed. If the client defined a template background for this
                        // format, Incoders-Hive must hard-enforce it instead of merely trusting the
                        // AI to obey the prompt. The AI/content generators provide the copy slots,
                        // but the final markup uses a deterministic, high-contrast, non-overlapping
                        // layout on top of the brand-manual background template.
                        var enforcedBackground = ResolveEnforcedBackground(selection);
                        var validatedMarkup = enforcedBackground is not null
                            ? BuildTemplateBackedMarkup(spec, enforcedBackground)
                            : guarded.Value.Markup;

                        // Promote spec to Html kind with validated/enforced markup.
                        spec = spec with
                        {
                            OutputKind = CompositionOutputKind.Html,
                            DesignerMarkup = validatedMarkup,
                        };

                        // WU-1 D1: capture the validated markup and canvas dimensions for persistence.
                        capturedMarkup = validatedMarkup;
                        capturedDimensions = spec.Dimensions;
                    }

                    // WU-1 D3: when the renderer supports geometry capture (IContentRendererWithGeometry)
                    // and the spec is an Html composition, use RenderWithGeometryAsync to capture
                    // per-[data-slot] bounding rects in the same Chromium page session.
                    // Falls back to the base RenderAsync for non-Html specs or legacy renderers.
                    RenderedAsset asset;
                    if (spec.OutputKind == CompositionOutputKind.Html
                        && _renderer is IContentRendererWithGeometry geometryRenderer)
                    {
                        var renderedWithGeo = await geometryRenderer.RenderWithGeometryAsync(
                            spec, new RenderOptions(), ct);
                        if (!renderedWithGeo.IsSuccess)
                        {
                            var detail = $"{renderedWithGeo.Error.Code}: {renderedWithGeo.Error.MessageKey}";
                            renderFailures.Add($"{formatKey}: {detail}");
                            _logger.LogWarning(
                                "ContentGeneration: render failed for format {FormatKey} spec {SpecIndex} (job {JobId}): {Detail}",
                                formatKey, specIndex, job.Id, detail);
                            await PersistRenderFailureAsync(job.Id, formatKey, step, detail, ct);
                            break;
                        }
                        asset = renderedWithGeo.Value.Asset;
                        capturedGeometry = renderedWithGeo.Value.SlotGeometry;
                        // WU-9: capture the background data-URI from the same render pass.
                        capturedBackgroundDataUri = renderedWithGeo.Value.BackgroundImageDataUri;
                    }
                    else
                    {
                        var rendered = await _renderer.RenderAsync(spec, new RenderOptions(), ct);
                        if (!rendered.IsSuccess)
                        {
                            var detail = $"{rendered.Error.Code}: {rendered.Error.MessageKey}";
                            renderFailures.Add($"{formatKey}: {detail}");
                            _logger.LogWarning(
                                "ContentGeneration: render failed for format {FormatKey} spec {SpecIndex} (job {JobId}): {Detail}",
                                formatKey, specIndex, job.Id, detail);
                            await PersistRenderFailureAsync(job.Id, formatKey, step, detail, ct);
                            break; // Fail the whole format on first render failure
                        }
                        asset = rendered.Value;
                    }

                    // Determine the relative path for storage.
                    // Carousel: carousel/slide_{N}.png (1-based index)
                    // Single-asset: just the filename (e.g. flyer.png, reel-script.txt)
                    string relativePath;
                    if (string.Equals(formatKey, "carousel", StringComparison.OrdinalIgnoreCase) && specs.Count > 1)
                    {
                        relativePath = $"carousel/slide_{specIndex + 1}.png";
                    }
                    else
                    {
                        relativePath = asset.Filename;
                    }

                    // 5. Upload to Supabase Storage (if the adapter is wired).
                    if (_assetStorage is not null)
                    {
                        try
                        {
                            var storageKey = new StoredAssetKey(
                                job.OrganizationId,
                                effectiveClientId.Value,
                                job.AnalysisId,
                                relativePath);

                            var storedRef = await _assetStorage.PutAssetAsync(
                                storageKey,
                                new StoredAssetContent(asset.GetBytes(), asset.Mime),
                                ct);

                            formatAssetRows.Add(new FormatAssetRow(
                                Id: Guid.NewGuid(),
                                FormatResultId: formatResultId,
                                AssetIndex: specs.Count > 1 ? specIndex + 1 : 0,
                                StoragePath: storedRef.StoragePath,
                                Filename: relativePath.Contains('/') ? relativePath.Split('/').Last() : relativePath,
                                MimeType: asset.Mime,
                                SizeBytes: storedRef.SizeBytes,
                                ContentHash: storedRef.ContentHash,
                                CreatedAt: DateTime.UtcNow,
                                UpdatedAt: DateTime.UtcNow));

                            formatRendered = true;
                        }
                        catch (GeneratedAssetStorageException ex)
                        {
                            var detail = FormatException(ex);
                            renderFailures.Add($"{formatKey}: storage-upload-failed:{detail}");
                            _logger.LogError(ex,
                                "ContentGeneration: storage upload failed for format {FormatKey} spec {SpecIndex} (job {JobId}).",
                                formatKey, specIndex, job.Id);
                            formatStorageFailed = true;
                            break; // No orphan row for this format
                        }
                    }
                    else
                    {
                        // No asset storage wired — this is the legacy path (backward compat).
                        formatRendered = true;
                    }

                    assets.Add(asset);

                    // Capture this slide's editable composition (designed-raster formats only —
                    // text/markdown slides have no markup). One envelope per slide keeps every
                    // carousel slide editable on PPTX/Canva export.
                    if (capturedMarkup is not null)
                    {
                        var canvas = capturedDimensions is not null
                            ? new CanvasDimensions(capturedDimensions.Width, capturedDimensions.Height, capturedDimensions.Dpi)
                            : new CanvasDimensions(0, 0, 96);
                        capturedEnvelopes.Add(new CompositionSpecEnvelope(
                            CompositionSpecEnvelope.CurrentSchemaVersion,
                            capturedMarkup,
                            canvas,
                            capturedGeometry,
                            capturedBackgroundDataUri));
                    }
                }

                if (formatRendered && !formatStorageFailed)
                {
                    var publication = ResolvePublicationMetadata(formatKey, step.Payload);
                    if (publication is not null)
                    {
                        publicationByFormat[formatKey] = publication;
                    }

                    // Persist format result first (so formatResultId is stable in DB).
                    // WU-1: pass captured markup + canvas dimensions + geometry so
                    // PersistRenderSuccessAsync can serialize the CompositionSpecEnvelope
                    // into composition_spec_json.
                    var persistedFormatResult = await PersistRenderSuccessAsync(
                        formatResultId,
                        job.Id,
                        formatKey,
                        step,
                        assets.Last(),
                        capturedEnvelopes,
                        ct);
                    existingByFormat[formatKey] = persistedFormatResult;

                    // Persist per-asset rows if we have any.
                    if (_assetRepository is not null && formatAssetRows.Count > 0)
                    {
                        var persistedAssetRows = persistedFormatResult.Id == formatResultId
                            ? formatAssetRows
                            : formatAssetRows
                                .Select(row => row with { FormatResultId = persistedFormatResult.Id })
                                .ToList();

                        await _assetRepository.UpsertManyForFormatAsync(
                            persistedFormatResult.Id,
                            persistedAssetRows,
                            ct);
                    }

                    // Create carousel folder placeholder.
                    if (_assetStorage is not null
                        && string.Equals(formatKey, "carousel", StringComparison.OrdinalIgnoreCase)
                        && specs.Count > 1)
                    {
                        try
                        {
                            var folderPath = $"{job.OrganizationId}/{effectiveClientId}/{job.AnalysisId}/carousel";
                            await _assetStorage.PutFolderPlaceholderAsync(folderPath, ct);
                        }
                        catch (GeneratedAssetStorageException ex)
                        {
                            // Folder placeholder failure is non-fatal — log and continue.
                            _logger.LogWarning(ex,
                                "ContentGeneration: carousel folder placeholder PUT failed (job {JobId}). Continuing.",
                                job.Id);
                        }
                    }
                }
                else if (formatStorageFailed)
                {
                    // Storage failed — record as render failure (no asset row written).
                    await PersistRenderFailureAsync(job.Id, formatKey, step, "storage-upload-failed", ct);
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

        // 5b. Write manifest.json to Supabase Storage.
        if (_assetStorage is not null && assets.Count > 0)
        {
            try
            {
                var manifestContent = BuildManifestJson(
                    job,
                    effectiveClientId.Value,
                    brand.Client?.Name,
                    analysisRecord.UserId,
                    job.RequestedFormats.Except(aiFailures.Select(f => f.Split(':')[0])).ToList(),
                    DateTime.UtcNow);

                var manifestKey = new StoredAssetKey(
                    job.OrganizationId,
                    effectiveClientId.Value,
                    job.AnalysisId,
                    "manifest.json");

                await _assetStorage.PutAssetAsync(
                    manifestKey,
                    new StoredAssetContent(Encoding.UTF8.GetBytes(manifestContent), "application/json"),
                    ct);
            }
            catch (GeneratedAssetStorageException ex)
            {
                // Manifest failure is non-fatal — the individual assets are already uploaded.
                _logger.LogWarning(ex,
                    "ContentGeneration: manifest.json upload failed (job {JobId}). Assets already uploaded.",
                    job.Id);
            }
        }

        // 6. Nothing renderable → fail the job.
        if (assets.Count == 0)
        {
            var statusReason = BuildStatusReason(aiFailures, renderFailures);
            var reducedManifest = SerializeReducedManifest(job.RequestedFormats, freshResults, existingByFormat, aiFailures, renderFailures);
            await _jobs.UpdateManifestAsync(job.Id, reducedManifest, DateTime.UtcNow, ct);
            await _jobs.UpdateStatusAsync(job.Id, "Failed", statusReason, "no-renderable-assets", ct);
            return;
        }

        // 7. Build the frozen ZIP — backward-compat path; stays through INC-4.
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
                publicationByFormat,
                brand.Client?.Name),
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
        var packageStoredRef = await _packageStorage.StoreAsync(
            built.Content,
            new StorePackageKey(job.OrganizationId, job.Id),
            ct);

        await _packages.InsertAsync(
            new GeneratedPackage(
                Id: Guid.NewGuid(),
                JobId: job.Id,
                StorageUri: packageStoredRef.Uri,
                SizeBytes: built.SizeBytes,
                ManifestJson: JsonSerializer.Serialize(built.Manifest, JsonOptions),
                ExpiresAt: null,
                CreatedAt: DateTime.UtcNow),
            ct);

        // 8. Persist the full manifest and final status.
        // Set HasStoredAssets when the durable per-asset storage pipeline ran (INC-3+).
        var manifestToStore = _assetStorage is not null && assets.Count > 0
            ? built.Manifest with { HasStoredAssets = true }
            : built.Manifest;
        var fullManifestJson = JsonSerializer.Serialize(manifestToStore, JsonOptions);
        await _jobs.UpdateManifestAsync(job.Id, fullManifestJson, DateTime.UtcNow, ct);

        var anyFailure = aiFailures.Count + renderFailures.Count > 0;
        var finalStatus = anyFailure ? "Partial" : "Completed";
        var finalReason = anyFailure ? BuildStatusReason(aiFailures, renderFailures) : null;
        await _jobs.UpdateStatusAsync(job.Id, finalStatus, finalReason, null, ct);
    }

    private static string BuildManifestJson(
        ContentGenerationJob job,
        Guid clientId,
        string? clientName,
        Guid generatingUserId,
        IReadOnlyList<string> succeededFormats,
        DateTime now) =>
        JsonSerializer.Serialize(new
        {
            organizationId = job.OrganizationId,
            clientId,
            clientName,
            analysisId = job.AnalysisId,
            generatingUserId,
            formats = succeededFormats,
            createdAt = now.ToString("O"),
            updatedAt = now.ToString("O"),
        }, JsonOptions);

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
        if (fresh.TryGetValue(formatKey, out var freshResult))
        {
            if (freshResult.Status == FormatGenerationStatus.Succeeded && freshResult.Payload is not null)
            {
                return new FormatStep(
                    Payload: freshResult.Payload,
                    PromptVersion: freshResult.PromptVersion,
                    OutputJson: JsonSerializer.Serialize(freshResult.Payload, JsonOptions),
                    InferredFields: freshResult.InferredFields,
                    AiErrorCode: null);
            }
            return new FormatStep(
                Payload: null,
                PromptVersion: freshResult.PromptVersion,
                OutputJson: null,
                InferredFields: [],
                AiErrorCode: freshResult.FailureReason);
        }

        if (cached.TryGetValue(formatKey, out var cachedResult) && !string.IsNullOrWhiteSpace(cachedResult.OutputJson))
        {
            JsonElement payload;
            try
            {
                using var doc = JsonDocument.Parse(cachedResult.OutputJson);
                payload = doc.RootElement.Clone();
            }
            catch (Exception ex)
            {
                return new FormatStep(null, cachedResult.PromptVersion, null, [], $"cached-output-unreadable:{ex.GetType().Name}");
            }

            return new FormatStep(
                Payload: payload,
                PromptVersion: cachedResult.PromptVersion,
                OutputJson: cachedResult.OutputJson,
                InferredFields: ParseInferredFields(cachedResult.InferredFieldsJson),
                AiErrorCode: null);
        }

        return new FormatStep(null, null, null, [], "no-output-recorded");
    }

    private static IReadOnlyList<string> ParseInferredFields(string? inferredFieldsJson)
    {
        if (string.IsNullOrWhiteSpace(inferredFieldsJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(inferredFieldsJson, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private async Task<ContentGenerationFormatResult> PersistAiOutcomeAsync(
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

        return await _formatResults.UpsertAsync(record, ct);
    }

    private async Task<ContentGenerationFormatResult> PersistRenderSuccessAsync(
        Guid resultId,
        Guid jobId,
        string formatKey,
        FormatStep step,
        RenderedAsset asset,
        IReadOnlyList<CompositionSpecEnvelope> compositionEnvelopes,
        CancellationToken ct)
    {
        // WU-1 D1: serialize the per-slide composition envelopes (markup + canvas + geometry +
        // background data-URI) into composition_spec_json so the PPTX/Canva export adapters can
        // retrieve everything without re-composing. Single-asset formats store one envelope;
        // carousel stores one per slide so every slide stays editable. Non-designed formats
        // (text, markdown, legacy raster) produce no envelopes and leave the column null.
        var compositionSpecJson = CompositionSpecJson.Serialize(compositionEnvelopes);

        var now = DateTime.UtcNow;
        return await _formatResults.UpsertAsync(new ContentGenerationFormatResult(
            Id: resultId,
            JobId: jobId,
            FormatKey: formatKey,
            Status: "Rendered",
            OutputJson: step.OutputJson,
            CompositionSpecJson: compositionSpecJson,
            // v1 storage: asset-hash marker for legacy compat (INC-4 switches to per-asset rows).
            RenderedAssetUri: $"asset-hash://{asset.ContentHash}",
            InferredFieldsJson: step.InferredFields.Count == 0
                ? null
                : JsonSerializer.Serialize(step.InferredFields, JsonOptions),
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
            InferredFieldsJson: step.InferredFields.Count == 0
                ? null
                : JsonSerializer.Serialize(step.InferredFields, JsonOptions),
            PromptVersion: step.PromptVersion,
            ErrorCode: detail,
            CreatedAt: now,
            UpdatedAt: now), ct);
    }

    private static EnforcedBackgroundTemplate? ResolveEnforcedBackground(BackgroundSelection selection)
    {
        if (selection is BackgroundSelection.ForcedDefault forcedDefault)
        {
            return new EnforcedBackgroundTemplate(
                forcedDefault.Url,
                forcedDefault.Name,
                "brand-manual-forced-default");
        }

        if (selection is BackgroundSelection.References references)
        {
            var first = references.Entries.FirstOrDefault(entry => !string.IsNullOrWhiteSpace(entry.Url));
            if (first is not null)
            {
                return new EnforcedBackgroundTemplate(
                    first.Url,
                    first.Name,
                    "brand-manual-template");
            }
        }

        return null;
    }

    private static string BuildTemplateBackedMarkup(
        VisualCompositionSpec spec,
        EnforcedBackgroundTemplate background)
    {
        var isCarousel = string.Equals(spec.FormatKey, "carousel", StringComparison.OrdinalIgnoreCase);
        var width = spec.Dimensions.Width;
        var height = spec.Dimensions.Height;
        var headingFont = CssString(spec.Typography.HeadingFamily);
        var bodyFont = CssString(spec.Typography.BodyFamily);
        var safeUrl = Html(background.Url);
        var safeName = Html(string.IsNullOrWhiteSpace(background.Name) ? "Brand background template" : background.Name);
        var source = Html(background.Source);

        var title = FirstSlot(spec.CopySlots, isCarousel ? ["title", "headline"] : ["headline", "title"])
            ?? (isCarousel ? "Carousel" : "Flyer");
        var subtitle = FirstSlot(spec.CopySlots, ["subtitle", "body", "text"]);
        var body = isCarousel
            ? null
            : FirstSlot(spec.CopySlots, ["body", "callouts"]);
        var cta = FirstSlot(spec.CopySlots, ["cta"]);
        var signature = FirstSlot(spec.CopySlots, ["signature"]);

        var content = isCarousel
            ? BuildCarouselTemplateSlots(title, subtitle, cta, headingFont, bodyFont)
            : BuildFlyerTemplateSlots(title, subtitle, body, cta, signature, headingFont, bodyFont);

        var backgroundDecor = isCarousel
            ? """
              <div style="position:absolute;left:64px;right:64px;top:140px;bottom:140px;border-radius:46px;background:rgba(0,0,0,0.52);box-shadow:0 28px 80px rgba(0,0,0,0.32);"></div>
              <div style="position:absolute;left:64px;right:64px;bottom:140px;height:10px;border-radius:999px;background:rgba(255,255,255,0.82);"></div>
              """
            : """
              <div style="position:absolute;left:56px;right:56px;top:170px;bottom:150px;border-radius:56px;background:rgba(0,0,0,0.50);box-shadow:0 32px 90px rgba(0,0,0,0.34);"></div>
              <div style="position:absolute;left:88px;top:205px;width:168px;height:10px;border-radius:999px;background:rgba(255,255,255,0.86);"></div>
              """;

        return $$"""
            <div class="composition" data-format="{{Html(spec.FormatKey)}}" style="position:relative;width:{{width}}px;height:{{height}}px;overflow:hidden;background:#000000;">
              <div data-layer="background" style="position:absolute;inset:0;width:100%;height:100%;overflow:hidden;">
                <img src="{{safeUrl}}" alt="{{safeName}}" data-background-source="{{source}}" style="position:absolute;inset:0;width:100%;height:100%;object-fit:cover;" />
                <div style="position:absolute;inset:0;background:linear-gradient(180deg,rgba(0,0,0,0.18) 0%,rgba(0,0,0,0.35) 46%,rgba(0,0,0,0.62) 100%);"></div>
                {{backgroundDecor}}
              </div>
              <div data-layer="content" style="position:absolute;inset:0;width:100%;height:100%;">
                {{content}}
              </div>
            </div>
            """;
    }

    private static string BuildFlyerTemplateSlots(
        string title,
        string? subtitle,
        string? body,
        string? cta,
        string? signature,
        string headingFont,
        string bodyFont)
    {
        var slots = new List<string>
        {
            Slot(
                "h1",
                "headline",
                title,
                "position:absolute;left:96px;top:255px;width:888px;min-height:260px;" +
                $"font-family:{headingFont},system-ui,sans-serif;font-size:88px;line-height:0.98;" +
                "font-weight:900;letter-spacing:-2.5px;color:#FFFFFF;text-align:left;text-shadow:0 4px 24px rgba(0,0,0,0.58);",
                3)
        };

        AddSlot(slots, "p", "subtitle", subtitle,
            "position:absolute;left:100px;top:575px;width:825px;min-height:122px;" +
            $"font-family:{bodyFont},system-ui,sans-serif;font-size:36px;line-height:1.16;" +
            "font-weight:600;color:#FFFFFF;text-align:left;text-shadow:0 3px 18px rgba(0,0,0,0.60);",
            3);

        AddSlot(slots, "p", "body", body,
            "position:absolute;left:100px;top:745px;width:825px;min-height:210px;" +
            $"font-family:{bodyFont},system-ui,sans-serif;font-size:30px;line-height:1.24;" +
            "font-weight:400;color:#F8FAFC;text-align:left;text-shadow:0 3px 18px rgba(0,0,0,0.60);",
            5);

        AddSlot(slots, "p", "cta", cta,
            "position:absolute;left:100px;bottom:245px;width:640px;min-height:58px;" +
            $"font-family:{bodyFont},system-ui,sans-serif;font-size:34px;line-height:1.05;" +
            "font-weight:800;color:#FFFFFF;text-transform:uppercase;letter-spacing:1.2px;text-shadow:0 3px 18px rgba(0,0,0,0.60);",
            2);

        AddSlot(slots, "p", "signature", signature,
            "position:absolute;right:98px;bottom:190px;width:460px;min-height:42px;" +
            $"font-family:{bodyFont},system-ui,sans-serif;font-size:25px;line-height:1.1;" +
            "font-weight:600;color:#E5E7EB;text-align:right;text-shadow:0 3px 18px rgba(0,0,0,0.60);",
            1);

        return string.Join(Environment.NewLine, slots);
    }

    private static string BuildCarouselTemplateSlots(
        string title,
        string? subtitle,
        string? cta,
        string headingFont,
        string bodyFont)
    {
        var slots = new List<string>
        {
            Slot(
                "h1",
                "title",
                title,
                "position:absolute;left:104px;top:235px;width:872px;min-height:230px;" +
                $"font-family:{headingFont},system-ui,sans-serif;font-size:74px;line-height:1.02;" +
                "font-weight:900;letter-spacing:-1.8px;color:#FFFFFF;text-align:left;text-shadow:0 4px 24px rgba(0,0,0,0.58);",
                3)
        };

        AddSlot(slots, "p", "subtitle", subtitle,
            "position:absolute;left:108px;top:540px;width:815px;min-height:310px;" +
            $"font-family:{bodyFont},system-ui,sans-serif;font-size:38px;line-height:1.22;" +
            "font-weight:500;color:#F8FAFC;text-align:left;text-shadow:0 3px 18px rgba(0,0,0,0.62);",
            6);

        AddSlot(slots, "p", "cta", cta,
            "position:absolute;left:108px;bottom:220px;width:760px;min-height:58px;" +
            $"font-family:{bodyFont},system-ui,sans-serif;font-size:28px;line-height:1.1;" +
            "font-weight:800;color:#FFFFFF;text-transform:uppercase;letter-spacing:1.1px;text-shadow:0 3px 18px rgba(0,0,0,0.62);",
            2);

        return string.Join(Environment.NewLine, slots);
    }

    private static void AddSlot(
        ICollection<string> slots,
        string tag,
        string slotName,
        string? value,
        string style,
        int maxLines)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            slots.Add(Slot(tag, slotName, value, style, maxLines));
        }
    }

    private static string Slot(string tag, string slotName, string value, string style, int maxLines) =>
        $"<{tag} class=\"slot\" data-slot=\"{Html(slotName)}\" data-max-lines=\"{maxLines}\" style=\"{style}\">{Html(value)}</{tag}>";

    private static string? FirstSlot(IReadOnlyDictionary<string, string> slots, IReadOnlyList<string> names)
    {
        foreach (var name in names)
        {
            if (slots.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static string Html(string value) => System.Net.WebUtility.HtmlEncode(value);

    private static string CssString(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? "system-ui"
            : "'" + value.Replace("\\", "\\\\").Replace("'", "\\'", StringComparison.Ordinal) + "'";

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
        IReadOnlyList<string> InferredFields,
        string? AiErrorCode);

    private sealed record EnforcedBackgroundTemplate(
        string Url,
        string Name,
        string Source);
}
