using SocialStudio.Domain.Social.NicheDiscovery;

namespace SocialStudio.Tests.Domain.Social.NicheDiscovery;

public sealed class MarketCatalogEntryTests
{
    [Fact]
    public void Create_WithValidValues_PersistsCodeDisplayNameKeyLanguageAndActiveFlag()
    {
        var code = MarketCode.TryParse("es-AR").Value;

        var result = MarketCatalogEntry.Create(code, "social.markets.es_ar", "es", isActive: true);

        Assert.True(result.IsSuccess);
        Assert.Equal(code, result.Value.Code);
        Assert.Equal("social.markets.es_ar", result.Value.DisplayNameKey);
        Assert.Equal("es", result.Value.PrimaryLanguage);
        Assert.True(result.Value.IsActive);
    }

    [Fact]
    public void Create_AssignsANewMarketId()
    {
        var code = MarketCode.TryParse("pt-BR").Value;

        var result = MarketCatalogEntry.Create(code, "social.markets.pt_br", "pt", isActive: true);

        Assert.True(result.IsSuccess);
        Assert.NotEqual(Guid.Empty, result.Value.Id.Value);
    }

    [Fact]
    public void Create_WithEmptyDisplayNameKey_Fails()
    {
        var code = MarketCode.TryParse("es-AR").Value;

        var result = MarketCatalogEntry.Create(code, "  ", "es", isActive: true);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Create_WithEmptyPrimaryLanguage_Fails()
    {
        var code = MarketCode.TryParse("es-AR").Value;

        var result = MarketCatalogEntry.Create(code, "social.markets.es_ar", "  ", isActive: true);

        Assert.True(result.IsFailure);
    }
}
