using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Infrastructure.Integrations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Incoders.Hive.Infrastructure.Ai.Providers;

public sealed class ClaudeModelClient : IAiModelClient
{
    private readonly ResolvedProviderCredential _credential;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ClaudeModelClient> _logger;

    public ClaudeModelClient(
        ResolvedProviderCredential credential,
        IHttpClientFactory httpClientFactory,
        ILogger<ClaudeModelClient>? logger = null)
    {
        _credential = credential;
        _httpClientFactory = httpClientFactory;
        _logger = logger ?? NullLogger<ClaudeModelClient>.Instance;
    }

    public async Task<Result<AiCompletionResponse>> CompleteAsync(
        AiCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ProviderCredentialReader.TryGet(_credential, "api_key", out var apiKey))
        {
            _logger.LogWarning(
                "Claude completion cannot start because the API key is missing. OrganizationId={OrganizationId} CapabilityKey={CapabilityKey} PromptVersionKey={PromptVersionKey}",
                request.OrganizationId,
                request.CapabilityKey,
                request.PromptVersionKey);

            return Result.Failure<AiCompletionResponse>(IntegrationErrors.ProviderCredentialUnavailable);
        }

        var model = request.PreferredModel ?? _credential.SelectedModel;
        if (string.IsNullOrWhiteSpace(model))
        {
            _logger.LogWarning(
                "Claude completion cannot start because the model is missing. OrganizationId={OrganizationId} CapabilityKey={CapabilityKey} PromptVersionKey={PromptVersionKey}",
                request.OrganizationId,
                request.CapabilityKey,
                request.PromptVersionKey);

            return Result.Failure<AiCompletionResponse>(IntegrationErrors.AiModelRequired);
        }

        using var http = _httpClientFactory.CreateClient(nameof(ClaudeModelClient));
        http.DefaultRequestHeaders.Add("x-api-key", apiKey);
        http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

        // JSON-mode via Tool Use: Anthropic does not expose `response_format: json_object`.
        //
        // The PREVIOUS technique (pre-filling the assistant turn with `{`) works on Claude 3.x
        // but Claude 4.x models REJECT it with HTTP 400 "This model does not support assistant
        // message prefill. The conversation must end with a user message."
        //
        // Tool Use is the forward-compatible technique: declare a synthetic tool with the exact
        // input_schema we expect and force Claude to call it via `tool_choice`. The model is
        // OBLIGATED to return a structured object in `content[].input` that conforms to the
        // schema — no prose, no markdown fences, no shape drift (e.g. arrays of objects when we
        // asked for arrays of strings).
        // Ref: https://docs.claude.com/en/docs/build-with-claude/tool-use
        //
        // The schema is provided by the gateway (auto-generated from TResponse via
        // JsonSchemaExporter). If absent, fall back to a permissive object schema; that still
        // guarantees valid JSON, just no shape enforcement.
        //
        // max_tokens=8192 leaves headroom for dense schemas; claude-sonnet-4 supports up to 64K.
        object inputSchema = string.IsNullOrWhiteSpace(request.OutputJsonSchema)
            ? new { type = "object", additionalProperties = true }
            : JsonSerializer.Deserialize<JsonElement>(request.OutputJsonSchema);

        var body = JsonSerializer.Serialize(new
        {
            model,
            max_tokens = 8192,
            messages = new object[]
            {
                new { role = "user", content = request.Input }
            },
            tools = new object[]
            {
                new
                {
                    name = "submit_response",
                    description = "Submit the structured JSON response that satisfies the requested schema. Always call this tool exactly once with the complete response as its input.",
                    input_schema = inputSchema
                }
            },
            tool_choice = new { type = "tool", name = "submit_response" }
        });

        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation(
            "Claude completion START. OrganizationId={OrganizationId} CapabilityKey={CapabilityKey} PromptVersionKey={PromptVersionKey} Model={Model} RequestBytes={RequestBytes} InputPreview={InputPreview}",
            request.OrganizationId,
            request.CapabilityKey,
            request.PromptVersionKey,
            model,
            body.Length,
            Preview(request.Input, 400));

        HttpResponseMessage response;
        try
        {
            response = await http.PostAsync(
                "https://api.anthropic.com/v1/messages",
                new StringContent(body, Encoding.UTF8, "application/json"),
                cancellationToken);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            _logger.LogWarning(
                ex,
                "Claude completion TIMED OUT after {ElapsedMs} ms. OrganizationId={OrganizationId} Model={Model}",
                stopwatch.ElapsedMilliseconds,
                request.OrganizationId,
                model);

            return Result.Failure<AiCompletionResponse>(IntegrationErrors.ProviderTimeout);
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            _logger.LogWarning(
                ex,
                "Claude completion TRANSPORT FAILURE after {ElapsedMs} ms. OrganizationId={OrganizationId} Model={Model} ExceptionType={ExceptionType} Message={Message} InnerType={InnerType} InnerMessage={InnerMessage}",
                stopwatch.ElapsedMilliseconds,
                request.OrganizationId,
                model,
                ex.GetType().FullName,
                ex.Message,
                ex.InnerException?.GetType().FullName,
                ex.InnerException?.Message);

            return Result.Failure<AiCompletionResponse>(IntegrationErrors.ExternalProviderUnavailable);
        }

        using var responseHandle = response;
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        stopwatch.Stop();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Claude completion RESPONSE ERROR after {ElapsedMs} ms. OrganizationId={OrganizationId} CapabilityKey={CapabilityKey} PromptVersionKey={PromptVersionKey} Model={Model} StatusCode={StatusCode} ReasonPhrase={ReasonPhrase} ResponseBytes={ResponseBytes} BodyPreview={BodyPreview}",
                stopwatch.ElapsedMilliseconds,
                request.OrganizationId,
                request.CapabilityKey,
                request.PromptVersionKey,
                model,
                (int)response.StatusCode,
                response.ReasonPhrase,
                payload.Length,
                Preview(payload, 1500));

            return Result.Failure<AiCompletionResponse>(MapClaudeStatus((int)response.StatusCode));
        }

        _logger.LogInformation(
            "Claude completion RESPONSE OK after {ElapsedMs} ms. OrganizationId={OrganizationId} Model={Model} StatusCode={StatusCode} ResponseBytes={ResponseBytes}",
            stopwatch.ElapsedMilliseconds,
            request.OrganizationId,
            model,
            (int)response.StatusCode,
            payload.Length);

        // Tool Use response shape: content[].type === "tool_use", content[].input is the JSON
        // object. The mapper extracts and serializes it as the Output string for the gateway.
        return ProviderHttpResponseMapper.MapAiSuccess(payload, "claude", model);
    }

    private static Error MapClaudeStatus(int statusCode) =>
        statusCode switch
        {
            401 or 403 => IntegrationErrors.ProviderCredentialUnavailable,
            408 => IntegrationErrors.ProviderTimeout,
            429 => IntegrationErrors.ProviderRateLimited,
            >= 500 => IntegrationErrors.ExternalProviderUnavailable,
            _ => IntegrationErrors.ExternalProviderUnavailable,
        };

    private static string Preview(string? text, int max)
    {
        if (string.IsNullOrEmpty(text)) return "(empty)";
        return text.Length <= max ? text : text[..max] + "…(truncated)";
    }
}
