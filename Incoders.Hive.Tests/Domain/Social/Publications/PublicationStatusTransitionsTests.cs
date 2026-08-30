using Incoders.Hive.Domain.Social.Publications;

namespace Incoders.Hive.Tests.Domain.Social.Publications;

public sealed class PublicationStatusTransitionsTests
{
    [Theory]
    [InlineData(PublicationStatus.Scheduled, PublicationStatus.Publishing)]
    [InlineData(PublicationStatus.Scheduled, PublicationStatus.Cancelled)]
    [InlineData(PublicationStatus.Publishing, PublicationStatus.Published)]
    [InlineData(PublicationStatus.Publishing, PublicationStatus.PublishFailed)]
    [InlineData(PublicationStatus.PublishFailed, PublicationStatus.Publishing)]
    [InlineData(PublicationStatus.PublishFailed, PublicationStatus.Cancelled)]
    public void IsValid_ReturnsTrue_ForLegalTransitions(PublicationStatus from, PublicationStatus to)
    {
        Assert.True(PublicationStatusTransitions.IsValid(from, to));
    }

    [Theory]
    [InlineData(PublicationStatus.Scheduled, PublicationStatus.Published)]
    [InlineData(PublicationStatus.Scheduled, PublicationStatus.PublishFailed)]
    [InlineData(PublicationStatus.Publishing, PublicationStatus.Scheduled)]
    [InlineData(PublicationStatus.Publishing, PublicationStatus.Cancelled)]
    [InlineData(PublicationStatus.PublishFailed, PublicationStatus.Published)]
    [InlineData(PublicationStatus.Published, PublicationStatus.Publishing)]
    [InlineData(PublicationStatus.Published, PublicationStatus.Cancelled)]
    [InlineData(PublicationStatus.Cancelled, PublicationStatus.Publishing)]
    [InlineData(PublicationStatus.Cancelled, PublicationStatus.Scheduled)]
    public void IsValid_ReturnsFalse_ForIllegalTransitions(PublicationStatus from, PublicationStatus to)
    {
        Assert.False(PublicationStatusTransitions.IsValid(from, to));
    }

    [Fact]
    public void IsTerminal_OnlyPublishedAndCancelled()
    {
        Assert.True(PublicationStatusTransitions.IsTerminal(PublicationStatus.Published));
        Assert.True(PublicationStatusTransitions.IsTerminal(PublicationStatus.Cancelled));
        Assert.False(PublicationStatusTransitions.IsTerminal(PublicationStatus.Scheduled));
        Assert.False(PublicationStatusTransitions.IsTerminal(PublicationStatus.Publishing));
        Assert.False(PublicationStatusTransitions.IsTerminal(PublicationStatus.PublishFailed));
    }
}
