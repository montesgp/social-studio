using Incoders.Hive.Application.Abstractions.Social.NicheDiscovery;
using Incoders.Hive.Domain.Social.NicheDiscovery;

namespace Incoders.Hive.Tests.Application.Abstractions.Social.NicheDiscovery;

public sealed class DiscoveryBudgetTests
{
    [Theory]
    [InlineData(DiscoveryDepth.Quick, 400, 5)]
    [InlineData(DiscoveryDepth.Standard, 1000, 15)]
    [InlineData(DiscoveryDepth.Deep, 2500, 40)]
    public void ForDepth_ReturnsTheCeilingTableFromDesignSection5Point3(
        DiscoveryDepth depth, int expectedProviderUnits, int expectedAiCompletions)
    {
        var budget = DiscoveryBudget.ForDepth(depth);

        Assert.Equal(expectedProviderUnits, budget.ProviderUnits);
        Assert.Equal(expectedAiCompletions, budget.AiCompletions);
    }

    [Fact]
    public void Ledger_DecrementsBeforeEachSpend()
    {
        var budget = DiscoveryBudget.ForDepth(DiscoveryDepth.Quick);
        IDiscoveryBudgetLedger ledger = new FakeDiscoveryBudgetLedger(budget);

        ledger.Spend(100, 1);

        Assert.Equal(300, ledger.RemainingProviderUnits);
        Assert.Equal(4, ledger.RemainingAiCompletions);
    }

    [Fact]
    public void Ledger_ReportsWhenRemainingUnitsCannotCoverTheNextCall()
    {
        var budget = DiscoveryBudget.ForDepth(DiscoveryDepth.Quick);
        IDiscoveryBudgetLedger ledger = new FakeDiscoveryBudgetLedger(budget);

        ledger.Spend(390, 0);

        Assert.True(ledger.CanAfford(10, 0));
        Assert.False(ledger.CanAfford(11, 0));
    }

    /// <summary>
    /// Minimal reference double exercising the <see cref="IDiscoveryBudgetLedger"/> contract shape.
    /// The concrete production implementation lands with the adapter in WU-5 (task 3.6 note).
    /// </summary>
    private sealed class FakeDiscoveryBudgetLedger : IDiscoveryBudgetLedger
    {
        public FakeDiscoveryBudgetLedger(DiscoveryBudget budget)
        {
            RemainingProviderUnits = budget.ProviderUnits;
            RemainingAiCompletions = budget.AiCompletions;
        }

        public int RemainingProviderUnits { get; private set; }

        public int RemainingAiCompletions { get; private set; }

        public bool CanAfford(int providerUnits, int aiCompletions = 0) =>
            providerUnits <= RemainingProviderUnits && aiCompletions <= RemainingAiCompletions;

        public void Spend(int providerUnits, int aiCompletions = 0)
        {
            RemainingProviderUnits -= providerUnits;
            RemainingAiCompletions -= aiCompletions;
        }
    }
}
