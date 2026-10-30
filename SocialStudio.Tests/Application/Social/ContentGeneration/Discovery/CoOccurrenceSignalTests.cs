using SocialStudio.Application.Abstractions.Social.ContentDiscovery;
using SocialStudio.Application.Features.Social.ContentGeneration.Discovery;

namespace SocialStudio.Tests.Application.Social.ContentGeneration.Discovery;

/// <summary>WU-4: design §6.4. Cheap lexical co-occurrence proxy, not clustering.</summary>
public sealed class CoOccurrenceSignalTests
{
    [Fact]
    public void BelowKThreshold_RawSignalIsZero_NotMissing()
    {
        // Only 2 distinct authors mention "marketing" — below K=3.
        var set = new[]
        {
            Candidate("marketing tips for creators", "@a"),
            Candidate("marketing tips for teams", "@b"),
        };

        var result = CoOccurrenceSignal.Compute(set);

        Assert.All(result, value => Assert.Equal(0.0, value));
    }

    [Fact]
    public void AtOrAboveKThreshold_RawSignalIsPositive()
    {
        var set = new[]
        {
            Candidate("marketing tips for creators", "@a"),
            Candidate("marketing tips for teams", "@b"),
            Candidate("marketing playbook today", "@c"),
        };

        var result = CoOccurrenceSignal.Compute(set);

        Assert.All(result, value => Assert.True(value > 0.0));
    }

    [Fact]
    public void DistinctAuthorCounting_SameAuthorTwice_CountsOnce()
    {
        var set = new[]
        {
            Candidate("marketing tips for creators", "@a"),
            Candidate("marketing again from the same author", "@a"),
            Candidate("marketing playbook today", "@b"),
        };

        var result = CoOccurrenceSignal.Compute(set);

        // Only 2 distinct authors ("@a" appears twice) — below K=3, so the raw signal is 0.
        Assert.All(result, value => Assert.Equal(0.0, value));
    }

    [Fact]
    public void Tokenizer_StripsPunctuationAndShortTokensAndStopwords()
    {
        // "the" and "and" are stopwords; "a", "of" are < 4 chars — none should drive co-occurrence.
        var set = new[]
        {
            Candidate("The Marketing, and the Creators!", "@a"),
            Candidate("marketing of the creators", "@b"),
            Candidate("Creators & Marketing today", "@c"),
        };

        var result = CoOccurrenceSignal.Compute(set);

        Assert.All(result, value => Assert.True(value > 0.0));
    }

    [Fact]
    public void NormalizedBySetDistinctAuthorCount()
    {
        var set = new[]
        {
            Candidate("marketing tips for creators", "@a"),
            Candidate("marketing tips for teams", "@b"),
            Candidate("marketing playbook today", "@c"),
        };

        var result = CoOccurrenceSignal.Compute(set);

        // 3 distinct authors share "marketing"; normalized by 3 distinct authors in the set => 1.0.
        Assert.All(result, value => Assert.Equal(1.0, value, 3));
    }

    private static DiscoveredContent Candidate(string title, string handle) =>
        new(title, string.Empty, "tiktok", $"https://tiktok.com/{handle}/video/{Guid.NewGuid()}", null,
            1_000, 10, 1, null, null, DateTimeOffset.UtcNow, handle, "niche");
}
