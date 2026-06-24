using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Background;

/// <summary>
/// Writes a per-slide image prompt from the client's aesthetic profile using the ORGANIZATION's
/// configured TEXT route (OQ-6: reuses <see cref="IAiCompletionGateway"/>, capability "ai-generation",
/// prompt-version "image-prompt/v1"). Claude (or whatever text model the org configured) is excellent at
/// authoring the prompt; the IMAGE route renders the pixels separately.
/// <para>
/// A weak/empty aesthetic signal, a gateway failure, or an empty/null returned prompt yields a
/// <see cref="Result"/> failure so the caller degrades that slide to the CSS designer path — the
/// non-negotiable resilience contract. No provider SDK type appears in this class or its contracts.
/// </para>
/// Refs: IMG-03, CIG-CG-04; design §3.2, OQ-6.
/// </summary>
public sealed class ImagePromptWriter : IImagePromptWriter
{
    private const string CapabilityKey = "ai-generation";
    private const string PromptVersionKey = "image-prompt/v1";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IAiCompletionGateway _gateway;
    private readonly ILogger<ImagePromptWriter> _logger;

    public ImagePromptWriter(IAiCompletionGateway gateway, ILogger<ImagePromptWriter> logger)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(logger);
        _gateway = gateway;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result<string>> WriteAsync(ImagePromptRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var inputJson = JsonSerializer.Serialize(BuildInput(request), JsonOptions);

        var aiRequest = new AiJsonCompletionRequest(
            request.OrganizationId,
            AnalysisId: null,
            CapabilityKey: CapabilityKey,
            PromptVersionKey: PromptVersionKey,
            InputJson: inputJson,
            PreferredProviderKey: null,
            PreferredModel: null);

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
    /// never bakes copy or the client's @handle into the raster.
    /// </summary>
    private static object BuildInput(ImagePromptRequest request)
    {
        var a = request.Aesthetic;
        return new
        {
            instruction =
                "Write ONE concise text-to-image prompt for a single carousel slide BACKGROUND image in this " +
                "client's established visual style. The background MUST be free of any text, letters, words, " +
                "numbers, logos, handles, or watermarks — copy is composited separately as an editable layer. " +
                "Emulate the client's real aesthetic (composition, palette, mood, motifs) so the result looks " +
                "authentically authored by this client, never generic. Keep it vivid but concise. " +
                "Respond with ONLY raw JSON matching the expected structure.",
            slideIndex = request.SlideIndex,
            slideCopySummary = request.SlideCopySummary,
            clientAesthetic = new
            {
                styleDescriptors = a.StyleDescriptors,
                motifs = a.Motifs,
                backgroundDescriptors = a.BackgroundDescriptors,
                voiceTone = a.VoiceTone,
                captionVoice = a.CaptionVoice,
                palette = a.Palette?.Select(p => new { hex = p.Hex, role = p.Role }),
            },
            expectedOutput = new
            {
                imagePrompt = "string - one vivid image-generation prompt for a TEXT-FREE background in the client's style",
            },
        };
    }
}

/// <summary>Structured response parsed from the TEXT route: the authored image prompt.</summary>
internal sealed record ImagePromptResponse(string? ImagePrompt);
