using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Visual.Composers;
using Incoders.Hive.Domain.Social.ContentGeneration;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Visual;

/// <summary>
/// Composes <see cref="VisualCompositionSpec"/> values for each supported content format.
/// Uses a strategy/registry pattern: one <see cref="IFormatVisualComposer"/> per format key.
/// </summary>
public sealed class VisualCompositionService : IVisualCompositionService
{
    /// <summary>Default palette exposed for test verification.</summary>
    public static CompositionPalette DefaultPalette => CompositionDefaults.Palette;

    private readonly IReadOnlyDictionary<string, IFormatVisualComposer> _composers;
    private InferenceAccumulator _lastInferred = new();

    /// <summary>
    /// Parameterless constructor that builds a hardcoded composer list.
    /// </summary>
    /// <remarks>
    /// DEPRECATED — use the DI constructor <see cref="VisualCompositionService(IEnumerable{IFormatVisualComposer})"/>.
    /// This ctor predates the FormatCatalog and does NOT include catalog-derived surface-scoped
    /// carousel composers (story-carousel, feed-carousel). Production code goes through DI;
    /// tests that need a no-DI instance must supply the full composer set explicitly
    /// (including both <c>SurfaceScopedCarouselVisualComposer</c> instances).
    /// </remarks>
    [Obsolete("Use the DI constructor; the hardcoded composer list does not include catalog-derived surface-scoped composers.", error: false)]
    public VisualCompositionService()
    {
        var composers = new IFormatVisualComposer[]
        {
            new CarouselVisualComposer(),
            new StoryVisualComposer(),
            new FeedPostVisualComposer(),
            new HookVisualComposer(),
            new CaptionVisualComposer(),
            new CtaVariationVisualComposer(),
            new ReelScriptVisualComposer(),
            new ContentIdeaVisualComposer(),
        };

        _composers = composers.ToDictionary(c => c.FormatKey, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Constructor for DI injection (when composers are registered externally).</summary>
    public VisualCompositionService(IEnumerable<IFormatVisualComposer> composers)
    {
        _composers = composers.ToDictionary(c => c.FormatKey, StringComparer.OrdinalIgnoreCase);
    }

    public Task<VisualCompositionSpec> ComposeAsync(
        string formatKey,
        object generatedPayload,
        BrandSnapshot brand,
        CancellationToken ct)
    {
        _lastInferred = new InferenceAccumulator();

        if (!_composers.TryGetValue(NormalizeComposerKey(formatKey), out var composer))
        {
            throw new InvalidOperationException($"No visual composer registered for format key '{formatKey}'.");
        }

        var spec = composer.Compose(generatedPayload, brand, _lastInferred);
        return Task.FromResult(spec);
    }

    public Task<IReadOnlyList<VisualCompositionSpec>> ComposeManyAsync(
        string formatKey,
        object generatedPayload,
        BrandSnapshot brand,
        CancellationToken ct,
        CarouselBackgroundPlan? plan = null)
    {
        _lastInferred = new InferenceAccumulator();

        if (!_composers.TryGetValue(NormalizeComposerKey(formatKey), out var composer))
        {
            throw new InvalidOperationException($"No visual composer registered for format key '{formatKey}'.");
        }

        // WU-4: When a pre-computed plan is available, thread it into plan-aware composers.
        // CarouselVisualComposer and SurfaceScopedCarouselVisualComposer implement IBackgroundPlanAwareComposer.
        if (plan is not null && composer is IBackgroundPlanAwareComposer planAware)
        {
            return Task.FromResult(planAware.ComposeMany(generatedPayload, brand, _lastInferred, plan));
        }

        // If the composer supports multi-asset composition, use it.
        if (composer is IMultiAssetVisualComposer multi)
        {
            return Task.FromResult(multi.ComposeMany(generatedPayload, brand, _lastInferred));
        }

        // Fall back to wrapping the single spec in a list.
        var spec = composer.Compose(generatedPayload, brand, _lastInferred);
        return Task.FromResult<IReadOnlyList<VisualCompositionSpec>>([spec]);
    }

    public IReadOnlyList<string> GetInferredFields() => _lastInferred.Fields;

    private static string NormalizeComposerKey(string formatKey) =>
        formatKey switch
        {
            "story" => "story-single",
            "feed-post" => "feed-single",
            _ => formatKey,
        };
}
