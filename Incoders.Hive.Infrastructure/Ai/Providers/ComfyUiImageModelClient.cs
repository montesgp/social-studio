using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
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

    /// <summary>
    /// OQ-2 HTTP call sequence: <c>FromCredential -&gt; resolve checkpoint -&gt; resolve reference
    /// bytes -&gt; upload -&gt; substitute template -&gt; POST /prompt -&gt; poll GET /history/{id} -&gt;
    /// GET /view -&gt; bytes</c>. Refs: CUI-01-a, CUI-01-b, CUI-01-c; design §3.4 steps 0-5.
    /// </summary>
    public async Task<Result<ImageGenerationResult>> GenerateAsync(
        ImageGenerationRequest request,
        CancellationToken ct = default)
    {
        var settingsResult = ComfyUiSettings.FromCredential(_credential);
        if (settingsResult.IsFailure)
        {
            _logger.LogWarning(
                "ComfyUiImageModelClient: invalid settings. OrganizationId={OrganizationId} CapabilityKey={CapabilityKey}",
                request.OrganizationId,
                request.CapabilityKey);

            return Result.Failure<ImageGenerationResult>(settingsResult.Error);
        }

        var settings = settingsResult.Value;

        var checkpoint = request.PreferredModel ?? _credential.SelectedModel ?? settings.DefaultCheckpoint;
        if (string.IsNullOrWhiteSpace(checkpoint))
        {
            _logger.LogWarning(
                "ComfyUiImageModelClient: checkpoint missing. OrganizationId={OrganizationId} CapabilityKey={CapabilityKey}",
                request.OrganizationId,
                request.CapabilityKey);

            return Result.Failure<ImageGenerationResult>(IntegrationErrors.AiModelRequired);
        }

        var baseUrl = settings.BaseUrl.TrimEnd('/');
        using var http = _httpClientFactory.CreateClient(nameof(ComfyUiImageModelClient));

        var resolvedRefs = await ResolveReferenceImageBytesAsync(request, settings, ct);

        string? referenceImageName = null;
        if (resolvedRefs.Count > 0)
        {
            var uploadResult = await UploadReferenceImageAsync(http, baseUrl, resolvedRefs[0], ct);
            if (uploadResult.IsFailure)
            {
                return Result.Failure<ImageGenerationResult>(uploadResult.Error);
            }

            referenceImageName = uploadResult.Value;
        }

        var template = string.IsNullOrWhiteSpace(settings.WorkflowTemplateJson)
            ? LoadBundledDefaultTemplate()
            : settings.WorkflowTemplateJson;

        var seed = Random.Shared.NextInt64(0, int.MaxValue);
        var graphResult = BuildWorkflowGraph(
            template,
            checkpoint,
            request.Prompt,
            negativePrompt: string.Empty,
            referenceImageName,
            settings.IpAdapterModel,
            request.Width,
            request.Height,
            seed,
            DefaultSteps,
            DefaultCfg);

        if (graphResult.IsFailure)
        {
            return Result.Failure<ImageGenerationResult>(graphResult.Error);
        }

        var promptIdResult = await QueuePromptAsync(http, baseUrl, graphResult.Value, ct);
        if (promptIdResult.IsFailure)
        {
            return Result.Failure<ImageGenerationResult>(promptIdResult.Error);
        }

        var outputImageResult = await PollHistoryAsync(http, baseUrl, promptIdResult.Value, settings, ct);
        if (outputImageResult.IsFailure)
        {
            return Result.Failure<ImageGenerationResult>(outputImageResult.Error);
        }

        var bytesResult = await FetchImageBytesAsync(http, baseUrl, outputImageResult.Value, ct);
        if (bytesResult.IsFailure)
        {
            return Result.Failure<ImageGenerationResult>(bytesResult.Error);
        }

        return Result.Success(new ImageGenerationResult(
            bytesResult.Value,
            DefaultImageMime,
            ProviderKey,
            checkpoint,
            new ImageUsage(ImageCount: 1, OutputBytes: bytesResult.Value.Length)));
    }

    // -----------------------------------------------------------------------
    // OQ-5: reference byte-resolution from IGeneratedAssetStorage (hardened in WU-4)
    // -----------------------------------------------------------------------

    private async Task<IReadOnlyList<(byte[] Bytes, string Mime)>> ResolveReferenceImageBytesAsync(
        ImageGenerationRequest request,
        ComfyUiSettings settings,
        CancellationToken ct)
    {
        if (request.ReferenceImages is not { Count: > 0 })
        {
            return [];
        }

        var resolved = new List<(byte[], string)>();
        foreach (var refImage in request.ReferenceImages)
        {
            if (string.IsNullOrWhiteSpace(refImage.Url))
            {
                continue;
            }

            var openResult = await _storage.OpenAsync(refImage.Url, ct);
            if (openResult.IsFailure)
            {
                continue;
            }

            using var stream = openResult.Value;
            using var ms = new MemoryStream();
            await stream.Content.CopyToAsync(ms, ct);
            resolved.Add((ms.ToArray(), stream.Mime));
        }

        return resolved;
    }

    // -----------------------------------------------------------------------
    // OQ-2/OQ-5: upload -> queue -> poll -> fetch HTTP calls
    // -----------------------------------------------------------------------

    private static async Task<Result<string>> UploadReferenceImageAsync(
        HttpClient http,
        string baseUrl,
        (byte[] Bytes, string Mime) reference,
        CancellationToken ct)
    {
        using var multipart = new MultipartFormDataContent();
        var imageContent = new ByteArrayContent(reference.Bytes);
        imageContent.Headers.ContentType = new MediaTypeHeaderValue(
            string.IsNullOrWhiteSpace(reference.Mime) ? "image/png" : reference.Mime);
        multipart.Add(imageContent, "image", "reference.png");

        var response = await http.PostAsync($"{baseUrl}/upload/image", multipart, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var name = doc.RootElement.GetProperty("name").GetString();
        return Result.Success(name ?? string.Empty);
    }

    private static async Task<Result<string>> QueuePromptAsync(
        HttpClient http,
        string baseUrl,
        string graphJson,
        CancellationToken ct)
    {
        using var graphDoc = JsonDocument.Parse(graphJson);
        var body = JsonSerializer.Serialize(new
        {
            prompt = graphDoc.RootElement,
            client_id = Guid.NewGuid().ToString("N"),
        });

        var response = await http.PostAsync(
            $"{baseUrl}/prompt",
            new StringContent(body, Encoding.UTF8, "application/json"),
            ct);

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var promptId = doc.RootElement.GetProperty("prompt_id").GetString();
        return Result.Success(promptId ?? string.Empty);
    }

    private static async Task<Result<JsonElement>> PollHistoryAsync(
        HttpClient http,
        string baseUrl,
        string promptId,
        ComfyUiSettings settings,
        CancellationToken ct)
    {
        while (true)
        {
            var response = await http.GetAsync($"{baseUrl}/history/{promptId}", ct);
            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty(promptId, out var entry)
                && entry.TryGetProperty("outputs", out var outputs)
                && outputs.TryGetProperty(settings.OutputNodeId, out var outputNode)
                && outputNode.TryGetProperty("images", out var images)
                && images.ValueKind == JsonValueKind.Array
                && images.GetArrayLength() > 0)
            {
                return Result.Success(images[0].Clone());
            }

            await Task.Delay(settings.PollInterval, ct);
        }
    }

    private static async Task<Result<byte[]>> FetchImageBytesAsync(
        HttpClient http,
        string baseUrl,
        JsonElement image,
        CancellationToken ct)
    {
        var filename = image.TryGetProperty("filename", out var f) ? f.GetString() : null;
        var subfolder = image.TryGetProperty("subfolder", out var sf) ? sf.GetString() : null;
        var type = image.TryGetProperty("type", out var t) ? t.GetString() : "output";

        var query = $"{baseUrl}/view?filename={Uri.EscapeDataString(filename ?? string.Empty)}" +
            $"&subfolder={Uri.EscapeDataString(subfolder ?? string.Empty)}" +
            $"&type={Uri.EscapeDataString(type ?? "output")}";

        var response = await http.GetAsync(query, ct);
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        return Result.Success(bytes);
    }

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
