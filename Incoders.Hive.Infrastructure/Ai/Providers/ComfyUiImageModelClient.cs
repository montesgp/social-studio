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
    /// Default negative prompt. The generated image is used ONLY as a background raster; any text it
    /// bakes in is not editable in the studio and clashes with the separate editable text layer. The
    /// reference images conditioning the IP-Adapter are finished posts that contain text, so without
    /// this the model reproduces garbled pseudo-text. Suppress all typography plus common artifacts.
    /// </summary>
    private const string DefaultNegativePrompt =
        "text, letters, words, typography, watermark, signature, caption, logo, label, subtitles, " +
        "writing, font, handwriting, numbers, low quality, blurry, deformed, distorted";

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

        // WU-3 (CUI-02-c): bound the ENTIRE HTTP flow (upload/queue/poll/fetch) by the
        // configured overall Timeout, in addition to the caller-supplied CancellationToken.
        // Both an elapsed budget and a caller-cancelled token map onto ProviderTimeout below.
        using var timeoutCts = new CancellationTokenSource(settings.Timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        var effectiveCt = linkedCts.Token;

        try
        {
            using var http = _httpClientFactory.CreateClient(nameof(ComfyUiImageModelClient));

            var resolvedRefs = await ResolveReferenceImageBytesAsync(request, settings, effectiveCt);

            // OQ-5: upload every resolved reference (within the cap) so the ComfyUI server has
            // each byte handle available; the single-reference template (§3.3) only wires the
            // FIRST uploaded filename into the LoadImage node in v1.
            string? referenceImageName = null;
            if (resolvedRefs.Count > 0)
            {
                foreach (var resolvedRef in resolvedRefs)
                {
                    var uploadResult = await UploadReferenceImageAsync(http, baseUrl, resolvedRef, effectiveCt);
                    if (uploadResult.IsFailure)
                    {
                        return Result.Failure<ImageGenerationResult>(uploadResult.Error);
                    }

                    referenceImageName ??= uploadResult.Value;
                }
            }

            var template = string.IsNullOrWhiteSpace(settings.WorkflowTemplateJson)
                ? LoadBundledDefaultTemplate()
                : settings.WorkflowTemplateJson;

            var seed = Random.Shared.NextInt64(0, int.MaxValue);
            var graphResult = BuildWorkflowGraph(
                template,
                checkpoint,
                request.Prompt,
                negativePrompt: DefaultNegativePrompt,
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

            var promptIdResult = await QueuePromptAsync(http, baseUrl, graphResult.Value, effectiveCt);
            if (promptIdResult.IsFailure)
            {
                return Result.Failure<ImageGenerationResult>(promptIdResult.Error);
            }

            var outputImageResult = await PollHistoryAsync(http, baseUrl, promptIdResult.Value, settings, effectiveCt);
            if (outputImageResult.IsFailure)
            {
                return Result.Failure<ImageGenerationResult>(outputImageResult.Error);
            }

            var bytesResult = await FetchImageBytesAsync(http, baseUrl, outputImageResult.Value, effectiveCt);
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
        catch (OperationCanceledException)
        {
            // CUI-02-c: overall Timeout elapsed OR the caller-supplied CancellationToken was
            // cancelled — both degrade identically, never propagate.
            _logger.LogWarning(
                "ComfyUiImageModelClient: generation timed out or was cancelled. " +
                "OrganizationId={OrganizationId} CapabilityKey={CapabilityKey}",
                request.OrganizationId,
                request.CapabilityKey);

            return Result.Failure<ImageGenerationResult>(IntegrationErrors.ProviderTimeout);
        }
        catch (HttpRequestException ex)
        {
            // CUI-02-a: base URL unreachable / socket-level failure.
            _logger.LogWarning(
                ex,
                "ComfyUiImageModelClient: HTTP call threw. OrganizationId={OrganizationId} CapabilityKey={CapabilityKey}",
                request.OrganizationId,
                request.CapabilityKey);

            return Result.Failure<ImageGenerationResult>(IntegrationErrors.ExternalProviderUnavailable);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Blanket safety net (CUI-02): no path may propagate an unhandled exception.
            _logger.LogWarning(
                ex,
                "ComfyUiImageModelClient: unexpected failure. OrganizationId={OrganizationId} CapabilityKey={CapabilityKey}",
                request.OrganizationId,
                request.CapabilityKey);

            return Result.Failure<ImageGenerationResult>(IntegrationErrors.ExternalProviderUnavailable);
        }
    }

    // -----------------------------------------------------------------------
    // OQ-5: reference byte-resolution from IGeneratedAssetStorage, capped to
    // ComfyUiSettings.MaxReferenceImages; failed resolves are logged and skipped so the
    // remaining references still proceed (CUI-01-a, CUI-01-b, CUI-06-a).
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

        var cap = Math.Min(request.ReferenceImages.Count, settings.MaxReferenceImages);
        var resolved = new List<(byte[], string)>(cap);

        for (var i = 0; i < cap; i++)
        {
            var refImage = request.ReferenceImages[i];
            if (string.IsNullOrWhiteSpace(refImage.Url))
            {
                continue;
            }

            Result<StoredAssetStream> openResult;
            try
            {
                openResult = await _storage.OpenAsync(refImage.Url, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogWarning(
                    ex,
                    "ComfyUiImageModelClient: failed to resolve reference image handle '{Url}' for org {OrganizationId}. Skipping this reference.",
                    refImage.Url,
                    request.OrganizationId);
                continue;
            }

            if (openResult.IsFailure)
            {
                _logger.LogWarning(
                    "ComfyUiImageModelClient: failed to open reference image handle '{Url}' for org {OrganizationId}: {Code}. Skipping this reference.",
                    refImage.Url,
                    request.OrganizationId,
                    openResult.Error.Code);
                continue;
            }

            try
            {
                using var stream = openResult.Value;
                using var ms = new MemoryStream();
                await stream.Content.CopyToAsync(ms, ct);
                resolved.Add((ms.ToArray(), stream.Mime));
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogWarning(
                    ex,
                    "ComfyUiImageModelClient: failed to read reference image bytes from '{Url}' for org {OrganizationId}. Skipping this reference.",
                    refImage.Url,
                    request.OrganizationId);
            }
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

    /// <summary>
    /// CUI-02-b: a non-2xx <c>/prompt</c> response or a queue rejection surfaced via
    /// <c>node_errors</c> both map to <see cref="IntegrationErrors.ExternalProviderUnavailable"/>.
    /// </summary>
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

        if (!response.IsSuccessStatusCode)
        {
            return Result.Failure<string>(IntegrationErrors.ExternalProviderUnavailable);
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        if (doc.RootElement.TryGetProperty("node_errors", out var nodeErrors)
            && nodeErrors.ValueKind == JsonValueKind.Object
            && nodeErrors.EnumerateObject().Any())
        {
            return Result.Failure<string>(IntegrationErrors.ExternalProviderUnavailable);
        }

        var promptId = doc.RootElement.TryGetProperty("prompt_id", out var idProp) ? idProp.GetString() : null;
        return string.IsNullOrWhiteSpace(promptId)
            ? Result.Failure<string>(IntegrationErrors.ExternalProviderUnavailable)
            : Result.Success(promptId);
    }

    /// <summary>
    /// Polls <c>GET /history/{id}</c> every <see cref="ComfyUiSettings.PollInterval"/> until the
    /// configured <see cref="ComfyUiSettings.OutputNodeId"/> carries <c>images[]</c>. A
    /// <c>status.status_str == "error"</c> entry maps to
    /// <see cref="IntegrationErrors.ExternalProviderUnavailable"/> (CUI-02-b). The loop is bounded
    /// by the caller's <paramref name="ct"/> (already linked to the overall Timeout in
    /// <see cref="GenerateAsync"/>) — an elapsed/cancelled token propagates as
    /// <see cref="OperationCanceledException"/> and is mapped to
    /// <see cref="IntegrationErrors.ProviderTimeout"/> by the caller (CUI-02-c).
    /// </summary>
    private static async Task<Result<JsonElement>> PollHistoryAsync(
        HttpClient http,
        string baseUrl,
        string promptId,
        ComfyUiSettings settings,
        CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var response = await http.GetAsync($"{baseUrl}/history/{promptId}", ct);
            if (!response.IsSuccessStatusCode)
            {
                return Result.Failure<JsonElement>(IntegrationErrors.ExternalProviderUnavailable);
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty(promptId, out var entry))
            {
                if (entry.TryGetProperty("status", out var status)
                    && status.TryGetProperty("status_str", out var statusStr)
                    && string.Equals(statusStr.GetString(), "error", StringComparison.OrdinalIgnoreCase))
                {
                    return Result.Failure<JsonElement>(IntegrationErrors.ExternalProviderUnavailable);
                }

                if (entry.TryGetProperty("outputs", out var outputs)
                    && outputs.TryGetProperty(settings.OutputNodeId, out var outputNode)
                    && outputNode.TryGetProperty("images", out var images)
                    && images.ValueKind == JsonValueKind.Array
                    && images.GetArrayLength() > 0)
                {
                    return Result.Success(images[0].Clone());
                }
            }

            await Task.Delay(settings.PollInterval, ct);
        }
    }

    /// <summary>An empty <c>/view</c> body maps to <see cref="IntegrationErrors.AiJsonInvalid"/>.</summary>
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
        if (!response.IsSuccessStatusCode)
        {
            return Result.Failure<byte[]>(IntegrationErrors.ExternalProviderUnavailable);
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        return bytes.Length == 0
            ? Result.Failure<byte[]>(IntegrationErrors.AiJsonInvalid)
            : Result.Success(bytes);
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
