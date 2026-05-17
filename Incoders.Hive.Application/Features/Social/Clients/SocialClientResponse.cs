using Incoders.Hive.Domain.Social.Clients;

namespace Incoders.Hive.Application.Features.Social.Clients;

public sealed record SocialClientResponse(
    Guid Id,
    Guid OrganizationId,
    string Name,
    string? Description,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc)
{
    public static SocialClientResponse FromDomain(Client client) =>
        new(
            client.Id.Value,
            client.OrganizationId,
            client.Name,
            client.Description,
            client.IsActive,
            client.CreatedAtUtc,
            client.UpdatedAtUtc);
}
