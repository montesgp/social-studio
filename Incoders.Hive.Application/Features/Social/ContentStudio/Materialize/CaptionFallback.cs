using System.Text.RegularExpressions;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Pipeline;

namespace Incoders.Hive.Application.Features.Social.ContentStudio.Materialize;

/// <summary>
/// Derives a deterministic, editable caption from content ALREADY PRESENT in a visual format's
/// rendered markup — the first slide's headline/first text slot — when the AI-generated payload
/// carried no caption (<see cref="VisualPublicationCaption.Extract"/> returned <c>null</c>).
/// <para>
/// Decision E3/Q2 (Content Studio editing completeness): caption absence at generation must produce
/// a deterministic EDITABLE fallback, never a hard job failure, and never a literal placeholder
/// string an operator could publish unnoticed.
/// </para>
/// </summary>
public static class CaptionFallback
{
    // Matches the first text slot's opening/closing tag pair and captures its inner content
    // (including any nested tags, stripped afterward). Deliberately restricted to text-bearing tags
    // — never <div>/<img> — so it never grabs a wrapper's markup instead of copy.
    private static readonly Regex SlotTextRegex = new(
        @"<(?<tag>h1|h2|h3|p|span)\b[^>]*data-slot\s*=\s*""[^""]*""[^>]*>(?<inner>.*?)</\k<tag>>",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

    private static readonly Regex TagRegex = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// Extracts the first slide's first text-slot content from <paramref name="compositionSpecJson"/>
    /// (the SAME envelope format the render pipeline persists — see
    /// <see cref="CompositionSpecJson.ParseActiveList"/>), stripped of nested tags and collapsed
    /// whitespace. Returns <c>null</c> when there is nothing to derive from — the caller decides the
    /// last-resort behavior.
    /// </summary>
    public static string? DeriveFromCompositionSpec(string? compositionSpecJson)
    {
        var envelope = CompositionSpecJson.ParseActiveList(compositionSpecJson).FirstOrDefault();
        if (envelope is null || string.IsNullOrWhiteSpace(envelope.Markup))
        {
            return null;
        }

        var match = SlotTextRegex.Match(envelope.Markup);
        if (!match.Success)
        {
            return null;
        }

        var withoutTags = TagRegex.Replace(match.Groups["inner"].Value, " ");
        var decoded = System.Net.WebUtility.HtmlDecode(withoutTags);
        var collapsed = WhitespaceRegex.Replace(decoded, " ").Trim();

        return string.IsNullOrWhiteSpace(collapsed) ? null : collapsed;
    }
}
