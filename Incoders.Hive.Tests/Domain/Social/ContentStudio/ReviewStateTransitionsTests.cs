using Incoders.Hive.Domain.Social.ContentStudio;

namespace Incoders.Hive.Tests.Domain.Social.ContentStudio;

public sealed class ReviewStateTransitionsTests
{
    [Theory]
    [InlineData(ReviewState.Draft, ReviewState.InReview)]
    [InlineData(ReviewState.Draft, ReviewState.Edited)]
    [InlineData(ReviewState.Draft, ReviewState.Approved)]
    [InlineData(ReviewState.Draft, ReviewState.Rejected)]
    [InlineData(ReviewState.Draft, ReviewState.Discarded)]
    [InlineData(ReviewState.InReview, ReviewState.Approved)]
    [InlineData(ReviewState.InReview, ReviewState.Rejected)]
    [InlineData(ReviewState.InReview, ReviewState.Edited)]
    [InlineData(ReviewState.Edited, ReviewState.Approved)]
    [InlineData(ReviewState.Edited, ReviewState.Edited)]
    [InlineData(ReviewState.Approved, ReviewState.ReadyForPublication)]
    [InlineData(ReviewState.Approved, ReviewState.Edited)]
    [InlineData(ReviewState.Approved, ReviewState.Rejected)]
    [InlineData(ReviewState.Rejected, ReviewState.InReview)]
    [InlineData(ReviewState.Discarded, ReviewState.InReview)]
    [InlineData(ReviewState.ReadyForPublication, ReviewState.HandedOff)]
    [InlineData(ReviewState.ReadyForPublication, ReviewState.Approved)]
    public void IsValid_ReturnsTrue_ForLegalTransitions(ReviewState from, ReviewState to)
    {
        Assert.True(ReviewStateTransitions.IsValid(from, to));
    }

    [Theory]
    [InlineData(ReviewState.Draft, ReviewState.ReadyForPublication)]
    [InlineData(ReviewState.Draft, ReviewState.HandedOff)]
    [InlineData(ReviewState.InReview, ReviewState.ReadyForPublication)]
    [InlineData(ReviewState.InReview, ReviewState.HandedOff)]
    [InlineData(ReviewState.Approved, ReviewState.HandedOff)]
    [InlineData(ReviewState.Discarded, ReviewState.Approved)]
    [InlineData(ReviewState.HandedOff, ReviewState.Approved)]
    [InlineData(ReviewState.HandedOff, ReviewState.Draft)]
    public void IsValid_ReturnsFalse_ForIllegalTransitions(ReviewState from, ReviewState to)
    {
        Assert.False(ReviewStateTransitions.IsValid(from, to));
    }

    [Fact]
    public void IsTerminal_OnlyHandedOff()
    {
        Assert.True(ReviewStateTransitions.IsTerminal(ReviewState.HandedOff));
        Assert.False(ReviewStateTransitions.IsTerminal(ReviewState.Approved));
        Assert.False(ReviewStateTransitions.IsTerminal(ReviewState.ReadyForPublication));
    }
}
