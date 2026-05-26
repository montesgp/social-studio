using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Domain.Social.ContentGeneration;

namespace Incoders.Hive.Application.Abstractions.Ai;

public interface IAiModelClient
{
    Task<Result<AiCompletionResponse>> CompleteAsync(
        AiCompletionRequest request,
        CancellationToken cancellationToken = default);
}

public interface IAiCompletionGateway
{
    Task<Result<AiJsonCompletionResponse<TResponse>>> CompleteJsonAsync<TResponse>(
        AiJsonCompletionRequest request,
        CancellationToken cancellationToken = default)
        where TResponse : notnull;
}

public sealed record AiCompletionRequest(
    Guid OrganizationId,
    string CapabilityKey,
    string PromptVersionKey,
    string Input,
    string? PreferredProviderKey,
    string? PreferredModel,
    // Optional JSON Schema string describing the EXACT structure the model must return.
    // Used by providers that support structured output (Claude tool use, OpenAI json_schema).
    // When null, the provider falls back to a permissive schema and relies on the prompt.
    string? OutputJsonSchema = null);

public sealed record AiCompletionResponse(
    string Output,
    string ProviderKey,
    string? Model,
    IReadOnlyList<ProviderFailureDiagnostic> Diagnostics,
    TokenUsage? Usage = null);

public sealed record AiJsonCompletionRequest(
    Guid OrganizationId,
    Guid? AnalysisId,
    string CapabilityKey,
    string PromptVersionKey,
    string InputJson,
    string? PreferredProviderKey,
    string? PreferredModel);

public sealed record AiJsonCompletionResponse<TResponse>(
    TResponse Payload,
    string ProviderKey,
    string? Model,
    IReadOnlyList<ProviderFailureDiagnostic> Diagnostics)
    where TResponse : notnull;
