using System.Text.RegularExpressions;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Visual.Fonts;
using Incoders.Hive.Domain.Social.ContentGeneration;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Visual;

/// <summary>
/// Rewrites CSS-generic-only <c>font-family</c> declarations to the brand typeface BEFORE guardrail
/// validation runs (Decision D1 — Content Studio editing completeness design). Pure function: no I/O,
/// no provider types.
/// <para>
/// Normalization absorbs OMISSION (the AI forgot to name a font and just used a generic); it never
/// masks DEFIANCE (the AI invented a foreign named font). Rule 3 below is load-bearing: a foreign
/// named family is always passed through untouched, so the guardrail — not this normalizer — is the
/// one place that observes and rejects prompt non-compliance.
/// </para>
/// </summary>
public static class TypographyNormalizer
{
    // Matches font-family values in inline styles — same shape as the guardrail's matcher, kept in
    // sync deliberately: both operate on the same markup dialect.
    private static readonly Regex FontFamilyDeclarationRegex =
        new(@"font-family\s*:\s*([^;""'>]+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// CSS generic font-family keywords. Moved here from the guardrail's former waiver (Decision D2):
    /// the predicate is the same code, but its role changes from "always accept" to "classify as
    /// generic so it can be normalized before validation".
    /// </summary>
    internal static bool IsCssGenericFontFamily(string name) =>
        name.Equals("serif", StringComparison.OrdinalIgnoreCase)
        || name.Equals("sans-serif", StringComparison.OrdinalIgnoreCase)
        || name.Equals("monospace", StringComparison.OrdinalIgnoreCase)
        || name.Equals("cursive", StringComparison.OrdinalIgnoreCase)
        || name.Equals("fantasy", StringComparison.OrdinalIgnoreCase)
        || name.Equals("system-ui", StringComparison.OrdinalIgnoreCase)
        || name.Equals("ui-serif", StringComparison.OrdinalIgnoreCase)
        || name.Equals("ui-sans-serif", StringComparison.OrdinalIgnoreCase)
        || name.Equals("ui-monospace", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Normalizes every <c>font-family</c> declaration in <paramref name="markup"/> against
    /// <paramref name="catalog"/>. Rules, in order, per declaration:
    /// <list type="number">
    /// <item>Stack contains an allowed named family (brand or catalog system font) → untouched.</item>
    /// <item>Stack is generic-only → rewritten to <c>"&lt;effective family&gt;", &lt;original stack&gt;</c>.</item>
    /// <item>Stack contains a foreign named family → untouched (left for the guardrail to reject).</item>
    /// </list>
    /// </summary>
    public static (string Markup, NormalizationReport Report) Normalize(
        string markup,
        CompositionTypography effective,
        IReadOnlyList<FontEntry> catalog)
    {
        if (string.IsNullOrEmpty(markup))
        {
            return (markup, NormalizationReport.Empty);
        }

        var rewritten = new List<string>();

        var result = FontFamilyDeclarationRegex.Replace(markup, match =>
        {
            var raw = match.Groups[1].Value;
            var families = raw
                .Split(',')
                .Select(f => f.Trim().Trim('\'', '"'))
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .ToArray();

            if (families.Length == 0 || families.Any(f => !IsCssGenericFontFamily(f)))
            {
                // Contains at least one named family — either allowed (rule 1) or foreign (rule 3).
                // Either way, this declaration is left byte-identical.
                return match.Value;
            }

            // Generic-only stack (rule 2): prepend the brand's effective family, keep the original
            // generic stack as the graceful browser-side fallback tail. Single-quoted, deliberately —
            // this value is always embedded inside an already-double-quoted HTML style="..."
            // attribute; a double-quoted CSS string here would break the attribute and fail the
            // guardrail's XML schema check.
            rewritten.Add(raw.Trim());
            return $"""font-family:'{effective.HeadingFamily}', {raw.Trim()}""";
        });

        var report = rewritten.Count == 0
            ? NormalizationReport.Empty
            : new NormalizationReport(rewritten.Count, rewritten);

        return (result, report);
    }
}
