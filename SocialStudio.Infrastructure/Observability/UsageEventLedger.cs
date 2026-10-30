using SocialStudio.Application.Abstractions.Observability;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SocialStudio.Infrastructure.Observability;

/// <summary>
/// Safe implementation of <see cref="IUsageEventLedger"/> that wraps all storage
/// failures in a try/catch. A ledger write failure MUST NOT propagate into the
/// calling flow — it is logged as a warning and silently swallowed.
/// </summary>
public sealed class UsageEventLedger : IUsageEventLedger
{
    private readonly IUsageEventRepository _repository;
    private readonly IProviderCostRateLookup _costRates;
    private readonly ILogger<UsageEventLedger> _logger;

    public UsageEventLedger(
        IUsageEventRepository repository,
        IProviderCostRateLookup costRates,
        ILogger<UsageEventLedger>? logger = null)
    {
        _repository = repository;
        _costRates = costRates;
        _logger = logger ?? NullLogger<UsageEventLedger>.Instance;
    }

    public async Task RecordAsync(UsageEvent evt, CancellationToken cancellationToken)
    {
        try
        {
            // Enrich cost if the event doesn't already have one and tokens are present.
            var enrichedEvent = await EnrichCostAsync(evt, cancellationToken);
            await _repository.AddAsync(enrichedEvent, cancellationToken);
        }
        catch (Exception ex)
        {
            // Ledger failures must never surface to the caller.
            _logger.LogWarning(
                ex,
                "usage-ledger.write-failed: org={OrgId} workflow={WorkflowKey} step={StepKey} provider={ProviderKey} status={Status}",
                evt.OrganizationId,
                evt.WorkflowKey,
                evt.PipelineStepKey,
                evt.ProviderKey,
                evt.Status);
        }
    }

    private async Task<UsageEvent> EnrichCostAsync(UsageEvent evt, CancellationToken cancellationToken)
    {
        // If cost was already computed by the caller, respect it.
        if (evt.EstimatedCostUsd.HasValue) return evt;

        // No tokens — cannot compute token-based cost; fall through to per-request rate.
        var hasTokens = evt.InputTokens.HasValue || evt.OutputTokens.HasValue;

        try
        {
            var rate = await _costRates.GetRateAsync(
                evt.ProviderKey,
                evt.Model,
                evt.RequestTimestamp,
                cancellationToken);

            if (rate is null)
            {
                _logger.LogInformation(
                    "usage-ledger.rate-miss: no cost rate for provider={ProviderKey} model={Model} — estimated_cost_usd will be null",
                    evt.ProviderKey,
                    evt.Model ?? "(none)");
                return evt;
            }

            decimal? cost = null;

            if (hasTokens && (rate.CostPer1KInputTokens.HasValue || rate.CostPer1KOutputTokens.HasValue))
            {
                var inputCost = (evt.InputTokens ?? 0) / 1000m * (rate.CostPer1KInputTokens ?? 0m);
                var outputCost = (evt.OutputTokens ?? 0) / 1000m * (rate.CostPer1KOutputTokens ?? 0m);
                cost = inputCost + outputCost;
            }
            else if (rate.CostPerRequest.HasValue)
            {
                cost = rate.CostPerRequest;
            }

            return evt with { EstimatedCostUsd = cost };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "usage-ledger.cost-lookup-failed for provider={ProviderKey}", evt.ProviderKey);
            return evt;
        }
    }
}
