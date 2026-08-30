using Incoders.Hive.Application.Abstractions.Social.NicheDiscovery;

namespace Incoders.Hive.Application.Features.Social.NicheDiscovery.DiscoveryRuns;

/// <summary>
/// Run-scoped, in-memory, pure counter (design §5.3) — the Application-layer twin of Infrastructure's
/// internal <c>YouTubeQuotaBudget</c>. That type is <c>internal</c> to Infrastructure and cannot be
/// referenced from here, so <see cref="NicheDiscoveryRunProcessor"/> owns its own instance of this same
/// counter to thread the run's total ceiling across every stage call in
/// <see cref="NicheDiscoverySourceChain"/> (each origin adapter still decrements ITS OWN internal
/// ledger, built from the per-call <see cref="DiscoveryBudget"/> snapshot this ledger hands out).
/// </summary>
public sealed class RunDiscoveryBudgetLedger : IDiscoveryBudgetLedger
{
    private int _remainingProviderUnits;
    private int _remainingAiCompletions;

    public RunDiscoveryBudgetLedger(DiscoveryBudget budget)
    {
        ArgumentNullException.ThrowIfNull(budget);
        _remainingProviderUnits = budget.ProviderUnits;
        _remainingAiCompletions = budget.AiCompletions;
    }

    public int RemainingProviderUnits => _remainingProviderUnits;

    public int RemainingAiCompletions => _remainingAiCompletions;

    public bool CanAfford(int providerUnits, int aiCompletions = 0) =>
        providerUnits <= _remainingProviderUnits && aiCompletions <= _remainingAiCompletions;

    public void Spend(int providerUnits, int aiCompletions = 0)
    {
        _remainingProviderUnits = Math.Max(0, _remainingProviderUnits - providerUnits);
        _remainingAiCompletions = Math.Max(0, _remainingAiCompletions - aiCompletions);
    }
}
