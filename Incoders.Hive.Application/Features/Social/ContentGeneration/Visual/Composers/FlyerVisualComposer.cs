using System.Text.RegularExpressions;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Formats.Flyer;
using Incoders.Hive.Domain.Social.ContentGeneration;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Visual.Composers;

internal sealed class FlyerVisualComposer : IFormatVisualComposer
{
    public string FormatKey => "flyer";

    public VisualCompositionSpec Compose(object generatedPayload, BrandSnapshot brand, InferenceAccumulator inferred)
    {
        var payload = PayloadReader.As<FlyerContent>(generatedPayload);
        var palette = CompositionDefaults.ResolvePalette(brand, inferred);
        var typography = CompositionDefaults.ResolveTypography(brand, inferred);
        var backgroundTemplate = SelectBackgroundTemplate(brand.Brand?.BackgroundTemplates);
        inferred.Track("brand.BackgroundTemplates", backgroundTemplate is null);

        var copySlots = new Dictionary<string, string>
        {
            ["headline"] = payload?.Title ?? "Flyer Headline",
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

        return new VisualCompositionSpec(
            FormatKey: FormatKey,
            LayoutType: payload?.LayoutHint ?? "story-flyer",
            Palette: palette,
            Typography: typography,
            AssetSlots: new Dictionary<string, AssetSlotSpec>
            {
                ["background"] = new AssetSlotSpec(
                    backgroundTemplate?.Url,
                    backgroundTemplate is null
                        ? payload?.BackgroundPrompt ?? "AI-created branded story background"
                        : backgroundTemplate.Name),
                ["logo"] = new AssetSlotSpec(
                    brand.Brand?.Logos.FirstOrDefault(),
                    "Brand logo placeholder"),
            },
            CopySlots: copySlots,
            Dimensions: new CompositionDimensions(1080, 1920, 96));
    }

    private static BrandBackgroundTemplateSnapshot? SelectBackgroundTemplate(
        IReadOnlyList<BrandBackgroundTemplateSnapshot>? templates)
    {
        if (templates is null || templates.Count == 0)
        {
            return null;
        }

        return templates.FirstOrDefault(template =>
                ContainsAny(template.UseCase, "story", "stories", "flyer", "historia"))
            ?? templates[0];
    }

    private static bool ContainsAny(string? value, params string[] candidates) =>
        !string.IsNullOrWhiteSpace(value)
        && candidates.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));

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
