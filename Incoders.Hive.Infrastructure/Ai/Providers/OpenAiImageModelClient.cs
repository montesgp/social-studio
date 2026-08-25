using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Infrastructure.Integrations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Incoders.Hive.Infrastructure.Ai.Providers;

/// <summary>
/// Infrastructure adapter: OpenAI Images API (dall-e-3 / gpt-image-1 per request preference).
/// Implements <see cref="IImageModelClient"/> — the only place where OpenAI Images SDK/HTTP
/// types exist. Returns <see cref="ImageGenerationResult"/> with raw bytes and "image/png" MIME.
///
/// <para>
/// Architecture gate: ALL OpenAI-specific types, HTTP concerns, and response parsing are
/// CONFINED to this file. No OpenAI type escapes into Application or Domain.
/// </para>
///
/// <para>
/// OQ-7: The <c>Init</c> field on <see cref="ImageGenerationRequest"/> is reserved for
/// image-to-image conditioning and is NOT used in v1; it is silently ignored, not thrown.
/// </para>
///
/// <para>
/// CAC-IMG-01: This adapter supports image conditioning (<see cref="Capabilities"/>).
/// When <see cref="ImageGenerationRequest.ReferenceImages"/> is non-null the adapter resolves
/// bytes from <see cref="IGeneratedAssetStorage"/> for each <see cref="ImageInitReference"/>
/// Url handle (capped to <see cref="Capabilities"/>.MaxConditioningImages), then routes the
/// call to <c>POST /v1/images/edits</c> as <c>multipart/form-data</c> (gpt-image-1).
/// References whose <c>OpenAsync</c> returns failure are logged and skipped.
/// When no references resolve, falls back to <c>POST /v1/images/generations</c>.
/// </para>
///
/// <para>
/// CAC-IMG-02: When <see cref="ImageGenerationRequest.BackgroundOnly"/> is true the adapter
/// prepends the background-only system directive to the prompt before calling the provider.
/// The directive is forwarded on both the generations and edits paths.
/// </para>
///
/// Refs: IMG-01, IMG-08; design §2.3, §3.4, §6; tasks 4.5, 4.9.
/// </summary>
public sealed class OpenAiImageModelClient : IImageModelClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private const string OpenAiImagesGenerationsEndpoint = "https://api.openai.com/v1/images/generations";
    private const string OpenAiImagesEditsEndpoint = "https://api.openai.com/v1/images/edits";
    private const string DefaultImageMime = "image/png";
    private const string ProviderKey = "openai";

    private readonly ResolvedProviderCredential _credential;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IGeneratedAssetStorage _storage;
    private readonly ILogger<OpenAiImageModelClient> _logger;

    /// <summary>
    /// OpenAI supports image conditioning via the gpt-image-1 image-edit endpoint.
    /// MaxConditioningImages = 3 (bounded to 3 reference images per generation call).
    /// Refs: CAC-IMG-01; design §3.4.
    /// </summary>
    public ImageModelCapabilities Capabilities { get; } =
        new(SupportsImageConditioning: true, MaxConditioningImages: 3);

    public OpenAiImageModelClient(
        ResolvedProviderCredential credential,
        IHttpClientFactory httpClientFactory,
        IGeneratedAssetStorage storage,
        ILogger<OpenAiImageModelClient>? logger = null)
    {
        _credential = credential;
        _httpClientFactory = httpClientFactory;
        _storage = storage;
        _logger = logger ?? NullLogger<OpenAiImageModelClient>.Instance;
    }

    public async Task<Result<ImageGenerationResult>> GenerateAsync(
        ImageGenerationRequest request,
        CancellationToken ct = default)
    {
        if (!ProviderCredentialReader.TryGet(_credential, "api_key", out var apiKey))
        {
            _logger.LogWarning(
                "OpenAiImageModelClient: API key missing. " +
                "OrganizationId={OrganizationId} CapabilityKey={CapabilityKey}",
                request.OrganizationId,
                request.CapabilityKey);

            return Result.Failure<ImageGenerationResult>(IntegrationErrors.ProviderCredentialUnavailable);
        }

        var model = request.PreferredModel ?? _credential.SelectedModel;
        if (string.IsNullOrWhiteSpace(model))
        {
            _logger.LogWarning(
                "OpenAiImageModelClient: model missing. " +
                "OrganizationId={OrganizationId} CapabilityKey={CapabilityKey}",
                request.OrganizationId,
                request.CapabilityKey);

            return Result.Failure<ImageGenerationResult>(IntegrationErrors.AiModelRequired);
        }

        using var http = _httpClientFactory.CreateClient(nameof(OpenAiImageModelClient));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        // CAC-IMG-02: inject background-only directive when requested (both paths).
        var effectivePrompt = request.BackgroundOnly
            ? $"{ImagePromptDirectives.BackgroundOnly}\n\n{request.Prompt}"
            : request.Prompt;

        // CAC-IMG-01: resolve Url-handle reference images from storage (byte resolution).
        var resolvedBytes = await ResolveReferenceImageBytesAsync(request, ct);

        if (resolvedBytes.Count > 0)
        {
            return await GenerateWithConditioningAsync(http, model, effectivePrompt, request, resolvedBytes, ct);
        }

        // OQ-7: Init (image-to-image) is reserved for v1; silently ignored here.
        return await GenerateFromTextAsync(http, model, effectivePrompt, request, ct);
    }

    // -----------------------------------------------------------------------
    // CAC-IMG-01: byte resolution from IGeneratedAssetStorage
    // -----------------------------------------------------------------------

    private async Task<IReadOnlyList<byte[]>> ResolveReferenceImageBytesAsync(
        ImageGenerationRequest request,
        CancellationToken ct)
    {
        if (request.ReferenceImages is not { Count: > 0 })
        {
            return [];
        }

        var cap = Math.Min(request.ReferenceImages.Count, Capabilities.MaxConditioningImages);
        var resolved = new List<byte[]>(cap);

        for (var i = 0; i < cap; i++)
        {
            var refImage = request.ReferenceImages[i];
            if (string.IsNullOrWhiteSpace(refImage.Url))
            {
                // Bytes-only reference (not a Url handle): skip for now (deferred).
                continue;
            }

            var openResult = await _storage.OpenAsync(refImage.Url, ct);
            if (openResult.IsFailure)
            {
                _logger.LogWarning(
                    "OpenAiImageModelClient: failed to resolve reference image handle '{Url}' for org {OrgId}: {Code}. " +
                    "Skipping this reference.",
                    refImage.Url,
                    request.OrganizationId,
                    openResult.Error.Code);
                continue;
            }

            try
            {
                using var storageStream = openResult.Value;
                using var ms = new MemoryStream();
                await storageStream.Content.CopyToAsync(ms, ct);
                resolved.Add(ms.ToArray());
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogWarning(
                    ex,
                    "OpenAiImageModelClient: failed to read reference image bytes from '{Url}' for org {OrgId}. " +
                    "Skipping this reference.",
                    refImage.Url,
                    request.OrganizationId);
            }
        }

        return resolved;
    }

    // -----------------------------------------------------------------------
    // CAC-IMG-01: route to /v1/images/edits with multipart/form-data when refs resolved
    // -----------------------------------------------------------------------

    private async Task<Result<ImageGenerationResult>> GenerateWithConditioningAsync(
        HttpClient http,
        string model,
        string effectivePrompt,
        ImageGenerationRequest request,
        IReadOnlyList<byte[]> referenceBytes,
        CancellationToken ct)
    {
        using var multipart = new MultipartFormDataContent();
        multipart.Add(new StringContent(model), "model");
        multipart.Add(new StringContent(effectivePrompt), "prompt");
        multipart.Add(new StringContent("1"), "n");
        multipart.Add(new StringContent($"{request.Width}x{request.Height}"), "size");
        multipart.Add(new StringContent("b64_json"), "response_format");

        for (var i = 0; i < referenceBytes.Count; i++)
        {
            var imageContent = new ByteArrayContent(referenceBytes[i]);
            imageContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            multipart.Add(imageContent, "image[]", $"ref_{i}.png");
        }

        HttpResponseMessage? response = null;
        try
        {
            response = await http.PostAsync(OpenAiImagesEditsEndpoint, multipart, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(
                ex,
                "OpenAiImageModelClient (edits): HTTP call threw for org {OrganizationId} model {Model}.",
                request.OrganizationId,
                model);

            return Result.Failure<ImageGenerationResult>(IntegrationErrors.ExternalProviderUnavailable);
        }

        return await ExtractResultAsync(response!, model, request.OrganizationId, ct);
    }

    // -----------------------------------------------------------------------
    // Standard text-to-image path: POST /v1/images/generations (JSON)
    // -----------------------------------------------------------------------

    private async Task<Result<ImageGenerationResult>> GenerateFromTextAsync(
        HttpClient http,
        string model,
        string effectivePrompt,
        ImageGenerationRequest request,
        CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(new
        {
            model,
            prompt = effectivePrompt,
            n = 1,
            size = $"{request.Width}x{request.Height}",
            response_format = "b64_json",
        }, JsonOptions);

        HttpResponseMessage? response = null;
        try
        {
            response = await http.PostAsync(
                OpenAiImagesGenerationsEndpoint,
                new StringContent(body, Encoding.UTF8, "application/json"),
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(
                ex,
                "OpenAiImageModelClient: HTTP call threw for org {OrganizationId} model {Model}.",
                request.OrganizationId,
                model);

            return Result.Failure<ImageGenerationResult>(IntegrationErrors.ExternalProviderUnavailable);
        }

        return await ExtractResultAsync(response!, model, request.OrganizationId, ct);
    }

    // -----------------------------------------------------------------------
    // Shared response handling (both paths return the same OpenAI response shape)
    // -----------------------------------------------------------------------

    private async Task<Result<ImageGenerationResult>> ExtractResultAsync(
        HttpResponseMessage response,
        string model,
        Guid organizationId,
        CancellationToken ct)
    {
        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning(
                    "OpenAiImageModelClient: image generation failed. " +
                    "OrganizationId={OrganizationId} Model={Model} StatusCode={StatusCode} Body={Body}",
                    organizationId,
                    model,
                    (int)response.StatusCode,
                    errorBody.Length > 400 ? errorBody[..400] + "…" : errorBody);

                return Result.Failure<ImageGenerationResult>(MapErrorStatus(response.StatusCode));
            }

            try
            {
                var bytes = await ExtractImageBytesAsync(response, ct);
                if (bytes is null)
                {
                    _logger.LogWarning(
                        "OpenAiImageModelClient: response contained no image bytes. " +
                        "OrganizationId={OrganizationId} Model={Model}",
                        organizationId,
                        model);

                    return Result.Failure<ImageGenerationResult>(IntegrationErrors.AiJsonInvalid);
                }

                return Result.Success(new ImageGenerationResult(
                    bytes,
                    DefaultImageMime,
                    ProviderKey,
                    model,
                    new ImageUsage(ImageCount: 1)));
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogWarning(
                    ex,
                    "OpenAiImageModelClient: failed to parse image response for org {OrganizationId} model {Model}.",
                    organizationId,
                    model);

                return Result.Failure<ImageGenerationResult>(IntegrationErrors.AiJsonInvalid);
            }
        }
    }

    // -----------------------------------------------------------------------
    // Private helpers — all OpenAI response parsing stays here (architecture gate)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Extracts raw image bytes from the OpenAI Images response.
    /// Response shape: <c>{ "data": [{ "b64_json": "&lt;base64&gt;" }] }</c>
    /// Used by both the generations and edits paths.
    /// </summary>
    private static async Task<byte[]?> ExtractImageBytesAsync(
        HttpResponseMessage response,
        CancellationToken ct)
    {
        var json = await response.Content.ReadAsStringAsync(ct);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Array
            || data.GetArrayLength() == 0)
        {
            return null;
        }

        var first = data[0];
        if (!first.TryGetProperty("b64_json", out var b64Prop))
        {
            return null;
        }

        var b64 = b64Prop.GetString();
        return string.IsNullOrWhiteSpace(b64) ? null : Convert.FromBase64String(b64);
    }

    private static Error MapErrorStatus(System.Net.HttpStatusCode statusCode) =>
        (int)statusCode switch
        {
            401 or 403 => IntegrationErrors.ProviderCredentialUnavailable,
            429 => IntegrationErrors.ProviderRateLimited,
            >= 500 => IntegrationErrors.ExternalProviderUnavailable,
            _ => IntegrationErrors.ExternalProviderUnavailable,
        };
}
