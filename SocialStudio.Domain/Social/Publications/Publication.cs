using SocialStudio.Domain.Common;

namespace SocialStudio.Domain.Social.Publications;

/// <summary>
/// Aggregate root for a single publish action toward one <see cref="PublicationDestination"/>.
/// References <see cref="ContentItemId"/> as a bare <see cref="Guid"/> — never navigated, never
/// mutated. Orthogonal to the editorial <c>ReviewState</c> axis by construction: no
/// <c>SocialStudio.Domain.Social.ContentStudio</c> type is imported here.
/// </summary>
public sealed class Publication : Entity<PublicationId>
{
    private readonly List<PublicationAttempt> _attempts;

    private Publication(
        PublicationId id,
        Guid organizationId,
        Guid clientId,
        Guid contentItemId,
        PublicationDestination destination,
        PublicationStatus status,
        DateTime? scheduledForUtc,
        List<PublicationAttempt> attempts,
        string? externalReference,
        DateTime createdAtUtc,
        DateTime updatedAtUtc)
        : base(id)
    {
        OrganizationId = organizationId;
        ClientId = clientId;
        ContentItemId = contentItemId;
        Destination = destination;
        Status = status;
        ScheduledForUtc = scheduledForUtc;
        _attempts = attempts;
        ExternalReference = externalReference;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
    }

    // Parameterless constructor for EF Core
    private Publication() : base(default)
    {
        _attempts = [];
    }

    public Guid OrganizationId { get; private set; }

    public Guid ClientId { get; private set; }

    /// <summary>SOFT ref to the source ContentItem — never navigated, never mutated.</summary>
    public Guid ContentItemId { get; private set; }

    public PublicationDestination Destination { get; private set; } = default!;

    public PublicationStatus Status { get; private set; }

    public DateTime? ScheduledForUtc { get; private set; }

    public IReadOnlyList<PublicationAttempt> Attempts => _attempts;

    public string? ExternalReference { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public int AttemptCount => _attempts.Count;

    /// <summary>Entry path A — schedule for later. No dispatcher fires it in v1.</summary>
    public static Result<Publication> Schedule(
        PublicationId id,
        Guid organizationId,
        Guid clientId,
        Guid contentItemId,
        PublicationDestination destination,
        DateTime scheduledForUtc,
        DateTime nowUtc)
    {
        var validation = ValidateCommon(contentItemId, destination);
        if (validation.IsFailure)
        {
            return Result.Failure<Publication>(validation.Error);
        }

        if (scheduledForUtc <= nowUtc)
        {
            return Result.Failure<Publication>(PublicationErrors.Validation("Scheduled time must be in the future."));
        }

        return Result.Success(new Publication(
            id, organizationId, clientId, contentItemId, destination,
            PublicationStatus.Scheduled, scheduledForUtc, [], null, nowUtc, nowUtc));
    }

    /// <summary>Entry path B — publish immediately (starts in Publishing, opens attempt #1).</summary>
    public static Result<Publication> PublishNow(
        PublicationId id,
        Guid organizationId,
        Guid clientId,
        Guid contentItemId,
        PublicationDestination destination,
        DateTime nowUtc)
    {
        var validation = ValidateCommon(contentItemId, destination);
        if (validation.IsFailure)
        {
            return Result.Failure<Publication>(validation.Error);
        }

        var publication = new Publication(
            id, organizationId, clientId, contentItemId, destination,
            PublicationStatus.Scheduled, null, [], null, nowUtc, nowUtc);

        var begin = publication.BeginPublishing(nowUtc);
        return begin.IsFailure
            ? Result.Failure<Publication>(begin.Error)
            : Result.Success(publication);
    }

    /// <summary>Re-hydration factory used by EF mapping. Bypasses validation — data is trusted.</summary>
    public static Publication Restore(
        PublicationId id,
        Guid organizationId,
        Guid clientId,
        Guid contentItemId,
        PublicationDestination destination,
        PublicationStatus status,
        DateTime? scheduledForUtc,
        IReadOnlyList<PublicationAttempt> attempts,
        string? externalReference,
        DateTime createdAtUtc,
        DateTime updatedAtUtc) =>
        new(id, organizationId, clientId, contentItemId, destination, status, scheduledForUtc,
            attempts.OrderBy(a => a.AttemptNumber).ToList(), externalReference, createdAtUtc, updatedAtUtc);

    /// <summary>Scheduled -> Publishing (opens a new attempt). Used by publish-now internally and by
    /// the future dispatcher.</summary>
    public Result BeginPublishing(DateTime nowUtc)
    {
        var transition = Transition(PublicationStatus.Publishing, nowUtc);
        if (transition.IsFailure)
        {
            return transition;
        }

        var nextAttemptNumber = _attempts.Count == 0 ? 1 : _attempts[^1].AttemptNumber + 1;
        _attempts.Add(new PublicationAttempt(
            nextAttemptNumber, nowUtc, null, PublicationAttemptOutcome.Pending, null, null));
        return Result.Success();
    }

    /// <summary>Publishing -> Published (closes the open attempt as Succeeded, stores the external
    /// reference).</summary>
    public Result MarkPublished(string externalReference, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(externalReference))
        {
            return Result.Failure(PublicationErrors.Validation("External reference is required."));
        }

        var transition = Transition(PublicationStatus.Published, nowUtc);
        if (transition.IsFailure)
        {
            return transition;
        }

        CloseOpenAttempt(PublicationAttemptOutcome.Succeeded, nowUtc, errorMessage: null, externalReference);
        ExternalReference = externalReference.Trim();
        return Result.Success();
    }

    /// <summary>Publishing -> PublishFailed (closes the open attempt as Failed with the error).</summary>
    public Result MarkFailed(string errorMessage, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            return Result.Failure(PublicationErrors.Validation("Error message is required."));
        }

        var transition = Transition(PublicationStatus.PublishFailed, nowUtc);
        if (transition.IsFailure)
        {
            return transition;
        }

        CloseOpenAttempt(PublicationAttemptOutcome.Failed, nowUtc, errorMessage.Trim(), externalReference: null);
        return Result.Success();
    }

    /// <summary>PublishFailed -> Publishing (manual retry, opens attempt #N+1). OQ-5.</summary>
    public Result Retry(DateTime nowUtc) => BeginPublishing(nowUtc);

    /// <summary>Scheduled | PublishFailed -> Cancelled.</summary>
    public Result Cancel(DateTime nowUtc) => Transition(PublicationStatus.Cancelled, nowUtc);

    /// <summary>
    /// Publishing -> PublishFailed. Operator/reaper recovery of a row stuck in Publishing (process
    /// crashed after persist-before-call, before the connector callback returned). Closes the open
    /// attempt as Failed with the reclaim reason; the publication is then retryable/cancellable.
    /// </summary>
    public Result ReclaimStuck(string reason, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(PublicationErrors.Validation("Reclaim reason is required."));
        }

        var transition = Transition(PublicationStatus.PublishFailed, nowUtc);
        if (transition.IsFailure)
        {
            return transition;
        }

        CloseOpenAttempt(PublicationAttemptOutcome.Failed, nowUtc, reason.Trim(), externalReference: null);
        return Result.Success();
    }

    private static Result ValidateCommon(Guid contentItemId, PublicationDestination destination)
    {
        if (contentItemId == Guid.Empty)
        {
            return Result.Failure(PublicationErrors.Validation("Content item id is required."));
        }

        if (destination is null
            || destination.ClientAccountConnectionId == Guid.Empty
            || destination.SocialNetworkId == Guid.Empty
            || string.IsNullOrWhiteSpace(destination.NetworkKey))
        {
            return Result.Failure(PublicationErrors.Validation("A valid destination is required."));
        }

        return Result.Success();
    }

    private Result Transition(PublicationStatus target, DateTime nowUtc)
    {
        if (!PublicationStatusTransitions.IsValid(Status, target))
        {
            return Result.Failure(PublicationErrors.IllegalPublicationTransition);
        }

        Status = target;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    private void CloseOpenAttempt(
        PublicationAttemptOutcome outcome, DateTime nowUtc, string? errorMessage, string? externalReference)
    {
        if (_attempts.Count == 0)
        {
            return;
        }

        var open = _attempts[^1];
        _attempts[^1] = open with
        {
            FinishedAtUtc = nowUtc,
            Outcome = outcome,
            ErrorMessage = errorMessage,
            ExternalReference = externalReference,
        };
    }
}
