using SocialStudio.Application.Abstractions.Social.ContentDiscovery;

namespace SocialStudio.Tests.Application.Abstractions.Social;

/// <summary>
/// WU-1: the per-source result is three-valued, not a bare list — "unconfigured" is a first-class
/// outcome (D12/CD-13), distinct from "ran and found nothing" and from "failed".
/// </summary>
public sealed class SourceDiscoveryResultTests
{
    [Fact]
    public void Ran_CanCarryCandidates()
    {
        var candidate = new DiscoveredContent(
            "t", "s", "tiktok", "https://tiktok.com/@a/video/1", null,
            100, 10, 1, null, null, DateTimeOffset.UtcNow, "@a", "references");

        var result = new SourceDiscoveryResult(SourceOutcome.Ran, [candidate]);

        Assert.Equal(SourceOutcome.Ran, result.Outcome);
        Assert.Single(result.Candidates);
    }

    [Fact]
    public void Unconfigured_CandidatesAreEmpty()
    {
        var result = new SourceDiscoveryResult(SourceOutcome.Unconfigured, []);

        Assert.Equal(SourceOutcome.Unconfigured, result.Outcome);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public void Failed_CandidatesAreEmpty()
    {
        var result = new SourceDiscoveryResult(SourceOutcome.Failed, []);

        Assert.Equal(SourceOutcome.Failed, result.Outcome);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public void SourceOutcome_IsThreeValued()
    {
        Assert.Equal(3, Enum.GetValues<SourceOutcome>().Length);
    }
}
