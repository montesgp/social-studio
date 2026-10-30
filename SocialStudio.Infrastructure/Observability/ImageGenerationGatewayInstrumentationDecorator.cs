using System.Diagnostics;
using SocialStudio.Application.Abstractions.Ai;
using SocialStudio.Application.Abstractions.Observability;
using SocialStudio.Domain.Common;
// WU-9: alias removed — IImageGenerationClient retired; ImageGenerationRequest is unambiguous now.

namespace SocialStudio.Infrastructure.Observability;

/// <summary>
/// Wraps <see cref="IImageGenerationGateway"/> and writes one <see cref="UsageEvent"/>
/// per logical image-generation call (success or failure).
///
/// DECORATOR ORDER CONTRACT: mirrors <see cref="AiCompletionGatewayInstrumentationDecorator"/>.
/// Requires an active <see cref="ProviderCallContext.Current"/> — throws if absent (programming error).
/// Refs: IMG-02; design §2; tasks 5.4.
/// </summary>
public sealed class ImageGenerationGatewayInstrumentationDecorator : IImageGenerationGateway
{
    private readonly IImageGenerationGateway _inner;
    private readonly IUsageEventLedger _ledger;
    private readonly ProviderCallContext _callContext;

    public ImageGenerationGatewayInstrumentationDecorator(
        IImageGenerationGateway inner,
        IUsageEventLedger ledger,
        ProviderCallContext callContext)
    {
        _inner = inner;
        _ledger = ledger;
        _callContext = callContext;
    }

    /// <summary>
    /// Pass-through to the inner gateway — DescribeAsync does not make a provider call,
    /// so there is no usage event to record.
    /// </summary>
    public Task<Result<ImageModelCapabilities>> DescribeAsync(
        Guid organizationId,
        CancellationToken ct = default)
        => _inner.DescribeAsync(organizationId, ct);

    public async Task<Result<ImageGenerationResult>> GenerateAsync(
        ImageGenerationRequest request,
        CancellationToken ct = default)
    {
        var context = _callContext.Current
            ?? throw new InvalidOperationException(
                "ProviderCallContext.Current is null. The orchestrator must call ProviderCallContext.Begin() " +
                "before any provider call. This is a programming error, not a runtime fallback.");

        var requestedAt = DateTime.UtcNow;
        var stopwatch = Stopwatch.StartNew();

        var result = await _inner.GenerateAsync(request, ct);

        stopwatch.Stop();
        var completedAt = DateTime.UtcNow;

        string status;
        string? failureCode = null;
        string providerKey = request.PreferredProviderKey ?? "unknown";
        string? model = request.PreferredModel;

        if (result.IsSuccess)
        {
            status = "succeeded";
            providerKey = result.Value.ProviderKey;
            model = result.Value.Model ?? model;
        }
        else
        {
            status = "failed";
            failureCode = result.Error.Code;
        }

        var evt = new UsageEvent(
            OrganizationId: context.OrganizationId,
            ClientId: context.ClientId,
            WorkflowKey: context.WorkflowKey,
            PipelineStepKey: context.PipelineStepKey,
            ProviderKey: providerKey,
            Model: model,
            PromptVersion: context.PromptVersion,
            RequestTimestamp: requestedAt,
            CompletedTimestamp: completedAt,
            DurationMs: (int)stopwatch.ElapsedMilliseconds,
            InputTokens: null,       // Image generation has no token usage
            OutputTokens: null,
            EstimatedCostUsd: null,  // Enriched by the ledger
            RequestSizeBytes: null,
            ResponseSizeBytes: result.IsSuccess ? (int?)result.Value.Bytes.Length : null,
            RetryCount: 0,
            Status: status,
            FailureCode: failureCode,
            CorrelationId: context.CorrelationId);

        await _ledger.RecordAsync(evt, ct);

        return result;
    }
}
