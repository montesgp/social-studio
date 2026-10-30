using SocialStudio.Domain.Common;

namespace SocialStudio.Application.Abstractions.Ai;

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

    /// <summary>
    /// Returns the image-conditioning capability of the IMAGE route configured for
    /// <paramref name="organizationId"/> WITHOUT making a provider call.
    /// The background service calls this ONCE per job to decide whether to pass
    /// reference-image handles or rely on the descriptor prompt (OQ-4).
    /// Refs: CAC-IMG-01; design §3.4.
    /// </summary>
    Task<Result<ImageModelCapabilities>> DescribeAsync(
        Guid organizationId,
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

    /// <summary>
    /// Declarative capability advertisement — never triggers a provider call.
    /// Supporting adapters return <c>SupportsImageConditioning = true</c>;
    /// non-supporting adapters return <c>false</c> and ignore
    /// <see cref="ImageGenerationRequest.ReferenceImages"/>.
    /// Refs: CAC-IMG-01; design §3.4.
    /// </summary>
    ImageModelCapabilities Capabilities { get; }
}

/// <summary>
/// Capability record for image conditioning, advertised per adapter.
/// Pure Application type — no provider, HTTP, or EF types.
/// Refs: CAC-IMG-01; design §3.4.
/// </summary>
public sealed record ImageModelCapabilities(
    /// <summary>True when the adapter can accept reference images for conditioning.</summary>
    bool SupportsImageConditioning,
    /// <summary>Maximum number of reference images the adapter will forward to the provider.</summary>
    int MaxConditioningImages);

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
    string? NetworkKey = null,
    /// <summary>
    /// Reference images for multi-image conditioning (OQ-4 realised).
    /// Application layer passes Url handles only; the adapter resolves bytes from storage.
    /// Null when the provider does not support conditioning or no reference images are available.
    /// </summary>
    IReadOnlyList<ImageInitReference>? ReferenceImages = null,
    /// <summary>
    /// When true the adapter must inject the background-only system directive before calling
    /// the provider: "generate ONLY the background; never render text, logos, @handles, or signatures".
    /// Set by <c>ClientAestheticImageBackgroundService</c> for ClientAesthetic creative slides (CAC-IMG-02).
    /// </summary>
    bool BackgroundOnly = false,
    /// <summary>
    /// Telemetry correlation anchor for per-slide attempt rows (CAC-INT-01).
    /// Set to <c>FormatResultId</c> by <c>ClientAestheticImageBackgroundService</c>.
    /// Passed through to <see cref="IntegrationExecutionRequest"/> so the policy
    /// persists an attempt row even when <c>AnalysisId</c> is null.
    /// </summary>
    Guid? CorrelationId = null,
    /// <summary>
    /// Human-readable correlation kind, e.g. <c>"content-generation/generate-image"</c>.
    /// Paired with <see cref="CorrelationId"/>.
    /// </summary>
    string? CorrelationKind = null);

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
