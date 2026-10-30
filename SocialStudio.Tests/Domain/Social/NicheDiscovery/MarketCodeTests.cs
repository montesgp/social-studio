using SocialStudio.Domain.Social.NicheDiscovery;

namespace SocialStudio.Tests.Domain.Social.NicheDiscovery;

public sealed class MarketCodeTests
{
    [Theory]
    [InlineData("es-AR", "es", "es-AR")]
    [InlineData("pt-BR", "pt", "pt-BR")]
    [InlineData("en-US", "en", "en-US")]
    [InlineData("Es-ar", "es", "es-AR")]
    public void TryParse_WithValidMarketCode_NormalizesLanguageAndRegion(
        string raw, string expectedLanguage, string expectedValue)
    {
        var result = MarketCode.TryParse(raw);

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedLanguage, result.Value.Language);
        Assert.Equal(expectedValue, result.Value.Value);
    }

    [Theory]
    [InlineData("ARG")]
    [InlineData("es_ar")]
    [InlineData("")]
    [InlineData("es")]
    [InlineData("es-argentina")]
    public void TryParse_WithMalformedInput_YieldsValidationFailureNeverThrows(string raw)
    {
        var result = MarketCode.TryParse(raw);

        Assert.True(result.IsFailure);
        Assert.Equal("social.niche_discovery.market_code_invalid", result.Error.Code);
    }

    [Fact]
    public void TryParse_WithNull_YieldsValidationFailureNeverThrows()
    {
        var result = MarketCode.TryParse(null!);

        Assert.True(result.IsFailure);
    }
}
