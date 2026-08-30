using FluentValidation;
using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social.Publications;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.Publications;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.Publications.Reclaim;

/// <summary>
/// Recovers a publication stuck in <see cref="PublicationStatus.Publishing"/> (process crashed after
/// persist-before-call, before the connector callback returned — design §7). Guarded by a staleness
/// threshold on the open attempt's <c>StartedAtUtc</c> so an operator cannot race a publish that is
/// still legitimately in flight.
/// </summary>
public sealed record ReclaimStuckPublicationCommand(Guid OrganizationId, Guid PublicationId, string Reason)
    : IRequest<Result<PublicationResponse>>;

public sealed class ReclaimStuckPublicationCommandValidator : AbstractValidator<ReclaimStuckPublicationCommand>
{
    public ReclaimStuckPublicationCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.PublicationId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty();
    }
}

public sealed class ReclaimStuckPublicationCommandHandler
    : IRequestHandler<ReclaimStuckPublicationCommand, Result<PublicationResponse>>
{
    /// <summary>
    /// v1 heuristic (not pinned by design.md): a <c>Publishing</c> row younger than this is treated as
    /// possibly still in flight and is NOT reclaimable — the operator must wait it out. A future
    /// reaper worker can safely reuse the exact same guard.
    /// </summary>
    private static readonly TimeSpan StalenessThreshold = TimeSpan.FromMinutes(10);

    private readonly IPublicationRepository _publications;
    private readonly IClock _clock;

    public ReclaimStuckPublicationCommandHandler(IPublicationRepository publications, IClock clock)
    {
        _publications = publications;
        _clock = clock;
    }

    public async Task<Result<PublicationResponse>> Handle(ReclaimStuckPublicationCommand request, CancellationToken ct)
    {
        var publication = await _publications.GetByIdAsync(
            request.OrganizationId, new PublicationId(request.PublicationId), ct);
        if (publication is null)
        {
            return Result.Failure<PublicationResponse>(PublicationErrors.NotFound);
        }

        var openAttempt = publication.Attempts.Count == 0 ? null : publication.Attempts[^1];
        if (publication.Status == PublicationStatus.Publishing
            && openAttempt is not null
            && _clock.UtcNow - openAttempt.StartedAtUtc < StalenessThreshold)
        {
            return Result.Failure<PublicationResponse>(PublicationErrors.Validation(
                "This publication may still be actively publishing; it is not yet past the staleness threshold for reclaim."));
        }

        var reclaimed = publication.ReclaimStuck(request.Reason, _clock.UtcNow);
        if (reclaimed.IsFailure)
        {
            return Result.Failure<PublicationResponse>(reclaimed.Error);
        }

        await _publications.UpdateAsync(publication, ct);
        return Result.Success(PublicationResponse.FromDomain(publication));
    }
}
