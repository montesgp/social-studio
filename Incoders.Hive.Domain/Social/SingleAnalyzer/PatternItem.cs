using System.Text.Json.Serialization;
using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Domain.Social.SingleAnalyzer;

/// <summary>
/// Represents a single pattern identified during content analysis.
/// </summary>
public sealed record PatternItem
{
    [JsonConstructor]
    public PatternItem(string name, string evidence, double confidence)
    {
        Name = Normalize(name);
        Evidence = Normalize(evidence);
        Confidence = confidence;
    }

    public string Name { get; }

    public string Evidence { get; }

    /// <summary>Confidence score in the range [0.0, 1.0].</summary>
    public double Confidence { get; }

    public bool IsValid() =>
        !string.IsNullOrWhiteSpace(Name)
        && !string.IsNullOrWhiteSpace(Evidence)
        && Confidence is >= 0.0 and <= 1.0;

    public static Result<PatternItem> Create(string name, string evidence, double confidence)
    {
        var item = new PatternItem(name, evidence, confidence);
        if (!item.IsValid())
        {
            return Result.Failure<PatternItem>(SingleAnalyzerPatternErrors.PatternItemInvalid);
        }

        return Result.Success(item);
    }

    private static string Normalize(string? value) => (value ?? string.Empty).Trim();
}
