namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Visual.Fonts;

/// <summary>
/// Wire shape for one <see cref="FontEntry"/> — the picker/render font contract (R2.3).
/// <see cref="IsBundled"/> tells Content Studio the API serves the family's files itself
/// (client-style-fidelity CSF-CS-01), so it must not be fetched from a webfont CDN.
/// </summary>
public sealed record FontEntryDto(string Family, bool IsBrand, bool IsBundled = false)
{
    public static FontEntryDto FromDomain(FontEntry entry) => new(entry.Family, entry.IsBrand, entry.IsBundled);
}

/// <summary>
/// Wire shape for the render-capability signal (Decision F3): the font catalog for a client plus
/// whether the render path actually supports color emoji glyphs, computed by probing the bundled
/// emoji typeface — never a config flag (R6.2/R6.4 — the slide-text emoji picker gate).
/// </summary>
public sealed record RenderCapabilitiesResponse(IReadOnlyList<FontEntryDto> Fonts, bool SupportsColorEmoji);
