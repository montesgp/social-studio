using Incoders.Hive.Domain.Social.Clients;

namespace Incoders.Hive.Application.Features.Social.Clients;

public sealed record SocialClientResponse(
    Guid Id,
    Guid OrganizationId,
    string Name,
    string? Description,
    string? SectorOrNiche,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    // WU-8 (BSP-08a): expose the client's background sourcing policy in the API response.
    // Null = Unset = legacy 3-tier behavior (unchanged from pre-WU-8 behavior for existing clients).
    BackgroundSourcingPolicy? BackgroundSourcingPolicy = null,
    // WU-7 (CAG-CS-02): expose per-client aesthetic refresh threshold.
    // Null = use system default (30 days).
    int? AestheticRefreshThresholdDays = null,
    // Issue #70: expose the client's opt-in for @handle signature on generated slides.
    // Plain boolean; default false = not signed.
    bool SignsOwnSlides = false)
{
    public static SocialClientResponse FromDomain(Client client) =>
        new(
            client.Id.Value,
            client.OrganizationId,
            client.Name,
            client.Description,
            client.SectorOrNiche,
            client.IsActive,
            client.CreatedAtUtc,
            client.UpdatedAtUtc,
            client.BackgroundSourcingPolicy,
            client.AestheticRefreshThresholdDays,
            client.SignsOwnSlides);
}
