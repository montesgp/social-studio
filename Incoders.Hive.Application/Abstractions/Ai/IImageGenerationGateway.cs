using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Application.Abstractions.Ai;

/// <summary>
/// Application port for AI image generation — the byte-returning sibling of
/// <see cref="IAiCompletionGateway"/> (which returns JSON, not bytes).
/// Routes per organisation via the workflow-configured IMAGE route
/// (<c>generate-image</c> / <c>MediaGeneration</c> step).
/// Instrumented by <c>ImageGenerationGatewayInstrumentationDecorator</c> (Infrastructure).
/// No OpenAI, HttpClient, Supabase, or EF type may appear in this namespace.
/// Refs: IMG-01, IMG-08; design §3.1.
/// </summary>
public interface IImageGenerationGateway
{
    Task<Result<ImageGenerationResult>> GenerateAsync(
        ImageGenerationRequest request,
        CancellationToken ct = default);
}

/// <summary>
/// Provider client contract created by <c>IExternalProviderClientFactory</c>.
/// Mirrors <see cref="IAiModelClient"/> — one adapter per image provider lives in Infrastructure.
/// </summary>
public interface IImageModelClient
{
    Task<Result<ImageGenerationResult>> GenerateAsync(
        ImageGenerationRequest request,
        CancellationToken ct = default);
}

/// <summary>
/// Bytes-only image generation request.
/// All fields are primitives or Application-layer types — no provider SDK types.
/// Refs: design §3.1.
/// </summary>
public sealed record ImageGenerationRequest(
    Guid OrganizationId,
    /// <summary>Capability key for the workflow step, e.g. "ai-image".</summary>
    string CapabilityKey,
    /// <summary>Prompt-version key for observability, e.g. "image-gen/v1".</summary>
    string PromptVersionKey,
    string Prompt,
    int Width,
    int Height,
    string? PreferredProviderKey,
    string? PreferredModel,
    /// <summary>
    /// Reserved for image-to-image conditioning (OQ-7). Always null in v1.
    /// </summary>
    ImageInitReference? Init = null,
    string WorkflowKey = "single-analyzer",
    string AnalysisType = "content-analysis",
    string StepKey = "generate-image",
    string? OperationKey = null,
    string? NetworkKey = null);

/// <summary>
/// Bytes-only image generation result.
/// The provider key and optional usage data enable the usage-ledger decorator to record cost.
/// </summary>
public sealed record ImageGenerationResult(
    byte[] Bytes,
    /// <summary>MIME type of the returned bytes, e.g. "image/png".</summary>
    string Mime,
    string ProviderKey,
    string? Model,
    ImageUsage? Usage = null);

/// <summary>
/// Reserved slot for image-to-image reference conditioning (OQ-7).
/// Not used in v1 — always passed as null. The deferred follow-up plugs in without a contract change.
/// </summary>
public sealed record ImageInitReference(
    byte[]? Bytes,
    string? Url,
    string Mime);

/// <summary>
/// Image usage counters for the usage-ledger decorator.
/// </summary>
public sealed record ImageUsage(
    int ImageCount,
    long? OutputBytes = null);
