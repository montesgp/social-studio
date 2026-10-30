namespace SocialStudio.Infrastructure.Persistence.Postgres.Entities;

/// <summary>
/// EF data entity for <c>social.account_baselines</c> — one cached row per author per organization
/// (design §5.2). Deliberately carries NO <c>client_id</c>: a baseline is a property of the author,
/// not of whichever client discovered them, so two clients sharing a referent share one cached scrape.
/// </summary>
public sealed class AccountBaselineData
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public string NetworkKey { get; set; } = string.Empty;

    public string AuthorHandle { get; set; } = string.Empty;

    /// <summary>Null when not yet computable (e.g. no video posts) — never fabricated (CD-07).</summary>
    public double? MedianViews { get; set; }

    public double? MedianEngagement { get; set; }

    public int PostCount { get; set; }

    public DateTime ComputedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
