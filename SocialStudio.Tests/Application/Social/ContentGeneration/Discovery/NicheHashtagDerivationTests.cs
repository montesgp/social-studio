using SocialStudio.Application.Features.Social.ContentGeneration.Discovery;

namespace SocialStudio.Tests.Application.Social.ContentGeneration.Discovery;

/// <summary>
/// A client's niche is FREE TEXT ("Medical &amp; health"), not a hashtag. Written RED first against the
/// real failure: the previous normalisation only removed whitespace, so it searched
/// <c>#Medical&amp;health</c> and the actor answered <c>NOT_FOUND</c>.
/// </summary>
public sealed class NicheHashtagDerivationTests
{
    [Fact]
    public void Derive_EmitsEachTokenPlusTheFullConcatenation()
    {
        // The exact failing production input.
        var hashtags = NicheHashtagDerivation.Derive("Medical & health");

        Assert.Equal(["medical", "health", "medicalhealth"], hashtags);
    }

    [Fact]
    public void Derive_StripsDiacritics_AndDropsSpanishConnectors()
    {
        var hashtags = NicheHashtagDerivation.Derive("Salud & Nutrición");

        Assert.Equal(["salud", "nutricion", "saludnutricion"], hashtags);
    }

    [Fact]
    public void Derive_DropsEnglishAndSpanishStopwords()
    {
        var hashtags = NicheHashtagDerivation.Derive("the art of fitness for la vida");

        Assert.Equal(["art", "fitness", "vida", "artfitnessvida"], hashtags);
    }

    [Fact]
    public void Derive_StripsALeadingHash_FromAnAlreadyHashtagInput()
    {
        // Already a single hashtag: the concatenation equals the only token, so it is deduplicated away.
        Assert.Equal(["fitness"], NicheHashtagDerivation.Derive("#fitness"));
    }

    [Fact]
    public void Derive_ReturnsEmpty_WhenNothingSurvivesTheStopwordFilter()
    {
        Assert.Empty(NicheHashtagDerivation.Derive("the and of & para con"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("& - /")]
    public void Derive_ReturnsEmpty_ForBlankOrPunctuationOnlyInput(string? term)
    {
        Assert.Empty(NicheHashtagDerivation.Derive(term));
    }

    [Fact]
    public void Derive_DropsTokensShorterThanTwoCharacters()
    {
        // "b" is a single character — not a searchable hashtag on its own, but it still belongs to the
        // concatenation because it is part of the phrase the client wrote.
        var hashtags = NicheHashtagDerivation.Derive("plan b coaching");

        Assert.Equal(["plan", "coaching", "planbcoaching"], hashtags);
    }

    [Fact]
    public void Derive_CapsTheNumberOfHashtagsSentInOneCall()
    {
        var hashtags = NicheHashtagDerivation.Derive("alpha bravo charlie delta echo foxtrot golf");

        Assert.Equal(NicheHashtagDerivation.MaxHashtags, hashtags.Count);
        Assert.Equal(["alpha", "bravo", "charlie", "delta", "echo"], hashtags);
    }

    [Fact]
    public void Derive_DeduplicatesWhileKeepingTheOriginalOrder()
    {
        var hashtags = NicheHashtagDerivation.Derive("health & Health care");

        Assert.Equal(["health", "care", "healthhealthcare"], hashtags);
    }

    [Fact]
    public void Derive_RemovesEveryCharacterThatIsNotALetterOrDigit()
    {
        var hashtags = NicheHashtagDerivation.Derive("e-commerce (B2B)!");

        Assert.Equal(["ecommerce", "b2b", "ecommerceb2b"], hashtags);
    }
}
