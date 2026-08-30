using System.Globalization;
using System.Text.RegularExpressions;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Visual.Layout;

/// <summary>
/// WU-4 (CLIF-CG-04, design.md file map): parses the DECLARED <c>left/top/width/min-height</c> box
/// for every <c>[data-slot]</c> element in already-built markup — pre-render, no Chromium
/// measurement involved. A slot whose declared style is <c>bottom:</c>-anchored (cta, accent,
/// signature, brand-mark) is excluded by construction, matching the reflowable-slot-set rule
/// (top-anchored stacked slots only participate in the reflow).
///
/// <para>
/// This is the single reusable declared-box parser for the change: WU-3's
/// <c>ContentGenerationJobProcessor.ExtractDeclaredSlotBoxes</c> delegates to
/// <see cref="ParseDeclaredSlotBoxes"/> instead of duplicating the regex-based parsing.
/// </para>
/// </summary>
public static class MarkupSlotBoxParser
{
    /// <summary>Matches an element carrying both <c>data-slot="..."</c> and <c>style="..."</c>, in
    /// that attribute order — the exact order every <c>Build*TemplateSlots</c> helper emits.</summary>
    private static readonly Regex SlotElementRegex = new(
        @"<[a-zA-Z][a-zA-Z0-9]*\b[^>]*?\sdata-slot=""(?<slot>[^""]+)""[^>]*?\sstyle=""(?<style>[^""]*)""[^>]*>",
        RegexOptions.Compiled);

    private static readonly Regex LeftPxRegex =
        new(@"(?:^|;)\s*left\s*:\s*(-?\d+(?:\.\d+)?)px", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex TopPxRegex =
        new(@"(?:^|;)\s*top\s*:\s*(-?\d+(?:\.\d+)?)px", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex WidthPxRegex =
        new(@"(?:^|;)\s*width\s*:\s*(-?\d+(?:\.\d+)?)px", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex MinHeightPxRegex =
        new(@"(?:^|;)\s*min-height\s*:\s*(-?\d+(?:\.\d+)?)px", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Parses every <c>[data-slot]</c> element's declared box from <paramref name="markup"/>. Skips
    /// any slot whose style declares <c>bottom:</c> (bottom-anchored — never reflowed), or that is
    /// missing any of the four required dimensions.
    /// </summary>
    public static IReadOnlyList<SlotBox> ParseDeclaredSlotBoxes(string markup)
    {
        var boxes = new List<SlotBox>();

        foreach (Match match in SlotElementRegex.Matches(markup))
        {
            var style = match.Groups["style"].Value;
            if (style.Contains("bottom:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var left = LeftPxRegex.Match(style);
            var top = TopPxRegex.Match(style);
            var width = WidthPxRegex.Match(style);
            var height = MinHeightPxRegex.Match(style);
            if (!left.Success || !top.Success || !width.Success || !height.Success)
            {
                continue;
            }

            boxes.Add(new SlotBox(
                match.Groups["slot"].Value,
                double.Parse(left.Groups[1].Value, CultureInfo.InvariantCulture),
                double.Parse(top.Groups[1].Value, CultureInfo.InvariantCulture),
                double.Parse(width.Groups[1].Value, CultureInfo.InvariantCulture),
                double.Parse(height.Groups[1].Value, CultureInfo.InvariantCulture)));
        }

        return boxes;
    }
}
