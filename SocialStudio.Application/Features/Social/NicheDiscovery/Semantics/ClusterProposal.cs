namespace SocialStudio.Application.Features.Social.NicheDiscovery.Semantics;

/// <summary>
/// One AI-proposed (or lexically-derived, N11) cluster. EVERY member is a <c>string</c> or a
/// collection of <c>string</c> — there is nowhere to put a number (guard §3.2/design §8).
/// <see cref="PatternLabel"/> is a free-text label crossing the AI boundary; map it through
/// <see cref="ContentPatternKindMapper.FromLabel"/> to reach the closed <c>ContentPatternKind</c> enum
/// before it can influence anything downstream — unknown always maps to <c>Unclassified</c> (N11).
/// </summary>
public sealed record ClusterProposal(string ClusterLabel, string PatternLabel, IReadOnlyList<string> VideoExternalRefs);
