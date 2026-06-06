using System.Text.RegularExpressions;
using System.Xml;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ContentGeneration;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Visual;

/// <summary>
/// Concrete implementation of the visual guardrail pipeline (VD-02, VD-03, VD-04).
/// Four ordered steps:
///   1. Schema      — single root div.composition, allowed tags, mandatory data-layer attributes.
///   2. Palette     — every color token in inline styles must be in the brand palette.
///   3. Typography  — every font-family must be in the brand typography allow-list.
///   4. Overflow    — static heuristic: char-count estimate vs data-max-lines per slot.
///
/// First failure short-circuits — the renderer is NEVER called on a guardrail violation.
/// </summary>
public sealed class VisualGuardrailValidator : IVisualGuardrailValidator
{
    /// <summary>
    /// Bundled brand-neutral fallback font family (never Arial unless the manual lists it).
    /// Used when the brand has an empty typography list — ensures generation never hard-fails.
    /// </summary>
    private const string FallbackFontFamily = "system-ui";

    /// <summary>
    /// Average glyph advance used for the static overflow estimate (pixels).
    /// Conservative value — intentionally generous to avoid false positives while still
    /// catching extreme overflow at reasonable slot widths.
    /// </summary>
    private const float AvgGlyphAdvancePx = 9f;

    /// <summary>Assumed font size for overflow estimate when no explicit size is detectable.</summary>
    private const float DefaultFontSizePx = 16f;

    /// <summary>Default slot width (pixels) for overflow estimates when not inferable from spec.</summary>
    private const float DefaultSlotWidthPx = 900f;

    private static readonly HashSet<string> AllowedTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "div", "h1", "h2", "p", "span", "img", "ul", "li"
    };

    private static readonly HashSet<string> AllowedColorKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "transparent", "none", "inherit", "currentcolor", "initial", "unset"
    };

    // Matches CSS hex colors: #RGB, #RRGGBB, #RRGGBBAA
    private static readonly Regex HexColorRegex =
        new(@"#([0-9A-Fa-f]{3,4}|[0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Matches font-family values in inline styles
    private static readonly Regex FontFamilyRegex =
        new(@"font-family\s*:\s*([^;""'>]+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Matches data-max-lines attribute value
    private static readonly Regex DataMaxLinesRegex =
        new(@"data-max-lines\s*=\s*[""']?(\d+)[""']?",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Matches data-slot attribute value
    private static readonly Regex DataSlotRegex =
        new(@"data-slot\s*=\s*[""']([^""']+)[""']",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public Result<VisualDesignOutput> Validate(
        VisualDesignOutput output,
        BrandManualSnapshot brand,
        VisualCompositionSpec spec)
    {
        // Step 1 — Schema validation (VD-04)
        var schemaResult = ValidateSchema(output.Markup);
        if (schemaResult.IsFailure)
        {
            return Result.Failure<VisualDesignOutput>(schemaResult.Error);
        }

        // Step 2 — Palette validation (VD-02)
        var paletteResult = ValidatePalette(output.Markup, brand);
        if (paletteResult.IsFailure)
        {
            return Result.Failure<VisualDesignOutput>(paletteResult.Error);
        }

        // Step 3 — Typography validation (VD-02)
        var typoResult = ValidateTypography(output.Markup, brand);
        if (typoResult.IsFailure)
        {
            return Result.Failure<VisualDesignOutput>(typoResult.Error);
        }

        // Step 4 — Overflow check: static heuristic (VD-03)
        var overflowResult = ValidateOverflow(output.Markup, spec);
        if (overflowResult.IsFailure)
        {
            return Result.Failure<VisualDesignOutput>(overflowResult.Error);
        }

        return Result.Success(output.WithValidated());
    }

    // -------------------------------------------------------------------------
    // Step 1: Schema
    // -------------------------------------------------------------------------

    private static Result ValidateSchema(string markup)
    {
        if (string.IsNullOrWhiteSpace(markup))
        {
            return Result.Failure(VisualGuardrailErrors.InvalidMarkup);
        }

        // Wrap in a root element to allow XML-parse of fragments
        var wrapped = $"<root>{markup}</root>";
        XmlDocument doc;
        try
        {
            doc = new XmlDocument();
            doc.LoadXml(wrapped);
        }
        catch
        {
            return Result.Failure(VisualGuardrailErrors.InvalidMarkup);
        }

        var root = doc.DocumentElement!;

        // Must have exactly one direct child of root with class="composition"
        var compositionNodes = root.ChildNodes
            .Cast<XmlNode>()
            .Where(n => n.NodeType == XmlNodeType.Element)
            .ToList();

        if (compositionNodes.Count != 1)
        {
            return Result.Failure(VisualGuardrailErrors.InvalidMarkup);
        }

        var composition = (XmlElement)compositionNodes[0];
        if (!HasClass(composition, "composition"))
        {
            return Result.Failure(VisualGuardrailErrors.InvalidMarkup);
        }

        // Check allowed tags recursively and mandatory data-layer children
        if (!AllTagsAllowed(composition))
        {
            return Result.Failure(VisualGuardrailErrors.InvalidMarkup);
        }

        // Mandatory: at least one data-layer="background" and one data-layer="content"
        if (!HasDataLayer(composition, "background") || !HasDataLayer(composition, "content"))
        {
            return Result.Failure(VisualGuardrailErrors.InvalidMarkup);
        }

        return Result.Success();
    }

    private static bool HasClass(XmlElement el, string className)
    {
        var cls = el.GetAttribute("class");
        return !string.IsNullOrWhiteSpace(cls)
            && cls.Split(' ').Contains(className, StringComparer.OrdinalIgnoreCase);
    }

    private static bool AllTagsAllowed(XmlNode node)
    {
        foreach (XmlNode child in node.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            if (!AllowedTags.Contains(child.Name))
            {
                return false;
            }

            if (!AllTagsAllowed(child))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasDataLayer(XmlElement parent, string layerValue)
    {
        foreach (XmlNode child in parent.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            var el = (XmlElement)child;
            if (string.Equals(el.GetAttribute("data-layer"), layerValue, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    // -------------------------------------------------------------------------
    // Step 2: Palette
    // -------------------------------------------------------------------------

    private static Result ValidatePalette(string markup, BrandManualSnapshot brand)
    {
        var brandPalette = brand.ColorPalette
            .Select(c => NormalizeHex(c))
            .Where(c => c is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase)!;

        foreach (Match match in HexColorRegex.Matches(markup))
        {
            var hex = NormalizeHex(match.Value);
            if (hex is null)
            {
                continue;
            }

            if (!brandPalette.Contains(hex) && !IsAllowedColorKeyword(match.Value))
            {
                return Result.Failure(VisualGuardrailErrors.OffPalette(match.Value));
            }
        }

        return Result.Success();
    }

    private static string? NormalizeHex(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var s = raw.Trim().TrimStart('#');
        // Expand 3-char shorthand to 6-char for comparison
        if (s.Length == 3)
        {
            s = $"{s[0]}{s[0]}{s[1]}{s[1]}{s[2]}{s[2]}";
        }

        return s.Length is 6 or 8 ? ("#" + s).ToUpperInvariant() : null;
    }

    private static bool IsAllowedColorKeyword(string value) =>
        AllowedColorKeywords.Contains(value.Trim());

    // -------------------------------------------------------------------------
    // Step 3: Typography
    // -------------------------------------------------------------------------

    private static Result ValidateTypography(string markup, BrandManualSnapshot brand)
    {
        // Build allow-list from brand.Typography. If empty, add the bundled fallback.
        var allowed = brand.Typography.Count > 0
            ? brand.Typography.ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase) { FallbackFontFamily };

        foreach (Match match in FontFamilyRegex.Matches(markup))
        {
            var raw = match.Groups[1].Value.Trim();
            // Font-family values can be comma-separated stacks: "Inter, Roboto, sans-serif"
            var families = raw
                .Split(',')
                .Select(f => f.Trim().Trim('\'', '"'))
                .Where(f => !string.IsNullOrWhiteSpace(f));

            foreach (var family in families)
            {
                // Generic CSS keywords are always allowed
                if (IsCssGenericFontFamily(family))
                {
                    continue;
                }

                if (!allowed.Contains(family))
                {
                    return Result.Failure(VisualGuardrailErrors.FontNotAllowed(family));
                }
            }
        }

        return Result.Success();
    }

    private static bool IsCssGenericFontFamily(string name) =>
        name.Equals("serif", StringComparison.OrdinalIgnoreCase)
        || name.Equals("sans-serif", StringComparison.OrdinalIgnoreCase)
        || name.Equals("monospace", StringComparison.OrdinalIgnoreCase)
        || name.Equals("cursive", StringComparison.OrdinalIgnoreCase)
        || name.Equals("fantasy", StringComparison.OrdinalIgnoreCase)
        || name.Equals("system-ui", StringComparison.OrdinalIgnoreCase)
        || name.Equals("ui-serif", StringComparison.OrdinalIgnoreCase)
        || name.Equals("ui-sans-serif", StringComparison.OrdinalIgnoreCase)
        || name.Equals("ui-monospace", StringComparison.OrdinalIgnoreCase);

    // -------------------------------------------------------------------------
    // Step 4: Overflow (static heuristic — B1a)
    // -------------------------------------------------------------------------

    private static Result ValidateOverflow(string markup, VisualCompositionSpec spec)
    {
        // Extract all [data-slot] elements with data-max-lines for overflow estimation.
        // Strategy: find each data-slot="..." + data-max-lines="..." pair in the markup.
        // For each, estimate the number of lines the text would occupy given the slot width
        // and compare to the allowed max.

        var slotWidth = spec.Dimensions.Width > 0
            ? spec.Dimensions.Width * 0.8f    // 80% of total width as a conservative slot width
            : DefaultSlotWidthPx;

        // Use a simple regex to find text-slot segments. We scan for all data-slot attributes
        // and measure the TEXT content within that element.
        // Parse via XmlDocument for correctness (already validated in step 1).
        var wrapped = $"<root>{markup}</root>";
        XmlDocument doc;
        try
        {
            doc = new XmlDocument();
            doc.LoadXml(wrapped);
        }
        catch
        {
            // If parsing fails here, schema step should have caught it already.
            return Result.Success();
        }

        var slotNodes = doc.DocumentElement!
            .SelectNodes("//*[@data-slot and @data-max-lines]")
            ?.Cast<XmlElement>()
            .ToList() ?? new List<XmlElement>();

        foreach (var slotEl in slotNodes)
        {
            var slotName = slotEl.GetAttribute("data-slot");
            if (!int.TryParse(slotEl.GetAttribute("data-max-lines"), out var maxLines) || maxLines <= 0)
            {
                continue;
            }

            var text = slotEl.InnerText ?? string.Empty;
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            // Static overflow estimate: characters × avg glyph advance ÷ slot width = lines
            var estimatedLines = (int)Math.Ceiling(
                text.Length * AvgGlyphAdvancePx / slotWidth);

            if (estimatedLines > maxLines)
            {
                return Result.Failure(VisualGuardrailErrors.TextOverflow(slotName));
            }
        }

        return Result.Success();
    }
}
