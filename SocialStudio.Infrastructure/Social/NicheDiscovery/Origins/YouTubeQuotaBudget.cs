using SocialStudio.Application.Abstractions.Social.NicheDiscovery;

namespace SocialStudio.Infrastructure.Social.NicheDiscovery.Origins;

/// <summary>
/// Run-scoped, in-memory, pure counter (design §5.3): decrements BEFORE each provider call and reports
/// when the remaining budget cannot cover the next one. Constructed from
/// <see cref="DiscoveryBudget.ForDepth"/> — the depth->ceiling table (Quick 400/Standard 1,000/Deep
/// 2,500 provider units) lives there, not duplicated here.
/// </summary>
internal sealed class YouTubeQuotaBudget : IDiscoveryBudgetLedger
{
    private int _remainingProviderUnits;
    private int _remainingAiCompletions;

    internal YouTubeQuotaBudget(DiscoveryBudget budget)
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
        if (!CanAfford(providerUnits, aiCompletions))
        {
            throw new InvalidOperationException(
                $"Cannot spend {providerUnits} provider units / {aiCompletions} AI completions: only " +
                $"{_remainingProviderUnits}/{_remainingAiCompletions} remain. Callers MUST check " +
                $"{nameof(CanAfford)} before every call.");
        }

        _remainingProviderUnits -= providerUnits;
        _remainingAiCompletions -= aiCompletions;
    }
}
