namespace Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;

/// <summary>
/// EF data entity for <c>social.market_catalog</c> — platform-level, no <c>organization_id</c>, exactly
/// like the niche catalog (design §9, N6, ND-14). Adding a second market is one seeded row here.
/// </summary>
public sealed class MarketCatalogData
{
    public Guid Id { get; set; }

    /// <summary>Canonical <c>MarketCode.Value</c> string form, e.g. <c>"es-AR"</c>.</summary>
    public string MarketCode { get; set; } = string.Empty;

    /// <summary>i18n key resolved at the API/UI boundary — never a literal display string (N6).</summary>
    public string DisplayNameKey { get; set; } = string.Empty;

    public string PrimaryLanguage { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }
}
