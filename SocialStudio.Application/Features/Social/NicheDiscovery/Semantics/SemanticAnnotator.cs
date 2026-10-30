using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SocialStudio.Application.Abstractions.Ai;
using SocialStudio.Application.Abstractions.Integrations;
using SocialStudio.Domain.Common;

namespace SocialStudio.Application.Features.Social.NicheDiscovery.Semantics;

/// <summary>
/// Classification, clustering, cluster naming, content-pattern detection and topic/entity
/// identification (ND-07, design §8) — routed through <see cref="IAiCompletionGateway"/> with
/// <c>CapabilityKey = "ai-analysis"</c>. On failure or an unparsable payload it falls back to
/// <see cref="LexicalClusterFallback"/> (N11): the run still ranks, marked <see cref="SemanticAnnotationOutcome.Degraded"/>.
///
/// FLAGGED DEVIATION from design §8's literal five-prompt-version-key framing: the five keys are
/// reserved as named constants below, but today's implementation issues ONE consolidated gateway call
/// on <see cref="ClusterPromptVersionKey"/> — the <see cref="SemanticAnnotation"/> contract already
/// combines cluster/name/pattern/topic/format in one JSON shape, and a genuinely staged five-call
/// pipeline (classify → cluster → name → pattern → topic) would multiply run latency and AI-completion
/// budget 5× with no capability this MVP requires. Decomposing into real staged calls, each memoised
/// independently, is a wiring change against these same constants, not a new vocabulary.
///
/// Within-run memoisation by SHA-256 of the canonical JSON payload (N10): identical candidates are never
/// re-annotated twice on the same <see cref="SemanticAnnotator"/> instance — one instance is scoped to
/// one run.
/// </summary>
public sealed class SemanticAnnotator
{
    public const string CapabilityKey = "ai-analysis";
    public const string ClassifyPromptVersionKey = "niche-discovery.classify.v1";
    public const string ClusterPromptVersionKey = "niche-discovery.cluster.v1";
    public const string NameClusterPromptVersionKey = "niche-discovery.name-cluster.v1";
    public const string ExtractPatternPromptVersionKey = "niche-discovery.extract-pattern.v1";
    public const string IdentifyTopicPromptVersionKey = "niche-discovery.identify-topic.v1";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>The workflow step the operator configures to choose the AI provider/model.</summary>
    public const string WorkflowKey = "niche-discovery";
    public const string AnalysisType = "market-opportunity";
    public const string StepKey = "semantic-clustering";

    private readonly IAiCompletionGateway _ai;
    private readonly IWorkflowConfigurationResolver _workflowResolver;
    private readonly Dictionary<string, SemanticAnnotation> _memo = new(StringComparer.Ordinal);

    public SemanticAnnotator(IAiCompletionGateway ai, IWorkflowConfigurationResolver workflowResolver)
    {
        ArgumentNullException.ThrowIfNull(ai);
        ArgumentNullException.ThrowIfNull(workflowResolver);
        _ai = ai;
        _workflowResolver = workflowResolver;
    }

    public async Task<SemanticAnnotationOutcome> AnnotateAsync(
        Guid organizationId,
        IReadOnlyList<SemanticAnnotationCandidate> candidates,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        if (candidates.Count == 0)
        {
            return new SemanticAnnotationOutcome(new SemanticAnnotation([], [], []), Degraded: false);
        }

        var inputJson = JsonSerializer.Serialize(BuildInput(candidates), JsonOptions);
        var cacheKey = ComputeSha256(inputJson);

        if (_memo.TryGetValue(cacheKey, out var cached))
        {
            return new SemanticAnnotationOutcome(cached, Degraded: false);
        }

        // The gateway rejects a blank provider key before calling anything, so the route has to be
        // resolved HERE — the same way every other AI caller in the system does it. Sending null made the
        // semantic layer permanently unreachable: every run degraded to the lexical fallback.
        var route = await _workflowResolver.ResolveStepAsync(
            new WorkflowStepResolutionRequest(organizationId, WorkflowKey, AnalysisType, StepKey, StepKey, NetworkKey: null),
            cancellationToken);

        var candidate = route.IsSuccess
            ? route.Value.Where(c => c.IsEnabled).OrderBy(c => c.Priority).FirstOrDefault()
            : null;

        if (candidate is null)
        {
            // A legitimate state, not a crash: the run still ranks on deterministic metrics, and the
            // labels come from the lexical path. Degraded is how the operator learns to configure it.
            return new SemanticAnnotationOutcome(LexicalClusterFallback.Cluster(candidates), Degraded: true);
        }

        Result<AiJsonCompletionResponse<SemanticAnnotation>> completion;
        try
        {
            completion = await _ai.CompleteJsonAsync<SemanticAnnotation>(
                new AiJsonCompletionRequest(
                    organizationId,
                    AnalysisId: null,
                    CapabilityKey,
                    ClusterPromptVersionKey,
                    inputJson,
                    PreferredProviderKey: candidate.ProviderKey,
                    PreferredModel: candidate.ModelId),
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return new SemanticAnnotationOutcome(LexicalClusterFallback.Cluster(candidates), Degraded: true);
        }

        if (completion.IsFailure)
        {
            return new SemanticAnnotationOutcome(LexicalClusterFallback.Cluster(candidates), Degraded: true);
        }

        _memo[cacheKey] = completion.Value.Payload;

        return new SemanticAnnotationOutcome(completion.Value.Payload, Degraded: false);
    }

    private static object BuildInput(IReadOnlyList<SemanticAnnotationCandidate> candidates) => new
    {
        instruction =
            "Classify, cluster and name these video candidates. Return ONLY the raw JSON object matching " +
            "the SemanticAnnotation contract: clusters (clusterLabel, patternLabel, videoExternalRefs), " +
            "topicLabels, formatTraitLabels. Labels and groupings only — never a number.",
        candidates = candidates.Select(c => new
        {
            c.ExternalRef,
            c.ChannelExternalRef,
            c.Title,
            c.Description,
        }),
    };

    private static string ComputeSha256(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexStringLower(bytes);
    }
}
