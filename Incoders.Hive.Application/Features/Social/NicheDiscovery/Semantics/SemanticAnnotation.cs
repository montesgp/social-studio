namespace Incoders.Hive.Application.Features.Social.NicheDiscovery.Semantics;

/// <summary>
/// The whole AI (or lexical-fallback, N11) response contract (design §8). EVERY member is a
/// <c>string</c>, <c>bool</c> or <c>enum</c> — or a collection of those — never a number (guard §3.2,
/// asserted by <c>NicheDiscoverySemanticsNoNumericGuardTests</c>). The AI cannot emit a number because
/// this contract has nowhere to put one.
/// </summary>
public sealed record SemanticAnnotation(
    IReadOnlyList<ClusterProposal> Clusters,
    IReadOnlyList<string> TopicLabels,
    IReadOnlyList<string> FormatTraitLabels);
