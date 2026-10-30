using System.Text.Json;
using SocialStudio.Application.Abstractions.Ai;
using SocialStudio.Application.Abstractions.Integrations;
using SocialStudio.Application.Abstractions.Social.ContentGeneration;
using SocialStudio.Application.Features.Social.ContentGeneration.Shared;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Integrations;
using Microsoft.Extensions.Logging;

namespace SocialStudio.Application.Features.Social.ContentGeneration.Background;

/// <summary>
/// Writes a per-slide image prompt from the client's aesthetic profile using the ORGANIZATION's
/// configured TEXT route (OQ-6: reuses <see cref="IAiCompletionGateway"/>, capability "ai-generation",
/// prompt-version "image-prompt/v2"). Claude (or whatever text model the org configured) is excellent at
/// authoring the prompt; the IMAGE route renders the pixels separately.
/// <para>
/// The org's AI route is resolved via <see cref="IWorkflowConfigurationResolver"/> (same mechanism as the
/// content-package orchestrator) before every call. A missing/unconfigured route, a gateway failure, or an
/// empty/null returned prompt yields a <see cref="Result"/> failure so the caller degrades that slide to the
/// CSS designer path — the non-negotiable resilience contract. No provider SDK type appears in this class or
/// its contracts.
/// </para>
/// Refs: IMG-03, CIG-CG-04; design §3.2, OQ-6.
/// </summary>
public sealed class ImagePromptWriter : IImagePromptWriter
{
    private const string CapabilityKey = "ai-generation";
    private const string PromptVersionKey = "image-prompt/v2";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IAiCompletionGateway _gateway;
    private readonly IWorkflowConfigurationResolver _workflowResolver;
    private readonly ILogger<ImagePromptWriter> _logger;

    public ImagePromptWriter(
        IAiCompletionGateway gateway,
        IWorkflowConfigurationResolver workflowResolver,
        ILogger<ImagePromptWriter> logger)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(workflowResolver);
        ArgumentNullException.ThrowIfNull(logger);
        _gateway = gateway;
        _workflowResolver = workflowResolver;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result<string>> WriteAsync(ImagePromptRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Resolve the org's AI route before building the completion request.
        // Mirrors ContentPackageOrchestrator.ResolveGenerationAiAsync — no default-AI fallback.
        var route = await _workflowResolver.ResolveStepAsync(
            new WorkflowStepResolutionRequest(
                request.OrganizationId,
                "single-analyzer",
                "content-analysis",
                "generate-content",
                "ai-generation",
                null),
            ct);

        if (route.IsFailure)
        {
            _logger.LogWarning(
                "ImagePromptWriter: route resolution failed for slide {SlideIndex} (org {OrgId}): {Code}. Slide degrades to CSS.",
                request.SlideIndex, request.OrganizationId, route.Error.Code);
            return Result.Failure<string>(route.Error);
        }

        var candidate = route.Value
            .Where(c => c.IsEnabled)
            .OrderBy(c => c.Priority)
            .FirstOrDefault();

        if (candidate is null)
        {
            _logger.LogWarning(
                "ImagePromptWriter: no enabled AI route candidate for slide {SlideIndex} (org {OrgId}). Slide degrades to CSS.",
                request.SlideIndex, request.OrganizationId);
            return Result.Failure<string>(IntegrationErrors.AiRouteNotConfigured);
        }

        var inputJson = JsonSerializer.Serialize(BuildInput(request), JsonOptions);

        var aiRequest = new AiJsonCompletionRequest(
            request.OrganizationId,
            AnalysisId: null,
            CapabilityKey: CapabilityKey,
            PromptVersionKey: PromptVersionKey,
            InputJson: inputJson,
            PreferredProviderKey: candidate.ProviderKey,
            PreferredModel: candidate.ModelId);

        var result = await _gateway.CompleteJsonAsync<ImagePromptResponse>(aiRequest, ct);
        if (result.IsFailure)
        {
            _logger.LogWarning(
                "ImagePromptWriter: prompt write failed for slide {SlideIndex} (org {OrgId}): {Code}. Slide degrades to CSS.",
                request.SlideIndex, request.OrganizationId, result.Error.Code);
            return Result.Failure<string>(result.Error);
        }

        var prompt = result.Value.Payload.ImagePrompt?.Trim();
        if (string.IsNullOrWhiteSpace(prompt))
        {
            _logger.LogWarning(
                "ImagePromptWriter: empty image prompt for slide {SlideIndex} (org {OrgId}). Slide degrades to CSS.",
                request.SlideIndex, request.OrganizationId);
            return Result.Failure<string>(Error.Failure("image-prompt.empty", "imagePrompt.empty"));
        }

        return Result.Success(prompt);
    }

    /// <summary>
    /// Builds the structured input for the TEXT route. The prompt MUST describe a TEXT-FREE background
    /// in the client's real visual line (text is added later as an editable layer), so the image model
    /// never bakes copy or the client's handle into the raster.
    ///
    /// <para>
    /// When <see cref="ClientAestheticProfile.ConceptualMethod"/> is non-null and not
    /// <see cref="ArtDirectionMethod.Unknown"/>, and the slide is creative (Photo or unspecified),
    /// the method-gated ideation instruction fires: the model is asked to INVENT a concept in the
    /// detected art-direction method, constrained to the captured cues (OQ-5; CACD-CG-01).
    /// Otherwise the existing emulate-style instruction runs verbatim (cold-start parity; CACD-PA-07-a).
    /// </para>
    ///
    /// <para>
    /// When <see cref="ClientAestheticProfile.ImageTypeRatio"/> is non-null and
    /// <see cref="ImagePromptRequest.SlideKind"/> is known, a ratio-aware cue is appended:
    /// photographic for <see cref="SlideBackgroundKind.Photo"/>;
    /// flat mono-color for <see cref="SlideBackgroundKind.FlatColor"/> (CACD-CG-02-a/b/c).
    /// </para>
    /// </summary>
    private static object BuildInput(ImagePromptRequest request)
    {
        var a = request.Aesthetic;
        var cm = a.ConceptualMethod;
        var ratio = a.ImageTypeRatio;

        // Method-gated ideation fires when ConceptualMethod is present (non-null, non-Unknown)
        // and the slide is creative (Photo) or the kind is unspecified (writer only called for
        // creative slides in the production pipeline — single responsibility, OQ-5).
        string instruction;
        if (cm is not null
            && cm.Method != ArtDirectionMethod.Unknown
            && (request.SlideKind is null || request.SlideKind == SlideBackgroundKind.Photo))
        {
            // CACD-CG-01: invent a concept in the detected method, constrained to cues + slide message.
            // Text-free, no aesthetics outside the cues. Method-specific hints guide the model.
            var cuesStr = cm.Cues.Count > 0
                ? string.Join(", ", cm.Cues)
                : cm.Rationale;

            instruction =
                $"INVENT a single visual CONCEPT for THIS slide's message using the client's " +
                $"art-direction method {cm.Method} and ONLY these detected visual cues: {cuesStr}. " +
                "For MetaphoricalEditorial, design a fresh mono-color conceptual still-life or staged metaphor " +
                "that embodies the message through the detected cues. " +
                "For LiteralIllustration, create a literal tender scene depicting the message subjects. " +
                "The concept MUST depict the slide message, stay completely free of any text, letters, words, " +
                "numbers, logos, or watermarks — copy is composited separately as an editable layer — and must " +
                "not introduce aesthetics or motifs outside the detected cues. " +
                "Respond with ONLY raw JSON matching the expected structure.";
        }
        else
        {
            // CACD-IMG-02: cold-start branch, now instructed to depict the slide's topic. Aesthetic
            // guidance is retained verbatim (additive change only); no ideation, no ratio cue.
            instruction =
                "Write ONE concise text-to-image prompt for a single carousel slide BACKGROUND image in this " +
                "client's established visual style. " +
                "The image MUST depict the subject matter of THIS slide's message as given in slideCopySummary — " +
                "a literal or metaphorical scene of the message subjects — rendered in the client's established " +
                "visual style. " +
                "The background MUST be free of any text, letters, words, " +
                "numbers, logos, handles, or watermarks — copy is composited separately as an editable layer. " +
                "Emulate the client's real aesthetic (composition, palette, mood, motifs) so the result looks " +
                "authentically authored by this client, never generic. Keep it vivid but concise. " +
                "Respond with ONLY raw JSON matching the expected structure.";
        }

        // Ratio-aware cue: appended when ImageTypeRatio is present and the slide kind is known.
        // CACD-CG-02-a: photo slide → photographic cue; CACD-CG-02-b: flat slide → flat cue.
        // CACD-CG-02-c: null ratio OR null SlideKind → no cue appended (cold-start safe).
        if (ratio is not null)
        {
            if (request.SlideKind == SlideBackgroundKind.Photo)
            {
                instruction +=
                    " Favor a photographic, staged-photography background consistent with the client's photo-dominant visual corpus.";
            }
            else if (request.SlideKind == SlideBackgroundKind.FlatColor)
            {
                instruction +=
                    " Favor a flat mono-color background consistent with the client's flat-color-dominant visual corpus.";
            }
        }

        // client-style-fidelity CSF-CG-08: reproduce the client's treatment at this slide position.
        instruction += SlideTreatmentCue(request);

        // Build the clientAesthetic block. New fields (conceptualMethod, imageTypeRatio) are added
        // conditionally so the JSON remains clean. Existing fields serialized in the same order as before.
        var clientAesthetic = BuildClientAesthetic(a, cm, ratio);

        return new
        {
            instruction,
            slideIndex = request.SlideIndex,
            slideCopySummary = request.SlideCopySummary,
            clientAesthetic,
            expectedOutput = new
            {
                imagePrompt = "string - one vivid image-generation prompt for a TEXT-FREE background in the client's style",
            },
        };
    }

    /// <summary>
    /// CSF-CG-08: the client's own treatment at this slide position, the backdrop tone behind their
    /// text, and the room their headline needs. Empty when nothing was observed (cold-start parity).
    /// </summary>
    internal static string SlideTreatmentCue(ImagePromptRequest request)
    {
        var cue = new System.Text.StringBuilder();
        if (!string.IsNullOrWhiteSpace(request.SlideTreatment))
        {
            cue.Append(" SLIDE TREATMENT: in the client's own carousels this slide position is treated as \"")
                .Append(request.SlideTreatment.Trim())
                .Append("\". Reproduce that treatment faithfully — subject placement, backdrop, lighting and how ")
                .Append("the image blends; a subject isolated on a seamless backdrop stays isolated on a seamless backdrop. ")
                .Append("Any product, bag or packaging is plain and unbranded, with no printed text, labels or logos.");
        }

        if (!string.IsNullOrWhiteSpace(request.BackdropHex))
        {
            cue.Append(" The backdrop and the tone behind the headline are ")
                .Append(request.BackdropHex.Trim())
                .Append(", as in the client's slides.");
        }

        var heading = request.Aesthetic.Typography?
            .FirstOrDefault(t => string.Equals(t.Role, TypographyRoles.Heading, StringComparison.OrdinalIgnoreCase));
        if (heading?.BlockWidthRatio is > 0 and <= 1 && (cue.Length > 0))
        {
            var percent = (int)Math.Round(heading.BlockWidthRatio.Value * 100, MidpointRounding.AwayFromZero);
            cue.Append(" Keep the left ")
                .Append(percent)
                .Append("% of the frame calm and uncluttered: the client's headline sits there.");
        }

        return cue.ToString();
    }

    /// <summary>
    /// Builds the clientAesthetic block. The six pre-change fields are always included
    /// (preserving cold-start shape). New CACD fields are added only when non-null.
    /// </summary>
    private static Dictionary<string, object?> BuildClientAesthetic(
        ClientAestheticProfile a,
        ConceptualMethod? cm,
        ImageTypeRatio? ratio)
    {
        var dict = new Dictionary<string, object?>
        {
            ["styleDescriptors"] = a.StyleDescriptors,
            ["motifs"] = a.Motifs,
            ["backgroundDescriptors"] = a.BackgroundDescriptors,
            ["voiceTone"] = a.VoiceTone,
            ["captionVoice"] = a.CaptionVoice,
            ["palette"] = a.Palette?.Select(p => new { hex = p.Hex, role = p.Role }),
        };

        if (cm is not null)
        {
            dict["conceptualMethod"] = new
            {
                method = cm.Method.ToString(),
                rationale = cm.Rationale,
                cues = cm.Cues,
            };
        }

        if (ratio is not null)
        {
            dict["imageTypeRatio"] = new
            {
                photo = ratio.Photo,
                flatColor = ratio.FlatColor,
                decorator = ratio.Decorator,
                sampleSize = ratio.SampleSize,
            };
        }

        return dict;
    }
}

/// <summary>Structured response parsed from the TEXT route: the authored image prompt.</summary>
internal sealed record ImagePromptResponse(string? ImagePrompt);
