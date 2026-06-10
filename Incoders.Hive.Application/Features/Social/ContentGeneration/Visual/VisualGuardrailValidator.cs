using System.Text.RegularExpressions;
using System.Xml;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ContentGeneration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Visual;

/// <summary>
/// Concrete implementation of the visual guardrail pipeline (VD-02, VD-03, VD-04).
/// Four ordered steps with two enforcement levels:
///   1. Schema      — single root div.composition, allowed tags, mandatory data-layer attributes. HARD.
///   2. Palette     — colors SHOULD be in the brand palette. ADVISORY (logged, never blocks).
///   3. Typography  — fonts SHOULD be in the brand allow-list. ADVISORY (logged, never blocks).
///   4. Overflow    — static heuristic: char-count estimate vs data-max-lines per slot. HARD.
///
/// Product decision: the brand palette/typography are strong preferences, not gates — a flyer must
/// always generate even if the AI's design deviates (e.g. white text for legibility). Only the HARD
/// steps (a structurally invalid markup or text that won't fit the canvas) stop the render; the
/// SOFT steps are logged for observability and otherwise allowed through.
/// </summary>
public sealed class VisualGuardrailValidator : IVisualGuardrailValidator
{
    private readonly ILogger<VisualGuardrailValidator> _logger;

    public VisualGuardrailValidator(ILogger<VisualGuardrailValidator>? logger = null)
    {
        _logger = logger ?? NullLogger<VisualGuardrailValidator>.Instance;
    }

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
        // Structural + text tags the designer legitimately uses. Kept deliberately small, but it
        // must cover the common, render-safe HTML the LLM emits (line breaks, emphasis, headings,
        // ordered lists) — otherwise a perfectly renderable flyer is hard-rejected as invalid markup.
        "div", "h1", "h2", "h3", "h4", "p", "span", "img",
        "ul", "ol", "li", "br", "strong", "em", "b", "i", "u", "small"
    };

    /// <summary>XML's five built-in entities — left untouched when normalizing markup for the parser.</summary>
    private static readonly HashSet<string> XmlBuiltinEntities = new(StringComparer.Ordinal)
    {
        "amp", "lt", "gt", "quot", "apos"
    };

    /// <summary>
    /// HTML void elements. The designer emits these in HTML form (<c>&lt;img&gt;</c>, <c>&lt;br&gt;</c>)
    /// which Chromium renders fine but <see cref="XmlDocument"/> rejects — we self-close them before
    /// the structural XML parse only (the original markup still goes to the renderer unchanged).
    /// </summary>
    private static readonly Regex VoidElementRegex =
        new(@"<(?<tag>img|br|hr|input|meta|link|source|area|col|embed|track|wbr|base|param)(?<attrs>(?:\s[^<>]*?)?)\s*/?>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Matches a full HTML entity (named/numeric) or a bare ampersand.</summary>
    private static readonly Regex EntityOrAmpRegex =
        new(@"&(?:#x[0-9A-Fa-f]+|#[0-9]+|[A-Za-z][A-Za-z0-9]*);|&",
            RegexOptions.Compiled);

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
        // Step 1 — Schema validation (VD-04). HARD: a malformed composition cannot render.
        var schemaResult = ValidateSchema(output.Markup, spec.FormatKey);
        if (schemaResult.IsFailure)
        {
            return Result.Failure<VisualDesignOutput>(schemaResult.Error);
        }

        // Step 2 — Palette (VD-02). ADVISORY: the brand palette is a preference, not a gate.
        // We still want the design to use it "as much as possible" (the designer prompt enforces
        // that), but a deviation (e.g. white for text legibility) must NOT block generation —
        // we log it and render anyway.
        var paletteResult = ValidatePalette(output.Markup, brand);
        if (paletteResult.IsFailure)
        {
            _logger.LogWarning(
                "Visual guardrail (advisory): {Reason} Rendering anyway for format '{FormatKey}'.",
                paletteResult.Error.MessageKey, spec.FormatKey);
        }

        // Step 3 — Typography (VD-02). ADVISORY for the same reason; off-allowlist fonts fall back
        // gracefully in the renderer rather than failing the format.
        var typoResult = ValidateTypography(output.Markup, brand);
        if (typoResult.IsFailure)
        {
            _logger.LogWarning(
                "Visual guardrail (advisory): {Reason} Rendering anyway for format '{FormatKey}'.",
                typoResult.Error.MessageKey, spec.FormatKey);
        }

        // Step 4 — Overflow check: static heuristic (VD-03). HARD: protects legibility (text must
        // fit the canvas, not spill out).
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

    private Result ValidateSchema(string markup, string formatKey)
    {
        if (string.IsNullOrWhiteSpace(markup))
        {
            _logger.LogWarning("Visual guardrail schema: empty markup for format '{FormatKey}'.", formatKey);
            return Result.Failure(VisualGuardrailErrors.InvalidMarkup);
        }

        // The designer emits HTML; XmlDocument needs well-formed XML. Normalize render-safe HTML-isms
        // (unclosed void elements like <img>/<br>, HTML named entities, bare ampersands) so a
        // legitimately renderable composition is not falsely rejected. The ORIGINAL markup still
        // flows to the renderer unchanged — this normalization is only for the structural check.
        var wrapped = $"<root>{PrepareForXmlParse(markup)}</root>";
        XmlDocument doc;
        try
        {
            doc = new XmlDocument();
            doc.LoadXml(wrapped);
        }
        catch (XmlException ex)
        {
            // Capture WHY it failed — without this the failure is opaque ("invalid-markup") and
            // impossible to diagnose from production data. Logged, not surfaced (keeps i18n contract).
            _logger.LogWarning(ex,
                "Visual guardrail schema: markup is not well-formed XML for format '{FormatKey}'. Snippet: {Snippet}",
                formatKey, Truncate(markup, 600));
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
            _logger.LogWarning(
                "Visual guardrail schema: expected exactly one root element, found {Count} for format '{FormatKey}'.",
                compositionNodes.Count, formatKey);
            return Result.Failure(VisualGuardrailErrors.InvalidMarkup);
        }

        var composition = (XmlElement)compositionNodes[0];
        if (!HasClass(composition, "composition"))
        {
            _logger.LogWarning(
                "Visual guardrail schema: root element <{Tag}> missing class=\"composition\" for format '{FormatKey}'.",
                composition.Name, formatKey);
            return Result.Failure(VisualGuardrailErrors.InvalidMarkup);
        }

        // Check allowed tags recursively and mandatory data-layer children
        if (!AllTagsAllowed(composition, out var offendingTag))
        {
            _logger.LogWarning(
                "Visual guardrail schema: disallowed tag <{Tag}> for format '{FormatKey}'.",
                offendingTag, formatKey);
            return Result.Failure(VisualGuardrailErrors.InvalidMarkup);
        }

        // Mandatory: at least one data-layer="background" and one data-layer="content"
        if (!HasDataLayer(composition, "background") || !HasDataLayer(composition, "content"))
        {
            _logger.LogWarning(
                "Visual guardrail schema: missing required data-layer 'background'/'content' for format '{FormatKey}'.",
                formatKey);
            return Result.Failure(VisualGuardrailErrors.InvalidMarkup);
        }

        return Result.Success();
    }

    /// <summary>
    /// Makes designer HTML parseable by <see cref="XmlDocument"/> without changing how it renders:
    /// self-closes void elements and converts HTML named entities / bare ampersands into XML-safe forms.
    /// </summary>
    private static string PrepareForXmlParse(string markup)
    {
        // 1) Neutralize HTML entities XML doesn't know + escape bare ampersands.
        var withEntities = EntityOrAmpRegex.Replace(markup, static m =>
        {
            var token = m.Value;
            if (token == "&")
            {
                return "&amp;"; // bare ampersand
            }

            var inner = token[1..^1]; // strip leading '&' and trailing ';'
            if (inner[0] == '#' || XmlBuiltinEntities.Contains(inner))
            {
                return token; // numeric entity or XML built-in — already valid
            }

            var decoded = System.Net.WebUtility.HtmlDecode(token);
            if (string.Equals(decoded, token, StringComparison.Ordinal))
            {
                return "&amp;" + inner + ";"; // unknown entity — keep the text, escape the '&'
            }

            // Re-escape so a decoded char can never re-introduce a markup-significant character.
            return decoded
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;");
        });

        // 2) XML rejects raw '<' inside attribute values or text. Chromium is more forgiving,
        // and designers often use tiny CSS decorations like content:"<". Escape only the
        // parser-only copy so we can still validate render-safe HTML structurally.
        var withSafeAngles = EscapeRawLessThanForXml(withEntities);

        // 3) Self-close void elements (<img ...> -> <img ... />).
        return VoidElementRegex.Replace(withSafeAngles, "<${tag}${attrs} />");
    }

    private static string EscapeRawLessThanForXml(string markup)
    {
        var output = new System.Text.StringBuilder(markup.Length);
        var insideTag = false;
        char? quote = null;

        for (var i = 0; i < markup.Length; i++)
        {
            var ch = markup[i];

            if (insideTag)
            {
                if (quote is not null)
                {
                    if (ch == quote.Value)
                    {
                        quote = null;
                        output.Append(ch);
                    }
                    else if (ch == '<')
                    {
                        output.Append("&lt;");
                    }
                    else
                    {
                        output.Append(ch);
                    }

                    continue;
                }

                if (ch is '"' or '\'')
                {
                    quote = ch;
                    output.Append(ch);
                    continue;
                }

                if (ch == '>')
                {
                    insideTag = false;
                }

                output.Append(ch);
                continue;
            }

            if (ch == '<')
            {
                if (StartsHtmlMarkup(markup, i))
                {
                    insideTag = true;
                    output.Append(ch);
                }
                else
                {
                    output.Append("&lt;");
                }

                continue;
            }

            output.Append(ch);
        }

        return output.ToString();
    }

    private static bool StartsHtmlMarkup(string markup, int index)
    {
        if (index + 1 >= markup.Length)
        {
            return false;
        }

        var next = markup[index + 1];
        return char.IsLetter(next)
            || next is '/' or '!' or '?';
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";

    private static bool HasClass(XmlElement el, string className)
    {
        var cls = el.GetAttribute("class");
        return !string.IsNullOrWhiteSpace(cls)
            && cls.Split(' ').Contains(className, StringComparer.OrdinalIgnoreCase);
    }

    private static bool AllTagsAllowed(XmlNode node, out string? offendingTag)
    {
        foreach (XmlNode child in node.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            if (!AllowedTags.Contains(child.Name))
            {
                offendingTag = child.Name;
                return false;
            }

            if (!AllTagsAllowed(child, out offendingTag))
            {
                return false;
            }
        }

        offendingTag = null;
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
        // Parse via XmlDocument for correctness (already validated in step 1). Use the same
        // normalization so void elements / entities don't trip the parse here either.
        var wrapped = $"<root>{PrepareForXmlParse(markup)}</root>";
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
