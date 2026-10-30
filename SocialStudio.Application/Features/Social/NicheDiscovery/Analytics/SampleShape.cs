namespace SocialStudio.Application.Features.Social.NicheDiscovery.Analytics;

/// <summary>
/// Sample-shape facts ONLY — never magnitudes (design §7.2). The independent input to
/// <c>ConfidenceScorer</c> (WU-8): scaling every metric magnitude leaves this record, and therefore
/// the confidence score, untouched.
/// </summary>
public sealed record SampleShape(
    int ChannelCount,
    int VideoCount,
    int DistinctChannelCount,
    int WindowCoverageDays,
    int SourceCount,
    int DataAgeDays,
    double? SignalConsistency);
