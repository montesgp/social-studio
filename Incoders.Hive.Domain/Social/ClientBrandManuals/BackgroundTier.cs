namespace Incoders.Hive.Domain.Social.ClientBrandManuals;

/// <summary>
/// Classification tier for a background template entry in a brand manual.
/// Creative = 0 is the zero-value so System.Text.Json fills missing Tier fields with Creative,
/// ensuring backward-compatible JSON deserialization for legacy entries (BG-01).
/// </summary>
public enum BackgroundTier
{
    /// <summary>Free creative generation — brand-bound. Default for legacy JSON entries.</summary>
    Creative = 0,

    /// <summary>Style-guide references passed as multimodal inputs to the designer.</summary>
    Reference = 1,

    /// <summary>
    /// Specific image forced as the composition background for a given format.
    /// Requires a non-empty FormatKey and IsFormatDefault = true on the entry.
    /// </summary>
    ForcedDefault = 2,
}
