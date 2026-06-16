using System.Text.RegularExpressions;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Formats.FeedPost;
using Incoders.Hive.Domain.Social.ContentGeneration;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Visual.Composers;

internal sealed class FeedPostVisualComposer : IFormatVisualComposer
{
    public string FormatKey => "feed-post";

    public VisualCompositionSpec Compose(object generatedPayload, BrandSnapshot brand, InferenceAccumulator inferred)
    {
        var payload = PayloadReader.As<FeedPostContent>(generatedPayload);
        var palette = CompositionDefaults.ResolvePalette(brand, inferred);
        var typography = CompositionDefaults.ResolveTypography(brand, inferred);

        // Tier-aware background selection (BG-03).
        var templates = brand.Brand?.BackgroundTemplates ?? Array.Empty<BrandBackgroundTemplateSnapshot>();
        var backgroundSelection = BackgroundSelector.Select(templates, FormatKey);
        inferred.Track("brand.BackgroundTemplates", backgroundSelection is BackgroundSelection.Creative);

        var copySlots = new Dictionary<string, string>
        {
            ["headline"] = payload?.Title ?? "Feed Post Headline",
            ["cta"] = payload?.CtaText ?? "Call to Action"
        };

        AddIfNotBlank(copySlots, "subtitle", payload?.Subtitle);
        AddIfNotBlank(copySlots, "body", payload?.Body);
        AddIfNotBlank(copySlots, "callouts", payload?.Callouts is { Count: > 0 }
            ? string.Join(" | ", payload.Callouts)
            : null);

        var signatureText = FirstNotBlank(
            payload?.SignatureText,
            ExtractHandleFromNotes(brand.Brand?.Notes));
        if (!string.IsNullOrWhiteSpace(signatureText))
        {
            copySlots["signature"] = signatureText;
        }

        // Resolve background asset slot from the tier-aware selection.
        var (bgUrl, bgHint) = backgroundSelection switch
        {
            BackgroundSelection.ForcedDefault fd => (fd.Url, fd.Name),
            BackgroundSelection.References refs when refs.Entries.Count > 0 =>
                ((string?)refs.Entries[0].Url, refs.Entries[0].Name),
            _ => (null, payload?.BackgroundPrompt ?? "AI-created branded feed post background")
        };

        return new VisualCompositionSpec(
            FormatKey: FormatKey,
            LayoutType: payload?.LayoutHint ?? "feed-post",
            Palette: palette,
            Typography: typography,
            AssetSlots: new Dictionary<string, AssetSlotSpec>
            {
                ["background"] = new AssetSlotSpec(bgUrl, bgHint),
                ["logo"] = new AssetSlotSpec(
                    brand.Brand?.Logos.FirstOrDefault(),
                    "Brand logo placeholder"),
            },
            CopySlots: copySlots,
            Dimensions: new CompositionDimensions(1080, 1080, 96));
    }

    private static string? ExtractHandleFromNotes(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
        {
            return null;
        }

        var match = Regex.Match(notes, @"@\w[\w\.\-_]{1,60}");
        return match.Success ? match.Value : null;
    }

    private static string? FirstNotBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static void AddIfNotBlank(Dictionary<string, string> slots, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            slots[key] = value;
        }
    }
}
