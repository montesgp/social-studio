using System.Security.Cryptography;
using System.Text;
using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
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
/// runs <see cref="Task.WhenAll"/> bounded to ≤2 slides (OQ-4),
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
/// Storage key: <c>(org, client, formatResultId, "carousel/slide_{index}.png")</c>.
/// Hash sidecar: <c>(org, client, formatResultId, "carousel/slide_{index}.png.hash")</c>
/// — stores <c>{promptHash}|{imageStoragePath}</c> for reuse detection.
///
/// Refs: IMG-03, IMG-04, IMG-05, CIG-CG-03, CIG-CG-04, CAC-IMG-01, CAC-IMG-02, CAC-IMG-03;
/// design §3.3, §3.4, §4.1, §4.2, §5, OQ-3, OQ-4.
/// </summary>
public sealed class ClientAestheticImageBackgroundService : IClientAestheticImageBackgroundService
{
    private const string ImageCapabilityKey = "ai-image";
    private const string ImagePromptVersionKey = "image-gen/v1";
    private const int TargetWidth = 1024;
    private const int TargetHeight = 1024;
    private const string ImageMime = "image/png";

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

    public ClientAestheticImageBackgroundService(
        IImagePromptWriter promptWriter,
        IImageGenerationGateway imageGateway,
        IGeneratedAssetStorage storage,
        ILogger<ClientAestheticImageBackgroundService> logger,
        IImageGuardrailValidator? guardrailValidator = null)
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

        // OQ-4: bounded Task.WhenAll across ≤2 slides.
        var tasks = cappedIndices.Select(
            slideIndex => BuildSingleSlideAsync(request, slideIndex, referenceImages, ct));
        var results = await Task.WhenAll(tasks);

        var map = new Dictionary<int, BackgroundSelection.ForcedDefault>();
        foreach (var (slideIndex, forcedDefault) in results)
        {
            if (forcedDefault is not null)
            {
                map[slideIndex] = forcedDefault;
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
    // -----------------------------------------------------------------------

    private async Task<(int SlideIndex, BackgroundSelection.ForcedDefault? ForcedDefault)> BuildSingleSlideAsync(
        ClientAestheticImageRequest request,
        int slideIndex,
        IReadOnlyList<ImageInitReference>? referenceImages,
        CancellationToken ct)
    {
        try
        {
            // 1. Write the image prompt for this slide.
            var copySummary = GetCopySummary(request, slideIndex);
            var promptRequest = new ImagePromptRequest(
                request.OrganizationId,
                request.ClientId,
                slideIndex,
                copySummary,
                request.Aesthetic);

            var promptResult = await _promptWriter.WriteAsync(promptRequest, ct);
            if (promptResult.IsFailure)
            {
                _logger.LogWarning(
                    "ClientAestheticImageBackgroundService: prompt write failed for slide {SlideIndex} " +
                    "(org {OrgId}): {Code}. Slide degrades to CSS.",
                    slideIndex, request.OrganizationId, promptResult.Error.Code);
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
                return (slideIndex, new BackgroundSelection.ForcedDefault(reuseResult, "ai-generated-background"));
            }

            // 3. Render the image with guardrail retry (CAC-IMG-03).
            var renderedImage = await RenderWithGuardrailRetryAsync(
                request, slideIndex, prompt, referenceImages, ct);

            if (renderedImage is null)
            {
                // Guardrail exhaustion already logged in RenderWithGuardrailRetryAsync.
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
            return (slideIndex, null);
        }
    }

    // -----------------------------------------------------------------------
    // CAC-IMG-03: image generation with bounded guardrail retry per slide
    // -----------------------------------------------------------------------

    private async Task<ImageGenerationResult?> RenderWithGuardrailRetryAsync(
        ClientAestheticImageRequest request,
        int slideIndex,
        string prompt,
        IReadOnlyList<ImageInitReference>? referenceImages,
        CancellationToken ct)
    {
        for (var attempt = 1; attempt <= MaxGuardrailAttempts; attempt++)
        {
            // CAC-IMG-02: set BackgroundOnly = true so the adapter injects the system directive.
            var imageRequest = new ImageGenerationRequest(
                request.OrganizationId,
                ImageCapabilityKey,
                ImagePromptVersionKey,
                prompt,
                TargetWidth,
                TargetHeight,
                PreferredProviderKey: null,
                PreferredModel: null,
                ReferenceImages: referenceImages,
                BackgroundOnly: true);

            var imageResult = await _imageGateway.GenerateAsync(imageRequest, ct);
            if (imageResult.IsFailure)
            {
                _logger.LogWarning(
                    "ClientAestheticImageBackgroundService: image generation failed for slide {SlideIndex} " +
                    "(org {OrgId}) attempt {Attempt}: {Code}. Slide degrades to CSS.",
                    slideIndex, request.OrganizationId, attempt, imageResult.Error.Code);
                return null; // generation failure is not retried
            }

            // CAC-IMG-03: apply guardrail validator when injected.
            if (_guardrailValidator is null)
            {
                return imageResult.Value;
            }

            var validationResult = _guardrailValidator.Validate(imageResult.Value);
            if (validationResult.IsSuccess)
            {
                return validationResult.Value;
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
                return null;
            }
        }

        return null; // unreachable but satisfies the compiler
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
