using SocialStudio.Application.Abstractions.Social.NicheDiscovery;
using SocialStudio.Domain.Social.NicheDiscovery;
using SocialStudio.Infrastructure.Social.NicheDiscovery.Origins;

namespace SocialStudio.Tests.Infrastructure.Social.NicheDiscovery;

/// <summary>
/// WU-5 (design §5.3, N9): depth -> ceiling table; the ledger stops BEFORE an uncoverable call rather
/// than throwing mid-call. The 403 quotaExceeded -> Failed/PartialReason.ProviderBudgetExhausted
/// mapping is exercised end-to-end against the client+source in
/// <c>YouTubeDataApiNicheDiscoverySourceTests</c> (5.8/5.9), not duplicated here — this file stays
/// scoped to the PURE counter contract.
/// </summary>
public sealed class YouTubeQuotaBudgetTests
{
    [Theory]
    [InlineData(DiscoveryDepth.Quick, 400, 5)]
    [InlineData(DiscoveryDepth.Standard, 1_000, 15)]
    [InlineData(DiscoveryDepth.Deep, 2_500, 40)]
    public void ForDepth_MatchesTheCeilingTable(DiscoveryDepth depth, int expectedUnits, int expectedCompletions)
    {
        var ledger = new YouTubeQuotaBudget(DiscoveryBudget.ForDepth(depth));

        Assert.Equal(expectedUnits, ledger.RemainingProviderUnits);
        Assert.Equal(expectedCompletions, ledger.RemainingAiCompletions);
    }

    [Fact]
    public void Spend_DecrementsBeforeTheCall_NeverAfter()
    {
        var ledger = new YouTubeQuotaBudget(DiscoveryBudget.ForDepth(DiscoveryDepth.Quick));

        ledger.Spend(100);

        Assert.Equal(300, ledger.RemainingProviderUnits);
    }

    [Fact]
    public void CanAfford_StopsBeforeAnUncoverableCall()
    {
        var ledger = new YouTubeQuotaBudget(DiscoveryBudget.ForDepth(DiscoveryDepth.Quick));

        ledger.Spend(300);
        ledger.Spend(90);

        Assert.False(ledger.CanAfford(100));
        Assert.True(ledger.CanAfford(10));
    }

    [Fact]
    public void Spend_WhenUncoverable_ThrowsRatherThanSilentlyOverspending()
    {
        var ledger = new YouTubeQuotaBudget(DiscoveryBudget.ForDepth(DiscoveryDepth.Quick));
        ledger.Spend(400);

        Assert.Throws<InvalidOperationException>(() => ledger.Spend(1));
    }

    [Fact]
    public void CanAfford_TracksAiCompletionsIndependentlyOfProviderUnits()
    {
        var ledger = new YouTubeQuotaBudget(DiscoveryBudget.ForDepth(DiscoveryDepth.Quick));

        ledger.Spend(0, aiCompletions: 5);

        Assert.Equal(0, ledger.RemainingAiCompletions);
        Assert.False(ledger.CanAfford(0, aiCompletions: 1));
        Assert.True(ledger.CanAfford(1));
    }
}
