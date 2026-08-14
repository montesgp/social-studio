using Incoders.Hive.Domain.Social.NicheDiscovery;

namespace Incoders.Hive.Tests.Domain.Social.NicheDiscovery;

public sealed class DiscoveryRunIdTests
{
    [Fact]
    public void New_ProducesNonEmptyGuid()
    {
        var id = DiscoveryRunId.New();

        Assert.NotEqual(Guid.Empty, id.Value);
    }

    [Fact]
    public void New_ProducesDistinctValuesEachCall()
    {
        var first = DiscoveryRunId.New();
        var second = DiscoveryRunId.New();

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Equality_IsValueBased()
    {
        var value = Guid.NewGuid();

        var first = new DiscoveryRunId(value);
        var second = new DiscoveryRunId(value);

        Assert.Equal(first, second);
        Assert.True(first == second);
    }
}
