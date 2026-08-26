using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Incoders.Hive.Application.Abstractions.Observability;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Background;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Formats.Carousel;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Formats.Story;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Orchestration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Visual;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Visual.Fonts;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ContentFormats;
using Incoders.Hive.Domain.Social.ContentGeneration;
using Incoders.Hive.Domain.Social.SingleAnalyzer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

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
    /// Designed-raster formats: all visual (image/video) catalog deliverables.
    /// Derived from <see cref="FormatCatalog"/> so new catalog rows are picked up automatically.
    /// Text formats (hook, caption, cta-variation, reel-script, content-idea) bypass the designer.
    /// Note: the legacy "carousel" alias is NOT included — only canonical keys appear here.
    /// Legacy "carousel" rows are handled at assemble time via ResolveForAssembly (ADR-3.6).
    /// </summary>
    private static readonly HashSet<string> DesignedRasterFormats =
        FormatCatalog.All()
            .Where(d => d.IsVisual)
            .Select(d => d.FormatKey)
            .Concat(["story", "feed-post"])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private readonly IBackgroundStrategyResolver? _backgroundStrategyResolver;
    private readonly IClientAestheticDescriptorReader? _aestheticDescriptorReader;
    private readonly IClientAestheticProfileReader? _aestheticProfileReader;
    private readonly IClientAestheticEnsureFreshService? _aestheticEnsureFreshService;
    private readonly IClientAestheticImageBackgroundService? _imageBackgroundService;
    private readonly ContentGenerationAestheticGatingOptions _aestheticGatingOptions;
    private readonly IAestheticAssetCropExtractor? _brandMarkAssetExtractor;

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
        ILogger<ContentGenerationJobProcessor>? logger = null,
        // WU-4: injected optionally so existing unit tests and DI registrations compile without change.
        // When null, the processor falls back to the legacy per-slide BackgroundSelector.Select path
        // (UNSET / no-policy behavior identical to pre-WU-4). Full wiring is task 5.6.
        IBackgroundStrategyResolver? backgroundStrategyResolver = null,
        // WU-5 (legacy): CASE C aesthetic descriptor reader — retained for back-compat while
        // IClientAestheticProfileReader rolls out. Null = no descriptors (degrades to Creative).
        IClientAestheticDescriptorReader? aestheticDescriptorReader = null,
        // WU-2: widened CASE C profile reader (replaces IClientAestheticDescriptorReader).
        // When non-null, this reader takes precedence over aestheticDescriptorReader.
        // Injected optionally so all existing unit tests compile without change.
        IClientAestheticProfileReader? aestheticProfileReader = null,
        // WU-4 (CAG-CG-04): non-blocking ensure-fresh service for CASE C jobs.
        // Injected optionally so existing call sites and unit tests compile without change.
        // When null, the ensure-fresh step is skipped (no staleness check, no enqueue).
        IClientAestheticEnsureFreshService? aestheticEnsureFreshService = null,
        // WU-8 (CIG-CG-01..04, IMG-05, IMG-06): creative-background producer for ClientAesthetic carousels.
        // NULLABLE: when null (no IMAGE route, policy ≠ ClientAesthetic, or service not registered) the
        // existing CSS designer path runs unchanged — rollback-safe. See design §4.1.
        IClientAestheticImageBackgroundService? imageBackgroundService = null,
        // issue #67 (BAPA-CG-01/02): aesthetic-gating config (max wait, feature toggle).
        // Injected optionally so existing unit tests and DI registrations compile without change.
        // When null, falls back to the documented defaults (BlockOnFirstTime=true, MaxWaitSeconds=600).
        IOptions<ContentGenerationAestheticGatingOptions>? aestheticGatingOptions = null,
        // WU-7b (D13; generalised in D3): icon-mark crop extractor over the element-agnostic
        // IAestheticAssetCropExtractor port. NULLABLE — when null (adapter not registered, or
        // a pre-WU-7b test) the icon branch degrades to the PositionHint+ColorHex chip exactly as
        // it did before this port existed; the "unknown"/"text" mark-type paths are unaffected.
        IAestheticAssetCropExtractor? brandMarkAssetExtractor = null)
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
        // decorator throws "ProviderCallContext.Current is null", failing every story/carousel.
        // In production DI injects the scoped instance the decorator reads; the fallback only
        // keeps unit construction simple.
        _providerCallContext = providerCallContext ?? new ProviderCallContext();
        _logger = logger ?? NullLogger<ContentGenerationJobProcessor>.Instance;
        _backgroundStrategyResolver = backgroundStrategyResolver;
        _aestheticDescriptorReader = aestheticDescriptorReader;
        _aestheticProfileReader = aestheticProfileReader;
        _aestheticEnsureFreshService = aestheticEnsureFreshService;
        _imageBackgroundService = imageBackgroundService;
        _aestheticGatingOptions = aestheticGatingOptions?.Value ?? new ContentGenerationAestheticGatingOptions();
        _brandMarkAssetExtractor = brandMarkAssetExtractor;
    }

    /// <summary>
    /// Bounded budget for a terminal-status write made after <c>ct</c> is already cancelled or faulted.
    /// Mirrors <c>IntegrationExecutionPolicy.TelemetryWriteTimeout</c> (commit 0e0f7dd): never
    /// <see cref="CancellationToken.None"/> with no bound, which could let a stalled DB connection hang
    /// shutdown indefinitely — the write is best-effort observability of a job that is already done,
    /// not work anything is still waiting on.
    /// </summary>
    internal static readonly TimeSpan TerminalStatusWriteTimeout = TimeSpan.FromSeconds(5);

    public async Task ProcessAsync(
        Guid jobId,
        CancellationToken ct,
        // Gap 1 (incident job 8757e85a-6acd-49b4-af11-a2d19fbcd3e1, Change 5): supplied by
        // ContentGenerationBackgroundWorker so the cancellation branch below can tell a per-job
        // wall-clock budget timeout apart from host shutdown / any other cancellation. Null (the
        // default) preserves the original generic "cancelled"/"cancelled-partial" classification for
        // any caller — including every existing test — that has no such distinction to offer.
        ContentGenerationJobCancellationContext? cancellationContext = null)
    {
        var job = await _jobs.GetByIdInternalAsync(jobId, ct);
        if (job is null)
        {
            _logger.LogWarning("ContentGeneration: job {JobId} not found — skipping.", jobId);
            return;
        }

        await _jobs.UpdateStatusAsync(job.Id, "Running", null, null, ct);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            await ProcessInternalAsync(job, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            sw.Stop();

            // Per-job timeout or host shutdown. The terminal write MUST use a fresh, BOUNDED token —
            // writing with the already-cancelled ct would cancel SaveChanges too, leaving the job
            // stuck in "Running" forever (the exact "pill never changes" symptom). Bounded (not plain
            // CancellationToken.None) so a wedged DB connection cannot hang shutdown indefinitely.
            //
            // Change 1: by this point every slide that finished before cancellation was already
            // checkpointed durably (see the 5c per-slide persist in ProcessInternalAsync). Re-read
            // that truth from the DB rather than threading a flag through the exception — if any
            // format result exists for this job, the job produced SOMETHING and its terminal status
            // must say so: reuse the existing "Partial" job status (already used for the
            // some-formats-failed case) instead of "Failed", which would hide the safeguarded work
            // from anyone reading the job list.
            bool hasPersistedWork;
            try
            {
                using var statusReadCts = new CancellationTokenSource(ResilientPersistenceTimeout);
                hasPersistedWork = await HasAnyPersistedFormatResultAsync(job.Id, statusReadCts.Token);
            }
            catch (Exception readEx)
            {
                // The read itself is best-effort — if it fails or times out we cannot confirm
                // partial success, so fall back to the pre-existing "Failed" behavior rather than
                // risk hanging shutdown or mislabeling a job that actually produced nothing.
                _logger.LogWarning(readEx,
                    "ContentGeneration: could not determine partial-persistence state for job {JobId} " +
                    "after cancellation. Defaulting terminal status to Failed.", job.Id);
                hasPersistedWork = false;
            }

            var (status, reason, errorCode) = ClassifyCancellation(cancellationContext, sw.Elapsed, hasPersistedWork);

            using var statusWriteCts = new CancellationTokenSource(TerminalStatusWriteTimeout);
            await _jobs.UpdateStatusAsync(job.Id, status, reason, errorCode, statusWriteCts.Token);

            throw;
        }
        catch (Exception ex)
        {
            // Anything that escapes the per-step handling is surfaced verbatim — the user
            // needs the real failure on their screen, not a generic "unexpected-error".
            // Bounded (not CancellationToken.None) — see TerminalStatusWriteTimeout.
            _logger.LogError(ex, "ContentGeneration: unhandled error processing job {JobId}.", job.Id);
            using var statusWriteCts = new CancellationTokenSource(TerminalStatusWriteTimeout);
            await _jobs.UpdateStatusAsync(job.Id, "Failed", FormatException(ex), $"unhandled:{ex.GetType().Name}", statusWriteCts.Token);
        }
    }

    /// <summary>
    /// Resolves the (status, reason, errorCode) triple for a cancelled/timed-out job. When
    /// <paramref name="cancellationContext"/> confirms the job's OWN wall-clock budget fired (its host
    /// token is intact — only the linked <c>CancelAfter</c> could have cancelled <c>ct</c>), the reason
    /// carries the exact elapsed time and the configured limit that was exceeded, and the errorCode is
    /// a distinct "cancelled-timeout[-partial]" — precise enough that an operator (or
    /// <c>EfContentGenerationFailurePolicy</c> / the UI) can tell a budget timeout apart from a manual
    /// cancellation or host shutdown, which keep the pre-existing generic "cancelled[-partial]".
    /// </summary>
    private static (string Status, string Reason, string ErrorCode) ClassifyCancellation(
        ContentGenerationJobCancellationContext? cancellationContext,
        TimeSpan elapsed,
        bool hasPersistedWork)
    {
        var status = hasPersistedWork ? "Partial" : "Failed";

        var isConfirmedBudgetTimeout =
            cancellationContext is not null && !cancellationContext.HostShutdownToken.IsCancellationRequested;

        if (isConfirmedBudgetTimeout)
        {
            var limitMs = (long)cancellationContext!.JobTimeoutLimit.TotalMilliseconds;
            var elapsedMs = (long)elapsed.TotalMilliseconds;
            var reason = hasPersistedWork
                ? $"cancelled-timeout-partial: job exceeded the configured wall-clock limit ({limitMs} ms) " +
                  $"after {elapsedMs} ms, some format(s) already persisted assets"
                : $"cancelled-timeout: job exceeded the configured wall-clock limit ({limitMs} ms) after {elapsedMs} ms";
            var errorCode = hasPersistedWork ? "cancelled-timeout-partial" : "cancelled-timeout";
            return (status, reason, errorCode);
        }

        var genericReason = hasPersistedWork
            ? "cancelled-partial: job was cancelled or timed out after some format(s) already persisted assets"
            : "cancelled";
        var genericErrorCode = hasPersistedWork ? "cancelled-partial" : "cancelled";
        return (status, genericReason, genericErrorCode);
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

        // WU-4 (CAG-CG-04, CAG-CG-06, CAG-CG-07): pre-loop — resolve aesthetic profile BEFORE the
        // orchestrator call so it can be threaded into OrchestratorRequest → GenerationContext → generators.
        // Requires the brand snapshot to read backgroundPolicy. The brand loader is idempotent; the
        // orchestrator will call it again internally (cached in DI scope — no extra I/O on the hot path).
        //
        // ORDERING (design §5.1):
        //   1. Load brand to determine policy.
        //   2. If policy == ClientAesthetic: call EnsureFreshAsync (fire-and-forget, any exception swallowed).
        //   3. Read profile (best-available data post-refresh enqueue).
        //   4. Pass AestheticProfile into OrchestratorRequest below.
        // All other policies → aestheticProfile stays null; generators emit ConcisionConstraint only.
        IReadOnlyList<string> aestheticDescriptors = [];
        ClientAestheticProfile? aestheticProfile = null;
        var preLoopBrand = await _brandLoader.LoadAsync(job.OrganizationId, effectiveClientId.Value, ct);
        var preLoopPolicy = preLoopBrand.Client?.BackgroundSourcingPolicy;

        if (preLoopPolicy == Domain.Social.Clients.BackgroundSourcingPolicy.ClientAesthetic
            && effectiveClientId.HasValue)
        {
            // Step 1 (issue #67, BAPA-CG-01/02, design Decision 1): branch on whether a usable
            // aesthetic is present RIGHT NOW.
            //   - Present (fresh or stale-but-present): unchanged CAG-CG-04 fire-and-forget branch.
            //   - Absent (first-time/empty) AND gating enabled: blocking branch — the job transitions
            //     to AnalyzingProfile and waits, bounded by MaxWaitSeconds, for the just-enqueued/
            //     in-flight analysis. NoOwnAccount/TimedOut/AnalysisFailed end the job Failed
            //     immediately; Ready resumes generation with the freshly analyzed aesthetic.
            if (_aestheticEnsureFreshService is not null)
            {
                var usablePresent = !_aestheticGatingOptions.BlockOnFirstTime
                    || await _aestheticEnsureFreshService.LatestUsableAestheticPresentAsync(
                        job.OrganizationId, effectiveClientId.Value, ct);

                if (usablePresent)
                {
                    // Fire-and-forget; failure MUST NOT fail the job (CAG-CG-04).
                    try
                    {
                        var ensureFreshOutcome = await _aestheticEnsureFreshService.EnsureFreshAsync(
                            job.OrganizationId,
                            effectiveClientId.Value,
                            ct);
                        _logger.LogDebug(
                            "ContentGeneration: EnsureFreshAsync for client {ClientId} (job {JobId}) → {Outcome}.",
                            effectiveClientId.Value, job.Id, ensureFreshOutcome);
                    }
                    catch (Exception ex)
                    {
                        // CAG-CG-04: isolation contract — exception swallowed + logged; generation continues.
                        _logger.LogWarning(ex,
                            "ContentGeneration: EnsureFreshAsync failed for client {ClientId} (job {JobId}). " +
                            "Generation continues with best-available aesthetic data.",
                            effectiveClientId.Value, job.Id);
                    }
                }
                else
                {
                    // No usable aesthetic — block (BAPA-CG-01/02). The job status is written to
                    // AnalyzingProfile ONLY if a real wait begins (onWaitStarted), never for the
                    // NoOwnAccount immediate-fail path.
                    var maxWait = TimeSpan.FromSeconds(_aestheticGatingOptions.MaxWaitSeconds);
                    var outcome = await _aestheticEnsureFreshService.EnsureAvailableBlockingAsync(
                        job.OrganizationId,
                        effectiveClientId.Value,
                        maxWait,
                        onWaitStarted: waitCt => _jobs.UpdateStatusAsync(job.Id, "AnalyzingProfile", null, null, waitCt),
                        ct);

                    switch (outcome)
                    {
                        // The three codes are also embedded as a "code: message" prefix in the
                        // StatusReason text (mirrors the existing render_failed[...] convention)
                        // because ContentGenerationJobStatusResponse does NOT surface a separate
                        // ErrorCode field to the frontend — resolveGenerationFailureError matches
                        // codes by substring inside statusReason (design Decision 5).
                        case EnsureAvailableOutcome.NoOwnAccount:
                            await _jobs.UpdateStatusAsync(
                                job.Id,
                                "Failed",
                                "aesthetic-own-account-required: This client uses the Client Aesthetic modality but has no own account configured. Add the client's own Instagram account to generate in this modality.",
                                "aesthetic-own-account-required",
                                CancellationToken.None);
                            return;
                        case EnsureAvailableOutcome.TimedOut:
                            await _jobs.UpdateStatusAsync(
                                job.Id,
                                "Failed",
                                "aesthetic-analysis-timeout: Profile aesthetic analysis did not finish in time. Please try again in a few minutes.",
                                "aesthetic-analysis-timeout",
                                CancellationToken.None);
                            return;
                        case EnsureAvailableOutcome.AnalysisFailed:
                            await _jobs.UpdateStatusAsync(
                                job.Id,
                                "Failed",
                                "aesthetic-analysis-failed: Profile aesthetic analysis could not be completed for this client. Check the client's own account and try again.",
                                "aesthetic-analysis-failed",
                                CancellationToken.None);
                            return;
                        case EnsureAvailableOutcome.Ready:
                        default:
                            // Resume: write Running (out of AnalyzingProfile) and proceed to read
                            // the now-usable profile below exactly as today.
                            await _jobs.UpdateStatusAsync(job.Id, "Running", null, null, ct);
                            break;
                    }
                }
            }

            // Step 2: read profile (IClientAestheticProfileReader WU-2 takes precedence over legacy reader).
            if (_aestheticProfileReader is not null)
            {
                aestheticProfile = await _aestheticProfileReader.GetProfileAsync(
                    job.OrganizationId,
                    effectiveClientId.Value,
                    ct);
                aestheticDescriptors = aestheticProfile?.BackgroundDescriptors ?? [];
            }
            else if (_aestheticDescriptorReader is not null)
            {
                aestheticDescriptors = await _aestheticDescriptorReader.GetDescriptorsAsync(
                    job.OrganizationId,
                    effectiveClientId.Value,
                    ct);
            }
        }

        // WU-9 (CAG2-CG-01): for a ClientAesthetic job with an aesthetic profile, build the
        // creatives-first design context once and thread it into every designer call below. Reference
        // images / palette / descriptors come from the enriched profile (empty until the analyzer
        // enrichment lands — the designer degrades gracefully). The variation seed is the job id so a
        // package varies within the client's coherent style (CAG2-CG-03).
        ClientAestheticDesignContext? aestheticDesignContext = null;
        if (preLoopPolicy == Domain.Social.Clients.BackgroundSourcingPolicy.ClientAesthetic
            && aestheticProfile is not null)
        {
            aestheticDesignContext = new ClientAestheticDesignContext(
                ReferenceImages: aestheticProfile.ReferenceImages ?? [],
                Palette: aestheticProfile.Palette,
                StyleDescriptors: aestheticProfile.StyleDescriptors ?? [],
                Motifs: aestheticProfile.Motifs ?? [],
                VariationSeed: job.Id.ToString(),
                // FFS-CG-02: threaded so the placement stage below can emit the editable
                // brand-mark slot; null → no mark rendered (cold-start parity, FFS-CG-03-a).
                BrandMark: aestheticProfile.BrandMark,
                // client-driven-decoration: lets the designer suppress its default shapes/blobs/glow
                // for a client whose measured corpus contains no decorated slides.
                ImageTypeRatio: aestheticProfile.ImageTypeRatio,
                // D4/D7: the client's observed per-role typography, so AiVisualDesigner can
                // supplement AllowedFonts with the client's own classes instead of the brand kit alone.
                Typography: aestheticProfile.Typography,
                // WU-3b (D3/D5): the client's observed per-slide photo-transparency signal, threaded
                // so the render loop can resolve it per specIndex via
                // CreativeSlideIndexResolver.ResolveTransparencyForSlide.
                BackgroundTransparency: aestheticProfile.BackgroundTransparency);
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
                    RegenerationObjection: job.RegenerationObjection,
                    // WU-4 (CAG-CG-07): thread profile so orchestrator → GenerationContext → generators
                    // can emit advisory aesthetic guidance (additive to ConcisionConstraint).
                    AestheticProfile: aestheticProfile),
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

        // WU-4: resolve background strategy ONCE per job (before the format loop).
        // policy = null/Unset → legacy 3-tier behavior (backward-compatible default).
        // When no resolver is registered, falls back entirely to the legacy BackgroundSelector path.
        var backgroundPolicy = brand.Client?.BackgroundSourcingPolicy;
        var backgroundStrategy = _backgroundStrategyResolver?.Resolve(backgroundPolicy);
        var backgroundSeedToken = job.Id != Guid.Empty
            ? job.Id.ToString()
            : DateTime.UtcNow.ToString("yyyy-MM-dd");

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

            // WU-7: advisory concision check — log-only, never rejects or retries (CG-CON-03).
            // Runs after payload is confirmed; pipeline continues regardless of violations.
            CheckConcisionAdvisory(job.Id, formatKey, step.Payload);

            // Change 1: declared OUTSIDE the try block (unlike formatRendered/formatStorageFailed)
            // so the catch blocks below can inspect how many slides already checkpointed successfully
            // before deciding whether it is safe to call PersistRenderFailureAsync — see the guard
            // comments in those catch blocks.
            var formatAssetRows = new List<FormatAssetRow>();

            try
            {
                // WU-4 C4: compute ONE background plan per format BEFORE the slide loop.
                // This eliminates per-slide BackgroundSelector.Select calls and ensures all
                // slides of one carousel share the same coherent background decision.
                CarouselBackgroundPlan? backgroundPlan = null;
                if (backgroundStrategy is not null)
                {
                    var slideCount = TryGetSlideCount(step.Payload, formatKey);
                    backgroundPlan = backgroundStrategy.Plan(new BackgroundPlanRequest(
                        brand.Brand!,
                        formatKey,
                        slideCount,
                        backgroundSeedToken,
                        ClientAestheticDescriptors: aestheticDescriptors));
                }

                var formatRendered = false;
                var formatStorageFailed = false;
                Guid formatResultId;

                // WU-1 (B3 D1+D3): one editable composition envelope (validated markup + canvas +
                // per-slot geometry + background data-URI) is captured PER successfully rendered
                // slide and persisted in composition_spec_json. Single-asset formats (story) yield
                // one; carousel yields one per slide so every slide stays editable on export.
                var capturedEnvelopes = new List<CompositionSpecEnvelope>();

                // We need the format result ID to associate asset rows.
                // Generate a stable ID now (it will be upserted into the DB after render).
                formatResultId = existingByFormat.TryGetValue(formatKey, out var existingResult)
                    ? existingResult.Id
                    : Guid.NewGuid();

                // WU-8 (CIG-CG-01..04, IMG-05, IMG-06): creative-background build step.
                // For ClientAesthetic CAROUSEL formats with an image background service registered:
                //   1. Resolve the creative slide indices for this format's slide count.
                //   2. If non-empty, call BuildCreativeBackgroundsAsync (returns only successes).
                //   3. Overlay ForcedDefault onto the plan for each successful creative slide.
                //
                // FIX-1: a ProviderCallContext scope MUST be active around this call because
                // BuildCreativeBackgroundsAsync transitively invokes both IAiCompletionGateway
                // (ImagePromptWriter) and IImageGenerationGateway — both wrapped by instrumentation
                // decorators that throw when ProviderCallContext.Current is null. The scope mirrors
                // the designer scope opened in the per-slide loop below (same arguments/source).
                //
                // FIX-2 (CG-COH-02): this block runs BEFORE ComposeManyAsync so the overlay is
                // applied to backgroundPlan first, and ComposeManyAsync receives the AI URLs rather
                // than the stale CSS plan — keeping the composed spec coherent with the designer path.
                //
                // FIX-6: scoped to carousel-layout formats only (the feature is carousel-specific).
                // Non-carousel ClientAesthetic formats use the CSS designer path unchanged.
                //
                // Resilience (IMG-05): any non-cancellation exception from the service is caught, logged
                // as a warning, and results in an empty map — all slides degrade to the CSS designer path.
                // The CSS path MUST remain byte-for-byte unchanged when service is null or policy ≠ ClientAesthetic.
                //
                // Observability: every reason this block is skipped is otherwise SILENT — a ClientAesthetic
                // carousel then finishes looking like a normal success but with zero AI image backgrounds,
                // which is indistinguishable in the logs from "worked as intended". Emit one explicit line
                // per skip so a support/forensic pass sees WHY no photo backgrounds were generated.
                var isClientAestheticCarousel =
                    preLoopPolicy == Domain.Social.Clients.BackgroundSourcingPolicy.ClientAesthetic
                    && FormatCatalog.ResolveForAssembly(formatKey)?.Layout == Layout.Carousel;

                // Single source of truth for "this slide received a generated image background". The
                // per-slide FlatColor short-circuit below MUST NOT override these slides with flat CSS
                // (that discarded the generated image and produced the all-flat carousel we observed).
                var slidesWithGeneratedImage = new HashSet<int>();

                if (isClientAestheticCarousel
                    && (_imageBackgroundService is null || backgroundPlan is null || aestheticProfile is null))
                {
                    _logger.LogWarning(
                        "ContentGeneration: AI image backgrounds skipped for ClientAesthetic carousel {FormatKey} " +
                        "(job {JobId}) — missing precondition: imageService={HasService} backgroundPlan={HasPlan} " +
                        "aestheticProfile={HasProfile}. All slides use the CSS background path.",
                        formatKey, job.Id,
                        _imageBackgroundService is not null, backgroundPlan is not null, aestheticProfile is not null);
                }

                if (isClientAestheticCarousel
                    && _imageBackgroundService is not null
                    && backgroundPlan is not null
                    && aestheticProfile is not null)
                {
                    var formatSlideCount = TryGetSlideCount(step.Payload, formatKey);
                    // Route AI image generation to the client's Photo slides only, derived from the
                    // captured BackgroundPattern. FlatColor slides must NOT be conditioned on a
                    // photographic reference (the two conditionings conflict and the sampler diverges
                    // to noise) — they fall through to the CSS/palette path (ColorPairings) instead.
                    // Null/empty pattern → ResolveFromPattern delegates to the {first,last} cold-start default.
                    // When a photo ratio is known, it is AUTHORITATIVE for the COUNT (owner-approved rule
                    // change): the pattern only supplies preferred positions. This can now yield FEWER
                    // creative slides than the pattern suggests (e.g. ratio 0 → zero), not just more — the
                    // measured proportion of the client's real corpus wins over the per-slide guess.
                    var creativeIndices = CreativeSlideIndexResolver.ResolveFromPattern(
                        aestheticProfile.BackgroundPattern, formatSlideCount, aestheticProfile.ImageTypeRatio?.Photo);

                    if (creativeIndices.Count > 0)
                    {
                        IReadOnlyDictionary<int, BackgroundSelection.ForcedDefault> creativeMap;
                        try
                        {
                            var imgRequest = new ClientAestheticImageRequest(
                                OrganizationId: job.OrganizationId,
                                ClientId: effectiveClientId,
                                JobId: job.Id,
                                FormatResultId: formatResultId,
                                FormatKey: formatKey,
                                SlideCount: formatSlideCount,
                                CreativeSlideIndices: creativeIndices,
                                Aesthetic: aestheticProfile,
                                // Thread the per-slide copy so the image prompt writer grounds each
                                // generated background as a metaphor of THAT slide's actual message
                                // (the copy is produced before backgrounds run). Empty when the payload
                                // carries no per-slide copy — the writer then emulates style only.
                                PerSlideCopySummaries: TryGetPerSlideCopySummaries(step.Payload));

                            // FIX-1: open a ProviderCallContext scope so both gateways
                            // (IAiCompletionGateway for the prompt-writer, IImageGenerationGateway for
                            // image render) have an active context. The scope mirrors the designer scope
                            // in the per-slide loop below (same workflow/org/client arguments).
                            using (_providerCallContext.Begin(
                                job.OrganizationId,
                                effectiveClientId,
                                workflowKey: "content-generation",
                                pipelineStepKey: $"image-background:{formatKey}",
                                promptVersion: "image-gen/v1",
                                correlationId: job.CorrelationId))
                            {
                                creativeMap = await _imageBackgroundService.BuildCreativeBackgroundsAsync(
                                    imgRequest, ct);
                            }
                        }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested)
                        {
                            // FIX-3: genuine job cancellation — re-throw so the job aborts cleanly
                            // rather than silently continuing with CSS backgrounds.
                            throw;
                        }
                        catch (Exception ex)
                        {
                            // IMG-05: exception isolation — log warning; all slides degrade to CSS.
                            _logger.LogWarning(ex,
                                "ContentGeneration: image background service threw for format {FormatKey} " +
                                "(job {JobId}). All slides will degrade to the CSS designer background.",
                                formatKey, job.Id);
                            creativeMap = new Dictionary<int, BackgroundSelection.ForcedDefault>();
                        }

                        if (creativeMap.Count > 0)
                        {
                            // FIX-2: overlay applied to backgroundPlan BEFORE ComposeManyAsync runs
                            // so the composer receives AI URLs, not the stale CSS plan (CG-COH-02).
                            backgroundPlan = backgroundPlan.WithForcedDefaults(creativeMap);
                            foreach (var slide in creativeMap.Keys)
                            {
                                slidesWithGeneratedImage.Add(slide);
                            }
                        }
                    }
                    else
                    {
                        // Observability: the ClientAesthetic BackgroundPattern classified every slide as
                        // non-photo (e.g. all FlatColor), so NO AI image background is requested and the whole
                        // carousel uses the CSS/flat path. This is the most common silent "no image backgrounds"
                        // outcome (see memory clientaesthetic-image-gate-skipped): the profile can carry many
                        // reference images and a high photo ratio yet still resolve a flat-only per-slide pattern.
                        _logger.LogInformation(
                            "ContentGeneration: AI image backgrounds skipped for ClientAesthetic carousel {FormatKey} " +
                            "(job {JobId}) — BackgroundPattern resolved 0 photo slides (SlideCount={SlideCount}); " +
                            "every slide uses the CSS background path.",
                            formatKey, job.Id, formatSlideCount);
                    }
                }

                // Fan-out: carousel returns N specs, single-asset formats return 1 spec.
                // The plan (when available) is threaded into the composer so asset slot URLs
                // reflect the same per-slide background decision enforced below (CG-COH-02).
                // FIX-2: ComposeManyAsync runs AFTER the ForcedDefaults overlay above so the
                // composer sees AI image URLs for creative slides, not the stale CSS plan.
                // Issue #81: thread the already-resolved ClientAesthetic profile through so the
                // carousel composer's profile-aware typography resolution (real observed
                // family/weight) is reachable from production, not just from direct unit tests.
                var specs = await _composer.ComposeManyAsync(formatKey, step.Payload, brand, ct, backgroundPlan, aestheticProfile);
                foreach (var f in _composer.GetInferredFields())
                {
                    inferred.Add(f);
                }

                for (var specIndex = 0; specIndex < specs.Count; specIndex++)
                {
                    var spec = specs[specIndex];

                    // Per-slide capture — built into an envelope at the end of a successful iteration.
                    string? capturedMarkup = null;
                    CompositionDimensions? capturedDimensions = null;
                    IReadOnlyList<SlotGeometryEntry> capturedGeometry = [];
                    string? capturedBackgroundDataUri = null;
                    // CAFF-CG-04 (D2): set only when trigger A or trigger B fired for THIS slide.
                    // Null for every other slide, including the pre-existing FlatColor short-circuit
                    // (that path is not a degrade — it is the proven, always-faithful treatment).
                    string? capturedDegradeReason = null;
                    // WU-7c (D11, F3): storage-path references the Chromium render pass must inline as
                    // self-contained data URIs before rendering — it has no base URL to fetch a bare
                    // storage path from. Originally a single `renderBackgroundStoragePath` (the enforced
                    // background only); generalised to a set because a successfully-extracted brand-mark
                    // icon crop (BuildBrandMarkSlot below) is a SECOND independent reference. The
                    // persisted markup/DB rows keep the lightweight storage-path references — only the
                    // transient render-pass copy gets the inlined data URIs, so we do not bloat
                    // content_generation_format_results / studio drafts with base64.
                    var renderInlineRefs = new HashSet<string>(StringComparer.Ordinal);

                    // Designer + guardrail stage for designed-raster formats (story, carousel).
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
                        // FFS-CG-01: deterministic FlatColor short-circuit. For a ClientAesthetic
                        // carousel slide whose per-slide analyzed kind (mapped from
                        // ClientAestheticProfile.BackgroundPattern via the SAME proportional formula
                        // as CreativeSlideIndexResolver.ResolveFromPattern) is FlatColor AND the profile
                        // has resolved ColorPairings, render a flat background+text pairing from
                        // CompositionDefaults.ResolvePalette and SKIP the AI designer entirely — no
                        // gradient/blob/glow/grain, no AI provider call, no usage-ledger event (the
                        // skipped call correctly emits none). Gated to carousel-layout formats only,
                        // mirroring the existing carousel-only gate above (WU-8).
                        //
                        // Cold-start (BackgroundPattern or ColorPairings absent) falls through
                        // UNCHANGED to the existing designer+guardrail flow below — byte-identical to
                        // pre-change behavior (FFS-CG-03-a).
                        var isCarouselLayout = FormatCatalog.ResolveForAssembly(formatKey)?.Layout == Layout.Carousel;
                        var mappedFlatKind = isCarouselLayout
                            ? CreativeSlideIndexResolver.ResolveKindForSlide(
                                aestheticProfile?.BackgroundPattern, specs.Count, specIndex + 1)
                            : null;

                        // FFS-CG-02: the resolved text color for THIS slide, when known (flat path).
                        // Used as the brand-mark's default color-source when the extractor gave no
                        // explicit ColorHex. Left null on the AI/photo path — the placement stage
                        // below falls back to a profile-level default in that case.
                        string? slideTextColorHex = null;

                        // CAFF-CG-01/02 (D1): shared eligibility precondition for BOTH degrade triggers —
                        // ClientAesthetic policy, carousel layout, and resolved ColorPairings (without
                        // ColorPairings there is nothing faithful to degrade to; cold-start parity). The
                        // processor branches ONLY on ClientAestheticDegradeDecision's pure predicates —
                        // no degrade logic lives inline here.
                        var hasColorPairings = aestheticProfile?.ColorPairings is { Count: > 0 };
                        var clientAestheticDegradeEligible = ClientAestheticDegradeDecision.IsEligible(
                            preLoopPolicy, isCarouselLayout, hasColorPairings);

                        // CAFF-CG-01/02: single deterministic palette-backed flat-treatment renderer,
                        // reused by the pre-existing FlatColor short-circuit AND both new degrade triggers
                        // — its output is identical to the short-circuit's (the degrade IS the proven flat
                        // path), written once instead of three times.
                        void RenderFaithfulFlat()
                        {
                            var paletteInferred = new InferenceAccumulator();
                            var flatPalette = CompositionDefaults.ResolvePalette(
                                brand, aestheticProfile, specIndex + 1, paletteInferred);
                            foreach (var f in paletteInferred.Fields)
                            {
                                inferred.Add(f);
                            }

                            var flatMarkup = BuildFlatColorMarkup(spec, flatPalette, aestheticDesignContext);

                            spec = spec with
                            {
                                OutputKind = CompositionOutputKind.Html,
                                DesignerMarkup = flatMarkup,
                            };

                            capturedMarkup = flatMarkup;
                            capturedDimensions = spec.Dimensions;
                            slideTextColorHex = CssColor(flatPalette.Text, CompositionDefaults.Palette.Text);
                        }

                        // A slide that received a generated image background must keep it: the ratio floor
                        // can mark an all-flat-pattern slide as creative, and ResolveKindForSlide (pattern-
                        // only) would still map it to FlatColor. Without this guard the short-circuit would
                        // overwrite the generated image with flat CSS — the exact all-flat carousel observed.
                        if (preLoopPolicy == Domain.Social.Clients.BackgroundSourcingPolicy.ClientAesthetic
                            && mappedFlatKind == SlideBackgroundKind.FlatColor
                            && !slidesWithGeneratedImage.Contains(specIndex + 1)
                            && aestheticProfile?.ColorPairings is { Count: > 0 })
                        {
                            // UNCHANGED, FIRST: this short-circuit runs before either new trigger is even
                            // evaluated — a FlatColor slide can never reach trigger A or trigger B
                            // (CAFF-CG-03).
                            RenderFaithfulFlat();
                        }
                        else
                        {
                            // CAFF-CG-01 (trigger A): the pattern calls this slide Photo, but no generated
                            // image reached it. Route to the deterministic flat treatment WITHOUT resolving
                            // a BackgroundSelection or calling the AI designer at all.
                            var triggerA = ClientAestheticDegradeDecision.TriggerA(
                                clientAestheticDegradeEligible,
                                mappedFlatKind,
                                hasGeneratedImage: slidesWithGeneratedImage.Contains(specIndex + 1));

                            if (triggerA != SlideDegradeTrigger.None)
                            {
                                RenderFaithfulFlat();
                                capturedDegradeReason = SlideDegradeReason.PhotoImageUnavailable;
                            }
                            else
                            {
                            // WU-4 C4: use the pre-computed plan for this slide rather than calling
                            // BackgroundSelector.Select independently per slide. When no resolver is
                            // registered (legacy path), fall back to the 3-tier selector directly.
                            BackgroundSelection selection;
                            if (backgroundPlan is not null)
                            {
                                selection = backgroundPlan.ForSlide(specIndex + 1);
                            }
                            else
                            {
                                var backgroundTemplates = brand.Brand?.BackgroundTemplates
                                    ?? Array.Empty<BrandBackgroundTemplateSnapshot>();
                                selection = Domain.Social.ContentGeneration.BackgroundSelector.Select(
                                    backgroundTemplates, formatKey);
                            }

                            // Design stage. OrganizationId is threaded per-job so the designer's AI
                            // provider call resolves credentials for the correct tenant.
                            // A ProviderCallContext scope MUST be active around this call: the
                            // instrumentation decorator reads ProviderCallContext.Current to write the
                            // usage-ledger event and throws if it is null. The content generators open
                            // their own scope inside the orchestrator; the designer runs here, in the
                            // render loop, so the scope must be opened explicitly.
                            //
                            // For ClientAesthetic jobs the client's REAL reference creatives are passed to the
                            // designer as VISION input (see AiVisualDesigner), so it CREATES NEW art in the
                            // client's visual line. We never composite the client's finished slides verbatim as
                            // backgrounds — that copies their posts (text-over-text) instead of emulating style.
                            string? aiMarkup = null;
                            {
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
                                        job.OrganizationId, spec, brand.Brand!, selection, ct, aestheticDesignContext);
                                }

                                // Resilience contract: content generation MUST produce the asset regardless
                                // of which provider/model the org configured. The copy slots are already
                                // generated, so a designer error OR a guardrail rejection is ADVISORY here —
                                // we log it and fall back to a deterministic, brand-bound composition rather
                                // than failing the format. (AI-art quality is a separate concern from "the
                                // flow must complete for any model/agent".)
                                if (designed.IsSuccess)
                                {
                                    // R4.2: normalize CSS-generic-only font-family declarations to the
                                    // brand typeface BEFORE guardrail validation, which is now a HARD
                                    // gate on typography (R4.3). This is the image/AI path only — the
                                    // flat path (BuildFlatColorMarkup) already applies spec.Typography
                                    // deterministically and is never routed through the guardrail.
                                    var catalog = FontCatalog.For(brand.Brand!.Typography);
                                    var (normalizedMarkup, _) = TypographyNormalizer.Normalize(
                                        designed.Value.Markup, spec.Typography, catalog);
                                    var normalizedOutput = designed.Value with { Markup = normalizedMarkup };

                                    var guarded = _guardrails.Validate(normalizedOutput, brand.Brand!, spec);
                                    if (guarded.IsSuccess)
                                    {
                                        aiMarkup = guarded.Value.Markup;
                                    }
                                    else
                                    {
                                        _logger.LogWarning(
                                            "ContentGeneration: guardrail rejected AI design for format {FormatKey} spec {SpecIndex} (job {JobId}): {Detail}. Falling back to a deterministic brand-bound composition.",
                                            formatKey, specIndex, job.Id, $"{guarded.Error.Code}: {guarded.Error.MessageKey}");
                                    }
                                }
                                else
                                {
                                    _logger.LogWarning(
                                        "ContentGeneration: AI designer failed for format {FormatKey} spec {SpecIndex} (job {JobId}): {Detail}. Falling back to a deterministic brand-bound composition.",
                                        formatKey, specIndex, job.Id, $"{designed.Error.Code}: {designed.Error.MessageKey}");
                                }
                            }

                            // Resolve the final markup. Priority:
                            //   1. Enforced brand background → deterministic template (the brand-manual
                            //      template is hard-enforced over whatever the AI invented).
                            //   2. AI markup that passed guardrails → use the design.
                            //   3. Otherwise → deterministic brand-bound composition built from the copy
                            //      slots (Creative gradient). This is the resilience backstop that
                            //      guarantees the story/carousel is produced for ANY model.
                            // Resolve the final markup. Priority:
                            //   1. Enforced brand background → deterministic template (the brand-manual
                            //      template is hard-enforced over whatever the AI invented).
                            //   2. AI markup that passed guardrails → use the design.
                            //   3. CAFF-CG-02 (trigger B): the designer produced nothing usable (error OR
                            //      guardrail rejection) AND no enforced background exists — for an eligible
                            //      ClientAesthetic slide, route to the same deterministic flat treatment as
                            //      trigger A, replacing the unconditional rgba(0,0,0,0.52) scrim card.
                            //   4. Otherwise → the pre-existing scrim-card resilience backstop, UNCHANGED for
                            //      every non-eligible (non-ClientAesthetic, non-carousel, or cold-start) job.
                            var enforcedBackground = ResolveEnforcedBackground(selection);
                            var triggerB = ClientAestheticDegradeDecision.TriggerB(
                                clientAestheticDegradeEligible,
                                hasEnforcedBackground: enforcedBackground is not null,
                                hasUsableAiMarkup: aiMarkup is not null);

                            if (triggerB != SlideDegradeTrigger.None)
                            {
                                RenderFaithfulFlat();
                                capturedDegradeReason = SlideDegradeReason.DesignerUnusable;
                            }
                            else
                            {
                                string validatedMarkup;
                                if (enforcedBackground is not null)
                                {
                                    // Persist the LIGHTWEIGHT markup that references the background by its storage
                                    // path (not a multi-MB base64 data URI). The Chromium renderer can't fetch a
                                    // bare storage path (no base URL), so the data URI is inlined transiently for
                                    // the render pass only (renderSpec below) — keeping the persisted markup, and
                                    // therefore the DB rows, small.
                                    // WU-3b (D3/D5): resolved per specIndex — the SAME proportional
                                    // mapping formula ResolveKindForSlide already applies, so the two
                                    // signals never drift for a carousel whose length differs from the
                                    // analyzed corpus. This is the enforced-background case, which is
                                    // where a real generated photo actually renders.
                                    var slideTransparency = CreativeSlideIndexResolver.ResolveTransparencyForSlide(
                                        aestheticDesignContext?.BackgroundTransparency, specs.Count, specIndex + 1);
                                    validatedMarkup = BuildTemplateBackedMarkup(
                                        spec, enforcedBackground, aestheticDesignContext, slideTransparency);
                                    renderInlineRefs.Add(enforcedBackground.Url);
                                }
                                else if (aiMarkup is not null)
                                {
                                    validatedMarkup = aiMarkup;
                                }
                                else
                                {
                                    // Legacy resilience backstop — reached only when trigger B did NOT fire
                                    // (non-ClientAesthetic, non-carousel, or cold-start job). UNCHANGED.
                                    validatedMarkup = BuildTemplateBackedMarkup(spec, background: null, aestheticDesignContext);
                                }

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
                            }
                        }

                        // FFS-CG-02: brand-mark placement. ONE deterministic, editable content-layer
                        // data-slot="brand-mark" element, injected identically whichever branch above
                        // resolved the markup (FlatColor short-circuit OR AI/photo designer path) — the
                        // AI designer NEVER draws/places a mark itself (its prompt explicitly defers
                        // this to a separately-supplied editable element; see AiVisualDesigner). Absent,
                        // unrecognised, or ambiguous signals render NONE (FFS-CG-02-b/c;
                        // FFS-CG-03-a cold-start parity).
                        if (aestheticDesignContext?.BrandMark is { Present: true } brandMark
                            && capturedMarkup is not null)
                        {
                            var defaultBrandMarkColorHex = slideTextColorHex
                                ?? CssColor(aestheticProfile?.ColorPairings?.FirstOrDefault()?.TextHex, "#FFFFFF");
                            // CAGH-CG-04: text-mark rendering reuses the client's already-resolved
                            // caption typography (family/weight/size) — a signature is caption-scale.
                            var brandMarkCaptionFont = CssString(spec.Typography.EffectiveCaptionFamily);
                            var brandMarkCaptionWeight = spec.Typography.CaptionWeight;
                            var brandMarkCaptionSize = spec.Typography.CaptionSize;

                            // WU-7b/7c (D13, CAGH-CG-05-a/06-a): icon-type marks with a valid source
                            // image + bounding box are crop-extracted now so the durable asset exists
                            // before BuildBrandMarkSlot renders it. Extraction failure degrades silently
                            // to the pre-existing chip below — the generation job is NEVER failed by
                            // this. A successful crop's stored path is threaded into BuildBrandMarkSlot
                            // (renders the <img>) and into renderInlineRefs (D11) so the Chromium render
                            // pass inlines it as a data URI, exactly like the enforced background.
                            string? extractedBrandMarkIconPath = null;
                            if (string.Equals(brandMark.MarkType, "icon", StringComparison.OrdinalIgnoreCase)
                                && brandMark.BoundingBox is { } brandMarkBox
                                && !string.IsNullOrWhiteSpace(brandMark.SourceImagePath)
                                && _brandMarkAssetExtractor is not null)
                            {
                                var brandMarkAssetKey = new StoredAssetKey(
                                    job.OrganizationId,
                                    effectiveClientId.Value,
                                    job.AnalysisId,
                                    "brand-mark.png");

                                var brandMarkExtractResult = await _brandMarkAssetExtractor.ExtractAsync(
                                    brandMarkAssetKey,
                                    brandMark.SourceImagePath,
                                    brandMarkBox,
                                    ct);

                                if (brandMarkExtractResult.IsFailure)
                                {
                                    _logger.LogWarning(
                                        "ContentGeneration: brand-mark icon crop extraction failed for job {JobId} ({ErrorCode}) — degrading to chip.",
                                        job.Id, brandMarkExtractResult.Error.Code);
                                }
                                else
                                {
                                    extractedBrandMarkIconPath = brandMarkExtractResult.Value.StoragePath;
                                }
                            }

                            var brandMarkSlot = BuildBrandMarkSlot(
                                brandMark,
                                defaultBrandMarkColorHex,
                                brandMarkCaptionFont,
                                brandMarkCaptionWeight,
                                brandMarkCaptionSize,
                                extractedBrandMarkIconPath);
                            if (brandMarkSlot is not null)
                            {
                                var markupWithBrandMark = AppendBrandMarkSlot(capturedMarkup, brandMarkSlot);
                                spec = spec with { DesignerMarkup = markupWithBrandMark };
                                capturedMarkup = markupWithBrandMark;

                                if (extractedBrandMarkIconPath is not null)
                                {
                                    renderInlineRefs.Add(extractedBrandMarkIconPath);
                                }
                            }
                        }
                    }

                    // WU-1 D3: when the renderer supports geometry capture (IContentRendererWithGeometry)
                    // and the spec is an Html composition, use RenderWithGeometryAsync to capture
                    // per-[data-slot] bounding rects in the same Chromium page session.
                    // Falls back to the base RenderAsync for non-Html specs or legacy renderers.
                    RenderedAsset asset;
                    // Transiently inline every collected storage-path reference (enforced background,
                    // and — WU-7c, D11/F3 — a successfully-extracted brand-mark icon crop) as data URIs
                    // for the render ONLY. The persisted spec/markup keeps the lightweight storage-path
                    // references; only what Chromium renders needs the self-contained images (it has no
                    // base URL to fetch a bare path from).
                    var renderSpec = spec;
                    if (renderInlineRefs.Count > 0
                        && spec.OutputKind == CompositionOutputKind.Html
                        && spec.DesignerMarkup is not null)
                    {
                        var renderMarkup = spec.DesignerMarkup;
                        foreach (var inlineRef in renderInlineRefs)
                        {
                            var dataUri = await InlineBackgroundUrlAsync(_assetStorage, inlineRef, ct);
                            if (!string.Equals(dataUri, inlineRef, StringComparison.Ordinal))
                            {
                                renderMarkup = renderMarkup.Replace(inlineRef, dataUri);
                            }
                        }

                        if (!string.Equals(renderMarkup, spec.DesignerMarkup, StringComparison.Ordinal))
                        {
                            renderSpec = spec with { DesignerMarkup = renderMarkup };
                        }
                    }
                    if (spec.OutputKind == CompositionOutputKind.Html
                        && _renderer is IContentRendererWithGeometry geometryRenderer)
                    {
                        var renderedWithGeo = await geometryRenderer.RenderWithGeometryAsync(
                            renderSpec, new RenderOptions(), ct);
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
                        var rendered = await _renderer.RenderAsync(renderSpec, new RenderOptions(), ct);
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
                    // Multi-asset (carousel family): {storageFolder}/slide_{N}.png (1-based index).
                    // Resolved via FormatCatalog.ResolveForAssembly so legacy "carousel" keys
                    // continue to write under carousel/ while new keys use their own folder.
                    // Single-asset: just the filename (e.g. story-single.png, reel-script.txt).
                    string relativePath;
                    var formatDef = FormatCatalog.ResolveForAssembly(formatKey);
                    if (formatDef?.IsMultiAsset == true && specs.Count > 1)
                    {
                        relativePath = $"{formatDef.StorageFolder}/slide_{specIndex + 1}.png";
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
                        // BGREF-01 (studio-background-asset-ref, Slice 1): when storage is wired,
                        // upload the captured background PNG as its own object and persist a storage
                        // path in BackgroundImageRef instead of the base64 data-URI — the data-URI
                        // was bloating composition_spec_json by the full PNG payload per slide.
                        // NO FormatAssetRow is created for it (see design: content_generation_format_assets
                        // has multiple consumers beyond materialize that are not kind-aware and would
                        // double-count/double-ship it). Storage-not-wired keeps the legacy inline
                        // data-URI unchanged (back-compat for local/dev and existing rows).
                        var backgroundImageRef = capturedBackgroundDataUri;
                        if (_assetStorage is not null && !string.IsNullOrWhiteSpace(capturedBackgroundDataUri))
                        {
                            var decodedBackground = TryDecodeBackgroundDataUri(capturedBackgroundDataUri);
                            if (decodedBackground is not null)
                            {
                                var backgroundRelativePath = formatDef?.IsMultiAsset == true && specs.Count > 1
                                    ? $"{formatDef.StorageFolder}/slide_{specIndex + 1}_bg.png"
                                    : $"{Path.GetFileNameWithoutExtension(asset.Filename)}_bg.png";

                                try
                                {
                                    var backgroundStorageKey = new StoredAssetKey(
                                        job.OrganizationId,
                                        effectiveClientId.Value,
                                        job.AnalysisId,
                                        backgroundRelativePath);

                                    var storedBackgroundRef = await _assetStorage.PutAssetAsync(
                                        backgroundStorageKey,
                                        new StoredAssetContent(decodedBackground.Value.Bytes, decodedBackground.Value.Mime),
                                        ct);

                                    backgroundImageRef = storedBackgroundRef.StoragePath;
                                }
                                catch (GeneratedAssetStorageException ex)
                                {
                                    // Non-fatal: the slide's primary asset already uploaded successfully.
                                    // Keep the inline data-URI so the slide still exports/renders.
                                    _logger.LogWarning(ex,
                                        "ContentGeneration: background asset upload failed for format {FormatKey} spec {SpecIndex} (job {JobId}). Keeping inline data-URI.",
                                        formatKey, specIndex, job.Id);
                                }
                            }
                        }

                        var canvas = capturedDimensions is not null
                            ? new CanvasDimensions(capturedDimensions.Width, capturedDimensions.Height, capturedDimensions.Dpi)
                            : new CanvasDimensions(0, 0, 96);
                        capturedEnvelopes.Add(new CompositionSpecEnvelope(
                            CompositionSpecEnvelope.CurrentSchemaVersion,
                            capturedMarkup,
                            canvas,
                            capturedGeometry,
                            backgroundImageRef,
                            DegradeReason: capturedDegradeReason));
                    }

                    // 5c. INCIDENT (job 8757e85a-6acd-49b4-af11-a2d19fbcd3e1): persist THIS slide's
                    // asset row + format-result checkpoint immediately, instead of waiting for the
                    // whole format's spec loop to finish. That job generated four ComfyUI backgrounds
                    // successfully (394s/391s/382s/402s) then hit the worker's wall-clock timeout on
                    // the next slide — the entire batch-at-the-end write meant zero rows ever reached
                    // content_generation_format_assets, discarding ~26 minutes of GPU work. Checkpoint
                    // status is "PartiallyRendered" until the last spec in this format succeeds, at
                    // which point it is upgraded to "Rendered" — see PersistRenderSuccessAsync callers
                    // below. PersistResilientlyAsync guards the write itself against a token that goes
                    // cancelled mid-checkpoint (Change 2's same trap, see IntegrationExecutionPolicy).
                    if (formatRendered)
                    {
                        var isLastSlideOfFormat = specIndex == specs.Count - 1;
                        var checkpointStatus = isLastSlideOfFormat ? "Rendered" : "PartiallyRendered";

                        var checkpointResult = await PersistResilientlyAsync(
                            ct,
                            token => PersistRenderSuccessAsync(
                                formatResultId, job.Id, formatKey, step, assets[^1], capturedEnvelopes, checkpointStatus, token));
                        existingByFormat[formatKey] = checkpointResult;
                        formatResultId = checkpointResult.Id;

                        if (_assetRepository is not null && formatAssetRows.Count > 0)
                        {
                            var checkpointRows = formatAssetRows
                                .Select(row => row.FormatResultId == formatResultId
                                    ? row
                                    : row with { FormatResultId = formatResultId })
                                .ToList();

                            await PersistResilientlyAsync(
                                ct,
                                async token =>
                                {
                                    await _assetRepository.UpsertManyForFormatAsync(formatResultId, checkpointRows, token);
                                    return true;
                                });
                        }
                    }
                }

                if (formatRendered && !formatStorageFailed)
                {
                    var publication = ResolvePublicationMetadata(formatKey, step.Payload);
                    if (publication is not null)
                    {
                        publicationByFormat[formatKey] = publication;
                    }

                    // Create carousel-family folder placeholder.
                    // Applies to any multi-asset format (story-carousel, feed-carousel, legacy carousel).
                    // The format-result row and asset rows are already durable — see the per-slide
                    // checkpoint above (5c) — so nothing left to persist here but the folder marker.
                    var folderDef = FormatCatalog.ResolveForAssembly(formatKey);
                    if (_assetStorage is not null
                        && folderDef?.IsMultiAsset == true
                        && specs.Count > 1)
                    {
                        try
                        {
                            var folderPath = $"{job.OrganizationId}/{effectiveClientId}/{job.AnalysisId}/{folderDef.StorageFolder}";
                            await _assetStorage.PutFolderPlaceholderAsync(folderPath, ct);
                        }
                        catch (GeneratedAssetStorageException ex)
                        {
                            // Folder placeholder failure is non-fatal — log and continue.
                            _logger.LogWarning(ex,
                                "ContentGeneration: carousel folder placeholder PUT failed for '{FormatKey}' (job {JobId}). Continuing.",
                                formatKey, job.Id);
                        }
                    }
                }
                else if (formatStorageFailed)
                {
                    // Storage failed on this slide. If earlier slides of the SAME format already
                    // checkpointed successfully (formatRendered was true before this slide), the row
                    // already sits at "PartiallyRendered" with their data — calling
                    // PersistRenderFailureAsync here would OVERWRITE it with ErrorCode/no-composition,
                    // destroying the safeguarded partial success. Only mark the whole format failed
                    // when nothing was ever persisted for it.
                    if (formatAssetRows.Count == 0)
                    {
                        await PersistRenderFailureAsync(job.Id, formatKey, step, "storage-upload-failed", ct);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "ContentGeneration: storage upload failed for format {FormatKey} (job {JobId}) after " +
                            "{PersistedCount} slide(s) already checkpointed as PartiallyRendered. Keeping the " +
                            "partial checkpoint instead of overwriting it with a failure record.",
                            formatKey, job.Id, formatAssetRows.Count);
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // FIX-3: propagate genuine job cancellation — do not swallow it as a render failure.
                // Nothing to persist here: every slide that finished before cancellation hit was
                // already checkpointed at 5c above, using the still-live token at the time.
                throw;
            }
            catch (Exception ex)
            {
                var detail = FormatException(ex);
                renderFailures.Add($"{formatKey}: {detail}");
                _logger.LogError(ex,
                    "ContentGeneration: compose/render threw for format {FormatKey} (job {JobId}).",
                    formatKey, job.Id);

                // Same guard as the storage-failure branch above: do not clobber an already-
                // checkpointed partial success with a failure record that nulls the composition spec.
                if (formatAssetRows.Count == 0)
                {
                    await PersistRenderFailureAsync(job.Id, formatKey, step, detail, ct);
                }
                else
                {
                    _logger.LogWarning(
                        "ContentGeneration: format {FormatKey} (job {JobId}) failed after {PersistedCount} " +
                        "slide(s) already checkpointed as PartiallyRendered. Keeping the partial checkpoint.",
                        formatKey, job.Id, formatAssetRows.Count);
                }
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
        else if (string.Equals(formatKey, "story-single", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(formatKey, "feed-single", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(formatKey, "story", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(formatKey, "feed-post", StringComparison.OrdinalIgnoreCase))
        {
            description = payload switch
            {
                StoryContent story => story.PublicationDescription,
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
        string status,
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
            // Change 1: "PartiallyRendered" until the format's last spec checkpoints as "Rendered"
            // (see the 5c checkpoint in ProcessInternalAsync). Reuses the same naming pattern as the
            // job-level "Partial" status rather than inventing an unrelated term, and is intentionally
            // excluded from EfContentGenerationFailurePolicy.FailedFormatStatuses — a partial success
            // with real assets must not trip the AI-retry cost guard the way AiFailed/RenderFailed do.
            Status: status,
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

    /// <summary>
    /// Bounded budget for a persistence write that must survive a token that is ALREADY cancelled
    /// (job timeout / host shutdown fired while a slide's checkpoint was about to be written). Kept
    /// short and deliberately NOT unbounded (<see cref="CancellationToken.None"/> with no time limit)
    /// so a wedged DB connection cannot hang job shutdown indefinitely — see Change 2 in the same
    /// incident report for the identical trap in <c>IntegrationExecutionPolicy.PersistAttemptAsync</c>.
    /// </summary>
    private static readonly TimeSpan ResilientPersistenceTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Runs a durable write with a token that is guaranteed not to already be cancelled. When
    /// <paramref name="ct"/> is still live it is forwarded unchanged (the common case). When it is
    /// already cancelled — e.g. the job's wall-clock timeout fired between this slide finishing and
    /// its checkpoint write starting — using it would throw <see cref="OperationCanceledException"/>
    /// immediately and lose the slide that just succeeded, defeating the whole point of checkpointing
    /// per slide. A short, bounded token is substituted instead.
    /// </summary>
    private static async Task<T> PersistResilientlyAsync<T>(
        CancellationToken ct,
        Func<CancellationToken, Task<T>> write)
    {
        if (!ct.IsCancellationRequested)
        {
            return await write(ct);
        }

        using var boundedCts = new CancellationTokenSource(ResilientPersistenceTimeout);
        return await write(boundedCts.Token);
    }

    /// <summary>
    /// Reports whether at least one format result was durably persisted for this job — i.e. the
    /// per-slide checkpoint (5c above) fired at least once before the job's cancellation. Used to
    /// pick "Partial" over "Failed" as the terminal status so a cancelled job that safeguarded real
    /// output does not read identically to one that produced nothing.
    /// </summary>
    private async Task<bool> HasAnyPersistedFormatResultAsync(Guid jobId, CancellationToken ct)
    {
        var results = await _formatResults.ListByJobAsync(jobId, ct);
        return results.Any(r =>
            string.Equals(r.Status, "Rendered", StringComparison.OrdinalIgnoreCase)
            || string.Equals(r.Status, "PartiallyRendered", StringComparison.OrdinalIgnoreCase));
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

    /// <summary>
    /// WU-4: infers the slide count from a format payload without deserializing the full content.
    /// For carousel payloads (CarouselContent or anonymous with "slides" property) returns the
    /// actual count; for everything else returns 1 (single-asset default).
    /// Used to build the <see cref="BackgroundPlanRequest"/> before calling <see cref="IBackgroundSourcingStrategy.Plan"/>.
    /// </summary>
    private static int TryGetSlideCount(object? payload, string formatKey)
    {
        if (payload is CarouselContent carousel)
        {
            return carousel.Slides.Count;
        }

        if (payload is JsonElement element
            && element.TryGetProperty("slides", out var slidesEl)
            && slidesEl.ValueKind == JsonValueKind.Array)
        {
            return slidesEl.GetArrayLength();
        }

        return 1;
    }

    /// <summary>
    /// Extracts a per-slide copy summary (one entry per slide, slide order preserved) from the
    /// generated content payload so the image prompt writer can ground each background as a metaphor
    /// of that slide's message. Prefers the copy AI's <c>ImagePromptHint</c>; otherwise falls back to
    /// title + body. Returns an empty list when the payload carries no recognizable per-slide copy.
    /// </summary>
    internal static IReadOnlyList<string> TryGetPerSlideCopySummaries(object? payload)
    {
        if (payload is CarouselContent carousel)
        {
            return carousel.Slides
                .Select(s => ComposeSlideCopySummary(s.ImagePromptHint, s.Title, s.Body))
                .ToList();
        }

        if (payload is JsonElement element
            && element.TryGetProperty("slides", out var slidesEl)
            && slidesEl.ValueKind == JsonValueKind.Array)
        {
            var summaries = new List<string>();
            foreach (var slide in slidesEl.EnumerateArray())
            {
                summaries.Add(ComposeSlideCopySummary(
                    GetJsonString(slide, "imagePromptHint"),
                    GetJsonString(slide, "title"),
                    GetJsonString(slide, "body")));
            }

            return summaries;
        }

        return [];
    }

    private static string ComposeSlideCopySummary(string? hint, string? title, string? body)
    {
        if (!string.IsNullOrWhiteSpace(hint))
        {
            return hint.Trim();
        }

        var parts = new[] { title, body }
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!.Trim());
        return string.Join(". ", parts);
    }

    private static string? GetJsonString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>
    /// Resolves an enforced background template from the per-slide <paramref name="selection"/>.
    ///
    /// WU-4 C6: with the coherence refactor, CASE A puts a single-entry
    /// <c>References([rotated])</c> per slide into the plan. First-entry-wins here correctly
    /// enforces that slide's rotated template (not a shared <c>Entries[0]</c> from a larger set),
    /// because the rotation was already done by <see cref="ClientDefinedSetStrategy"/> when
    /// building the plan. The method therefore needs no change — the per-slide plan input
    /// carries exactly the right entry.
    ///
    /// <c>ClientAesthetic</c> (CASE C) → null: the designer generates freely within the
    /// aesthetic guidance; there is no URL to enforce. AI markup or fallback gradient is used.
    /// <c>Creative</c> (CASE B) → null: fully AI-generated; no template enforcement.
    /// </summary>
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
            // First-entry-wins: enforces the first entry with a non-empty URL.
            // WU-4: CASE A plan emits References([rotatedSingle]) per slide — the rotated entry
            // IS Entries[0] for that slide's single-entry list, so this correctly enforces
            // the rotated template for each slide without any index change.
            var first = references.Entries.FirstOrDefault(entry => !string.IsNullOrWhiteSpace(entry.Url));
            if (first is not null)
            {
                return new EnforcedBackgroundTemplate(
                    first.Url,
                    first.Name,
                    "brand-manual-template");
            }
        }

        // Creative, ClientAesthetic, empty References → no template to enforce.
        // For ClientAesthetic: the AI designs freely within aesthetic guidance — return null
        // so the AI markup path is taken (or the gradient fallback if no AI markup).
        return null;
    }

    /// <summary>
    /// Resolves a hard-enforced background image reference to a form the HTML renderer can load.
    /// The Chromium renderer sets page content with no base URL, so a relative storage path (a
    /// Supabase object key such as <c>{org}/{client}/{analysis}/carousel/slide_1.png</c>) cannot be
    /// fetched and rasterizes to a broken-image/black background. When the URL is a bare storage
    /// path, download the bytes via <paramref name="storage"/> and inline them as a self-contained
    /// <c>data:</c> URI. URLs that are already <c>data:</c> or <c>http(s):</c> are returned as-is.
    /// Best-effort: on any failure the original URL is returned unchanged (no worse than before).
    /// </summary>
    internal static async Task<string> InlineBackgroundUrlAsync(
        IGeneratedAssetStorage? storage,
        string url,
        CancellationToken ct)
    {
        if (storage is null || string.IsNullOrWhiteSpace(url))
        {
            return url;
        }

        if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }

        try
        {
            var opened = await storage.OpenAsync(url, ct);
            if (opened.IsFailure)
            {
                return url;
            }

            using var stream = opened.Value;
            using var buffer = new MemoryStream();
            await stream.Content.CopyToAsync(buffer, ct);
            var mime = string.IsNullOrWhiteSpace(stream.Mime) ? "image/png" : stream.Mime;
            return $"data:{mime};base64,{Convert.ToBase64String(buffer.ToArray())}";
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return url;
        }
    }

    /// <summary>
    /// Regex for a <c>data:&lt;mime&gt;;base64,&lt;data&gt;</c> URI. Mirrors
    /// <c>PptxExportAdapter.DataUriRegex</c> — there is intentionally no shared Application-layer
    /// decoder yet, so each consumer owns its own copy until a third use case justifies extraction.
    /// </summary>
    private static readonly Regex BackgroundDataUriRegex = new(
        @"^data:(?<mime>[^;]+);base64,(?<data>.+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Decodes a captured background data-URI (<c>data:image/png;base64,...</c>) into raw bytes
    /// and its MIME type. Returns null for a non-data-URI string, malformed base64, or blank input.
    ///
    /// <para>
    /// BGREF-01 (studio-background-asset-ref, Slice 1): used to upload the captured background PNG
    /// via <see cref="IGeneratedAssetStorage"/> instead of persisting the base64 payload inline in
    /// <c>composition_spec_json</c>.
    /// </para>
    /// </summary>
    internal static (byte[] Bytes, string Mime)? TryDecodeBackgroundDataUri(string? dataUri)
    {
        if (string.IsNullOrWhiteSpace(dataUri))
        {
            return null;
        }

        var match = BackgroundDataUriRegex.Match(dataUri);
        if (!match.Success)
        {
            return null;
        }

        try
        {
            var bytes = Convert.FromBase64String(match.Groups["data"].Value);
            return (bytes, match.Groups["mime"].Value);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string BuildTemplateBackedMarkup(
        VisualCompositionSpec spec,
        EnforcedBackgroundTemplate? background,
        ClientAestheticDesignContext? aesthetic = null,
        // WU-3b (D3 render half + D5): resolved by the caller per specIndex via
        // CreativeSlideIndexResolver.ResolveTransparencyForSlide. Applies ONLY when there is a real
        // enforced background AND the resolved signal is Blended:true — absent/false preserves
        // today's unconditional object-fit:cover byte-identically (cold-start parity).
        SlideBackgroundTransparency? transparency = null)
    {
        var isCarousel = string.Equals(spec.FormatKey, "carousel", StringComparison.OrdinalIgnoreCase);
        var width = spec.Dimensions.Width;
        var height = spec.Dimensions.Height;
        var headingFont = CssString(spec.Typography.HeadingFamily);
        var bodyFont = CssString(spec.Typography.BodyFamily);
        // D4: caption/overline role — falls back to BodyFamily when unmeasured, so a client
        // without an observed caption renders exactly as before (CaptionWeight also defaults to
        // today's hardcoded cta weight, and a null CaptionSize keeps the builder's own hardcoded size).
        var captionFont = CssString(spec.Typography.EffectiveCaptionFamily);
        var captionWeight = spec.Typography.CaptionWeight;
        var captionSize = spec.Typography.CaptionSize;
        // D1: nullable measured heading/body sizes — null keeps each builder's own hardcoded literal
        // (cold-start parity); measured values come from CompositionDefaults.ResolveTypography.
        var measuredHeadingSize = spec.Typography.MeasuredHeadingSize;
        var measuredBodySize = spec.Typography.MeasuredBodySize;
        // Issue #81: the client's REAL observed heading weight, when a ClientAesthetic profile
        // measured one. Null keeps each builder's own pre-change font-weight:900 literal (cold-start
        // / non-ClientAesthetic parity) — mirrors the measuredHeadingSize/measuredBodySize precedent.
        var measuredHeadingWeight = spec.Typography.MeasuredHeadingWeight;

        var title = FirstSlot(spec.CopySlots, isCarousel ? ["title", "headline"] : ["headline", "title"])
            ?? (isCarousel ? "Carousel" : "Story");
        var subtitle = FirstSlot(spec.CopySlots, ["subtitle", "body", "text"]);
        var body = isCarousel
            ? null
            : FirstSlot(spec.CopySlots, ["body", "callouts"]);
        var cta = FirstSlot(spec.CopySlots, ["cta"]);
        var signature = FirstSlot(spec.CopySlots, ["signature"]);

        // WU-3 (D3/D4, CAGH-CG-03): enforced-photo text colour resolves from the resolved
        // spec.Palette.Text (which already carries the client's rotating ColorPairings text hex
        // when a ClientAesthetic profile has resolved them — see CompositionDefaults.ResolvePalette's
        // profile-aware overload) ONLY when it clears WCAG >= 4.5:1 contrast against black. Below
        // that guard, or with no resolvable pairing (default palette Text is near-black), null is
        // threaded through so each builder keeps its OWN pre-change per-slot literal unchanged
        // (cold-start/regression parity — those literals are not uniformly "#FFFFFF"). The shadow
        // is retained regardless of which colour renders.
        var textColor = WcagContrast.ContrastRatio(spec.Palette.Text, "#000000") >= 4.5
            ? spec.Palette.Text
            : null;

        var content = isCarousel
            ? BuildCarouselTemplateSlots(title, subtitle, cta, headingFont, bodyFont, captionFont, textColor, captionWeight, captionSize, measuredHeadingSize, measuredBodySize, measuredHeadingWeight)
            : BuildStoryTemplateSlots(title, subtitle, body, cta, signature, headingFont, bodyFont, captionFont, textColor, captionWeight, captionSize, measuredHeadingSize, measuredBodySize, measuredHeadingWeight);

        // No-scrim fix: the full-bleed dark box below ("scrimBox") is ONLY safe over a synthesized
        // CSS/gradient background, which has no inherent contrast guarantee. An ENFORCED background
        // (background != null — a brand-manual template, or an AI-generated photo promoted to
        // ForcedDefault by WithForcedDefaults once ClientAestheticImageBackgroundService succeeds)
        // is a real image, and this deterministic template ALWAYS wins over the AI designer's markup
        // (see the priority comment at the resolution site). That is why AiVisualDesigner's prompt-level
        // NoScrimDirective could never reach the rendered pixels for an enforced background — the only
        // place the rule can actually be enforced is HERE, in the markup that ships.
        // Legibility on a real image comes from the per-slot text-shadow already baked into
        // BuildCarouselTemplateSlots/BuildStoryTemplateSlots, matching the "achieve legibility locally,
        // never with a full-bleed veil" policy. The thin accent divider is purely decorative (not a
        // legibility aid) and stays on both paths.
        var scrimBox = isCarousel
            ? """
              <div style="position:absolute;left:64px;right:64px;top:140px;bottom:140px;border-radius:46px;background:rgba(0,0,0,0.52);box-shadow:0 28px 80px rgba(0,0,0,0.32);"></div>
              """
            : """
              <div style="position:absolute;left:56px;right:56px;top:170px;bottom:150px;border-radius:56px;background:rgba(0,0,0,0.50);box-shadow:0 32px 90px rgba(0,0,0,0.34);"></div>
              """;
        var accentDecor = isCarousel
            ? """
              <div style="position:absolute;left:64px;right:64px;bottom:140px;height:10px;border-radius:999px;background:rgba(255,255,255,0.82);"></div>
              """
            : string.Empty;

        // Background layer. With a brand template we hard-enforce the image; without one (Creative
        // tier / resilience fallback) we synthesize a deterministic brand-palette gradient. A dark
        // legibility overlay keeps the white content text readable regardless of the brand colors.
        string backgroundLayerInner;
        // WU-3b (D5): the wrapping data-layer="background" div's own backdrop — only populated when
        // the photo <img> below is rendered at reduced opacity, so the reduced-alpha photo composites
        // against the client's own palette background instead of the transparent root.
        var backgroundLayerBackdropStyle = string.Empty;
        if (background is not null)
        {
            var safeUrl = Html(background.Url);
            var safeName = Html(string.IsNullOrWhiteSpace(background.Name) ? "Brand background template" : background.Name);
            var source = Html(background.Source);

            // WU-3b (D3 render half + D5): a flat opacity, never a parsed descriptor (the descriptor is
            // advisory metadata only). One named constant so a visual review can retune it without a
            // contract change. Absent/false signal → byte-identical to before this change.
            var photoOpacityStyle = string.Empty;
            if (transparency is { Blended: true })
            {
                photoOpacityStyle = $"opacity:{CompositionDefaults.BlendedPhotoOpacity.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)};";
                backgroundLayerBackdropStyle = $"background:{CssColor(spec.Palette.Background, CompositionDefaults.Palette.Background)};";
            }

            backgroundLayerInner = $"""
                <img src="{safeUrl}" alt="{safeName}" data-background-source="{source}" style="position:absolute;inset:0;width:100%;height:100%;object-fit:cover;{photoOpacityStyle}" />
                {accentDecor}
                """;
        }
        else
        {
            // CAG2-CG-02 (no-grey): for a ClientAesthetic job with a feed-derived palette, derive the
            // resilience-fallback gradient from the CLIENT's colours rather than the dark default —
            // so even the total-designer-failure backstop reflects the client, not generic greys.
            string c1, c2;
            if (aesthetic is { HasPalette: true } && aesthetic.Palette!.Count > 0)
            {
                var entries = aesthetic.Palette!;
                c1 = CssColor(entries[0].Hex, "#0B1220");
                c2 = CssColor(entries[entries.Count > 1 ? 1 : 0].Hex, "#000000");
            }
            else
            {
                c1 = CssColor(spec.Palette.Primary, "#0B1220");
                c2 = CssColor(spec.Palette.Secondary, "#000000");
            }
            backgroundLayerInner = $"""
                <div style="position:absolute;inset:0;background:linear-gradient(160deg,{c1} 0%,{c2} 100%);"></div>
                <div style="position:absolute;inset:0;background:linear-gradient(180deg,rgba(0,0,0,0.20) 0%,rgba(0,0,0,0.45) 100%);"></div>
                {scrimBox}
                {accentDecor}
                """;
        }

        return $$"""
            <div class="composition" data-format="{{Html(spec.FormatKey)}}" style="position:relative;width:{{width}}px;height:{{height}}px;overflow:hidden;background:#000000;">
              <div data-layer="background" style="position:absolute;inset:0;width:100%;height:100%;overflow:hidden;{{backgroundLayerBackdropStyle}}">
                {{backgroundLayerInner}}
              </div>
              <div data-layer="content" style="position:absolute;inset:0;width:100%;height:100%;">
                {{content}}
              </div>
            </div>
            """;
    }

    private static string BuildStoryTemplateSlots(
        string title,
        string? subtitle,
        string? body,
        string? cta,
        string? signature,
        string headingFont,
        string bodyFont,
        string captionFont,
        // WU-3 (D3, CAGH-CG-03): headline/subtitle/body/cta text colour on the enforced-photo path.
        // Null (default) keeps each slot's own pre-change literal (cold-start/regression parity —
        // headline/subtitle/cta were "#FFFFFF", body was the slightly-off-white "#F8FAFC"). The
        // caller resolves a non-null value to spec.Palette.Text only when it clears the WCAG
        // contrast guard. Text-shadow below is retained either way.
        string? textColor = null,
        int captionWeight = 800,
        int? captionSize = null,
        // client-aesthetic-generation-hardening D1: null keeps the pre-change literal (cold-start parity).
        int? measuredHeadingSize = null,
        int? measuredBodySize = null,
        // Issue #81: the client's REAL observed heading weight. Null keeps the pre-change 900 literal
        // (cold-start / non-ClientAesthetic parity).
        int? measuredHeadingWeight = null)
    {
        var slots = new List<string>
        {
            // Decorative accent pill — moved out of the background raster so it becomes an
            // editable shape in Content Studio. Positioned identically to the former background div.
            Slot(
                "div",
                "accent",
                string.Empty,
                "position:absolute;left:88px;top:205px;width:168px;height:10px;" +
                "border-radius:999px;background:rgba(255,255,255,0.86);",
                1),
            Slot(
                "h1",
                "headline",
                title,
                "position:absolute;left:96px;top:255px;width:888px;min-height:260px;" +
                $"font-family:{headingFont},system-ui,sans-serif;font-size:{measuredHeadingSize ?? 88}px;line-height:0.98;" +
                $"font-weight:{measuredHeadingWeight ?? 900};letter-spacing:-2.5px;color:{textColor ?? "#FFFFFF"};text-align:left;text-shadow:0 4px 24px rgba(0,0,0,0.58);",
                3)
        };

        AddSlot(slots, "p", "subtitle", subtitle,
            "position:absolute;left:100px;top:575px;width:825px;min-height:122px;" +
            $"font-family:{bodyFont},system-ui,sans-serif;font-size:{measuredBodySize ?? 36}px;line-height:1.16;" +
            $"font-weight:600;color:{textColor ?? "#FFFFFF"};text-align:left;text-shadow:0 3px 18px rgba(0,0,0,0.60);",
            3);

        AddSlot(slots, "p", "body", body,
            "position:absolute;left:100px;top:745px;width:825px;min-height:210px;" +
            $"font-family:{bodyFont},system-ui,sans-serif;font-size:{measuredBodySize ?? 30}px;line-height:1.24;" +
            $"font-weight:400;color:{textColor ?? "#F8FAFC"};text-align:left;text-shadow:0 3px 18px rgba(0,0,0,0.60);",
            5);

        AddSlot(slots, "p", "cta", cta,
            "position:absolute;left:100px;bottom:245px;width:640px;min-height:58px;" +
            $"font-family:{captionFont},system-ui,sans-serif;font-size:{captionSize ?? 34}px;line-height:1.05;" +
            $"font-weight:{captionWeight};color:{textColor ?? "#FFFFFF"};text-transform:uppercase;letter-spacing:1.2px;text-shadow:0 3px 18px rgba(0,0,0,0.60);",
            2);

        AddSlot(slots, "p", "signature", signature,
            "position:absolute;right:98px;bottom:190px;width:460px;min-height:42px;" +
            // CAGH-CG-01: signature is a caption-role element; wired to the client's resolved
            // CaptionSize (mirroring cta above), falling back to the pre-change 25px literal.
            $"font-family:{captionFont},system-ui,sans-serif;font-size:{captionSize ?? 25}px;line-height:1.1;" +
            "font-weight:600;color:#E5E7EB;text-align:right;text-shadow:0 3px 18px rgba(0,0,0,0.60);",
            1);

        return string.Join(Environment.NewLine, slots);
    }

    private static string BuildCarouselTemplateSlots(
        string title,
        string? subtitle,
        string? cta,
        string headingFont,
        string bodyFont,
        string captionFont,
        // WU-3 (D3, CAGH-CG-03): title/subtitle/cta text colour on the enforced-photo path. Null
        // (default) keeps each slot's own pre-change literal (cold-start/regression parity — title/cta
        // were "#FFFFFF", subtitle was the slightly-off-white "#F8FAFC"). The caller resolves a
        // non-null value to spec.Palette.Text only when it clears the WCAG contrast guard. Text-shadow
        // below is retained either way.
        string? textColor = null,
        int captionWeight = 800,
        int? captionSize = null,
        // client-aesthetic-generation-hardening D1: null keeps the pre-change literal (cold-start parity).
        int? measuredHeadingSize = null,
        int? measuredBodySize = null,
        // Issue #81: the client's REAL observed heading weight. Null keeps the pre-change 900 literal
        // (cold-start / non-ClientAesthetic parity).
        int? measuredHeadingWeight = null)
    {
        var slots = new List<string>
        {
            Slot(
                "h1",
                "title",
                title,
                "position:absolute;left:104px;top:235px;width:872px;min-height:230px;" +
                $"font-family:{headingFont},system-ui,sans-serif;font-size:{measuredHeadingSize ?? 74}px;line-height:1.02;" +
                $"font-weight:{measuredHeadingWeight ?? 900};letter-spacing:-1.8px;color:{textColor ?? "#FFFFFF"};text-align:left;text-shadow:0 4px 24px rgba(0,0,0,0.58);",
                3)
        };

        AddSlot(slots, "p", "subtitle", subtitle,
            "position:absolute;left:108px;top:540px;width:815px;min-height:310px;" +
            $"font-family:{bodyFont},system-ui,sans-serif;font-size:{measuredBodySize ?? 38}px;line-height:1.22;" +
            $"font-weight:500;color:{textColor ?? "#F8FAFC"};text-align:left;text-shadow:0 3px 18px rgba(0,0,0,0.62);",
            6);

        AddSlot(slots, "p", "cta", cta,
            "position:absolute;left:108px;bottom:220px;width:760px;min-height:58px;" +
            $"font-family:{captionFont},system-ui,sans-serif;font-size:{captionSize ?? 28}px;line-height:1.1;" +
            $"font-weight:{captionWeight};color:{textColor ?? "#FFFFFF"};text-transform:uppercase;letter-spacing:1.1px;text-shadow:0 3px 18px rgba(0,0,0,0.62);",
            2);

        return string.Join(Environment.NewLine, slots);
    }

    /// <summary>
    /// FFS-CG-01/02: deterministic FlatColor carousel markup. A SOLID background from the
    /// resolved <see cref="CompositionPalette"/> — NO gradient, NO dark legibility overlay, NO
    /// <c>backgroundDecor</c> — with title/subtitle/cta rendered in the palette's text colour and
    /// NO text-shadow (a flat background needs none). Any signaled accent is emitted as an
    /// editable content-layer <c>data-slot="accent"</c> element (Story precedent), never baked
    /// into the rasterized background layer.
    /// </summary>
    private static string BuildFlatColorMarkup(
        VisualCompositionSpec spec,
        CompositionPalette palette,
        ClientAestheticDesignContext? aesthetic)
    {
        var width = spec.Dimensions.Width;
        var height = spec.Dimensions.Height;
        var headingFont = CssString(spec.Typography.HeadingFamily);
        var bodyFont = CssString(spec.Typography.BodyFamily);
        // D4: caption/overline role — falls back to BodyFamily when unmeasured (cold-start parity).
        var captionFont = CssString(spec.Typography.EffectiveCaptionFamily);
        var captionWeight = spec.Typography.CaptionWeight;
        var captionSize = spec.Typography.CaptionSize;
        // D1: nullable measured heading/body sizes — null keeps the builder's own hardcoded literal.
        var measuredHeadingSize = spec.Typography.MeasuredHeadingSize;
        var measuredBodySize = spec.Typography.MeasuredBodySize;
        // Issue #81: the client's REAL observed heading weight — null keeps the pre-change 900 literal.
        var measuredHeadingWeight = spec.Typography.MeasuredHeadingWeight;

        var title = FirstSlot(spec.CopySlots, ["title", "headline"]) ?? "Carousel";
        var subtitle = FirstSlot(spec.CopySlots, ["subtitle", "body", "text"]);
        var cta = FirstSlot(spec.CopySlots, ["cta"]);

        var safeBackground = CssColor(palette.Background, CompositionDefaults.Palette.Background);
        var safeText = CssColor(palette.Text, CompositionDefaults.Palette.Text);

        // Conservative accent heuristic (design open question resolved): only emit the editable
        // accent slot when the profile explicitly signals an "Accent" role entry; otherwise omit.
        var accentHex = aesthetic?.Palette?
            .FirstOrDefault(p => string.Equals(p.Role, "Accent", StringComparison.OrdinalIgnoreCase))
            ?.Hex;
        var safeAccent = IsSafeCssColor(accentHex) ? accentHex!.Trim() : null;

        var content = BuildFlatCarouselSlots(
            title, subtitle, cta, headingFont, bodyFont, safeText, safeAccent, captionFont, captionWeight, captionSize,
            measuredHeadingSize, measuredBodySize, measuredHeadingWeight, aesthetic?.DecorationMotifs?.DividerRule,
            aesthetic?.DecorationMotifs?.CompositionArchetype);

        return $$"""
            <div class="composition" data-format="{{Html(spec.FormatKey)}}" style="position:relative;width:{{width}}px;height:{{height}}px;overflow:hidden;background:{{safeBackground}};">
              <div data-layer="background" style="position:absolute;inset:0;width:100%;height:100%;background:{{safeBackground}};"></div>
              <div data-layer="content" style="position:absolute;inset:0;width:100%;height:100%;">
                {{content}}
              </div>
            </div>
            """;
    }

    /// <summary>
    /// Sibling of <see cref="BuildCarouselTemplateSlots"/> — dedicated so the photo path's
    /// hardcoded white text + text-shadow is NOT regressed. Title/subtitle/cta render in the
    /// resolved flat-palette text colour with no shadow. The optional accent slot (when
    /// <paramref name="accentColor"/> is non-null) reuses the Story accent-pill precedent
    /// (<see cref="BuildStoryTemplateSlots"/>) — an editable content-layer element, never rasterized.
    /// </summary>
    private static string BuildFlatCarouselSlots(
        string title,
        string? subtitle,
        string? cta,
        string headingFont,
        string bodyFont,
        string textColor,
        string? accentColor,
        string captionFont,
        int captionWeight = 800,
        int? captionSize = null,
        // client-aesthetic-generation-hardening D1: null keeps the pre-change literal (cold-start parity).
        int? measuredHeadingSize = null,
        int? measuredBodySize = null,
        // Issue #81: the client's REAL observed heading weight. Null keeps the pre-change 900 literal
        // (cold-start / non-ClientAesthetic parity).
        int? measuredHeadingWeight = null,
        // client-aesthetic-visual-sophistication (WU-4, CAVS-CG-01): the client's measured recurring
        // divider-rule style. Null (unmeasured/absent/unrecognised token) renders NO divider element —
        // required for the DecorationMotifs=null byte-identity cold-start guarantee (CAVS-CG-03).
        DividerRuleStyle? dividerRule = null,
        // client-aesthetic-visual-sophistication (WU-5, CAVS-CG-02): the client's measured recurring
        // composition-archetype token. Resolved through FlatGeometryPresets.Resolve, whose catch-all
        // arm returns the left-aligned identity preset for null/unrecognised tokens — required for the
        // DecorationMotifs=null byte-identity cold-start guarantee (CAVS-CG-03).
        string? compositionArchetype = null)
    {
        var geometry = FlatGeometryPresets.Resolve(compositionArchetype);
        var slots = new List<string>();

        if (accentColor is not null)
        {
            slots.Add(Slot(
                "div",
                "accent",
                string.Empty,
                $"position:absolute;left:{geometry.AccentLeft}px;top:{geometry.AccentTop}px;width:168px;height:10px;" +
                $"border-radius:999px;background:{accentColor};",
                1));
        }

        slots.Add(Slot(
            "h1",
            "title",
            title,
            $"position:absolute;left:{geometry.TitleLeft}px;top:{geometry.TitleTop}px;width:{geometry.TitleWidth}px;min-height:230px;" +
            $"font-family:{headingFont},system-ui,sans-serif;font-size:{measuredHeadingSize ?? 74}px;line-height:1.02;" +
            $"font-weight:{measuredHeadingWeight ?? 900};letter-spacing:-1.8px;color:{textColor};text-align:{geometry.TitleAlign};",
            3));

        var dividerElement = BuildDividerSlot(dividerRule, accentColor, textColor, geometry);
        if (dividerElement is not null)
        {
            slots.Add(dividerElement);
        }

        AddSlot(slots, "p", "subtitle", subtitle,
            $"position:absolute;left:{geometry.SubtitleLeft}px;top:{geometry.SubtitleTop}px;width:{geometry.SubtitleWidth}px;min-height:310px;" +
            $"font-family:{bodyFont},system-ui,sans-serif;font-size:{measuredBodySize ?? 38}px;line-height:1.22;" +
            $"font-weight:500;color:{textColor};text-align:{geometry.SubtitleAlign};",
            6);

        // The pre-change literal never emitted text-align on the cta slot (it was implicitly
        // left-aligned by document flow) — only append the property for a non-identity preset, or
        // the DecorationMotifs=null / "left-aligned" byte-identity guarantee breaks (CAVS-CG-03).
        var ctaAlignCss = geometry.CtaAlign == FlatGeometryPresets.LeftAligned.CtaAlign
            ? string.Empty
            : $"text-align:{geometry.CtaAlign};";
        AddSlot(slots, "p", "cta", cta,
            $"position:absolute;left:{geometry.CtaLeft}px;bottom:{geometry.CtaBottom}px;width:{geometry.CtaWidth}px;min-height:58px;" +
            $"font-family:{captionFont},system-ui,sans-serif;font-size:{captionSize ?? 28}px;line-height:1.1;" +
            $"font-weight:{captionWeight};color:{textColor};text-transform:uppercase;letter-spacing:1.1px;{ctaAlignCss}",
            2);

        return string.Join(Environment.NewLine, slots);
    }

    /// <summary>
    /// FFS-CG-02: builds the editable content-layer <c>data-slot="brand-mark"</c> element for a
    /// detected recurring brand mark. Always bottom-anchored (conservative — never competes with
    /// title/subtitle content near the top); horizontal placement is read from
    /// <see cref="BrandMarkDetection.PositionHint"/>. Returns <c>null</c> — meaning "render nothing"
    /// — when the mark is not present, or when the position hint / color hex is
    /// unrecognised/malformed (FFS-CG-02-b/c: ambiguous signals are treated as absent, never a
    /// best-guess fallback).
    /// </summary>
    /// <remarks>
    /// CAGH-CG-04 (WU-7a): when <see cref="BrandMarkDetection.MarkType"/> is <c>"text"</c> and
    /// <see cref="BrandMarkDetection.Text"/> is non-blank, the literal text is rendered through the
    /// client's already-resolved caption typography (<paramref name="captionFont"/>/
    /// <paramref name="captionWeight"/>/<paramref name="captionSize"/>) instead of the coloured
    /// chip — a signature is caption-scale.
    /// CAGH-CG-05 (WU-7c): when <see cref="BrandMarkDetection.MarkType"/> is <c>"icon"</c> and
    /// <paramref name="extractedIconAssetPath"/> is non-blank (the caller only supplies this after a
    /// successful <c>IAestheticAssetCropExtractor.ExtractAsync</c> crop), an <c>&lt;img&gt;</c>
    /// referencing the stored crop asset is rendered instead of the coloured chip. Every other case
    /// (<c>"unknown"</c>, <c>null</c> <see cref="BrandMarkDetection.MarkType"/>, blank/whitespace
    /// <see cref="BrandMarkDetection.Text"/> on a text mark, or a null/blank
    /// <paramref name="extractedIconAssetPath"/> on an icon mark — missing/invalid bounding box,
    /// extraction failure, or the extractor being unavailable) falls through to the EXACT
    /// pre-existing empty-chip behaviour (CAGH-CG-06-a).
    /// </remarks>
    private static string? BuildBrandMarkSlot(
        BrandMarkDetection brandMark,
        string defaultColorHex,
        string captionFont,
        int captionWeight,
        int? captionSize,
        string? extractedIconAssetPath = null)
    {
        if (!brandMark.Present)
        {
            return null;
        }

        if (!TryResolveBrandMarkHorizontalCss(brandMark.PositionHint, out var horizontalCss))
        {
            // Ambiguous/unrecognised position hint — treat the WHOLE mark as absent (FFS-CG-02-c).
            return null;
        }

        if (brandMark.MarkType == "text" && !string.IsNullOrWhiteSpace(brandMark.Text))
        {
            return Slot(
                "p",
                "brand-mark",
                brandMark.Text,
                $"position:absolute;{horizontalCss}bottom:56px;" +
                $"font-family:{captionFont},system-ui,sans-serif;font-size:{captionSize ?? 28}px;" +
                $"font-weight:{captionWeight};color:{defaultColorHex};",
                1);
        }

        if (brandMark.MarkType == "icon" && !string.IsNullOrWhiteSpace(extractedIconAssetPath))
        {
            // CAGH-CG-05: sized to the brand-mark slot's dimensions (mirrors the coloured chip's
            // 112x36 footprint below), not the full canvas — object-fit:cover mirrors the pattern the
            // AI-generated background <img> already uses (BuildTemplateBackedMarkup, above) so a
            // non-matching crop aspect ratio still fills the slot without distortion.
            return
                $"<img class=\"slot\" data-slot=\"brand-mark\" data-max-lines=\"1\" src=\"{Html(extractedIconAssetPath)}\" " +
                $"style=\"position:absolute;{horizontalCss}bottom:56px;width:112px;height:36px;" +
                "object-fit:cover;border-radius:8px;\" />";
        }

        string colorHex;
        if (brandMark.ColorHex is null)
        {
            // Color-source: no explicit hex → use the slide's text color.
            colorHex = defaultColorHex;
        }
        else if (IsSafeCssColor(brandMark.ColorHex))
        {
            colorHex = brandMark.ColorHex.Trim();
        }
        else
        {
            // Ambiguous/malformed explicit color — treat the WHOLE mark as absent (FFS-CG-02-c).
            return null;
        }

        return Slot(
            "div",
            "brand-mark",
            string.Empty,
            $"position:absolute;{horizontalCss}bottom:56px;width:112px;height:36px;" +
            $"border-radius:8px;background:{colorHex};opacity:0.92;",
            1);
    }

    /// <summary>
    /// Resolves the horizontal placement (left/center/right) of a brand mark from its position
    /// hint. A generous substring match tolerates hint variance from the vision model (e.g.
    /// "bottom-left", "top-left", "left" all resolve to the same horizontal offset — vertical
    /// placement is always conservatively bottom-anchored by the caller). Returns <c>false</c> for
    /// a null hint's caller-visible default (center) as well as any hint that matches none of the
    /// known tokens (ambiguous — caller treats the whole mark as absent).
    /// </summary>
    private static bool TryResolveBrandMarkHorizontalCss(string? positionHint, out string horizontalCss)
    {
        if (positionHint is null)
        {
            horizontalCss = "left:50%;transform:translateX(-50%);";
            return true;
        }

        var normalized = positionHint.Trim().ToLowerInvariant();
        if (normalized.Contains("left", StringComparison.Ordinal))
        {
            horizontalCss = "left:64px;";
            return true;
        }

        if (normalized.Contains("right", StringComparison.Ordinal))
        {
            horizontalCss = "right:64px;";
            return true;
        }

        if (normalized.Contains("center", StringComparison.Ordinal) || normalized.Contains("centre", StringComparison.Ordinal))
        {
            horizontalCss = "left:50%;transform:translateX(-50%);";
            return true;
        }

        horizontalCss = string.Empty;
        return false;
    }

    /// <summary>
    /// Injects the brand-mark slot as the first child of the resolved markup's
    /// <c>data-layer="content"</c> element. Works uniformly across every markup source this
    /// processor produces — the FlatColor builder, the deterministic template-backed builder, and
    /// guardrail-validated AI markup — because the guardrail schema step (VD-04) HARD-requires
    /// every composition to carry a <c>data-layer="content"</c> element (FFS-CG-02). Defensive: if
    /// no content layer is found (should never happen post-guardrail), the markup is returned
    /// unchanged rather than corrupted.
    /// </summary>
    private static string AppendBrandMarkSlot(string markup, string brandMarkSlotHtml)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            markup,
            "<div[^>]*\\bdata-layer\\s*=\\s*\"content\"[^>]*>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (!match.Success)
        {
            return markup;
        }

        var insertAt = match.Index + match.Length;
        return markup.Insert(insertAt, Environment.NewLine + brandMarkSlotHtml);
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

    /// <summary>
    /// CAVS-CG-01: builds the deterministic <c>data-slot="divider"</c> rule element for a client's
    /// measured recurring divider-rule style. Design D2 — this is exactly ONE
    /// <c>data-slot="divider"</c> element; the diamond/dot glyph is a nested plain <c>&lt;div&gt;</c>
    /// with NO <c>data-slot</c> attribute of its own (a second <c>data-slot="divider"</c> would emit
    /// two <see cref="Pipeline.SlotGeometryEntry"/> captures and two <c>PptxExportAdapter</c> matches
    /// for one logical slot). Built as a raw inline string — the <see cref="Slot"/> helper HTML-encodes
    /// its text content and cannot carry a child element (same technique as
    /// <see cref="BuildBrandMarkSlot"/>'s icon <c>&lt;img&gt;</c> branch). Returns <c>null</c> —
    /// meaning "render nothing" — when <paramref name="dividerRule"/> is <c>null</c> (unmeasured,
    /// absent, or an unrecognised token already collapsed to <c>null</c> upstream), which is what
    /// keeps the <c>DecorationMotifs = null</c> cold-start path byte-identical (CAVS-CG-03).
    /// </summary>
    /// <param name="dividerRule">The client's measured divider-rule style, or <c>null</c>.</param>
    /// <param name="accentColor">
    /// The slide's resolved accent hex (already CSS-safety-checked by the caller), or <c>null</c>
    /// when no <c>Accent</c> palette role resolved.
    /// </param>
    /// <param name="textColor">The slide's resolved text colour — the fallback when there is no accent (CAVS-CG-01-b).</param>
    /// <param name="geometry">
    /// The resolved composition-archetype geometry (WU-5, CAVS-CG-02) supplying the divider's
    /// left/top/width; the identity preset (<see cref="FlatGeometryPresets.LeftAligned"/>) reproduces
    /// the WU-4 hardcoded literal exactly.
    /// </param>
    private static string? BuildDividerSlot(
        DividerRuleStyle? dividerRule,
        string? accentColor,
        string textColor,
        FlatCarouselGeometry geometry)
    {
        if (dividerRule is null)
        {
            return null;
        }

        var ruleColor = accentColor ?? textColor;

        var glyph = dividerRule switch
        {
            DividerRuleStyle.LineWithDiamond =>
                "<div style=\"position:absolute;left:50%;top:50%;width:12px;height:12px;" +
                $"transform:translate(-50%,-50%) rotate(45deg);background:{ruleColor};\"></div>",
            DividerRuleStyle.LineWithDot =>
                "<div style=\"position:absolute;left:50%;top:50%;width:12px;height:12px;" +
                $"transform:translate(-50%,-50%);border-radius:50%;background:{ruleColor};\"></div>",
            _ => string.Empty,
        };

        return "<div class=\"slot\" data-slot=\"divider\" data-max-lines=\"1\" " +
            $"style=\"position:absolute;left:{geometry.DividerLeft}px;top:{geometry.DividerTop}px;width:{geometry.DividerWidth}px;height:2px;" +
            $"background:{ruleColor};\">{glyph}</div>";
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

    /// <summary>
    /// Returns <paramref name="value"/> when it is a safe CSS hex color (#RGB / #RRGGBB / #RRGGBBAA),
    /// otherwise <paramref name="fallback"/>. Guards the deterministic fallback markup from injecting
    /// arbitrary brand-config strings into an inline <c>style</c> attribute.
    /// </summary>
    private static string CssColor(string? value, string fallback) =>
        IsSafeCssColor(value) ? value!.Trim() : fallback;

    /// <summary>
    /// True when <paramref name="value"/> is a safe CSS hex color (#RGB / #RRGGBB / #RRGGBBAA).
    /// Extracted from <see cref="CssColor"/> (task 1.11 dedupe) so callers that need an
    /// omit-if-unsafe decision (e.g. the FlatColor accent slot) don't need a throwaway fallback.
    /// </summary>
    private static bool IsSafeCssColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var v = value.Trim();
        if (v[0] != '#' || v.Length is not (4 or 7 or 9))
        {
            return false;
        }

        for (var i = 1; i < v.Length; i++)
        {
            if (!Uri.IsHexDigit(v[i]))
            {
                return false;
            }
        }

        return true;
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

    /// <summary>
    /// WU-7: Advisory concision check (CG-CON-03).
    /// Inspects text slots in the generated payload and emits a structured LogWarning for any
    /// slot that exceeds its advisory soft limit. Never throws, never changes the result path.
    /// Soft limits (chars): headline/title ~80, body/subtitle ~160, CTA ~40, slide-title ~50, slide-body ~160.
    /// </summary>
    private void CheckConcisionAdvisory(Guid jobId, string formatKey, object? payload)
    {
        if (payload is null) return;

        // Re-serialize and inspect known payload shapes via JSON to avoid hard content-type
        // references in the processor (advisory-only, so approximation is fine).
        try
        {
            var json = JsonSerializer.Serialize(payload, JsonOptions);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            void WarnIfOver(string slotName, JsonElement element, int advisoryBound)
            {
                if (element.ValueKind != JsonValueKind.String) return;
                var text = element.GetString() ?? string.Empty;
                if (text.Length > advisoryBound)
                {
                    _logger.LogWarning(
                        "ContentGeneration: concision advisory — job {JobId} format {FormatKey} slot {SlotName} " +
                        "exceeds soft limit ({AdvisoryBound} chars); actual length {ActualLength}. Pipeline continues.",
                        jobId, formatKey, slotName, advisoryBound, text.Length);
                }
            }

            // Single formats: title, subtitle, body, ctaText
            if (root.TryGetProperty("title", out var title))         WarnIfOver("title",    title,    80);
            if (root.TryGetProperty("subtitle", out var subtitle))   WarnIfOver("subtitle", subtitle, 160);
            if (root.TryGetProperty("body", out var body))           WarnIfOver("body",     body,     160);
            if (root.TryGetProperty("ctaText", out var cta))         WarnIfOver("ctaText",  cta,       40);

            // Carousel: coverTitle + per-slide title/body
            if (root.TryGetProperty("coverTitle", out var coverTitle)) WarnIfOver("coverTitle", coverTitle, 80);
            if (root.TryGetProperty("slides", out var slides) && slides.ValueKind == JsonValueKind.Array)
            {
                var slideIdx = 0;
                foreach (var slide in slides.EnumerateArray())
                {
                    slideIdx++;
                    if (slide.TryGetProperty("title", out var sTitle)) WarnIfOver($"slides[{slideIdx}].title", sTitle, 50);
                    if (slide.TryGetProperty("body", out var sBody))   WarnIfOver($"slides[{slideIdx}].body",  sBody,  160);
                }
            }
        }
        catch
        {
            // Concision check is best-effort; swallow any serialization/parsing exceptions
            // to ensure the advisory path can never disrupt the production pipeline.
        }
    }

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

/// <summary>
/// Optional classification context <c>ContentGenerationBackgroundWorker</c> (ApiService) supplies to
/// <see cref="ContentGenerationJobProcessor.ProcessAsync"/> so its terminal-cancellation write can
/// tell a per-job wall-clock budget timeout apart from host shutdown / any other cancellation (Gap 1,
/// incident job 8757e85a-6acd-49b4-af11-a2d19fbcd3e1, Change 5).
///
/// <para>
/// <see cref="HostShutdownToken"/> is the OUTER token the worker links into the job's own
/// <c>CancelAfter</c> token to build <c>ct</c>. When <see cref="HostShutdownToken"/> is NOT
/// cancelled at the moment <c>ct</c> IS, only the job's own budget timer could have fired — the same
/// check <c>ContentGenerationBackgroundWorker.ProcessAsync</c> already performs to decide whether to
/// log "TIMED OUT" vs. treat the cancellation as host shutdown, now threaded into the persisted record
/// instead of staying log-only.
/// </para>
/// </summary>
public sealed record ContentGenerationJobCancellationContext(
    CancellationToken HostShutdownToken,
    TimeSpan JobTimeoutLimit);
