using Incoders.Hive.Domain.Integrations;

namespace Incoders.Hive.Tests.Domain.Integrations;

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

    [Fact]
    public void OfficialDataApi_IsAppended_AsValueFive()
    {
        Assert.Equal(5, (int)IntegrationProviderCategory.OfficialDataApi);
    }
}
