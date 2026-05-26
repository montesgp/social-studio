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

    public VisualCompositionService()
    {
        var composers = new IFormatVisualComposer[]
        {
            new CarouselVisualComposer(),
            new FlyerVisualComposer(),
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

        if (!_composers.TryGetValue(formatKey, out var composer))
        {
            throw new InvalidOperationException($"No visual composer registered for format key '{formatKey}'.");
        }

        var spec = composer.Compose(generatedPayload, brand, _lastInferred);
        return Task.FromResult(spec);
    }

    public IReadOnlyList<string> GetInferredFields() => _lastInferred.Fields;
}
