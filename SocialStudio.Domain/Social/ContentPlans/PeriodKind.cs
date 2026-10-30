namespace SocialStudio.Domain.Social.ContentPlans;

/// <summary>
/// Discriminator for Period cadence variants. Only Monthly (0) is implemented.
/// The enum is a forward-compat seam; future cadences add a value here without
/// restructuring the Content Plan aggregate identity.
/// </summary>
public enum PeriodKind
{
    Monthly = 0,
}
