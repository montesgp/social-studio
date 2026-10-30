using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization.Metadata;
using SocialStudio.Application.Abstractions.Ai;
using SocialStudio.Application.Abstractions.Integrations;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Integrations;
using SocialStudio.Domain.Social.ContentGeneration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SocialStudio.Infrastructure.Ai;

/// <summary>
/// Resolves the AI provider directly from the organization's integration config.
/// The provider key is chosen upstream by IDefaultAiSelector (is_default_ai → last configured).
/// Routing-per-client for AI is a future concern; for now we use the org's default agent.
/// </summary>
public sealed class AiCompletionGateway : IAiCompletionGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IIntegrationResolver _integrationResolver;
    private readonly IExternalProviderClientFactory _clientFactory;
    private readonly ILogger<AiCompletionGateway> _logger;

    public AiCompletionGateway(
        IIntegrationResolver integrationResolver,
        IExternalProviderClientFactory clientFactory,
        ILogger<AiCompletionGateway>? logger = null)
    {
        _integrationResolver = integrationResolver;
        _clientFactory = clientFactory;
        _logger = logger ?? NullLogger<AiCompletionGateway>.Instance;
    }

    public async Task<Result<AiJsonCompletionResponse<TResponse>>> CompleteJsonAsync<TResponse>(
        AiJsonCompletionRequest request,
        CancellationToken cancellationToken = default)
        where TResponse : notnull
    {
        var providerKey = request.PreferredProviderKey?.Trim();
        if (string.IsNullOrWhiteSpace(providerKey))
        {
            _logger.LogWarning(
                "AI completion has no provider key. OrganizationId={OrganizationId} CapabilityKey={CapabilityKey}",
                request.OrganizationId,
                request.CapabilityKey);

            return Result.Failure<AiJsonCompletionResponse<TResponse>>(IntegrationErrors.DefaultAiProviderNotConnected);
        }

        var resolved = await _integrationResolver.ResolveProviderAsync(request.OrganizationId, providerKey, cancellationToken);
        if (resolved.IsFailure)
        {
            _logger.LogWarning(
                "AI completion: provider {ProviderKey} unavailable for org {OrganizationId}: {ErrorCode}.",
                providerKey,
                request.OrganizationId,
                resolved.Error.Code);

            return Result.Failure<AiJsonCompletionResponse<TResponse>>(resolved.Error);
        }

        var model = request.PreferredModel ?? resolved.Value.SelectedAiModel;
        var credential = new ResolvedProviderCredential(
            Guid.Empty,
            resolved.Value.ProviderKey,
            resolved.Value.ConnectionMode,
            resolved.Value.Credentials,
            model);

        IAiModelClient client;
        try
        {
            client = _clientFactory.Create<IAiModelClient>(credential);
        }
        catch (InvalidOperationException)
        {
            _logger.LogWarning("AI completion: provider {ProviderKey} has no model client registered.", providerKey);
            return Result.Failure<AiJsonCompletionResponse<TResponse>>(IntegrationErrors.ExternalProviderUnsupported);
        }

        Result<AiJsonCandidateResponse<TResponse>> parsed;
        try
        {
            parsed = await CompleteAndParseAsync<TResponse>(request, client, model, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Never let an exception in the AI/parse path bubble as a silent 500.
            _logger.LogWarning(
                ex,
                "AI completion threw for org {OrganizationId} provider {ProviderKey} model {Model}.",
                request.OrganizationId,
                providerKey,
                model ?? "(provider default)");

            return Result.Failure<AiJsonCompletionResponse<TResponse>>(IntegrationErrors.AiJsonInvalid);
        }

        if (parsed.IsFailure)
        {
            _logger.LogWarning(
                "AI completion failed for org {OrganizationId} provider {ProviderKey} model {Model}: {ErrorCode}.",
                request.OrganizationId,
                providerKey,
                model ?? "(provider default)",
                parsed.Error.Code);

            return Result.Failure<AiJsonCompletionResponse<TResponse>>(parsed.Error);
        }

        return Result.Success(new AiJsonCompletionResponse<TResponse>(
            parsed.Value.Payload,
            resolved.Value.ProviderKey,
            parsed.Value.Model,
            [],
            parsed.Value.TokenUsage));
    }

    private async Task<Result<AiJsonCandidateResponse<TResponse>>> CompleteAndParseAsync<TResponse>(
        AiJsonCompletionRequest request,
        IAiModelClient client,
        string? model,
        CancellationToken cancellationToken)
        where TResponse : notnull
    {
        var outputSchema = GetCachedJsonSchema(typeof(TResponse));

        var completion = await client.CompleteAsync(
            new AiCompletionRequest(
                request.OrganizationId,
                request.CapabilityKey,
                request.PromptVersionKey,
                request.InputJson,
                request.PreferredProviderKey,
                model,
                OutputJsonSchema: outputSchema,
                // Phase 2 (CAG2): forward vision input on the primary call only (the json-repair
                // retry below is text-only — it just needs to fix JSON shape, not re-see images).
                Images: request.Images),
            cancellationToken);

        if (completion.IsFailure)
        {
            return Result.Failure<AiJsonCandidateResponse<TResponse>>(completion.Error);
        }

        var parsed = TryParse<TResponse>(completion.Value.Output);
        if (parsed.IsSuccess)
        {
            return Result.Success(new AiJsonCandidateResponse<TResponse>(parsed.Value, completion.Value.Model, completion.Value.Usage));
        }

        // First parse failed — log a bounded preview of the raw output so we can diagnose what
        // the model actually returned (truncation, prose, schema mismatch) instead of guessing.
        LogOutputPreview("first attempt", request, model, completion.Value.Output);

        // One JSON-repair retry against the same provider before giving up.
        var repair = await client.CompleteAsync(
            new AiCompletionRequest(
                request.OrganizationId,
                request.CapabilityKey,
                $"{request.PromptVersionKey}:json-repair",
                $"Return only valid JSON for this schema. Invalid output: {completion.Value.Output}",
                request.PreferredProviderKey,
                model,
                OutputJsonSchema: outputSchema),
            cancellationToken);

        if (repair.IsFailure)
        {
            return Result.Failure<AiJsonCandidateResponse<TResponse>>(repair.Error);
        }

        parsed = TryParse<TResponse>(repair.Value.Output);
        if (parsed.IsSuccess)
        {
            return Result.Success(new AiJsonCandidateResponse<TResponse>(parsed.Value, repair.Value.Model, repair.Value.Usage));
        }

        LogOutputPreview("json-repair retry", request, model, repair.Value.Output);
        return Result.Failure<AiJsonCandidateResponse<TResponse>>(IntegrationErrors.AiJsonInvalid);
    }

    // JSON schemas are stable per TResponse — generating one is non-trivial so we cache forever.
    private static readonly ConcurrentDictionary<Type, string> SchemaCache = new();

    // JsonSchemaExporter REQUIRES a JsonSerializerOptions instance with a TypeInfoResolver set,
    // and the instance must NOT already be marked read-only by prior use. Sharing `JsonOptions`
    // (used elsewhere for serialize/deserialize) fails with InvalidOperationException because:
    //   1. `new(JsonSerializerDefaults.Web)` does not assign a TypeInfoResolver, AND
    //   2. once used, options become read-only and can no longer accept a resolver.
    // So we maintain a dedicated, never-shared options instance for schema export only.
    private static readonly JsonSerializerOptions SchemaExportOptions = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    // TreatNullObliviousAsNonNullable=true tells the exporter to treat reference types whose
    // nullability annotations are absent (or oblivious) as NON-NULL. Without this, the exporter
    // emits `"type": ["object", "null"]` (a union) at the root, which Claude's tool-use API
    // rejects with: tools.0.custom.input_schema.type: Input should be 'object'.
    private static readonly JsonSchemaExporterOptions ExporterOptions = new()
    {
        TreatNullObliviousAsNonNullable = true
    };

    private static string GetCachedJsonSchema(Type type) =>
        SchemaCache.GetOrAdd(type, t =>
        {
            // .NET 9+ native: JsonSchemaExporter generates a JSON Schema node directly from a CLR
            // type using web defaults (camelCase). The output is exactly what the model honors.
            var node = JsonSchemaExporter.GetJsonSchemaAsNode(SchemaExportOptions, t, ExporterOptions);
            // Defensive: even with TreatNullObliviousAsNonNullable, the exporter may emit type
            // unions in nested properties for genuinely-nullable members. Claude tool-use accepts
            // unions on inner properties, but we normalize the ROOT to a plain "object" string to
            // guarantee compatibility — that's the only place where Claude is strict.
            NormalizeRootTypeForToolUse(node);
            return node.ToJsonString();
        });

    private static void NormalizeRootTypeForToolUse(JsonNode? schema)
    {
        if (schema is not JsonObject root) return;

        if (root["type"] is JsonArray typeArray)
        {
            // Pick the first non-"null" entry; default to "object" if somehow absent.
            string? picked = null;
            foreach (var entry in typeArray)
            {
                if (entry is JsonValue v && v.TryGetValue<string>(out var s) && s != "null")
                {
                    picked = s;
                    break;
                }
            }
            root["type"] = picked ?? "object";
        }
        else if (root["type"] is null)
        {
            // Some schemas (e.g. ref-only) lack an explicit type at root — Claude needs one.
            root["type"] = "object";
        }
    }

    private void LogOutputPreview(string stage, AiJsonCompletionRequest request, string? model, string? output)
    {
        const int MaxPreviewChars = 800;
        var safe = output ?? string.Empty;
        var preview = safe.Length <= MaxPreviewChars ? safe : safe[..MaxPreviewChars] + "…(truncated)";
        _logger.LogWarning(
            "AI JSON parse failed at {Stage} for org {OrganizationId} provider {ProviderKey} model {Model}. Output length={Length}. Preview: {Preview}",
            stage,
            request.OrganizationId,
            request.PreferredProviderKey,
            model ?? "(provider default)",
            safe.Length,
            preview);
    }

    private static Result<TResponse> TryParse<TResponse>(string output)
        where TResponse : notnull
    {
        // LLMs frequently wrap JSON in markdown fences or add prose. Extract the JSON before parsing.
        var json = ExtractJson(output);

        try
        {
            var parsed = JsonSerializer.Deserialize<TResponse>(json, JsonOptions);
            return parsed is null
                ? Result.Failure<TResponse>(IntegrationErrors.AiJsonInvalid)
                : Result.Success(parsed);
        }
        catch (JsonException)
        {
            return Result.Failure<TResponse>(IntegrationErrors.AiJsonInvalid);
        }
    }

    /// <summary>
    /// Pulls the JSON object out of a model response that may be fenced (```json ... ```) or
    /// surrounded by prose. Falls back to the original text if no object delimiters are found.
    /// </summary>
    private static string ExtractJson(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return output;
        }

        var text = output.Trim();

        // Strip a leading ```json / ``` fence and its closing ```.
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstLineBreak = text.IndexOf('\n');
            if (firstLineBreak >= 0)
            {
                text = text[(firstLineBreak + 1)..];
            }

            var closingFence = text.LastIndexOf("```", StringComparison.Ordinal);
            if (closingFence >= 0)
            {
                text = text[..closingFence];
            }

            text = text.Trim();
        }

        // Trim any remaining prose around the outermost JSON object.
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            text = text.Substring(start, end - start + 1);
        }

        return text;
    }

    private sealed record AiJsonCandidateResponse<TResponse>(TResponse Payload, string? Model, TokenUsage? TokenUsage = null)
        where TResponse : notnull;
}
