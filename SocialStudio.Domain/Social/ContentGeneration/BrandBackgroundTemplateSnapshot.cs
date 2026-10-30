using SocialStudio.Domain.Social.ClientBrandManuals;

namespace SocialStudio.Domain.Social.ContentGeneration;

/// <summary>
/// Immutable snapshot of a brand manual background template captured for generation.
/// Extended with Tier/FormatKey/IsFormatDefault to mirror BackgroundEntry (BG-01 snapshot scenario).
/// Defaulted trailing params keep existing constructions compiling.
/// </summary>
public sealed record BrandBackgroundTemplateSnapshot(
    string Name,
    string Url,
    string? UseCase,
    BackgroundTier Tier = BackgroundTier.Creative,
    string? FormatKey = null,
    bool IsFormatDefault = false);
