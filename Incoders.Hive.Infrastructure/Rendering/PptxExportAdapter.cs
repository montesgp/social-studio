using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Pipeline;
using Incoders.Hive.Domain.Common;
using D = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace Incoders.Hive.Infrastructure.Rendering;

/// <summary>
/// <see cref="IPptxExportAdapter"/> implementation using <c>DocumentFormat.OpenXml</c>.
///
/// <para>
/// 100% offline (D3): no Chromium, no network, no AI. Converts the persisted
/// <see cref="CompositionSpecEnvelope"/> (markup + geometry + canvas) into an editable PPTX
/// with one <c>SlidePart</c> per asset.
/// </para>
///
/// <para>
/// Mapping rules:
/// <list type="bullet">
///   <item><description><c>[data-slot]</c> → editable text box positioned from persisted geometry (EMU).</description></item>
///   <item><description><c>backgroundImageRef</c> (data-URI) → full-bleed <c>Picture</c> behind text boxes.</description></item>
///   <item><description>Slide size from <c>canvas.Width × canvas.Height</c> at <c>canvas.Dpi</c>.</description></item>
///   <item><description>Slots without geometry fall back to a deterministic stacked layout.</description></item>
/// </list>
/// </para>
/// </summary>
public sealed class PptxExportAdapter : IPptxExportAdapter
{
    // EMU per pixel at 96 dpi (9525 = 914400 / 96).
    // We read the actual dpi from the envelope canvas and compute at build time.
    private const int EmuPerInch = 914400;

    private static readonly Regex DataUriRegex = new(
        @"^data:(?<mime>[^;]+);base64,(?<data>.+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <inheritdoc />
    public Task<Result<byte[]>> BuildAsync(CompositionSpecEnvelope envelope, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return BuildManyAsync([envelope], ct);
    }

    /// <inheritdoc />
    public Task<Result<byte[]>> BuildManyAsync(IReadOnlyList<CompositionSpecEnvelope> envelopes, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(envelopes);

        if (envelopes.Count == 0)
        {
            return Task.FromResult(Result.Failure<byte[]>(
                PptxExportErrors.BuildFailed("No composition specs to export.")));
        }

        foreach (var envelope in envelopes)
        {
            if (envelope.SchemaVersion != CompositionSpecEnvelope.CurrentSchemaVersion)
            {
                return Task.FromResult(Result.Failure<byte[]>(
                    PptxExportErrors.UnsupportedSchemaVersion(envelope.SchemaVersion)));
            }
        }

        try
        {
            var bytes = BuildPptx(envelopes);
            return Task.FromResult(Result.Success(bytes));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result.Failure<byte[]>(
                PptxExportErrors.BuildFailed(ex.Message)));
        }
    }

    // -----------------------------------------------------------------------
    // Core builder
    // -----------------------------------------------------------------------

    private static byte[] BuildPptx(IReadOnlyList<CompositionSpecEnvelope> envelopes)
    {
        using var ms = new MemoryStream();
        using (var prs = PresentationDocument.Create(ms, PresentationDocumentType.Presentation))
        {
            // PPTX has a single presentation-wide slide size — use the first envelope's canvas.
            // For multi-slide carousels every slide shares the same canvas, so this is exact.
            var first = envelopes[0];
            var emuPerPx = EmuPerPx(first.Canvas.Dpi);
            var slideWidthEmu = (long)(first.Canvas.Width * emuPerPx);
            var slideHeightEmu = (long)(first.Canvas.Height * emuPerPx);

            // ---- Presentation part ----
            var presentationPart = prs.AddPresentationPart();
            presentationPart.Presentation = CreatePresentation(slideWidthEmu, slideHeightEmu);

            // ---- Slide master (required by the OOXML spec) ----
            var slideMasterPart = presentationPart.AddNewPart<SlideMasterPart>("rId1");
            // A slide master REQUIRES an associated theme; without it the package is invalid
            // (PowerPoint repairs it, Canva's import rejects it). Add the theme before the master XML.
            var themePart = slideMasterPart.AddNewPart<ThemePart>("rIdTheme1");
            themePart.Theme = CreateMinimalTheme();
            themePart.Theme.Save();
            InitSlideMaster(slideMasterPart);

            var slideLayoutPart = slideMasterPart.AddNewPart<SlideLayoutPart>("rIdSL1");
            InitSlideLayout(slideLayoutPart);
            slideLayoutPart.AddPart(slideMasterPart, "rIdSM1");

            // ---- One slide per envelope ----
            for (var i = 0; i < envelopes.Count; i++)
            {
                var relationshipId = $"rIdSlide{i + 1}";
                var slidePart = presentationPart.AddNewPart<SlidePart>(relationshipId);
                AddSlide(slidePart, slideLayoutPart, envelopes[i], emuPerPx, slideWidthEmu, slideHeightEmu);

                presentationPart.Presentation.SlideIdList!.AppendChild(
                    new P.SlideId { Id = 256U + (uint)i, RelationshipId = relationshipId });
            }

            presentationPart.Presentation.Save();
        }

        return ms.ToArray();
    }

    private static P.Presentation CreatePresentation(long widthEmu, long heightEmu) =>
        new P.Presentation(
            new P.SlideMasterIdList(
                new P.SlideMasterId { Id = 2147483648U, RelationshipId = "rId1" }),
            new P.SlideIdList(),
            new P.SlideSize
            {
                Cx = (Int32Value)(int)widthEmu,
                Cy = (Int32Value)(int)heightEmu,
                Type = P.SlideSizeValues.Custom
            },
            new P.NotesSize { Cx = 6858000L, Cy = 9144000L });

    // -----------------------------------------------------------------------
    // Slide construction
    // -----------------------------------------------------------------------

    private static void AddSlide(
        SlidePart slidePart,
        SlideLayoutPart slideLayoutPart,
        CompositionSpecEnvelope envelope,
        double emuPerPx,
        long slideWidthEmu,
        long slideHeightEmu)
    {
        var spTree = new P.ShapeTree(
            new P.NonVisualGroupShapeProperties(
                new P.NonVisualDrawingProperties { Id = 1U, Name = "" },
                new P.NonVisualGroupShapeDrawingProperties(),
                new P.ApplicationNonVisualDrawingProperties()),
            new P.GroupShapeProperties(new D.TransformGroup()));

        // ---- 1. Background picture (behind text) ----
        if (!string.IsNullOrWhiteSpace(envelope.BackgroundImageRef))
        {
            var bgBytes = TryDecodeDataUri(envelope.BackgroundImageRef);
            if (bgBytes is not null)
            {
                var imagePart = slidePart.AddNewPart<ImagePart>("image/png", "rIdBg");
                using var imgMs = new MemoryStream(bgBytes);
                imagePart.FeedData(imgMs);

                spTree.AppendChild(BuildBackgroundPicture(slideWidthEmu, slideHeightEmu));
            }
        }

        // ---- 2. Text boxes from slot geometry ----
        uint shapeId = 2U;
        var slots = ExtractSlots(envelope.Markup);

        if (envelope.SlotGeometry.Count > 0)
        {
            // Geometry-driven layout (D3 normal path)
            foreach (var geo in envelope.SlotGeometry)
            {
                var slotText = slots.TryGetValue(geo.Slot, out var t) ? t : string.Empty;
                var x = (long)(geo.X * emuPerPx);
                var y = (long)(geo.Y * emuPerPx);
                var w = (long)(geo.Width * emuPerPx);
                var h = (long)(geo.Height * emuPerPx);

                spTree.AppendChild(BuildTextBox(shapeId++, geo.Slot, slotText, x, y, w, h, geo));
            }
        }
        else if (slots.Count > 0)
        {
            // Stacked fallback (no geometry — deterministic layout)
            var stackY = (long)(slideHeightEmu * 0.1);
            var stackH = (long)(slideHeightEmu * 0.1);
            var stackX = (long)(slideWidthEmu * 0.05);
            var stackW = (long)(slideWidthEmu * 0.9);

            foreach (var (slot, text) in slots)
            {
                spTree.AppendChild(BuildTextBox(shapeId++, slot, text, stackX, stackY, stackW, stackH, null));
                stackY += stackH + (long)(slideHeightEmu * 0.02);
            }
        }

        var slide = new P.Slide(
            new P.CommonSlideData(spTree),
            new P.ColorMapOverride(new D.MasterColorMapping()));

        slidePart.Slide = slide;
        slidePart.AddPart(slideLayoutPart, "rIdSL");
        slidePart.Slide.Save();
    }

    // -----------------------------------------------------------------------
    // Shape builders
    // -----------------------------------------------------------------------

    private static P.Shape BuildTextBox(
        uint shapeId,
        string slotName,
        string text,
        long xEmu,
        long yEmu,
        long wEmu,
        long hEmu,
        SlotGeometryEntry? geo)
    {
        // Font size: px → pt (1px = 0.75pt); OpenXML uses hundredths of a point
        var fontSizeHundredths = geo is not null && geo.FontSizePx > 0
            ? (int)(geo.FontSizePx * 0.75 * 100)
            : 2000; // 20pt default

        var fontFamily = geo?.FontFamily ?? "Calibri";
        var hexColor = CssColorToHex(geo?.Color) ?? "000000";
        var justification = CssTextAlignToJustification(geo?.TextAlign);

        // Fidelity attributes (CT_TextCharacterProperties attributes — order-independent).
        var runProps = new D.RunProperties { Language = "en-US", FontSize = fontSizeHundredths };
        if (IsBold(geo?.FontWeight))
        {
            runProps.Bold = true;
        }

        if (string.Equals(geo?.FontStyle, "italic", StringComparison.OrdinalIgnoreCase))
        {
            runProps.Italic = true;
        }

        if (string.Equals(geo?.TextTransform, "uppercase", StringComparison.OrdinalIgnoreCase))
        {
            runProps.Capital = D.TextCapsValues.All;
        }

        // letter-spacing: px → 1/100 pt (spc). px*0.75 = pt, ×100 = 1/100 pt. May be negative (tight).
        if (geo is not null && Math.Abs(geo.LetterSpacingPx) > 0.01)
        {
            runProps.Spacing = (int)Math.Round(geo.LetterSpacingPx * 75);
        }

        // OOXML CT_TextCharacterProperties child order is fixed: the fill (a:solidFill) MUST come
        // BEFORE the latin font (a:latin). Emitting them in the wrong order produces a schema-invalid
        // run — PowerPoint silently repairs it, but stricter importers (Canva) reject the whole file.
        runProps.AppendChild(new D.SolidFill(new D.RgbColorModelHex { Val = hexColor }));
        if (!string.IsNullOrWhiteSpace(fontFamily))
        {
            runProps.AppendChild(new D.LatinFont { Typeface = fontFamily });
        }

        // Paragraph: alignment (attribute) + line spacing (lnSpc — must be the FIRST child).
        var paraProps = new D.ParagraphProperties { Alignment = justification };
        if (geo is not null && geo.LineHeightPx > 0 && geo.FontSizePx > 0)
        {
            var ratio = Math.Clamp(geo.LineHeightPx / geo.FontSizePx, 0.8, 3.0);
            paraProps.AppendChild(new D.LineSpacing(
                new D.SpacingPercent { Val = (int)Math.Round(ratio * 100000) }));
        }

        var paragraph = new D.Paragraph(paraProps, new D.Run(runProps, new D.Text(text)));

        // Vertically center text within its measured box so headings/CTAs don't top-stick.
        var bodyProps = new D.BodyProperties
        {
            Wrap = D.TextWrappingValues.Square,
            Anchor = D.TextAnchoringTypeValues.Center
        };
        var shape = new P.Shape(
            new P.NonVisualShapeProperties(
                new P.NonVisualDrawingProperties { Id = shapeId, Name = slotName },
                new P.NonVisualShapeDrawingProperties(new D.ShapeLocks { NoGrouping = true }),
                new P.ApplicationNonVisualDrawingProperties(
                    new P.PlaceholderShape { Type = P.PlaceholderValues.Body })),
            new P.ShapeProperties(
                new D.Transform2D(
                    new D.Offset { X = xEmu, Y = yEmu },
                    new D.Extents { Cx = wEmu, Cy = hEmu }),
                new D.PresetGeometry(new D.AdjustValueList()) { Preset = D.ShapeTypeValues.Rectangle }),
            new P.TextBody(bodyProps, new D.ListStyle(), paragraph));

        return shape;
    }

    private static P.Picture BuildBackgroundPicture(long slideWidthEmu, long slideHeightEmu) =>
        new P.Picture(
            new P.NonVisualPictureProperties(
                new P.NonVisualDrawingProperties { Id = 1U, Name = "background" },
                new P.NonVisualPictureDrawingProperties(new D.PictureLocks { NoChangeAspect = true }),
                new P.ApplicationNonVisualDrawingProperties()),
            new P.BlipFill(
                new D.Blip { Embed = "rIdBg" },
                new D.Stretch(new D.FillRectangle())),
            new P.ShapeProperties(
                new D.Transform2D(
                    new D.Offset { X = 0L, Y = 0L },
                    new D.Extents { Cx = slideWidthEmu, Cy = slideHeightEmu }),
                new D.PresetGeometry(new D.AdjustValueList()) { Preset = D.ShapeTypeValues.Rectangle }));

    // -----------------------------------------------------------------------
    // Slide master / layout (minimal stubs required by OOXML spec)
    // -----------------------------------------------------------------------

    private static void InitSlideMaster(SlideMasterPart part)
    {
        part.SlideMaster = new P.SlideMaster(
            new P.CommonSlideData(
                new P.ShapeTree(
                    new P.NonVisualGroupShapeProperties(
                        new P.NonVisualDrawingProperties { Id = 1U, Name = "" },
                        new P.NonVisualGroupShapeDrawingProperties(),
                        new P.ApplicationNonVisualDrawingProperties()),
                    new P.GroupShapeProperties(new D.TransformGroup()))),
            // Must be the Presentation-namespace clrMap (p:clrMap), NOT the Drawing one (a:clrMap).
            new P.ColorMap
            {
                Background1 = D.ColorSchemeIndexValues.Light1,
                Text1 = D.ColorSchemeIndexValues.Dark1,
                Background2 = D.ColorSchemeIndexValues.Light2,
                Text2 = D.ColorSchemeIndexValues.Dark2,
                Accent1 = D.ColorSchemeIndexValues.Accent1,
                Accent2 = D.ColorSchemeIndexValues.Accent2,
                Accent3 = D.ColorSchemeIndexValues.Accent3,
                Accent4 = D.ColorSchemeIndexValues.Accent4,
                Accent5 = D.ColorSchemeIndexValues.Accent5,
                Accent6 = D.ColorSchemeIndexValues.Accent6,
                Hyperlink = D.ColorSchemeIndexValues.Hyperlink,
                FollowedHyperlink = D.ColorSchemeIndexValues.FollowedHyperlink
            },
            new P.SlideLayoutIdList(
                new P.SlideLayoutId { Id = 2147483649U, RelationshipId = "rIdSL1" }));
        part.SlideMaster.Save();
    }

    /// <summary>
    /// Builds a minimal but schema-valid Office theme. A slide master must reference a theme; a
    /// stricter importer (Canva) fails the whole file if it is missing or malformed.
    /// </summary>
    private static D.Theme CreateMinimalTheme()
    {
        D.SolidFill PhClrFill() => new(new D.SchemeColor { Val = D.SchemeColorValues.PhColor });

        return new D.Theme(
            new D.ThemeElements(
                new D.ColorScheme(
                    new D.Dark1Color(new D.SystemColor { Val = D.SystemColorValues.WindowText, LastColor = "000000" }),
                    new D.Light1Color(new D.SystemColor { Val = D.SystemColorValues.Window, LastColor = "FFFFFF" }),
                    new D.Dark2Color(new D.RgbColorModelHex { Val = "44546A" }),
                    new D.Light2Color(new D.RgbColorModelHex { Val = "E7E6E6" }),
                    new D.Accent1Color(new D.RgbColorModelHex { Val = "4472C4" }),
                    new D.Accent2Color(new D.RgbColorModelHex { Val = "ED7D31" }),
                    new D.Accent3Color(new D.RgbColorModelHex { Val = "A5A5A5" }),
                    new D.Accent4Color(new D.RgbColorModelHex { Val = "FFC000" }),
                    new D.Accent5Color(new D.RgbColorModelHex { Val = "5B9BD5" }),
                    new D.Accent6Color(new D.RgbColorModelHex { Val = "70AD47" }),
                    new D.Hyperlink(new D.RgbColorModelHex { Val = "0563C1" }),
                    new D.FollowedHyperlinkColor(new D.RgbColorModelHex { Val = "954F72" }))
                { Name = "Office" },
                new D.FontScheme(
                    new D.MajorFont(
                        new D.LatinFont { Typeface = "Calibri Light" },
                        new D.EastAsianFont { Typeface = "" },
                        new D.ComplexScriptFont { Typeface = "" }),
                    new D.MinorFont(
                        new D.LatinFont { Typeface = "Calibri" },
                        new D.EastAsianFont { Typeface = "" },
                        new D.ComplexScriptFont { Typeface = "" }))
                { Name = "Office" },
                new D.FormatScheme(
                    new D.FillStyleList(PhClrFill(), PhClrFill(), PhClrFill()),
                    new D.LineStyleList(
                        new D.Outline(PhClrFill()) { Width = 6350 },
                        new D.Outline(PhClrFill()) { Width = 12700 },
                        new D.Outline(PhClrFill()) { Width = 19050 }),
                    new D.EffectStyleList(
                        new D.EffectStyle(new D.EffectList()),
                        new D.EffectStyle(new D.EffectList()),
                        new D.EffectStyle(new D.EffectList())),
                    new D.BackgroundFillStyleList(PhClrFill(), PhClrFill(), PhClrFill()))
                { Name = "Office" }),
            new D.ObjectDefaults(),
            new D.ExtraColorSchemeList())
        { Name = "Office Theme" };
    }

    private static void InitSlideLayout(SlideLayoutPart part)
    {
        part.SlideLayout = new P.SlideLayout(
            new P.CommonSlideData(
                new P.ShapeTree(
                    new P.NonVisualGroupShapeProperties(
                        new P.NonVisualDrawingProperties { Id = 1U, Name = "" },
                        new P.NonVisualGroupShapeDrawingProperties(),
                        new P.ApplicationNonVisualDrawingProperties()),
                    new P.GroupShapeProperties(new D.TransformGroup()))),
            new P.ColorMapOverride(new D.MasterColorMapping()));
        part.SlideLayout.Save();
    }

    // -----------------------------------------------------------------------
    // HTML slot text extraction (regex — no HtmlAgilityPack dependency)
    // -----------------------------------------------------------------------

    private static readonly Regex SlotRegex = new(
        @"<[^>]+data-slot=""(?<slot>[^""]+)""[^>]*>(?<inner>.*?)</[^>]+>",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

    private static readonly Regex StripTagsRegex = new(
        @"<[^>]+>",
        RegexOptions.Compiled);

    private static Dictionary<string, string> ExtractSlots(string markup)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in SlotRegex.Matches(markup))
        {
            var slot = m.Groups["slot"].Value;
            var inner = m.Groups["inner"].Value;
            // Strip nested HTML tags and normalize whitespace
            var text = StripTagsRegex.Replace(inner, string.Empty).Trim();
            text = System.Net.WebUtility.HtmlDecode(text);
            text = Regex.Replace(text, @"\s+", " ");
            result[slot] = text;
        }

        return result;
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>Converts px → EMU given the canvas DPI. Default 96 dpi → 9525 EMU/px.</summary>
    private static double EmuPerPx(int dpi) =>
        dpi > 0 ? (double)EmuPerInch / dpi : 9525.0;

    /// <summary>
    /// Decodes a data-URI (<c>data:image/png;base64,...</c>) to raw bytes.
    /// Returns null for non-data-URI strings or on decode failure.
    /// </summary>
    private static byte[]? TryDecodeDataUri(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var m = DataUriRegex.Match(value);
        if (!m.Success)
        {
            return null;
        }

        try
        {
            return Convert.FromBase64String(m.Groups["data"].Value);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Converts a CSS color string (rgb(r,g,b) or #rrggbb or named) to a 6-char uppercase hex string.
    /// Returns null when input is empty.
    /// </summary>
    private static string? CssColorToHex(string? cssColor)
    {
        if (string.IsNullOrWhiteSpace(cssColor))
        {
            return null;
        }

        // rgb(r, g, b) format
        var rgbMatch = Regex.Match(cssColor, @"rgb\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*\)");
        if (rgbMatch.Success)
        {
            var r = int.Parse(rgbMatch.Groups[1].Value);
            var g = int.Parse(rgbMatch.Groups[2].Value);
            var b = int.Parse(rgbMatch.Groups[3].Value);
            return $"{r:X2}{g:X2}{b:X2}";
        }

        // #rrggbb or #rgb hex format
        var hexMatch = Regex.Match(cssColor, @"^#([0-9A-Fa-f]{6}|[0-9A-Fa-f]{3})$");
        if (hexMatch.Success)
        {
            var hex = hexMatch.Groups[1].Value;
            return hex.Length == 3
                ? $"{hex[0]}{hex[0]}{hex[1]}{hex[1]}{hex[2]}{hex[2]}"
                : hex.ToUpperInvariant();
        }

        return "000000";
    }

    /// <summary>Bold when the computed font-weight is "bold" or numeric ≥ 600.</summary>
    private static bool IsBold(string? fontWeight)
    {
        if (string.IsNullOrWhiteSpace(fontWeight))
        {
            return false;
        }

        if (string.Equals(fontWeight, "bold", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return int.TryParse(fontWeight, out var weight) && weight >= 600;
    }

    private static D.TextAlignmentTypeValues? CssTextAlignToJustification(string? textAlign) =>
        textAlign?.ToLowerInvariant() switch
        {
            "left" => D.TextAlignmentTypeValues.Left,
            "center" => D.TextAlignmentTypeValues.Center,
            "right" => D.TextAlignmentTypeValues.Right,
            "justify" => D.TextAlignmentTypeValues.Justified,
            _ => null
        };
}

/// <summary>Error factory for PPTX export failures.</summary>
file static class PptxExportErrors
{
    public static Error UnsupportedSchemaVersion(int version) =>
        Error.Validation(
            "pptx.unsupported-schema-version",
            $"CompositionSpecEnvelope schema version {version} is not supported. Expected {CompositionSpecEnvelope.CurrentSchemaVersion}.");

    public static Error BuildFailed(string detail) =>
        Error.Failure("pptx.build-failed", $"Failed to build PPTX: {detail}");
}
