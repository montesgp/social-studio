using Incoders.Hive.Domain.Social.ContentGeneration;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Visual;

/// <summary>
/// Per-format strategy for composing a <see cref="VisualCompositionSpec"/> from generated content.
/// One implementation per format key.
/// </summary>
public interface IFormatVisualComposer
{
    string FormatKey { get; }

    VisualCompositionSpec Compose(
        object generatedPayload,
        BrandSnapshot brand,
        InferenceAccumulator inferred);
}

/// <summary>
/// Accumulates brand fields that were inferred (not explicit) during composition.
/// Passed by reference into each composer strategy.
/// </summary>
public sealed class InferenceAccumulator
{
    private readonly List<string> _fields = new();

    public void Track(string fieldKey, bool wasInferred)
    {
        if (wasInferred && !_fields.Contains(fieldKey))
        {
            _fields.Add(fieldKey);
        }
    }

    public IReadOnlyList<string> Fields => _fields;
}
