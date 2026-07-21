namespace Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;

/// <summary>
/// EF Core persistence entity for <c>social.publications</c>.
/// No annotations — all configuration is done via fluent API in AppIdentityDbContext.
/// <see cref="ContentItemId"/> is a soft Guid (no FK to content_studio_items) — orphan-tolerant.
/// Enums are stored as smallint; the domain↔short conversion lives in the repository mapping.
/// </summary>
public sealed class PublicationData
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid ClientId { get; set; }

    // Soft reference — no FK to content_studio_items
    public Guid ContentItemId { get; set; }

    // Destination (soft refs + a catalog-open provider key)
    public Guid DestinationClientAccountConnectionId { get; set; }
    public Guid DestinationSocialNetworkId { get; set; }
    public string DestinationNetworkKey { get; set; } = string.Empty;

    // Lifecycle
    public short Status { get; set; }
    public DateTime? ScheduledForUtc { get; set; }
    public string? ExternalReference { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public List<PublicationAttemptData> Attempts { get; set; } = new();
}
