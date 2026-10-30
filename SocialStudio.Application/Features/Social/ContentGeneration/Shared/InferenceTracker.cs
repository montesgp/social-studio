namespace SocialStudio.Application.Features.Social.ContentGeneration.Shared;

/// <summary>
/// Utility for tracking which output fields were derived from inference
/// (i.e. absent explicit client brand input) rather than from persisted data.
///
/// Field names use dot-notation to describe the path, e.g.:
///   "brand.voice", "brand.palette.primary", "brand.logos"
///
/// Usage:
///   var tracker = new InferenceTracker(brandSnapshot.InferredFields);
///   if (tracker.IsInferred("VoiceAndTone")) output.VoiceTone = "[inferred]";
///   var inferred = tracker.GetInferredOutputFields();
/// </summary>
public sealed class InferenceTracker
{
    private readonly IReadOnlyDictionary<string, bool> _sourceInferredFields;
    private readonly List<string> _inferredOutputFields = [];

    /// <summary>
    /// Initialises the tracker with the brand snapshot's IsInferred map.
    /// Keys match <see cref="Domain.Social.ContentGeneration.BrandSnapshot.InferredFields"/>.
    /// </summary>
    public InferenceTracker(IReadOnlyDictionary<string, bool> sourceInferredFields)
    {
        ArgumentNullException.ThrowIfNull(sourceInferredFields);
        _sourceInferredFields = sourceInferredFields;
    }

    /// <summary>
    /// Returns true if the given brand source field was inferred (i.e. not present in client data).
    /// Brand field keys: ColorPalette, Logos, Typography, VoiceAndTone, Notes,
    ///                   ContentPillars, TargetAudience, PostingFrequency,
    ///                   MonthlyObjectives, MonthlyKpis,
    ///                   ContentStrategy, CalendarSlots.
    /// </summary>
    public bool IsInferred(string brandFieldKey) =>
        _sourceInferredFields.TryGetValue(brandFieldKey, out var inferred) && inferred;

    /// <summary>
    /// Marks an output field path as inferred and records it for the result manifest.
    /// Call this when the generator used inferred values to fill an output field.
    /// </summary>
    public void MarkInferred(string outputFieldPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputFieldPath);
        if (!_inferredOutputFields.Contains(outputFieldPath))
        {
            _inferredOutputFields.Add(outputFieldPath);
        }
    }

    /// <summary>
    /// Conditionally marks a field as inferred if the given brand source key is inferred.
    /// Returns true if the field was marked (i.e. the brand field is inferred).
    /// </summary>
    public bool TrackIfInferred(string brandFieldKey, string outputFieldPath)
    {
        if (!IsInferred(brandFieldKey)) return false;
        MarkInferred(outputFieldPath);
        return true;
    }

    /// <summary>
    /// Returns the list of output field paths that were marked as inferred during generation.
    /// Used to populate <see cref="FormatGenerationResult{TPayload}.InferredFields"/>.
    /// </summary>
    public IReadOnlyList<string> GetInferredOutputFields() =>
        _inferredOutputFields.AsReadOnly();
}
