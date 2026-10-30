using System.Text.RegularExpressions;

namespace SocialStudio.Application.Features.Social.ContentGeneration.Visual.Layout;

/// <summary>
/// WU-4 (design.md D-C): rewrites the <c>top:NNNpx</c> declaration inside an already-built
/// markup string's per-slot <c>style</c> attribute — the reflow loop never re-invokes a builder.
/// A pure string rewrite is idempotent, inspectable, and preserves everything the builders already
/// produced (the appended brand-mark slot, inlined data-URI refs) that a builder re-invocation
/// would lose. Markup stays absolutely positioned; only the numeric <c>top</c> value changes.
/// </summary>
public static class SlotTopRewriter
{
    /// <summary>Matches a <c>[data-slot]</c> element, capturing the text before/after its
    /// <c>style="..."</c> attribute value so the value itself can be rewritten in place.</summary>
    private static readonly Regex SlotElementRegex = new(
        @"(?<prefix><[a-zA-Z][a-zA-Z0-9]*\b[^>]*?\sdata-slot=""(?<slot>[^""]+)""[^>]*?\sstyle="")(?<style>[^""]*)(?<suffix>""[^>]*>)",
        RegexOptions.Compiled);

    private static readonly Regex TopDeclRegex = new(
        @"(?<prefix>(?:^|;)\s*)top\s*:\s*-?\d+(?:\.\d+)?px",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Rewrites the <c>top:</c> declaration for every slot named in <paramref name="tops"/> to its
    /// new integer px value. Slots absent from <paramref name="tops"/>, or whose style has no
    /// <c>top:</c> declaration (bottom-anchored slots), are left byte-identical. Applying the same
    /// <paramref name="tops"/> map twice produces the same output as applying it once (idempotent).
    /// </summary>
    public static string Apply(string markup, IReadOnlyDictionary<string, int> tops)
    {
        ArgumentNullException.ThrowIfNull(markup);
        ArgumentNullException.ThrowIfNull(tops);

        if (tops.Count == 0)
        {
            return markup;
        }

        return SlotElementRegex.Replace(markup, match =>
        {
            var slot = match.Groups["slot"].Value;
            if (!tops.TryGetValue(slot, out var newTop))
            {
                return match.Value;
            }

            var style = match.Groups["style"].Value;
            if (!TopDeclRegex.IsMatch(style))
            {
                return match.Value;
            }

            var rewrittenStyle = TopDeclRegex.Replace(
                style,
                m => $"{m.Groups["prefix"].Value}top:{newTop}px",
                1);

            return match.Groups["prefix"].Value + rewrittenStyle + match.Groups["suffix"].Value;
        });
    }
}
