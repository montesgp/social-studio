using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Infrastructure.Integrations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
// WU-9: alias removed — IImageGenerationClient retired; ImageGenerationRequest is unambiguous now.

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
/// Refs: IMG-01, IMG-08; design §2.3; tasks 6.2.
/// </summary>
public sealed class OpenAiImageModelClient : IImageModelClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private const string OpenAiImagesEndpoint = "https://api.openai.com/v1/images/generations";
    private const string DefaultImageMime = "image/png";
    private const string ProviderKey = "openai";

    private readonly ResolvedProviderCredential _credential;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<OpenAiImageModelClient> _logger;

    public OpenAiImageModelClient(
        ResolvedProviderCredential credential,
        IHttpClientFactory httpClientFactory,
        ILogger<OpenAiImageModelClient>? logger = null)
    {
        _credential = credential;
        _httpClientFactory = httpClientFactory;
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

        // OQ-7: Init (image-to-image) is reserved for v1; silently ignored here.
        // If Init is non-null in a future version, pass it via the edit endpoint.

        var body = JsonSerializer.Serialize(new
        {
            model,
            prompt = request.Prompt,
            n = 1,
            size = $"{request.Width}x{request.Height}",
            response_format = "b64_json",
        }, JsonOptions);

        HttpResponseMessage response;
        try
        {
            response = await http.PostAsync(
                OpenAiImagesEndpoint,
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

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning(
                "OpenAiImageModelClient: image generation failed. " +
                "OrganizationId={OrganizationId} Model={Model} StatusCode={StatusCode} Body={Body}",
                request.OrganizationId,
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
                    request.OrganizationId,
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
                request.OrganizationId,
                model);

            return Result.Failure<ImageGenerationResult>(IntegrationErrors.AiJsonInvalid);
        }
    }

    // -----------------------------------------------------------------------
    // Private helpers — all OpenAI response parsing stays here (architecture gate)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Extracts raw image bytes from the OpenAI Images response.
    /// Response shape: <c>{ "data": [{ "b64_json": "&lt;base64&gt;" }] }</c>
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
