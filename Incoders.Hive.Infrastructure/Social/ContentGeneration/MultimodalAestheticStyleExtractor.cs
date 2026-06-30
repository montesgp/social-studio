using System.Text.Json.Serialization;
using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Domain.Social.ClientBrandManuals;
using Incoders.Hive.Domain.Social.ProfileAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Incoders.Hive.Infrastructure.Social.ContentGeneration;

/// <summary>
/// <see cref="IClientAestheticStyleExtractor"/> that learns a client's visual aesthetic from their
/// durably-hosted post images + captions using the org's configured MULTIMODAL route (the optional
/// <c>extract-aesthetic</c> / <c>ai-aesthetic</c> workflow step). Phase 2 (CAG2-PA-03).
/// </summary>
/// <remarks>
/// Reads each reference image's bytes from storage, base64-encodes them, and sends them as vision
/// input alongside a text instruction. Palette is OPTIONAL output: an empty palette is a VALID
/// result. When no aesthetic route is configured, or no images resolve, returns
/// <see cref="AestheticExtraction.Empty"/> (the caller degrades to the Phase 1 text signal).
/// </remarks>
public sealed class MultimodalAestheticStyleExtractor : IClientAestheticStyleExtractor
{
    private const string CapabilityKey = "ai-aesthetic";
    private const string PromptVersion = "aesthetic-extractor/v1";

    private readonly IAiCompletionGateway _gateway;
    private readonly IWorkflowConfigurationResolver _workflowResolver;
    private readonly IGeneratedAssetStorage _storage;
    private readonly ILogger<MultimodalAestheticStyleExtractor> _logger;

    public MultimodalAestheticStyleExtractor(
        IAiCompletionGateway gateway,
        IWorkflowConfigurationResolver workflowResolver,
        IGeneratedAssetStorage storage,
        ILogger<MultimodalAestheticStyleExtractor>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(workflowResolver);
        ArgumentNullException.ThrowIfNull(storage);
        _gateway = gateway;
        _workflowResolver = workflowResolver;
        _storage = storage;
        _logger = logger ?? NullLogger<MultimodalAestheticStyleExtractor>.Instance;
    }

    public async Task<AestheticExtraction> ExtractAsync(
        Guid organizationId,
        IReadOnlyList<ProfilePostImageRef> referenceImages,
        IReadOnlyList<string> captions,
        CancellationToken ct)
    {
        if (referenceImages.Count == 0)
        {
            return AestheticExtraction.Empty;
        }

        // Resolve the OPTIONAL aesthetic route. No route configured -> skip extraction (degrade to Phase 1).
        var route = await _workflowResolver.ResolveStepAsync(
            new WorkflowStepResolutionRequest(
                organizationId,
                "single-analyzer",
                "profile-analysis",
                "extract-aesthetic",
                "ai-aesthetic",
                null),
            ct);

        var candidate = route.IsSuccess
            ? route.Value.Where(c => c.IsEnabled).OrderBy(c => c.Priority).FirstOrDefault()
            : null;
        if (candidate is null)
        {
            _logger.LogInformation(
                "AestheticExtractor: no configured aesthetic (multimodal) route for org {OrganizationId} — skipping extraction.",
                organizationId);
            return AestheticExtraction.Empty;
        }

        // Load image bytes from durable storage and base64-encode for vision input.
        var images = new List<AiImageInput>(referenceImages.Count);
        foreach (var reference in referenceImages)
        {
            var opened = await _storage.OpenAsync(reference.StoragePath, ct);
            if (opened.IsFailure)
            {
                _logger.LogWarning(
                    "AestheticExtractor: could not open reference image {Path} (org {OrganizationId}) — skipping it.",
                    reference.StoragePath, organizationId);
                continue;
            }

            using var stream = opened.Value;
            using var buffer = new MemoryStream();
            await stream.Content.CopyToAsync(buffer, ct);
            images.Add(new AiImageInput(stream.Mime, Convert.ToBase64String(buffer.ToArray())));
        }

        if (images.Count == 0)
        {
            return AestheticExtraction.Empty;
        }

        var captionBlock = captions.Count > 0
            ? "Recent post captions:\n- " + string.Join("\n- ", captions.Take(20))
            : "(no captions available)";

        var instruction =
            "You are a brand-aesthetics analyst. The attached images are WHOLE recent carousels/posts of a " +
            "single Instagram client (multiple slides per post, in order). Study how the client composes a " +
            "post — clients often MIX a couple of creative/photographic slides with 2-3 flat colour/template " +
            "slides within the same carousel, and vary those templates across posts. Capture that COMBINATION " +
            "and recurring system, not just one image. Extract the client's VISUAL LANGUAGE so we can generate " +
            "NEW content that looks like THEY made it. Return: palette (role-ordered hex, ONLY if the client " +
            "clearly uses marked colours — otherwise return an EMPTY palette array, do not invent one), " +
            "styleDescriptors (composition, slide-mix pattern, layout system, mood, treatment), motifs " +
            "(recurring visual elements / template traits), and captionVoice (their copy tone). " + captionBlock;

        var request = new AiJsonCompletionRequest(
            OrganizationId: organizationId,
            AnalysisId: null,
            CapabilityKey: CapabilityKey,
            PromptVersionKey: PromptVersion,
            InputJson: instruction,
            PreferredProviderKey: candidate.ProviderKey,
            PreferredModel: candidate.ModelId,
            Images: images);

        var result = await _gateway.CompleteJsonAsync<AestheticExtractionDto>(request, ct);
        if (result.IsFailure)
        {
            _logger.LogWarning(
                "AestheticExtractor: extraction call failed for org {OrganizationId}: {ErrorCode}. Degrading to no aesthetic.",
                organizationId, result.Error.Code);
            return AestheticExtraction.Empty;
        }

        return Map(result.Value.Payload);
    }

    // Default background-candidate roles used when calling ColorPairingDerivation from the extractor.
    // The extractor has no access to the brand manual, so it considers all non-Neutral roles as candidates.
    private static readonly IReadOnlyList<ColorRole> DefaultBackgroundRoles =
        [ColorRole.Primary, ColorRole.Secondary, ColorRole.Accent];

    private static AestheticExtraction Map(AestheticExtractionDto dto)
    {
        var palette = (dto.Palette ?? [])
            .Where(p => !string.IsNullOrWhiteSpace(p.Hex))
            .Select((p, i) => new AestheticPaletteEntry(p.Hex!, p.Role, p.Rank > 0 ? p.Rank : i + 1))
            .ToArray();

        // client-aligned-carousel-generation: map slideMixPattern → BackgroundPattern (CAC-BSP-01-a)
        var backgroundPattern = MapSlideMixPattern(dto.SlideMixPattern);

        // client-aligned-carousel-generation: derive color pairings from extracted palette (CAC-BSP-03)
        // Uses all non-Neutral roles as background candidates since the brand manual is not available here.
        var colorPairings = ColorPairingDerivation.Derive(DefaultBackgroundRoles, palette);

        return new AestheticExtraction(
            Palette: palette,
            StyleDescriptors: dto.StyleDescriptors ?? [],
            Motifs: dto.Motifs ?? [],
            CaptionVoice: string.IsNullOrWhiteSpace(dto.CaptionVoice) ? null : dto.CaptionVoice,
            BackgroundPattern: backgroundPattern,
            ColorPairings: colorPairings.Count > 0 ? colorPairings : null);
    }

    private static IReadOnlyList<SlideBackgroundKind>? MapSlideMixPattern(string[]? pattern)
    {
        if (pattern is null || pattern.Length == 0)
        {
            return null;
        }

        var result = new SlideBackgroundKind[pattern.Length];
        for (var i = 0; i < pattern.Length; i++)
        {
            result[i] = pattern[i].Trim().ToLowerInvariant() switch
            {
                "photo"       => SlideBackgroundKind.Photo,
                "flat"        => SlideBackgroundKind.FlatColor,
                "flatcolor"   => SlideBackgroundKind.FlatColor,
                "flat_color"  => SlideBackgroundKind.FlatColor,
                "decorator"   => SlideBackgroundKind.Decorator,
                _             => SlideBackgroundKind.FlatColor, // safe default for unknown values
            };
        }

        return result;
    }

    // -- AI response DTO (palette optional; slideMixPattern additive) --
    private sealed record AestheticExtractionDto(
        [property: JsonPropertyName("palette")] PaletteEntryDto[]? Palette,
        [property: JsonPropertyName("styleDescriptors")] string[]? StyleDescriptors,
        [property: JsonPropertyName("motifs")] string[]? Motifs,
        [property: JsonPropertyName("captionVoice")] string? CaptionVoice,
        // client-aligned-carousel-generation: additive nullable field (CAC-BSP-01-a)
        [property: JsonPropertyName("slideMixPattern")] string[]? SlideMixPattern = null);

    private sealed record PaletteEntryDto(
        [property: JsonPropertyName("hex")] string? Hex,
        [property: JsonPropertyName("role")] string? Role,
        [property: JsonPropertyName("rank")] int Rank);
}
