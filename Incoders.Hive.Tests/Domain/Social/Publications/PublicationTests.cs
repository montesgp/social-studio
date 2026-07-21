using Incoders.Hive.Domain.Social.Publications;

namespace Incoders.Hive.Tests.Domain.Social.Publications;

public sealed class PublicationTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid Client = Guid.NewGuid();
    private static readonly Guid ContentItem = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 7, 20, 12, 0, 0, DateTimeKind.Utc);

    private static PublicationDestination Destination() =>
        new(Guid.NewGuid(), Guid.NewGuid(), "instagram");

    [Fact]
    public void PublishNow_StartsPublishing_WithAttemptOneOpen_AndNoSchedule()
    {
        var result = Publication.PublishNow(
            PublicationId.New(), Org, Client, ContentItem, Destination(), Now);

        Assert.True(result.IsSuccess);
        var publication = result.Value;
        Assert.Equal(PublicationStatus.Publishing, publication.Status);
        Assert.Null(publication.ScheduledForUtc);
        Assert.Equal(1, publication.AttemptCount);
        Assert.Equal(PublicationAttemptOutcome.Pending, publication.Attempts[0].Outcome);
        Assert.Equal(1, publication.Attempts[0].AttemptNumber);
    }

    [Fact]
    public void Schedule_RejectsPastScheduledForUtc()
    {
        var result = Publication.Schedule(
            PublicationId.New(), Org, Client, ContentItem, Destination(), Now.AddDays(-1), Now);

        Assert.True(result.IsFailure);
        Assert.Equal(PublicationErrors.Validation("detail").Code, result.Error.Code);
    }

    [Fact]
    public void Schedule_StartsScheduled_WithFutureDate()
    {
        var scheduledFor = Now.AddDays(1);
        var result = Publication.Schedule(
            PublicationId.New(), Org, Client, ContentItem, Destination(), scheduledFor, Now);

        Assert.True(result.IsSuccess);
        var publication = result.Value;
        Assert.Equal(PublicationStatus.Scheduled, publication.Status);
        Assert.Equal(scheduledFor, publication.ScheduledForUtc);
        Assert.Equal(0, publication.AttemptCount);
    }

    [Fact]
    public void BeginPublishing_OpensNewAttempt_FromScheduled()
    {
        var publication = Publication.Schedule(
            PublicationId.New(), Org, Client, ContentItem, Destination(), Now.AddDays(1), Now).Value;

        var result = publication.BeginPublishing(Now.AddDays(1));

        Assert.True(result.IsSuccess);
        Assert.Equal(PublicationStatus.Publishing, publication.Status);
        Assert.Equal(1, publication.AttemptCount);
    }

    [Fact]
    public void MarkPublished_RequiresExternalReference_AndClosesAttemptSucceeded()
    {
        var publication = Publication.PublishNow(
            PublicationId.New(), Org, Client, ContentItem, Destination(), Now).Value;

        var missingRef = publication.MarkPublished(string.Empty, Now.AddSeconds(5));
        Assert.True(missingRef.IsFailure);

        var success = publication.MarkPublished("media-123", Now.AddSeconds(5));
        Assert.True(success.IsSuccess);
        Assert.Equal(PublicationStatus.Published, publication.Status);
        Assert.Equal("media-123", publication.ExternalReference);
        Assert.Equal(PublicationAttemptOutcome.Succeeded, publication.Attempts[0].Outcome);
        Assert.Equal("media-123", publication.Attempts[0].ExternalReference);
        Assert.NotNull(publication.Attempts[0].FinishedAtUtc);
    }

    [Fact]
    public void MarkFailed_RequiresErrorMessage_AndSetsPublishFailed()
    {
        var publication = Publication.PublishNow(
            PublicationId.New(), Org, Client, ContentItem, Destination(), Now).Value;

        var missingMessage = publication.MarkFailed(string.Empty, Now.AddSeconds(5));
        Assert.True(missingMessage.IsFailure);

        var failed = publication.MarkFailed("provider error", Now.AddSeconds(5));
        Assert.True(failed.IsSuccess);
        Assert.Equal(PublicationStatus.PublishFailed, publication.Status);
        Assert.Equal(PublicationAttemptOutcome.Failed, publication.Attempts[0].Outcome);
        Assert.Equal("provider error", publication.Attempts[0].ErrorMessage);
    }

    [Fact]
    public void Retry_OpensAttemptNPlusOne_OnlyFromPublishFailed()
    {
        var publication = Publication.PublishNow(
            PublicationId.New(), Org, Client, ContentItem, Destination(), Now).Value;
        publication.MarkFailed("boom", Now.AddSeconds(5));

        var retry = publication.Retry(Now.AddSeconds(10));

        Assert.True(retry.IsSuccess);
        Assert.Equal(PublicationStatus.Publishing, publication.Status);
        Assert.Equal(2, publication.AttemptCount);
        Assert.Equal(2, publication.Attempts[1].AttemptNumber);
    }

    [Fact]
    public void Retry_IllegalFromPublishing_ReturnsIllegalTransition()
    {
        var publication = Publication.PublishNow(
            PublicationId.New(), Org, Client, ContentItem, Destination(), Now).Value;

        var retry = publication.Retry(Now.AddSeconds(10));

        Assert.True(retry.IsFailure);
        Assert.Equal(PublicationErrors.IllegalPublicationTransition, retry.Error);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Cancel_LegalOnlyFromScheduledOrPublishFailed(bool fromScheduled)
    {
        Publication publication;
        if (fromScheduled)
        {
            publication = Publication.Schedule(
                PublicationId.New(), Org, Client, ContentItem, Destination(), Now.AddDays(1), Now).Value;
        }
        else
        {
            publication = Publication.PublishNow(
                PublicationId.New(), Org, Client, ContentItem, Destination(), Now).Value;
            publication.MarkFailed("boom", Now.AddSeconds(5));
        }

        var result = publication.Cancel(Now.AddSeconds(10));

        Assert.True(result.IsSuccess);
        Assert.Equal(PublicationStatus.Cancelled, publication.Status);
    }

    [Fact]
    public void Cancel_IllegalFromPublishing_ReturnsIllegalTransition()
    {
        var publication = Publication.PublishNow(
            PublicationId.New(), Org, Client, ContentItem, Destination(), Now).Value;

        var result = publication.Cancel(Now.AddSeconds(5));

        Assert.True(result.IsFailure);
        Assert.Equal(PublicationErrors.IllegalPublicationTransition, result.Error);
    }

    [Fact]
    public void ReclaimStuck_MovesPublishingToPublishFailed_AndClosesOpenAttemptAsFailed()
    {
        var publication = Publication.PublishNow(
            PublicationId.New(), Org, Client, ContentItem, Destination(), Now).Value;

        var result = publication.ReclaimStuck("stale attempt reclaimed", Now.AddMinutes(30));

        Assert.True(result.IsSuccess);
        Assert.Equal(PublicationStatus.PublishFailed, publication.Status);
        Assert.Equal(PublicationAttemptOutcome.Failed, publication.Attempts[0].Outcome);
        Assert.Equal("stale attempt reclaimed", publication.Attempts[0].ErrorMessage);
    }

    [Fact]
    public void IllegalTransitions_NeverThrow_AlwaysReturnFailureResult()
    {
        var publication = Publication.Schedule(
            PublicationId.New(), Org, Client, ContentItem, Destination(), Now.AddDays(1), Now).Value;

        var result = publication.MarkPublished("ref", Now);

        Assert.True(result.IsFailure);
        Assert.Equal(PublicationErrors.IllegalPublicationTransition, result.Error);
    }
}
