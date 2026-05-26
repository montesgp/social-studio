using System.Diagnostics;
using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.Observability;
using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Infrastructure.Observability;

/// <summary>
/// Wraps <see cref="IAiCompletionGateway"/> and writes one <see cref="UsageEvent"/>
/// per logical completion call (after any internal retries inside the gateway).
///
/// DECORATOR ORDER CONTRACT: This decorator sits OUTSIDE retry/fallback logic,
/// so each logical call → one ledger event with the correct retry count.
/// For this codebase, retry/fallback is handled inside AiCompletionGateway
/// via provider-level routing — this decorator records the final outcome.
/// </summary>
public sealed class AiCompletionGatewayInstrumentationDecorator : IAiCompletionGateway
{
    private readonly IAiCompletionGateway _inner;
    private readonly IUsageEventLedger _ledger;
    private readonly ProviderCallContext _callContext;

    public AiCompletionGatewayInstrumentationDecorator(
        IAiCompletionGateway inner,
        IUsageEventLedger ledger,
        ProviderCallContext callContext)
    {
        _inner = inner;
        _ledger = ledger;
        _callContext = callContext;
    }

    public async Task<Result<AiJsonCompletionResponse<TResponse>>> CompleteJsonAsync<TResponse>(
        AiJsonCompletionRequest request,
        CancellationToken cancellationToken = default)
        where TResponse : notnull
    {
        var context = _callContext.Current
            ?? throw new InvalidOperationException(
                "ProviderCallContext.Current is null. The orchestrator must call ProviderCallContext.Begin() " +
                "before any provider call. This is a programming error, not a runtime fallback.");

        var requestedAt = DateTime.UtcNow;
        var stopwatch = Stopwatch.StartNew();

        var result = await _inner.CompleteJsonAsync<TResponse>(request, cancellationToken);

        stopwatch.Stop();
        var completedAt = DateTime.UtcNow;

        string status;
        string? failureCode = null;
        string? providerKey = request.PreferredProviderKey ?? "unknown";
        string? model = request.PreferredModel;
        int? inputTokens = null;
        int? outputTokens = null;

        if (result.IsSuccess)
        {
            status = "succeeded";
            providerKey = result.Value.ProviderKey;
            model = result.Value.Model ?? model;

            if (result.Value.TokenUsage is { } usage)
            {
                inputTokens = usage.InputTokens;
                outputTokens = usage.OutputTokens;

                if (usage.InputTokens is null && usage.OutputTokens is null)
                {
                    status = "tokens-missing";
                }
            }
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
            InputTokens: inputTokens,
            OutputTokens: outputTokens,
            EstimatedCostUsd: null, // enriched by the ledger
            RequestSizeBytes: null,
            ResponseSizeBytes: null,
            RetryCount: 0,
            Status: status,
            FailureCode: failureCode,
            CorrelationId: context.CorrelationId);

        await _ledger.RecordAsync(evt, cancellationToken);

        return result;
    }
}
