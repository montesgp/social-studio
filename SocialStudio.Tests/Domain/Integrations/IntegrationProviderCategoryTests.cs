using SocialStudio.Domain.Integrations;

namespace SocialStudio.Tests.Domain.Integrations;

/// <summary>
/// WU-4: <see cref="IntegrationProviderCategory"/> is persisted as an int, so a new member must be
/// APPENDED — never renumbered or inserted — or every stored provider silently re-categorises.
/// </summary>
public sealed class IntegrationProviderCategoryTests
{
    [Fact]
    public void ExistingCategoryValues_AreUnchanged()
    {
        Assert.Equal(0, (int)IntegrationProviderCategory.AiModel);
        Assert.Equal(1, (int)IntegrationProviderCategory.Design);
        Assert.Equal(2, (int)IntegrationProviderCategory.DataExtraction);
        Assert.Equal(3, (int)IntegrationProviderCategory.Publishing);
        Assert.Equal(4, (int)IntegrationProviderCategory.MediaGeneration);
    }

    /// <summary>
    /// <c>OfficialDataApi</c> (value 5) is RETIRED: it only ever held the YouTube Data API, while every
    /// other data source — Apify, Data365 — sat under <see cref="IntegrationProviderCategory.DataExtraction"/>.
    /// One provider in a category of its own is a taxonomy that splits equivalent things. Value 5 must
    /// stay unused rather than be recycled, or stored rows naming it would silently re-categorise.
    /// </summary>
    [Fact]
    public void RetiredValueFive_IsNotReused()
    {
        Assert.False(
            Enum.IsDefined(typeof(IntegrationProviderCategory), 5),
            "Value 5 is retired (was OfficialDataApi) and must not be reused by a new member.");
    }

    [Fact]
    public void EveryDataSourceProvider_SharesOneCategory()
    {
        Assert.False(Enum.TryParse<IntegrationProviderCategory>("OfficialDataApi", out _));
    }
}
