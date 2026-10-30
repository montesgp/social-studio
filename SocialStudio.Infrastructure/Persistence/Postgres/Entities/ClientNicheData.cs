namespace SocialStudio.Infrastructure.Persistence.Postgres.Entities;

/// <summary>
/// EF Core join entity for social.client_niches — the many-to-many between a client and the
/// platform niche catalog. Additive: <c>social.clients.sector_or_niche</c> free text is untouched.
/// </summary>
public sealed class ClientNicheData
{
    public Guid ClientId { get; set; }

    public Guid NicheId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
