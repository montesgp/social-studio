using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Incoders.Hive.Infrastructure.Ai;

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
            []));
    }

    private async Task<Result<AiJsonCandidateResponse<TResponse>>> CompleteAndParseAsync<TResponse>(
        AiJsonCompletionRequest request,
        IAiModelClient client,
        string? model,
        CancellationToken cancellationToken)
        where TResponse : notnull
    {
        var completion = await client.CompleteAsync(
            new AiCompletionRequest(
                request.OrganizationId,
                request.CapabilityKey,
                request.PromptVersionKey,
                request.InputJson,
                request.PreferredProviderKey,
                model),
            cancellationToken);

        if (completion.IsFailure)
        {
            return Result.Failure<AiJsonCandidateResponse<TResponse>>(completion.Error);
        }

        var parsed = TryParse<TResponse>(completion.Value.Output);
        if (parsed.IsSuccess)
        {
            return Result.Success(new AiJsonCandidateResponse<TResponse>(parsed.Value, completion.Value.Model));
        }

        // One JSON-repair retry against the same provider before giving up.
        var repair = await client.CompleteAsync(
            new AiCompletionRequest(
                request.OrganizationId,
                request.CapabilityKey,
                $"{request.PromptVersionKey}:json-repair",
                $"Return only valid JSON for this schema. Invalid output: {completion.Value.Output}",
                request.PreferredProviderKey,
                model),
            cancellationToken);

        if (repair.IsFailure)
        {
            return Result.Failure<AiJsonCandidateResponse<TResponse>>(repair.Error);
        }

        parsed = TryParse<TResponse>(repair.Value.Output);
        return parsed.IsSuccess
            ? Result.Success(new AiJsonCandidateResponse<TResponse>(parsed.Value, repair.Value.Model))
            : Result.Failure<AiJsonCandidateResponse<TResponse>>(IntegrationErrors.AiJsonInvalid);
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

    private sealed record AiJsonCandidateResponse<TResponse>(TResponse Payload, string? Model)
        where TResponse : notnull;
}
