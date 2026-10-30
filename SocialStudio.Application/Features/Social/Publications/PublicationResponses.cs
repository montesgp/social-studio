using SocialStudio.Domain.Social.Publications;

namespace SocialStudio.Application.Features.Social.Publications;

public sealed record PublicationDestinationDto(Guid ClientAccountConnectionId, Guid SocialNetworkId, string NetworkKey)
{
    public static PublicationDestinationDto FromDomain(PublicationDestination d) =>
        new(d.ClientAccountConnectionId, d.SocialNetworkId, d.NetworkKey);
}

public sealed record PublicationAttemptDto(
    int AttemptNumber,
    DateTime StartedAtUtc,
    DateTime? FinishedAtUtc,
    string Outcome,
    string? ErrorMessage,
    string? ExternalReference)
{
    public static PublicationAttemptDto FromDomain(PublicationAttempt a) =>
        new(a.AttemptNumber, a.StartedAtUtc, a.FinishedAtUtc, a.Outcome.ToString(), a.ErrorMessage, a.ExternalReference);
}

/// <summary>Full publication detail (design §9 — <c>GET /api/social/publications/{publicationId}</c>).</summary>
public sealed record PublicationResponse(
    Guid Id,
    Guid OrganizationId,
    Guid ClientId,
    Guid ContentItemId,
    PublicationDestinationDto Destination,
    string Status,
    DateTime? ScheduledForUtc,
    string? ExternalReference,
    IReadOnlyList<PublicationAttemptDto> Attempts,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc)
{
    public static PublicationResponse FromDomain(Publication p) =>
        new(
            p.Id.Value,
            p.OrganizationId,
            p.ClientId,
            p.ContentItemId,
            PublicationDestinationDto.FromDomain(p.Destination),
            p.Status.ToString(),
            p.ScheduledForUtc,
            p.ExternalReference,
            p.Attempts.Select(PublicationAttemptDto.FromDomain).ToList(),
            p.CreatedAtUtc,
            p.UpdatedAtUtc);
}

/// <summary>Lightweight per-item publication summary attached to a Content Bank item (design §5.2).</summary>
public sealed record PublicationSummaryDto(
    Guid Id,
    string Status,
    PublicationDestinationDto Destination,
    int AttemptCount,
    string? ExternalReference)
{
    public static PublicationSummaryDto FromDomain(Publication p) =>
        new(p.Id.Value, p.Status.ToString(), PublicationDestinationDto.FromDomain(p.Destination), p.AttemptCount, p.ExternalReference);
}
