using System.Reflection;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.NicheDiscovery;

namespace Incoders.Hive.Tests.Domain.Social.NicheDiscovery;

public sealed class NicheDiscoveryErrorsTests
{
    [Fact]
    public void AllErrorFields_FollowTheNicheDiscoveryCodeAndMessageKeyConvention()
    {
        var fields = typeof(NicheDiscoveryErrors)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(Error))
            .ToList();

        Assert.NotEmpty(fields);

        foreach (var field in fields)
        {
            var error = (Error)field.GetValue(null)!;

            Assert.StartsWith("social.niche_discovery.", error.Code, StringComparison.Ordinal);
            Assert.StartsWith("social.niche_discovery.errors.", error.MessageKey, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("MarketUnknown", "social.niche_discovery.market_unknown")]
    [InlineData("MarketInactive", "social.niche_discovery.market_inactive")]
    [InlineData("WindowOutOfRange", "social.niche_discovery.window_out_of_range")]
    [InlineData("DepthUnknown", "social.niche_discovery.depth_unknown")]
    [InlineData("ContentFormatInvalid", "social.niche_discovery.content_format_invalid")]
    [InlineData("NetworkUnsupported", "social.niche_discovery.network_unsupported")]
    [InlineData("OriginUnconfigured", "social.niche_discovery.origin_unconfigured")]
    [InlineData("ProviderQuotaExhausted", "social.niche_discovery.provider_quota_exhausted")]
    [InlineData("ProviderUnavailable", "social.niche_discovery.provider_unavailable")]
    [InlineData("RunNotFound", "social.niche_discovery.run_not_found")]
    [InlineData("RunAlreadyRunning", "social.niche_discovery.run_already_running")]
    [InlineData("OpportunityNotFound", "social.niche_discovery.opportunity_not_found")]
    public void SpecificErrorCodes_MatchDesignSection10(string fieldName, string expectedCode)
    {
        var field = typeof(NicheDiscoveryErrors).GetField(fieldName, BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(field);

        var error = (Error)field!.GetValue(null)!;

        Assert.Equal(expectedCode, error.Code);
    }
}
