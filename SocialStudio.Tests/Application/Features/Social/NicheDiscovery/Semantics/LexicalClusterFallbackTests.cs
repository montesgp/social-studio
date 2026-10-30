using SocialStudio.Application.Features.Social.NicheDiscovery.Semantics;
using SocialStudio.Domain.Social.NicheDiscovery.Taxonomy;

namespace SocialStudio.Tests.Application.Features.Social.NicheDiscovery.Semantics;

/// <summary>
/// WU-10 (design §8, N11): gateway failure or unparsable payload triggers this fallback. Tokenises
/// title+description, drops &lt;4-char tokens and an ES+EN stopword list, groups by shared token across
/// ≥ K=3 distinct channels — the same idiom as
/// <c>Social.ContentGeneration.Discovery.CoOccurrenceSignal</c>. Cluster name = top token;
/// <see cref="ContentPatternKind.Unclassified"/> always; empty <c>FormatTraitLabels</c> ⇒
/// <c>AutomationSuitability</c> → <c>NotReported</c> downstream.
/// </summary>
public sealed class LexicalClusterFallbackTests
{
    [Fact]
    public void Cluster_NoCandidates_ReturnsEmptyAnnotation()
    {
        var annotation = LexicalClusterFallback.Cluster([]);

        Assert.Empty(annotation.Clusters);
        Assert.Empty(annotation.TopicLabels);
        Assert.Empty(annotation.FormatTraitLabels);
    }

    [Fact]
    public void Cluster_SharedTokenAcrossThreeDistinctChannels_FormsOneCluster()
    {
        var candidates = new[]
        {
            Candidate("v1", "chan-1", "Cómo elegir presupuesto viaje barato"),
            Candidate("v2", "chan-2", "Guía presupuesto viaje económico"),
            Candidate("v3", "chan-3", "Presupuesto viaje low cost 2026"),
        };

        var annotation = LexicalClusterFallback.Cluster(candidates);

        Assert.Single(annotation.Clusters);
        Assert.Equal(3, annotation.Clusters[0].VideoExternalRefs.Count);
    }

    [Fact]
    public void Cluster_SharedTokenAcrossOnlyTwoDistinctChannels_FormsNoCluster()
    {
        // Below K=3 distinct channels: no cluster forms — same channel repeated does not count twice.
        var candidates = new[]
        {
            Candidate("v1", "chan-1", "presupuesto viaje barato"),
            Candidate("v2", "chan-2", "presupuesto viaje económico"),
        };

        var annotation = LexicalClusterFallback.Cluster(candidates);

        Assert.Empty(annotation.Clusters);
    }

    [Fact]
    public void Cluster_DropsTokensShorterThanFourCharacters()
    {
        var candidates = new[]
        {
            Candidate("v1", "chan-1", "de la vez uno"),
            Candidate("v2", "chan-2", "de la vez dos"),
            Candidate("v3", "chan-3", "de la vez tres"),
        };

        var annotation = LexicalClusterFallback.Cluster(candidates);

        // "de", "la", "vez" are all under 4 characters — nothing qualifies as a shared cluster token.
        Assert.Empty(annotation.Clusters);
    }

    [Fact]
    public void Cluster_ClusterLabel_IsTheTopSharedToken()
    {
        var candidates = new[]
        {
            Candidate("v1", "chan-1", "presupuesto viaje barato"),
            Candidate("v2", "chan-2", "presupuesto viaje económico"),
            Candidate("v3", "chan-3", "presupuesto viaje 2026"),
        };

        var annotation = LexicalClusterFallback.Cluster(candidates);

        Assert.Contains(annotation.Clusters[0].ClusterLabel, new[] { "presupuesto", "viaje" });
    }

    [Fact]
    public void Cluster_PatternLabel_AlwaysMapsToContentPatternKindUnclassified()
    {
        var candidates = new[]
        {
            Candidate("v1", "chan-1", "presupuesto viaje barato"),
            Candidate("v2", "chan-2", "presupuesto viaje económico"),
            Candidate("v3", "chan-3", "presupuesto viaje 2026"),
        };

        var annotation = LexicalClusterFallback.Cluster(candidates);

        Assert.Equal(
            ContentPatternKind.Unclassified,
            ContentPatternKindMapper.FromLabel(annotation.Clusters[0].PatternLabel));
    }

    [Fact]
    public void Cluster_NeverProposesFormatTraits()
    {
        // Empty FormatTraitLabels ⇒ AutomationSuitability → NotReported downstream (N11 scenario): the
        // lexical path has no basis to judge production format at all.
        var candidates = new[]
        {
            Candidate("v1", "chan-1", "presupuesto viaje barato"),
            Candidate("v2", "chan-2", "presupuesto viaje económico"),
            Candidate("v3", "chan-3", "presupuesto viaje 2026"),
        };

        var annotation = LexicalClusterFallback.Cluster(candidates);

        Assert.Empty(annotation.FormatTraitLabels);
    }

    [Fact]
    public void Cluster_ACandidateAlreadyClaimedByAStrongerCluster_IsNotDoubleCounted()
    {
        var candidates = new[]
        {
            Candidate("v1", "chan-1", "presupuesto viaje barato aventura"),
            Candidate("v2", "chan-2", "presupuesto viaje económico aventura"),
            Candidate("v3", "chan-3", "presupuesto viaje 2026 aventura"),
            Candidate("v4", "chan-4", "aventura sola"),
        };

        var annotation = LexicalClusterFallback.Cluster(candidates);

        var allMembers = annotation.Clusters.SelectMany(c => c.VideoExternalRefs).ToList();
        Assert.Equal(allMembers.Count, allMembers.Distinct().Count());
    }

    // ── fixture builder ──────────────────────────────────────────────────────

    private static SemanticAnnotationCandidate Candidate(string externalRef, string channelExternalRef, string title) =>
        new(externalRef, channelExternalRef, title, Description: null);
}
