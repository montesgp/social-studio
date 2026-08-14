using Incoders.Hive.Domain.Social.NicheDiscovery.Taxonomy;
using Incoders.Hive.Domain.Social.Niches;

namespace Incoders.Hive.Tests.Domain.Social.NicheDiscovery.Taxonomy;

public sealed class SubNicheRefTests
{
    [Fact]
    public void Construction_CrossReferencesNicheIdByValueOnly()
    {
        var nicheId = NicheId.New();

        var subNiche = new SubNicheRef(nicheId, "Home Gym Equipment Reviews");

        Assert.Equal(nicheId, subNiche.NicheId);
        Assert.Equal("Home Gym Equipment Reviews", subNiche.Label);
    }

    [Fact]
    public void SubNicheRef_HasNoNavigationPropertyToNiche()
    {
        // Structural guard: SubNicheRef must expose NicheId (Guid-backed value type), never a Niche
        // navigation property — the two catalogs are deliberately parallel (settled decision 2).
        var properties = typeof(SubNicheRef).GetProperties();

        Assert.DoesNotContain(properties, p => p.PropertyType == typeof(Niche));
    }
}
