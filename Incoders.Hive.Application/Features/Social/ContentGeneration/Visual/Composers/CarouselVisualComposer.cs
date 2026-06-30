using Incoders.Hive.Application.Features.Social.ContentGeneration.Formats.Carousel;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Incoders.Hive.Domain.Social.ContentGeneration;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Visual.Composers;

/// <summary>
/// Visual composer for carousel formats. Produces one <see cref="VisualCompositionSpec"/>
/// per slide in the carousel payload.
///
/// WU-4: implements <see cref="IBackgroundPlanAwareComposer"/> so the processor's pre-computed
/// <see cref="CarouselBackgroundPlan"/> can be threaded in to eliminate the per-slide
/// <c>BackgroundSelector.Select</c> call (C5 coherence fix). When a plan is provided, each
/// slide's <c>AssetSlots["slide-N"].ImageUrl</c> is set from <c>plan.ForSlide(N)</c> rather
/// than an independent selector call — ensuring composer and processor use the SAME decision.
///
/// Legacy path (no plan / IMultiAssetVisualComposer dispatch): falls back to
/// <c>BackgroundSelector.Select</c> per slide, preserving pre-WU-4 behavior exactly.
/// </summary>
internal sealed class CarouselVisualComposer : IFormatVisualComposer, IMultiAssetVisualComposer, IBackgroundPlanAwareComposer, IClientAestheticAwareComposer
{
    public string FormatKey => "carousel";

    /// <summary>
    /// Legacy single-spec path: returns a spec for the first slide only.
    /// Retained for backward compatibility — the pipeline now uses <see cref="ComposeMany"/>.
    /// </summary>
    public VisualCompositionSpec Compose(object generatedPayload, BrandSnapshot brand, InferenceAccumulator inferred)
    {
        var specs = ComposeMany(generatedPayload, brand, inferred);
        return specs.Count > 0
            ? specs[0]
            : BuildSlideSpec(brand, inferred, null, 1, "Carousel Title", string.Empty, backgroundSelection: null);
    }

    /// <summary>
    /// Returns one <see cref="VisualCompositionSpec"/> per slide — legacy path (no plan).
    /// Each slide independently calls <c>BackgroundSelector.Select</c> (pre-WU-4 behavior).
    /// </summary>
    public IReadOnlyList<VisualCompositionSpec> ComposeMany(
        object generatedPayload,
        BrandSnapshot brand,
        InferenceAccumulator inferred)
    {
        return ComposeManyCore(generatedPayload, brand, inferred, plan: null);
    }

    /// <summary>
    /// WU-4 C5: plan-aware path. Each slide reads <c>plan.ForSlide(slideIndex)</c> for its
    /// background selection instead of calling <c>BackgroundSelector.Select</c>.
    /// Both the asset slot URL and the designer's background input come from the same plan entry,
    /// killing the divergence that existed between the two call sites (CG-COH-02).
    /// </summary>
    public IReadOnlyList<VisualCompositionSpec> ComposeMany(
        object generatedPayload,
        BrandSnapshot brand,
        InferenceAccumulator inferred,
        CarouselBackgroundPlan plan)
    {
        return ComposeManyCore(generatedPayload, brand, inferred, plan, profile: null);
    }

    /// <summary>
    /// WU-3: aesthetic-profile-aware path. Passes the client's <see cref="ClientAestheticProfile"/>
    /// into per-slide palette resolution so the rotating colour pairing is applied.
    /// Each slide calls <c>CompositionDefaults.ResolvePalette(brand, profile, slideIndex, inferred)</c>
    /// which selects <c>pairings[(slideIndex-1) % count]</c> when pairings are present.
    /// Null profile or empty pairings degrade to the positional palette (CAC-BSP-04-a parity).
    /// Refs: CAC-CG-02; design §3.3.
    /// </summary>
    public IReadOnlyList<VisualCompositionSpec> ComposeMany(
        object generatedPayload,
        BrandSnapshot brand,
        InferenceAccumulator inferred,
        ClientAestheticProfile? profile)
    {
        return ComposeManyCore(generatedPayload, brand, inferred, plan: null, profile);
    }

    private IReadOnlyList<VisualCompositionSpec> ComposeManyCore(
        object generatedPayload,
        BrandSnapshot brand,
        InferenceAccumulator inferred,
        CarouselBackgroundPlan? plan,
        ClientAestheticProfile? profile = null)
    {
        var payload = PayloadReader.As<CarouselContent>(generatedPayload);
        if (payload is null || payload.Slides.Count == 0)
        {
            return [];
        }

        var specs = new List<VisualCompositionSpec>(payload.Slides.Count);
        for (var i = 0; i < payload.Slides.Count; i++)
        {
            var slide = payload.Slides[i];
            var slideIndex = i + 1; // 1-based

            // WU-4 C5: when a plan is available, read the per-slide selection from it.
            // When no plan is provided (legacy path), call BackgroundSelector.Select independently.
            BackgroundSelection? backgroundSelection = plan is not null
                ? plan.ForSlide(slideIndex)
                : null;

            specs.Add(BuildSlideSpec(brand, inferred, slide, slideIndex, payload.CoverTitle, payload.CtaText, backgroundSelection, profile));
        }

        return specs;
    }

    private VisualCompositionSpec BuildSlideSpec(
        BrandSnapshot brand,
        InferenceAccumulator inferred,
        CarouselSlide? slide,
        int slideIndex,
        string coverTitle,
        string ctaText,
        BackgroundSelection? backgroundSelection,
        ClientAestheticProfile? profile = null)
    {
        // WU-3: use the pairing/slide-index overload so rotating colour pairings are applied
        // when profile.ColorPairings is non-empty; null profile or empty pairings delegates
        // to the existing positional palette (cold-start parity — CAC-BSP-04-a).
        var palette = CompositionDefaults.ResolvePalette(brand, profile, slideIndex, inferred);
        var typography = CompositionDefaults.ResolveTypography(brand, inferred);

        // WU-4 C5: use the pre-resolved selection when provided; fall back to the
        // legacy 3-tier selector (BG-03) when null (no plan supplied by the processor).
        var resolvedSelection = backgroundSelection
            ?? SelectLegacy(brand);

        var (bgUrl, bgHint) = resolvedSelection switch
        {
            BackgroundSelection.ForcedDefault fd => (fd.Url, fd.Name),
            BackgroundSelection.References refs when refs.Entries.Count > 0 =>
                ((string?)refs.Entries[0].Url, refs.Entries[0].Name),
            _ => (null, slide?.ImagePromptHint ?? $"Slide {slideIndex} visual")
        };

        // WU-5: @handle slot — mirrors StoryVisualComposer via AttributionPolicy.ResolveSignature.
        // Populated only when the client has an explicit own-account handle (CAC-CG-03).
        // Slot is omitted (not empty) when Handle is null so the slide renders without it.
        var copySlots = new Dictionary<string, string>
        {
            ["title"] = slideIndex == 1 ? coverTitle : (slide?.Title ?? $"Slide {slideIndex}"),
            ["subtitle"] = slide is null ? "Supporting copy" : slide.Body,
            ["cta"] = ctaText,
        };

        if (brand.Client?.Handle is not null)
        {
            var signatureText = AttributionPolicy.ResolveSignature(brand, null);
            if (!string.IsNullOrWhiteSpace(signatureText))
            {
                copySlots["@handle"] = signatureText;
            }
        }

        return new VisualCompositionSpec(
            FormatKey: FormatKey,
            LayoutType: "grid",
            Palette: palette,
            Typography: typography,
            AssetSlots: new Dictionary<string, AssetSlotSpec>
            {
                [$"slide-{slideIndex}"] = new AssetSlotSpec(bgUrl, bgHint),
                ["logo"] = new AssetSlotSpec(brand.Brand?.Logos.FirstOrDefault(), "Brand logo"),
            },
            CopySlots: copySlots,
            Dimensions: CompositionDefaults.CarouselDimensions);
    }

    /// <summary>
    /// Legacy 3-tier background selector — called only when no pre-computed plan is available.
    /// Preserved as a private helper so the legacy fallback path stays in one place.
    /// </summary>
    private static BackgroundSelection SelectLegacy(BrandSnapshot brand)
    {
        var templates = brand.Brand?.BackgroundTemplates ?? Array.Empty<BrandBackgroundTemplateSnapshot>();
        return BackgroundSelector.Select(templates, "carousel");
    }
}
