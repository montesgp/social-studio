using Incoders.Hive.Domain.Social.NicheDiscovery;

namespace Incoders.Hive.Tests.Domain.Social.NicheDiscovery;

public sealed class DiscoveryWindowTests
{
    [Theory]
    [InlineData(7)]
    [InlineData(90)]
    [InlineData(365)]
    public void Create_WithValueInRange_Succeeds(int days)
    {
        var result = DiscoveryWindow.Create(days);

        Assert.True(result.IsSuccess);
        Assert.Equal(days, result.Value.Days);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(366)]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_WithValueOutOfRange_Fails(int days)
    {
        var result = DiscoveryWindow.Create(days);

        Assert.True(result.IsFailure);
        Assert.Equal("social.niche_discovery.window_out_of_range", result.Error.Code);
    }

    [Fact]
    public void Default_Is90Days()
    {
        var window = DiscoveryWindow.Default();

        Assert.Equal(90, window.Days);
    }
}
