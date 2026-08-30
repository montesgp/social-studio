using Incoders.Hive.Domain.Social.NicheDiscovery.Taxonomy;
using Incoders.Hive.Domain.Social.Niches;

namespace Incoders.Hive.Tests.Domain.Social.NicheDiscovery.Taxonomy;

public sealed class NicheTaxonomyNodeTests
{
    [Fact]
    public void Construction_CrossReferencesNicheIdByValueWithItsSubNiches()
    {
        var nicheId = NicheId.New();
        var subNiches = new[]
        {
            new SubNicheRef(nicheId, "Home Gym Equipment Reviews"),
            new SubNicheRef(nicheId, "Budget Home Gym Setups"),
        };

        var node = new NicheTaxonomyNode(nicheId, subNiches);

        Assert.Equal(nicheId, node.NicheId);
        Assert.Equal(2, node.SubNiches.Count);
    }

    [Fact]
    public void Construction_WithNoSubNiches_IsValid()
    {
        var nicheId = NicheId.New();

        var node = new NicheTaxonomyNode(nicheId, []);

        Assert.Empty(node.SubNiches);
    }
}
