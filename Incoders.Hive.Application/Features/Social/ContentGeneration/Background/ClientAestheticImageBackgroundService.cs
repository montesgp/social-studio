using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Visual;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ContentGeneration;
using Microsoft.Extensions.Logging;
// WU-9: alias removed — IImageGenerationClient retired; ImageGenerationRequest is unambiguous now.

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Background;

/// <summary>
/// Orchestrates: prompt-write → image-render → durable-store → ForcedDefault
/// for the subset of "creative" slides in a <c>ClientAesthetic</c> carousel.
///
/// <para>
/// Applies <see cref="CreativeSlideIndexResolver.MaxCreativeSlidesPerPackage"/> cap,
/// processes the ≤3 creative slides SERIALLY (they share a scoped, non-thread-safe EF
/// DbContext via the prompt-writer's workflow-config resolution; parallelism raced it),
/// reuses stored backgrounds when the prompt hash is unchanged (OQ-3),
/// and omits any failed slide from the returned map — degrading that slide to the
/// existing CSS designer path (CIG-CG-03 resilience contract).
/// </para>
///
/// <para>
/// CAC-IMG-01 (conditioning): calls <see cref="IImageGenerationGateway.DescribeAsync"/> ONCE per job
/// before the per-slide loop. When <c>SupportsImageConditioning = true</c> and the profile has
/// reference images, up to <c>min(MaxConditioningImages, 3)</c> Url handles are forwarded as
/// <see cref="ImageGenerationRequest.ReferenceImages"/>.
/// </para>
///
/// <para>
/// CAC-IMG-02 (background-only): sets <see cref="ImageGenerationRequest.BackgroundOnly"/> = true
/// on every creative-slide request so the adapter injects the background-only system directive.
/// </para>
///
/// <para>
/// CAC-IMG-03 (guardrail retry): when an optional <see cref="IImageGuardrailValidator"/> is
/// injected, each generated image is validated. A failing image triggers a retry up to
/// <see cref="MaxGuardrailAttempts"/> total attempts; on exhaustion the slide degrades to CSS.
/// </para>
///
/// <para>
/// CAC-INT-01 (per-slide telemetry): when an optional <see cref="IIntegrationExecutionAttemptRepository"/>
/// is injected, one attempt row is written per creative slide — <c>Succeeded</c> on a generated
/// background or <c>Failed</c> when the slide degrades to CSS.  Telemetry write failures are
/// non-fatal and never fail the generation job.
/// </para>
///
/// Storage key: <c>(org, client, formatResultId, "carousel/slide_{index}.png")</c>.
/// Hash sidecar: <c>(org, client, formatResultId, "carousel/slide_{index}.png.hash")</c>
/// — stores <c>{promptHash}|{imageStoragePath}</c> for reuse detection.
///
/// Refs: IMG-03, IMG-04, IMG-05, CIG-CG-03, CIG-CG-04, CAC-IMG-01, CAC-IMG-02, CAC-IMG-03,
/// CAC-INT-01; design §3.3, §3.4, §4.1, §4.2, §5, OQ-3, OQ-4, OQ-6.
/// </summary>
public sealed class ClientAestheticImageBackgroundService : IClientAestheticImageBackgroundService
{
    private const string ImageCapabilityKey = "ai-image";
    private const string ImagePromptVersionKey = "image-gen/v1";
    private const string ImageMime = "image/png";

    /// <summary>
    /// Generation dimensions MUST be a multiple of 64, not 8.
    /// </summary>
    /// <remarks>
    /// The VAE alone only needs 8 (that is what <c>EmptyLatentImage</c> validates), but the UNet
    /// applies three further 2x downsample stages on top of it — 8 * 2^3 = 64. Feeding it a size that
    /// is a multiple of 8 but not of 64 makes the skip connections mismatch, and the backend pads and
    /// reallocates instead of failing cleanly.
    ///
    /// <para>
    /// Measured on the reference dev backend (ComfyUI 0.3.48, --directml --lowvram --cpu-vae):
    /// 1024x1024 (both multiples of 64) SUCCEEDS; 912x1144 OOMs at ~4.2 GB despite having FEWER total
    /// pixels; 1080x1352 OOMs at ~4.0 GB. Total pixel count is not what decides it — divisibility is.
    /// 1024x1280 (4:5, both multiples of 64, 25% MORE pixels than 1024x1024) also succeeds.
    /// </para>
    /// </remarks>
    private const int LatentDimensionMultiple = 64;

    /// <summary>
    /// Upper bound on generated pixels per image, so a large canvas cannot exceed what the backend's
    /// video memory can hold. Set to the largest size verified to complete on the reference dev
    /// backend (1024x1280 = 1_310_720). Dimensions are scaled down proportionally to fit, preserving
    /// the canvas aspect ratio, so the render still never has to crop.
    /// </summary>
    private const int MaxGenerationPixels = 1_310_720;

    /// <summary>Capability key used for per-slide telemetry rows (CAC-INT-01).</summary>
    internal const string SlideCapabilityKey = "generate-image";

    /// <summary>Provider key used for per-slide telemetry rows (CAC-INT-01).</summary>
    internal const string SlideProviderKey = "carousel-background-service";

    /// <summary>Correlation kind forwarded on every per-slide image request (CAC-INT-01).</summary>
    internal const string SlideCorrelationKind = "content-generation/generate-image";

    /// <summary>
    /// Hard cap on reference images forwarded for conditioning (CAC-IMG-01).
    /// Applied even when MaxConditioningImages is higher.
    /// </summary>
    private const int ReferenceImageHardCap = 3;

    /// <summary>
    /// Maximum total generation attempts per slide for guardrail retry (CAC-IMG-03).
    /// 1 initial attempt + up to 2 retries = 3 total.
    /// </summary>
    private const int MaxGuardrailAttempts = 3;

    private readonly IImagePromptWriter _promptWriter;
    private readonly IImageGenerationGateway _imageGateway;
    private readonly IGeneratedAssetStorage _storage;
    private readonly ILogger<ClientAestheticImageBackgroundService> _logger;
    private readonly IImageGuardrailValidator? _guardrailValidator;
    private readonly IIntegrationExecutionAttemptRepository? _attemptRepository;

    public ClientAestheticImageBackgroundService(
        IImagePromptWriter promptWriter,
        IImageGenerationGateway imageGateway,
        IGeneratedAssetStorage storage,
        ILogger<ClientAestheticImageBackgroundService> logger,
        IImageGuardrailValidator? guardrailValidator = null,
        IIntegrationExecutionAttemptRepository? attemptRepository = null)
    {
        ArgumentNullException.ThrowIfNull(promptWriter);
        ArgumentNullException.ThrowIfNull(imageGateway);
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(logger);

        _promptWriter = promptWriter;
        _imageGateway = imageGateway;
        _storage = storage;
        _logger = logger;
        _guardrailValidator = guardrailValidator;
        _attemptRepository = attemptRepository;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<int, BackgroundSelection.ForcedDefault>> BuildCreativeBackgroundsAsync(
        ClientAestheticImageRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Apply OQ-2 cap — the caller already resolves indices but we enforce the hard cap here too.
        var cappedIndices = request.CreativeSlideIndices
            .Take(CreativeSlideIndexResolver.MaxCreativeSlidesPerPackage)
            .ToList();

        if (cappedIndices.Count == 0)
        {
            return new Dictionary<int, BackgroundSelection.ForcedDefault>();
        }

        // CAC-IMG-01: call DescribeAsync ONCE per job, before the per-slide loop.
        var referenceImages = await ResolveReferenceImagesAsync(request, ct);

        // CAC-IMG-04 (pixelation fix): resolve the target render size from the format's actual
        // logical canvas (CompositionDefaults) instead of a hardcoded 1024x1024 square. The prior
        // hardcoded square forced a ~2.6x upscale plus a 1:1 -> 4:5 aspect crop at render time
        // (carousel canvas is 1080x1350, physical output 2160x2700 with DeviceScaleFactor=2), which
        // produced visibly pixelated backgrounds. Every slide in this format shares the same canvas,
        // so this is resolved once per job, not per slide.
        var (targetWidth, targetHeight) = ResolveTargetDimensions(request.FormatKey);

        // Serialized per-slide loop (previously Task.WhenAll — OQ-4 "bounded parallel").
        // The per-slide pipeline transitively resolves workflow config through a scoped EF
        // DbContext (ImagePromptWriter → WorkflowConfigurationResolver →
        // EfWorkflowStepConfigurationRepository), and EF Core's DbContext is NOT thread-safe.
        // Running the ≤2 slides in parallel raced the shared context — "A second operation was
        // started on this context instance before a previous operation completed" — and the
        // losing slide degraded to CSS, so ClientAesthetic carousels came out without their
        // generated photo slides. With the cap at 2 slides (and image generation as the dominant
        // cost) the parallelism saved nothing meaningful; serial execution is the correct fix.
        var map = new Dictionary<int, BackgroundSelection.ForcedDefault>();
        foreach (var slideIndex in cappedIndices)
        {
            // Per-slide degradation is the right answer when a slide FAILS: the rest of the carousel
            // is still wanted. It is the wrong answer when the JOB is over — every remaining slide
            // then buys a fresh round of provider attempts nobody is waiting on. The adapters report
            // a cancelled token as a failed Result rather than throwing, so the loop has to ask.
            if (ct.IsCancellationRequested)
            {
                _logger.LogInformation(
                    "ClientAestheticImageBackgroundService: job cancelled — stopping before slide {SlideIndex} " +
                    "(org {OrgId}). {Completed} slide(s) resolved.",
                    slideIndex, request.OrganizationId, map.Count);
                break;
            }

            var (index, forcedDefault) = await BuildSingleSlideAsync(
                request, slideIndex, referenceImages, targetWidth, targetHeight, ct);
            if (forcedDefault is not null)
            {
                map[index] = forcedDefault;
            }
        }

        return map;
    }

    // -----------------------------------------------------------------------
    // CAC-IMG-01: resolve capability once per job → build reference image handles
    // -----------------------------------------------------------------------

    private async Task<IReadOnlyList<ImageInitReference>?> ResolveReferenceImagesAsync(
        ClientAestheticImageRequest request,
        CancellationToken ct)
    {
        var describeResult = await _imageGateway.DescribeAsync(request.OrganizationId, ct);
        if (describeResult.IsFailure)
        {
            _logger.LogDebug(
                "ClientAestheticImageBackgroundService: DescribeAsync failed for org {OrgId}: {Code}. " +
                "Conditioning disabled for this job.",
                request.OrganizationId, describeResult.Error.Code);
            return null;
        }

        var caps = describeResult.Value;
        if (!caps.SupportsImageConditioning)
        {
            return null;
        }

        var profileRefs = request.Aesthetic.ReferenceImages;
        if (profileRefs is not { Count: > 0 })
        {
            return null;
        }

        // CAC-IMG-01-a: cap to min(MaxConditioningImages, ReferenceImageHardCap).
        var cap = Math.Min(caps.MaxConditioningImages, ReferenceImageHardCap);
        return profileRefs
            .Take(cap)
            .Select(r => new ImageInitReference(Bytes: null, Url: r.StoragePath, Mime: "image/jpeg"))
            .ToList();
    }

    // -----------------------------------------------------------------------
    // Per-slide pipeline: write prompt → render (with guardrail retry) → persist → ForcedDefault
    // Any step failure → returns (slideIndex, null); the caller omits it from the map.
    // CAC-INT-01: a telemetry row is written at each terminal outcome (success or CSS fallback).
    // -----------------------------------------------------------------------

    private async Task<(int SlideIndex, BackgroundSelection.ForcedDefault? ForcedDefault)> BuildSingleSlideAsync(
        ClientAestheticImageRequest request,
        int slideIndex,
        IReadOnlyList<ImageInitReference>? referenceImages,
        int targetWidth,
        int targetHeight,
        CancellationToken ct)
    {
        try
        {
            // 1. Write the image prompt for this slide.
            var copySummary = GetCopySummary(request, slideIndex);
            // client-style-fidelity CSF-CG-08: the client's own treatment at this slide position (the
            // same proportional mapping the render loop uses), so the generated image reproduces it
            // instead of a generic photo in the client's colours.
            var slideKind = CreativeSlideIndexResolver.ResolveKindForSlide(
                request.Aesthetic.BackgroundPattern, request.SlideCount, slideIndex);
            var treatment = CreativeSlideIndexResolver.ResolveTransparencyForSlide(
                request.Aesthetic.BackgroundTransparency, request.SlideCount, slideIndex);
            var pairing = CompositionDefaults.SelectPairing(request.Aesthetic, slideIndex, request.SlideCount, slideKind);
            var promptRequest = new ImagePromptRequest(
                request.OrganizationId,
                request.ClientId,
                slideIndex,
                copySummary,
                request.Aesthetic,
                slideKind,
                string.IsNullOrWhiteSpace(treatment?.Descriptor) ? null : treatment.Descriptor,
                pairing is { IsObserved: true } ? pairing.BackgroundHex : null);

            var promptResult = await _promptWriter.WriteAsync(promptRequest, ct);
            if (promptResult.IsFailure)
            {
                _logger.LogWarning(
                    "ClientAestheticImageBackgroundService: prompt write failed for slide {SlideIndex} " +
                    "(org {OrgId}): {Code}. Slide degrades to CSS.",
                    slideIndex, request.OrganizationId, promptResult.Error.Code);
                await WriteCssFallbackTelemetryAsync(request, slideIndex, promptResult.Error.Code);
                return (slideIndex, null);
            }

            var prompt = promptResult.Value;

            // 2. OQ-3: check reuse by prompt hash.
            var promptHash = ComputePromptHash(prompt);
            var imageRelativePath = $"carousel/slide_{slideIndex}.png";
            var hashRelativePath = $"{imageRelativePath}.hash";

            var reuseResult = await TryReuseAsync(request, hashRelativePath, promptHash, ct);
            if (reuseResult is not null)
            {
                _logger.LogDebug(
                    "ClientAestheticImageBackgroundService: reusing stored background for slide {SlideIndex} " +
                    "(org {OrgId}) — prompt hash unchanged.",
                    slideIndex, request.OrganizationId);
                await WriteSuccessTelemetryAsync(request, slideIndex);
                return (slideIndex, new BackgroundSelection.ForcedDefault(reuseResult, "ai-generated-background"));
            }

            // 3. Render the image with guardrail retry (CAC-IMG-03).
            var (renderedImage, failureReason) = await RenderWithGuardrailRetryAsync(
                request, slideIndex, prompt, referenceImages, targetWidth, targetHeight, ct);

            if (renderedImage is null)
            {
                // Guardrail exhaustion or generation failure already logged inside RenderWithGuardrailRetryAsync.
                await WriteCssFallbackTelemetryAsync(request, slideIndex, failureReason ?? "generation-failed");
                return (slideIndex, null);
            }

            // 4. Persist the generated image to durable storage.
            var imageKey = new StoredAssetKey(
                request.OrganizationId,
                request.ClientId ?? Guid.Empty,
                request.FormatResultId,
                imageRelativePath);

            var storedRef = await _storage.PutAssetAsync(
                imageKey,
                new StoredAssetContent(renderedImage.Bytes, renderedImage.Mime),
                ct);

            // 5. Persist the hash sidecar for future reuse detection.
            await PersistHashSidecarAsync(request, hashRelativePath, promptHash, storedRef.StoragePath, ct);

            await WriteSuccessTelemetryAsync(request, slideIndex);
            var url = storedRef.StoragePath;
            return (slideIndex, new BackgroundSelection.ForcedDefault(url, "ai-generated-background"));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "ClientAestheticImageBackgroundService: unhandled exception for slide {SlideIndex} " +
                "(org {OrgId}). Slide degrades to CSS.",
                slideIndex, request.OrganizationId);
            await WriteCssFallbackTelemetryAsync(request, slideIndex, "unexpected-exception");
            return (slideIndex, null);
        }
    }

    // -----------------------------------------------------------------------
    // CAC-IMG-03: image generation with bounded guardrail retry per slide
    // Returns (result, null) on success; (null, reason) on any failure.
    // CAC-INT-01: caller threads CorrelationId so the gateway policy tracks provider attempts.
    // -----------------------------------------------------------------------

    private async Task<(ImageGenerationResult? Result, string? Reason)> RenderWithGuardrailRetryAsync(
        ClientAestheticImageRequest request,
        int slideIndex,
        string prompt,
        IReadOnlyList<ImageInitReference>? referenceImages,
        int targetWidth,
        int targetHeight,
        CancellationToken ct)
    {
        for (var attempt = 1; attempt <= MaxGuardrailAttempts; attempt++)
        {
            // CAC-IMG-02: set BackgroundOnly = true so the adapter injects the system directive.
            // CAC-INT-01: thread CorrelationId so the gateway policy records provider-level attempts.
            var imageRequest = new ImageGenerationRequest(
                request.OrganizationId,
                ImageCapabilityKey,
                ImagePromptVersionKey,
                prompt,
                targetWidth,
                targetHeight,
                PreferredProviderKey: null,
                PreferredModel: null,
                ReferenceImages: referenceImages,
                BackgroundOnly: true,
                CorrelationId: request.FormatResultId,
                CorrelationKind: SlideCorrelationKind);

            var imageResult = await _imageGateway.GenerateAsync(imageRequest, ct);
            if (imageResult.IsFailure)
            {
                _logger.LogWarning(
                    "ClientAestheticImageBackgroundService: image generation failed for slide {SlideIndex} " +
                    "(org {OrgId}) attempt {Attempt}: {Code}. Slide degrades to CSS.",
                    slideIndex, request.OrganizationId, attempt, imageResult.Error.Code);
                return (null, imageResult.Error.Code); // generation failure is not retried
            }

            // CAC-IMG-03: apply guardrail validator when injected.
            if (_guardrailValidator is null)
            {
                return (imageResult.Value, null);
            }

            var validationResult = _guardrailValidator.Validate(imageResult.Value);
            if (validationResult.IsSuccess)
            {
                return (validationResult.Value, null);
            }

            // Guardrail failed — retry if budget allows.
            var reason = validationResult.Error.Code;
            if (attempt < MaxGuardrailAttempts)
            {
                _logger.LogDebug(
                    "ClientAestheticImageBackgroundService: guardrail failed for slide {SlideIndex} " +
                    "(org {OrgId}) attempt {Attempt}/{Max}: {Reason}. Retrying.",
                    slideIndex, request.OrganizationId, attempt, MaxGuardrailAttempts, reason);
            }
            else
            {
                _logger.LogWarning(
                    "ClientAestheticImageBackgroundService: guardrail retries exhausted for slide {SlideIndex} " +
                    "(job {JobId}, org {OrgId}). Last reason: {Reason}. Slide degrades to CSS.",
                    slideIndex, request.JobId, request.OrganizationId, reason);
                return (null, "guardrail-exhausted");
            }
        }

        return (null, "guardrail-exhausted"); // unreachable but satisfies the compiler
    }

    // -----------------------------------------------------------------------
    // CAC-INT-01: per-slide telemetry writes — non-fatal, swallowed on failure
    // -----------------------------------------------------------------------

    private async Task WriteSuccessTelemetryAsync(ClientAestheticImageRequest request, int slideIndex)
    {
        if (_attemptRepository is null)
        {
            return;
        }

        try
        {
            var now = DateTime.UtcNow;
            await _attemptRepository.AddAsync(new IntegrationExecutionAttemptRecord(
                Id: Guid.NewGuid(),
                AnalysisId: null,
                CapabilityKey: SlideCapabilityKey,
                ProviderKey: SlideProviderKey,
                Status: IntegrationExecutionAttemptStatus.Succeeded,
                AttemptNumber: 1,
                FailureCode: null,
                RetryCount: 0,
                ElapsedMs: 0,
                DiagnosticsJson: JsonSerializer.Serialize(new { slideIndex }),
                StartedAtUtc: now,
                CompletedAtUtc: now,
                CorrelationId: request.FormatResultId,
                CorrelationKind: SlideCorrelationKind));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "ClientAestheticImageBackgroundService: telemetry write (success) failed for slide {SlideIndex}. " +
                "Continuing — telemetry failure must not fail the generation job.",
                slideIndex);
        }
    }

    private async Task WriteCssFallbackTelemetryAsync(
        ClientAestheticImageRequest request,
        int slideIndex,
        string reason)
    {
        if (_attemptRepository is null)
        {
            return;
        }

        try
        {
            var now = DateTime.UtcNow;
            await _attemptRepository.AddAsync(new IntegrationExecutionAttemptRecord(
                Id: Guid.NewGuid(),
                AnalysisId: null,
                CapabilityKey: SlideCapabilityKey,
                ProviderKey: SlideProviderKey,
                Status: IntegrationExecutionAttemptStatus.Failed,
                AttemptNumber: 1,
                FailureCode: reason,
                RetryCount: 0,
                ElapsedMs: 0,
                DiagnosticsJson: JsonSerializer.Serialize(new { slideIndex, outcome = "degraded-to-css", reason }),
                StartedAtUtc: now,
                CompletedAtUtc: now,
                CorrelationId: request.FormatResultId,
                CorrelationKind: SlideCorrelationKind));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "ClientAestheticImageBackgroundService: telemetry write (CSS fallback) failed for slide {SlideIndex}. " +
                "Continuing — telemetry failure must not fail the generation job.",
                slideIndex);
        }
    }

    // -----------------------------------------------------------------------
    // OQ-3: reuse check via hash sidecar
    // -----------------------------------------------------------------------

    private async Task<string?> TryReuseAsync(
        ClientAestheticImageRequest request,
        string hashRelativePath,
        string promptHash,
        CancellationToken ct)
    {
        var hashKey = new StoredAssetKey(
            request.OrganizationId,
            request.ClientId ?? Guid.Empty,
            request.FormatResultId,
            hashRelativePath);

        // Derive the full storage path from the same key used by the write path (PersistHashSidecarAsync),
        // eliminating the previously-separate hand-built string that could drift from the write path.
        var hashStoragePath = $"{hashKey.OrganizationId}/{hashKey.ClientId}/{hashKey.AnalysisId}/{hashKey.RelativePath}";

        var openResult = await _storage.OpenAsync(hashStoragePath, ct);
        if (openResult.IsFailure)
        {
            // Asset not found → no sidecar → must generate fresh.
            return null;
        }

        try
        {
            using var stream = openResult.Value;
            using var reader = new StreamReader(stream.Content, Encoding.UTF8);
            var sidecarContent = await reader.ReadToEndAsync(ct);

            // Format: "{promptHash}|{imageStoragePath}"
            var pipeIdx = sidecarContent.IndexOf('|', StringComparison.Ordinal);
            if (pipeIdx < 0)
            {
                return null;
            }

            var storedHash = sidecarContent[..pipeIdx];
            var storedImagePath = sidecarContent[(pipeIdx + 1)..];

            if (string.Equals(storedHash, promptHash, StringComparison.Ordinal))
            {
                return storedImagePath;
            }

            // Hash differs → regenerate.
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "ClientAestheticImageBackgroundService: failed to read hash sidecar '{Path}'. Will regenerate.",
                hashStoragePath);
            return null;
        }
    }

    private async Task PersistHashSidecarAsync(
        ClientAestheticImageRequest request,
        string hashRelativePath,
        string promptHash,
        string imageStoragePath,
        CancellationToken ct)
    {
        try
        {
            var sidecarContent = $"{promptHash}|{imageStoragePath}";
            var sidecarBytes = Encoding.UTF8.GetBytes(sidecarContent);

            var hashKey = new StoredAssetKey(
                request.OrganizationId,
                request.ClientId ?? Guid.Empty,
                request.FormatResultId,
                hashRelativePath);

            await _storage.PutAssetAsync(hashKey, new StoredAssetContent(sidecarBytes, "text/plain"), ct);
        }
        catch (Exception ex)
        {
            // Sidecar persistence failure is non-fatal — next run will simply regenerate.
            _logger.LogWarning(ex,
                "ClientAestheticImageBackgroundService: failed to persist hash sidecar for '{Path}'. " +
                "Next run will regenerate even if prompt is unchanged.",
                hashRelativePath);
        }
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Computes a deterministic SHA-256 hex hash of the given prompt string.
    /// Public so tests can pre-compute the expected hash for reuse scenarios.
    /// </summary>
    public static string ComputePromptHash(string prompt)
    {
        var bytes = Encoding.UTF8.GetBytes(prompt);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// CAC-IMG-04 (pixelation fix): resolves the (Width, Height) to request from the image
    /// generation gateway for the given format, derived from the format's logical canvas
    /// (<see cref="CompositionDefaults.ResolveDimensionsForFormat"/>) and snapped up to the nearest
    /// multiple of <see cref="LatentDimensionMultiple"/> the diffusion backend's latent space
    /// requires. Public so tests can assert the exact resolved size without duplicating the rounding
    /// rule.
    /// </summary>
    public static (int Width, int Height) ResolveTargetDimensions(string formatKey)
    {
        var canvas = CompositionDefaults.ResolveDimensionsForFormat(formatKey);
        return ResolveGenerationSize(canvas.Width, canvas.Height);
    }

    /// <summary>
    /// Resolves the generation size for a logical canvas: the canvas aspect ratio is preserved (so the
    /// render never has to crop), both dimensions land on a multiple of
    /// <see cref="LatentDimensionMultiple"/>, and the area stays within
    /// <see cref="MaxGenerationPixels"/>. Never generates larger than the canvas itself.
    /// </summary>
    /// <remarks>
    /// Resolved sizes: feed carousel 1080x1350 (4:5) -> 1024x1280, an exact 4:5 match verified to
    /// complete on the reference backend; story 1080x1920 (9:16) -> 832x1472; feed post 1080x1080 ->
    /// 1024x1024. This replaces the hardcoded 1024x1024 square, which forced a 1:1 -> 4:5 crop on
    /// every carousel slide.
    /// </remarks>
    internal static (int Width, int Height) ResolveGenerationSize(int canvasWidth, int canvasHeight)
    {
        if (canvasWidth < 1 || canvasHeight < 1)
        {
            return (LatentDimensionMultiple, LatentDimensionMultiple);
        }

        var aspect = (double)canvasWidth / canvasHeight;
        var canvasArea = (double)canvasWidth * canvasHeight;

        // Never upscale past the canvas; shrink only when the canvas exceeds the memory budget.
        var scale = Math.Min(1.0, Math.Sqrt(MaxGenerationPixels / canvasArea));

        var width = SnapToLatentMultiple(canvasWidth * scale);
        var height = SnapToLatentMultiple(width / aspect);

        // Snapping rounds outward and can push the area back over budget; step the width down one
        // latent block at a time until it fits, recomputing the height so the aspect ratio holds.
        while ((long)width * height > MaxGenerationPixels && width > LatentDimensionMultiple)
        {
            width -= LatentDimensionMultiple;
            height = SnapToLatentMultiple(width / aspect);
        }

        return (width, height);
    }

    /// <summary>
    /// Rounds to the NEAREST multiple of <see cref="LatentDimensionMultiple"/>, with a floor of one
    /// multiple. Nearest rather than up: rounding up is what pushed a 1080-wide canvas to 1088 and,
    /// combined with the old multiple of 8, produced sizes the backend could not allocate.
    /// </summary>
    internal static int SnapToLatentMultiple(double value)
    {
        var snapped = (int)Math.Round(value / LatentDimensionMultiple, MidpointRounding.AwayFromZero)
            * LatentDimensionMultiple;
        return Math.Max(LatentDimensionMultiple, snapped);
    }

    private static string GetCopySummary(ClientAestheticImageRequest request, int slideIndex)
    {
        // PerSlideCopySummaries is 0-indexed; slideIndex is 1-based.
        var idx = slideIndex - 1;
        if (idx >= 0 && idx < request.PerSlideCopySummaries.Count)
        {
            return request.PerSlideCopySummaries[idx];
        }
        return string.Empty;
    }
}
