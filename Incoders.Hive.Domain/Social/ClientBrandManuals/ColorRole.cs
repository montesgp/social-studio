namespace Incoders.Hive.Domain.Social.ClientBrandManuals;

/// <summary>
/// Semantic role for a color entry in a brand color palette.
/// Ordinal values define prevalence ordering: Primary is the most dominant.
/// </summary>
public enum ColorRole
{
    Primary   = 0,
    Secondary = 1,
    Accent    = 2,
    Neutral   = 3,
}
