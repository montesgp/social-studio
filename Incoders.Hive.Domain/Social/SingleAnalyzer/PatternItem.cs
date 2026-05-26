using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Domain.Social.SingleAnalyzer;

/// <summary>
/// Represents a single pattern identified during content analysis.
/// </summary>
public sealed record PatternItem
{
    private PatternItem(string name, string evidence, double confidence)
    {
        Name = name;
        Evidence = evidence;
        Confidence = confidence;
    }

    public string Name { get; }

    public string Evidence { get; }

    /// <summary>Confidence score in the range [0.0, 1.0].</summary>
    public double Confidence { get; }

    public static Result<PatternItem> Create(string name, string evidence, double confidence)
    {
        if (string.IsNullOrWhiteSpace(name)
            || string.IsNullOrWhiteSpace(evidence)
            || confidence < 0.0
            || confidence > 1.0)
        {
            return Result.Failure<PatternItem>(SingleAnalyzerPatternErrors.PatternItemInvalid);
        }

        return Result.Success(new PatternItem(name.Trim(), evidence.Trim(), confidence));
    }
}
