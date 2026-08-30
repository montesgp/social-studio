using System.Text.Json.Serialization;
using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Domain.Social.SingleAnalyzer;

/// <summary>
/// Value object holding the three pattern arrays extracted from a content analysis.
/// All three collections are required (may be empty); null arrays are rejected.
/// Equality is by value — two PatternSets with the same items in the same order are equal.
/// </summary>
public sealed class PatternSet : IEquatable<PatternSet>
{
    [JsonConstructor]
    public PatternSet(
        IReadOnlyList<PatternItem>? communicationPatterns,
        IReadOnlyList<PatternItem>? emotionalPatterns,
        IReadOnlyList<PatternItem>? conversionPatterns)
    {
        CommunicationPatterns = Normalize(communicationPatterns);
        EmotionalPatterns = Normalize(emotionalPatterns);
        ConversionPatterns = Normalize(conversionPatterns);
    }

    public IReadOnlyList<PatternItem> CommunicationPatterns { get; }

    public IReadOnlyList<PatternItem> EmotionalPatterns { get; }

    public IReadOnlyList<PatternItem> ConversionPatterns { get; }

    public bool IsValid() =>
        CommunicationPatterns is not null
        && EmotionalPatterns is not null
        && ConversionPatterns is not null
        && CommunicationPatterns.All(IsValidItem)
        && EmotionalPatterns.All(IsValidItem)
        && ConversionPatterns.All(IsValidItem);

    public bool Equals(PatternSet? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return CommunicationPatterns.SequenceEqual(other.CommunicationPatterns)
            && EmotionalPatterns.SequenceEqual(other.EmotionalPatterns)
            && ConversionPatterns.SequenceEqual(other.ConversionPatterns);
    }

    public override bool Equals(object? obj) => Equals(obj as PatternSet);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var item in CommunicationPatterns) hash.Add(item);
        foreach (var item in EmotionalPatterns) hash.Add(item);
        foreach (var item in ConversionPatterns) hash.Add(item);
        return hash.ToHashCode();
    }

    /// <summary>
    /// A fully empty PatternSet — represents "no patterns extracted yet".
    /// </summary>
    public static PatternSet Empty => new(
        Array.Empty<PatternItem>(),
        Array.Empty<PatternItem>(),
        Array.Empty<PatternItem>());

    public static Result<PatternSet> Create(
        IReadOnlyList<PatternItem> communicationPatterns,
        IReadOnlyList<PatternItem> emotionalPatterns,
        IReadOnlyList<PatternItem> conversionPatterns)
    {
        if (communicationPatterns is null
            || emotionalPatterns is null
            || conversionPatterns is null)
        {
            return Result.Failure<PatternSet>(SingleAnalyzerPatternErrors.PatternSetInvalid);
        }

        var patternSet = new PatternSet(
            communicationPatterns,
            emotionalPatterns,
            conversionPatterns);

        return patternSet.IsValid()
            ? Result.Success(patternSet)
            : Result.Failure<PatternSet>(SingleAnalyzerPatternErrors.PatternSetInvalid);
    }

    private static IReadOnlyList<PatternItem> Normalize(IReadOnlyList<PatternItem>? items) =>
        items?.Where(item => item is not null).ToArray() ?? Array.Empty<PatternItem>();

    private static bool IsValidItem(PatternItem? item) => item is not null && item.IsValid();
}
