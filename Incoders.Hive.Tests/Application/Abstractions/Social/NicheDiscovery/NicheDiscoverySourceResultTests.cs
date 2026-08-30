using Incoders.Hive.Application.Abstractions.Social.NicheDiscovery;

namespace Incoders.Hive.Tests.Application.Abstractions.Social.NicheDiscovery;

public sealed class NicheDiscoverySourceResultTests
{
    [Fact]
    public void Ran_MayCarryEmptyEvidence_AsASuccess()
    {
        var result = NicheDiscoverySourceResult.Ran([], [], new BudgetSpend(10, 0));

        Assert.Equal(NicheSourceOutcome.Ran, result.Outcome);
        Assert.Empty(result.Channels);
        Assert.Empty(result.Videos);
        Assert.Null(result.FailureReasonKey);
    }

    [Fact]
    public void Ran_WithPartialReason_CarriesIt()
    {
        var result = NicheDiscoverySourceResult.Ran(
            [], [], new BudgetSpend(400, 0), PartialReason.ProviderBudgetExhausted);

        Assert.Equal(NicheSourceOutcome.Ran, result.Outcome);
        Assert.Equal(PartialReason.ProviderBudgetExhausted, result.Partial);
    }

    [Fact]
    public void Unconfigured_CarriesEmptyEvidenceListsByConstruction()
    {
        var result = NicheDiscoverySourceResult.Unconfigured();

        Assert.Equal(NicheSourceOutcome.Unconfigured, result.Outcome);
        Assert.Empty(result.Channels);
        Assert.Empty(result.Videos);
        Assert.Null(result.FailureReasonKey);
    }

    [Fact]
    public void Failed_CarriesEmptyEvidenceListsByConstructionAndAFailureReasonKey()
    {
        var result = NicheDiscoverySourceResult.Failed("social.niche_discovery.provider_quota_exhausted");

        Assert.Equal(NicheSourceOutcome.Failed, result.Outcome);
        Assert.Empty(result.Channels);
        Assert.Empty(result.Videos);
        Assert.Equal("social.niche_discovery.provider_quota_exhausted", result.FailureReasonKey);
    }
}
