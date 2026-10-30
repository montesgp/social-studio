using SocialStudio.Domain.Common;
using SocialStudio.Domain.Integrations;
using SocialStudio.Domain.Social.ContentGeneration;

namespace SocialStudio.Application.Abstractions.Ai;

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
    string? OutputJsonSchema = null,
    // Phase 2 (CAG2): optional vision input. When present, multimodal providers (Claude) attach
    // these as image blocks alongside the text prompt so the model can SEE the reference creatives.
    // Providers without vision support ignore them and run text-only (graceful degrade).
    IReadOnlyList<AiImageInput>? Images = null);

/// <summary>
/// A single image attached to a multimodal completion. Phase 2 (CAG2). Prefer <see cref="Base64Data"/>
/// (durable, downscaled thumbnail bytes) over <see cref="Url"/>; a provider uses whichever is present.
/// </summary>
public sealed record AiImageInput(
    string MediaType,
    string? Base64Data,
    string? Url = null);

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
    string? PreferredModel,
    // Phase 2 (CAG2): optional vision input forwarded to multimodal providers (see AiCompletionRequest.Images).
    IReadOnlyList<AiImageInput>? Images = null);

public sealed record AiJsonCompletionResponse<TResponse>(
    TResponse Payload,
    string ProviderKey,
    string? Model,
    IReadOnlyList<ProviderFailureDiagnostic> Diagnostics,
    TokenUsage? TokenUsage = null)
    where TResponse : notnull;
