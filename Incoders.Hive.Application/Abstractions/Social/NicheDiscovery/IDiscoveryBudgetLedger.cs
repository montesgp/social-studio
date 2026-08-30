namespace Incoders.Hive.Application.Abstractions.Social.NicheDiscovery;

/// <summary>
/// Run-scoped, in-memory, pure counter contract (design §5.3). Decrements BEFORE each provider call
/// and reports when the remaining budget cannot cover the next one. The concrete implementation lands
/// with the origin adapter (WU-5); this contract is what lets the chain be TDD'd against it now.
/// </summary>
public interface IDiscoveryBudgetLedger
{
    int RemainingProviderUnits { get; }

    int RemainingAiCompletions { get; }

    bool CanAfford(int providerUnits, int aiCompletions = 0);

    void Spend(int providerUnits, int aiCompletions = 0);
}
