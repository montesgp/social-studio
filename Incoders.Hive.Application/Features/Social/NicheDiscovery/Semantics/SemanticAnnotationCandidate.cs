namespace Incoders.Hive.Application.Features.Social.NicheDiscovery.Semantics;

/// <summary>One video, reduced to what the semantic layer needs to classify, cluster and name it.</summary>
public sealed record SemanticAnnotationCandidate(
    string ExternalRef, string ChannelExternalRef, string Title, string? Description);

/// <summary>
/// <see cref="Annotation"/> plus whether it came from the gateway or <see cref="LexicalClusterFallback"/>
/// (N11) — <see cref="Degraded"/> feeds <c>discovery_runs.semantic_degraded</c> in WU-11a so a degraded
/// run is observably degraded, never silently presented as a full result.
/// </summary>
public sealed record SemanticAnnotationOutcome(SemanticAnnotation Annotation, bool Degraded);
