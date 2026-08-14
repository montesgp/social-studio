using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Domain.Social.NicheDiscovery;

/// <summary>
/// Platform-managed catalog entry for a target market (design §4, N6). Adding a second market is
/// one seeded row here — never an enum member, constant, or code branch (ND-14).
/// </summary>
public sealed class MarketCatalogEntry : Entity<MarketId>
{
    private MarketCatalogEntry(MarketId id, MarketCode code, string displayNameKey, string primaryLanguage, bool isActive)
        : base(id)
    {
        Code = code;
        DisplayNameKey = displayNameKey;
        PrimaryLanguage = primaryLanguage;
        IsActive = isActive;
    }

    public MarketCode Code { get; }

    /// <summary>i18n key resolved at the API/UI boundary — never a literal display string (N6).</summary>
    public string DisplayNameKey { get; }

    public string PrimaryLanguage { get; }

    public bool IsActive { get; }

    public static Result<MarketCatalogEntry> Create(
        MarketCode code, string displayNameKey, string primaryLanguage, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(displayNameKey))
        {
            return Result.Failure<MarketCatalogEntry>(NicheDiscoveryErrors.MarketCodeInvalid);
        }

        if (string.IsNullOrWhiteSpace(primaryLanguage))
        {
            return Result.Failure<MarketCatalogEntry>(NicheDiscoveryErrors.MarketCodeInvalid);
        }

        return Result.Success(new MarketCatalogEntry(MarketId.New(), code, displayNameKey.Trim(), primaryLanguage.Trim(), isActive));
    }
}
