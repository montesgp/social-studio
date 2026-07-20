namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Visual.Fonts;

/// <summary>
/// One entry in the font contract (see <see cref="FontCatalog"/>): a family name plus whether it is
/// a client brand font or a bundled system-safe substitute.
/// </summary>
/// <param name="Family">
/// The exact font-family name, used verbatim in CSS declarations, the guardrail allow-list, and the
/// bundled font lookup. Never an alias (Decision #1169 — a "safe list" name must be the name of the
/// font we actually embed and render, not a stand-in for a font we cannot ship).
/// </param>
/// <param name="IsBrand">
/// <c>true</c> when this entry comes from the client's <c>ClientBrandManual.Typography</c>.
/// </param>
/// <param name="IsBundled">
/// <c>true</c> when this family is guaranteed renderable because it is embedded in this assembly and
/// resolvable via <see cref="IFontProvider"/> (system-safe-list entries; brand fonts are not bundled
/// today).
/// </param>
public sealed record FontEntry(string Family, bool IsBrand, bool IsBundled);
