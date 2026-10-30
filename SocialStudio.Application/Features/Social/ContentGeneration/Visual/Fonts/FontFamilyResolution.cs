namespace SocialStudio.Application.Features.Social.ContentGeneration.Visual.Fonts;

/// <summary>
/// Maps a font family OBSERVED in a client's posts onto a family the renderer can actually embed.
///
/// <para>
/// Fonts are embedded resources (Decision A1 — the render host's font state is unknown, so nothing
/// may depend on host-provisioned fonts), and Arial/Helvetica are Monotype-licensed and must never
/// be aliased to (Decision #1169). A detected family is renderable when a binary for it is bundled —
/// the fallback families or the curated client catalog (<see cref="BundledFontCatalog"/>). Anything
/// else maps to the closest catalog family by classification and width, so a condensed headline
/// stays condensed and a serif headline never silently becomes a sans one.
/// </para>
///
/// <para>
/// The detected name is never discarded by callers — <see cref="Resolve"/> reports both what the
/// client actually uses and what will be drawn, so the gap is visible instead of silent.
/// </para>
/// </summary>
public static class FontFamilyResolution
{
    private const string LegacyDefault = "Liberation Sans";

    private static readonly string[] TokenSeparators = ["/", ",", "(", ")", "|", " or ", ";"];

    /// <summary>
    /// Catalog names, longest first, so "Roboto Condensed" is found before "Roboto" when both are
    /// contained in a description.
    /// </summary>
    private static readonly string[] NamesLongestFirst = FontCatalog.SystemSafeList
        .OrderByDescending(n => n.Length)
        .ToArray();

    /// <summary>
    /// Descriptor rules, checked in order. Each maps words a vision model uses to describe a face onto
    /// the bundled family that best represents that description. Order matters: "sans serif" contains
    /// "serif", and "condensed" must beat the generic sans rule.
    /// </summary>
    private static readonly (string[] Tokens, string Family)[] DescriptorRules =
    [
        (["typewriter", "courier"], "Courier Prime"),
        (["mono"], "Space Mono"),
        (["handwrit", "brush", "marker", "hand-drawn", "hand drawn"], "Caveat"),
        (["script", "cursive", "calligraph"], "Dancing Script"),
        (["sans"], string.Empty),
        (["didone", "high-contrast serif", "display serif", "fashion serif"], "Playfair Display"),
        (["garamond", "old-style", "oldstyle"], "EB Garamond"),
        (["serif", "georgia", "times", "baskerville", "bodoni", "didot", "merriweather"], "Lora"),
    ];

    private static readonly string[] CondensedTokens =
        ["condensed", "compressed", "narrow", "gothic", "impact", "tall", "extra-condensed"];

    private static readonly string[] HeavyTokens = ["heavy", "black", "ultra", "extra bold", "extrabold"];

    /// <summary>
    /// Resolves a detected family to a renderable one.
    /// </summary>
    /// <returns>
    /// <c>Requested</c> is the detected family, trimmed. <c>Renderable</c> is the family the renderer
    /// will draw with. <c>IsExact</c> is true when the detected text names a bundled family — when
    /// false, the caller is looking at a substitute and should say so rather than claim fidelity.
    /// </returns>
    public static (string Requested, string Renderable, bool IsExact) Resolve(string? detectedFamily)
    {
        var requested = (detectedFamily ?? string.Empty).Trim();
        if (requested.Length == 0)
        {
            return (string.Empty, LegacyDefault, false);
        }

        var named = FindNamedFamily(requested);
        if (named is not null)
        {
            return (requested, named, true);
        }

        return (requested, ClosestByDescription(requested), false);
    }

    /// <summary>
    /// A bundled family named by the detected text: the whole string, any single token of a
    /// "Name / description" answer, or a bundled name contained in it ("Poppins SemiBold").
    /// </summary>
    private static string? FindNamedFamily(string requested)
    {
        var whole = ExactName(requested);
        if (whole is not null)
        {
            return whole;
        }

        foreach (var token in requested.Split(TokenSeparators, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var match = ExactName(token);
            if (match is not null)
            {
                return match;
            }
        }

        return NamesLongestFirst.FirstOrDefault(name =>
            ContainsWord(requested, name));
    }

    private static string? ExactName(string candidate) =>
        FontCatalog.SystemSafeList.FirstOrDefault(f => string.Equals(f, candidate, StringComparison.OrdinalIgnoreCase));

    private static bool ContainsWord(string text, string name)
    {
        var index = text.IndexOf(name, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return false;
        }

        var end = index + name.Length;
        var startsClean = index == 0 || !char.IsLetterOrDigit(text[index - 1]);
        var endsClean = end == text.Length || !char.IsLetterOrDigit(text[end]);
        return startsClean && endsClean;
    }

    private static string ClosestByDescription(string family)
    {
        var lower = family.ToLowerInvariant();

        foreach (var (tokens, target) in DescriptorRules)
        {
            if (!tokens.Any(t => lower.Contains(t, StringComparison.Ordinal)))
            {
                continue;
            }

            // An explicit "sans" stops the serif rules below from matching "sans serif".
            if (target.Length == 0)
            {
                break;
            }

            return target;
        }

        if (CondensedTokens.Any(t => lower.Contains(t, StringComparison.Ordinal)))
        {
            return HeavyTokens.Any(t => lower.Contains(t, StringComparison.Ordinal)) ? "Anton" : "Oswald";
        }

        if (lower.Contains("geometric", StringComparison.Ordinal))
        {
            return "Poppins";
        }

        if (lower.Contains("rounded", StringComparison.Ordinal))
        {
            return "Nunito";
        }

        // Unknown families are overwhelmingly sans in social design; a neutral grotesque keeps the
        // deck's feel instead of guessing serif.
        return "Inter";
    }
}
