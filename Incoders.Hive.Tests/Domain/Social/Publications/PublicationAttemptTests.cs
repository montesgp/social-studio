using Incoders.Hive.Domain.Social.Publications;

namespace Incoders.Hive.Tests.Domain.Social.Publications;

public sealed class PublicationAttemptTests
{
    private static readonly DateTime Now = new(2026, 7, 20, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Equality_IsValueBased()
    {
        var a = new PublicationAttempt(1, Now, null, PublicationAttemptOutcome.Pending, null, null);
        var b = new PublicationAttempt(1, Now, null, PublicationAttemptOutcome.Pending, null, null);

        Assert.Equal(a, b);
    }

    [Fact]
    public void OrderingContract_ByAttemptNumber()
    {
        var attempts = new List<PublicationAttempt>
        {
            new(2, Now.AddMinutes(5), null, PublicationAttemptOutcome.Pending, null, null),
            new(1, Now, Now.AddMinutes(1), PublicationAttemptOutcome.Failed, "boom", null),
        };

        var ordered = attempts.OrderBy(a => a.AttemptNumber).ToList();

        Assert.Equal(1, ordered[0].AttemptNumber);
        Assert.Equal(2, ordered[1].AttemptNumber);
    }

    [Fact]
    public void SucceededAttempt_CarriesExternalReference()
    {
        var attempt = new PublicationAttempt(
            1, Now, Now.AddSeconds(30), PublicationAttemptOutcome.Succeeded, null, "media-123");

        Assert.Equal("media-123", attempt.ExternalReference);
        Assert.Null(attempt.ErrorMessage);
    }
}
