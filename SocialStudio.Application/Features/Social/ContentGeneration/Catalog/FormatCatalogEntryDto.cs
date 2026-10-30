namespace SocialStudio.Application.Features.Social.ContentGeneration.Catalog;

/// <summary>
/// Lightweight projection of a <see cref="Domain.Social.ContentFormats.FormatDefinition"/>
/// returned by GET /api/social/content-generation/formats.
/// All axis values are serialized as plain strings (per spec C5: no enum serialization on the wire).
/// </summary>
public sealed record FormatCatalogEntryDto(
    string FormatKey,
    string Surface,
    string? Layout,
    string Medium,
    string DisplayLabel,
    bool IsMultiAsset,
    bool IsAuxiliary);
