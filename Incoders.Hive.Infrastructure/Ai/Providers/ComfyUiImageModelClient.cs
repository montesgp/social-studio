using System.Globalization;
using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Incoders.Hive.Infrastructure.Ai.Providers;

/// <summary>
/// Infrastructure adapter: local ComfyUI server driven over HTTP with a templated IP-Adapter
/// workflow. Implements <see cref="IImageModelClient"/> — the only place where ComfyUI
/// HTTP/JSON-graph types exist.
///
/// <para>
/// Architecture gate: ALL ComfyUI-specific types, HTTP concerns, and JSON-graph parsing are
/// CONFINED to this file. No ComfyUI type escapes into Application or Domain.
/// </para>
///
/// <para>
/// OQ-1: the workflow graph is stored as an API-format JSON template string with named
/// <c>{{TOKEN}}</c> placeholders (<see cref="SubstituteTemplate"/>); a bundled default ships as
/// <c>DefaultComfyUiWorkflowTemplate.json</c> (embedded resource) for when
/// <see cref="ComfyUiSettings.WorkflowTemplateJson"/> is null/empty.
/// </para>
///
/// Refs: CUI-01, CUI-01-a, CUI-01-b, CUI-01-c, CUI-IMG-01; design §3.2, §3.3, §3.4, §0 OQ-1/OQ-2.
/// </summary>
public sealed class ComfyUiImageModelClient : IImageModelClient
{
    private const string ProviderKey = "comfyui";
    private const string DefaultImageMime = "image/png";
    private const string EmbeddedTemplateResourceName =
        "Incoders.Hive.Infrastructure.Ai.Providers.DefaultComfyUiWorkflowTemplate.json";

    /// <summary>Generation-time defaults not present on <see cref="ImageGenerationRequest"/>.</summary>
    private const int DefaultSteps = 20;
    private const double DefaultCfg = 7.0;

    /// <summary>
    /// Exact JSON substring for the KSampler "model" edge wired to the IP-Adapter output (node
    /// "6"). When no reference image resolves, this edge is replaced by
    /// <see cref="PassThroughModelEdge"/> so the graph bypasses the LoadImage/IP-Adapter chain
    /// entirely (design §3.3 no-reference bypass).
    /// </summary>
    private const string ConditionedModelEdge = "\"model\": [\"6\", 0]";

    /// <summary>Pass-through model edge wired directly to the checkpoint loader (node "1").</summary>
    private const string PassThroughModelEdge = "\"model\": [\"1\", 0]";

    private readonly ResolvedProviderCredential _credential;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IGeneratedAssetStorage _storage;
    private readonly ILogger<ComfyUiImageModelClient> _logger;

    public ComfyUiImageModelClient(
        ResolvedProviderCredential credential,
        IHttpClientFactory httpClientFactory,
        IGeneratedAssetStorage storage,
        ILogger<ComfyUiImageModelClient>? logger = null)
    {
        _credential = credential;
        _httpClientFactory = httpClientFactory;
        _storage = storage;
        _logger = logger ?? NullLogger<ComfyUiImageModelClient>.Instance;
    }

    /// <summary>
    /// ComfyUI supports IP-Adapter image conditioning (CUI-IMG-01). Declarative only — never
    /// triggers a provider call; falls back to <see cref="ComfyUiSettings.DefaultMaxReferenceImages"/>
    /// when the credential cannot be parsed.
    /// </summary>
    public ImageModelCapabilities Capabilities
    {
        get
        {
            var settingsResult = ComfyUiSettings.FromCredential(_credential);
            var maxConditioningImages = settingsResult.IsSuccess
                ? settingsResult.Value.MaxReferenceImages
                : ComfyUiSettings.DefaultMaxReferenceImages;

            return new ImageModelCapabilities(SupportsImageConditioning: true, MaxConditioningImages: maxConditioningImages);
        }
    }

    public Task<Result<ImageGenerationResult>> GenerateAsync(
        ImageGenerationRequest request,
        CancellationToken ct = default) =>
        throw new NotImplementedException("Implemented in WU-2 Phase 2.B (task 2.4).");

    // -----------------------------------------------------------------------
    // OQ-1: template loading + substitution + JSON validation
    // -----------------------------------------------------------------------

    private static string LoadBundledDefaultTemplate()
    {
        var assembly = typeof(ComfyUiImageModelClient).Assembly;
        using var stream = assembly.GetManifestResourceStream(EmbeddedTemplateResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{EmbeddedTemplateResourceName}' not found in {assembly.FullName}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Substitutes every <c>{{TOKEN}}</c> placeholder in <paramref name="template"/> with the
    /// JSON-escaped scalar values. When <paramref name="referenceImageName"/> is null/empty, the
    /// LoadImage/IP-Adapter chain is bypassed by rewriting the KSampler "model" edge to reference
    /// the checkpoint loader directly (descriptor-prompt-only path; design §3.3).
    /// </summary>
    internal static string SubstituteTemplate(
        string template,
        string checkpoint,
        string positivePrompt,
        string negativePrompt,
        string? referenceImageName,
        string? ipAdapterModel,
        int width,
        int height,
        long seed,
        int steps,
        double cfg)
    {
        var hasReference = !string.IsNullOrEmpty(referenceImageName);

        var substituted = template
            .Replace("{{CHECKPOINT_NAME}}", JsonEscape(checkpoint))
            .Replace("{{POSITIVE_PROMPT}}", JsonEscape(positivePrompt))
            .Replace("{{NEGATIVE_PROMPT}}", JsonEscape(negativePrompt))
            .Replace("{{REFERENCE_IMAGE}}", JsonEscape(referenceImageName ?? string.Empty))
            .Replace("{{IPADAPTER_MODEL}}", JsonEscape(ipAdapterModel ?? string.Empty))
            .Replace("{{WIDTH}}", width.ToString(CultureInfo.InvariantCulture))
            .Replace("{{HEIGHT}}", height.ToString(CultureInfo.InvariantCulture))
            .Replace("{{SEED}}", seed.ToString(CultureInfo.InvariantCulture))
            .Replace("{{STEPS}}", steps.ToString(CultureInfo.InvariantCulture))
            .Replace("{{CFG}}", cfg.ToString(CultureInfo.InvariantCulture));

        return hasReference
            ? substituted
            : substituted.Replace(ConditionedModelEdge, PassThroughModelEdge);
    }

    /// <summary>
    /// Substitutes the template and validates the result via <see cref="JsonDocument.Parse(string, JsonDocumentOptions)"/>.
    /// A parse failure maps to <see cref="IntegrationErrors.AiJsonInvalid"/>.
    /// </summary>
    internal static Result<string> BuildWorkflowGraph(
        string template,
        string checkpoint,
        string positivePrompt,
        string negativePrompt,
        string? referenceImageName,
        string? ipAdapterModel,
        int width,
        int height,
        long seed,
        int steps,
        double cfg)
    {
        var substituted = SubstituteTemplate(
            template,
            checkpoint,
            positivePrompt,
            negativePrompt,
            referenceImageName,
            ipAdapterModel,
            width,
            height,
            seed,
            steps,
            cfg);

        try
        {
            using var _ = JsonDocument.Parse(substituted);
        }
        catch (JsonException)
        {
            return Result.Failure<string>(IntegrationErrors.AiJsonInvalid);
        }

        return Result.Success(substituted);
    }

    private static string JsonEscape(string value)
    {
        var serialized = JsonSerializer.Serialize(value);
        return serialized[1..^1];
    }
}
