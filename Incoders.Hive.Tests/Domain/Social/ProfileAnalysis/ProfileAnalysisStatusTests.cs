using Incoders.Hive.Domain.Social.ProfileAnalysis;

namespace Incoders.Hive.Tests.Domain.Social.ProfileAnalysis;

public sealed class ProfileAnalysisStatusTests
{
    [Fact]
    public void StatusEnum_HasExpectedValues()
    {
        Assert.Equal(1, (int)ProfileAnalysisStatus.Requested);
        Assert.Equal(2, (int)ProfileAnalysisStatus.ScrapingProfile);
        Assert.Equal(3, (int)ProfileAnalysisStatus.ProfileScraped);
        Assert.Equal(4, (int)ProfileAnalysisStatus.ProfileScrapedPartial);
        Assert.Equal(5, (int)ProfileAnalysisStatus.Analyzing);
        Assert.Equal(6, (int)ProfileAnalysisStatus.Analyzed);
        Assert.Equal(7, (int)ProfileAnalysisStatus.Failed);
    }

    [Theory]
    [InlineData(ProfileAnalysisStatus.Requested, ProfileAnalysisStatus.ScrapingProfile, true)]
    [InlineData(ProfileAnalysisStatus.ScrapingProfile, ProfileAnalysisStatus.ProfileScraped, true)]
    [InlineData(ProfileAnalysisStatus.ScrapingProfile, ProfileAnalysisStatus.ProfileScrapedPartial, true)]
    [InlineData(ProfileAnalysisStatus.ProfileScraped, ProfileAnalysisStatus.Analyzing, true)]
    [InlineData(ProfileAnalysisStatus.ProfileScrapedPartial, ProfileAnalysisStatus.Analyzing, true)]
    [InlineData(ProfileAnalysisStatus.Analyzing, ProfileAnalysisStatus.Analyzed, true)]
    [InlineData(ProfileAnalysisStatus.Requested, ProfileAnalysisStatus.Failed, true)]
    [InlineData(ProfileAnalysisStatus.ScrapingProfile, ProfileAnalysisStatus.Failed, true)]
    [InlineData(ProfileAnalysisStatus.ProfileScraped, ProfileAnalysisStatus.Failed, true)]
    [InlineData(ProfileAnalysisStatus.ProfileScrapedPartial, ProfileAnalysisStatus.Failed, true)]
    [InlineData(ProfileAnalysisStatus.Analyzing, ProfileAnalysisStatus.Failed, true)]
    public void IsValidTransition_ReturnsTrue_ForAllLegalTransitions(
        ProfileAnalysisStatus from, ProfileAnalysisStatus to, bool expected)
    {
        Assert.Equal(expected, ProfileAnalysisStatusTransitions.IsValid(from, to));
    }

    [Theory]
    [InlineData(ProfileAnalysisStatus.Requested, ProfileAnalysisStatus.Analyzing)]
    [InlineData(ProfileAnalysisStatus.Requested, ProfileAnalysisStatus.Analyzed)]
    [InlineData(ProfileAnalysisStatus.Requested, ProfileAnalysisStatus.ProfileScraped)]
    [InlineData(ProfileAnalysisStatus.Analyzed, ProfileAnalysisStatus.ScrapingProfile)]
    [InlineData(ProfileAnalysisStatus.Analyzed, ProfileAnalysisStatus.Failed)]
    [InlineData(ProfileAnalysisStatus.Failed, ProfileAnalysisStatus.ScrapingProfile)]
    [InlineData(ProfileAnalysisStatus.Failed, ProfileAnalysisStatus.Analyzing)]
    [InlineData(ProfileAnalysisStatus.Failed, ProfileAnalysisStatus.Analyzed)]
    public void IsValidTransition_ReturnsFalse_ForIllegalTransitions(
        ProfileAnalysisStatus from, ProfileAnalysisStatus to)
    {
        Assert.False(ProfileAnalysisStatusTransitions.IsValid(from, to));
    }

    [Theory]
    [InlineData(ProfileAnalysisStatus.Analyzed)]
    [InlineData(ProfileAnalysisStatus.Failed)]
    public void IsTerminal_ReturnsTrue_ForTerminalStatuses(ProfileAnalysisStatus status)
    {
        Assert.True(ProfileAnalysisStatusTransitions.IsTerminal(status));
    }

    [Theory]
    [InlineData(ProfileAnalysisStatus.Requested)]
    [InlineData(ProfileAnalysisStatus.ScrapingProfile)]
    [InlineData(ProfileAnalysisStatus.ProfileScraped)]
    [InlineData(ProfileAnalysisStatus.ProfileScrapedPartial)]
    [InlineData(ProfileAnalysisStatus.Analyzing)]
    public void IsTerminal_ReturnsFalse_ForNonTerminalStatuses(ProfileAnalysisStatus status)
    {
        Assert.False(ProfileAnalysisStatusTransitions.IsTerminal(status));
    }
}
