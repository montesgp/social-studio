using System.Text;

namespace Incoders.Hive.Application.Features.Social.NicheDiscovery.Semantics;

/// <summary>
/// Deterministic fallback when <see cref="SemanticAnnotator"/>'s gateway call fails or returns an
/// unparsable payload (design §8, N11). Tokenises title+description, drops &lt;4-char tokens and an
/// ES+EN stopword list, groups by shared token across ≥ K=3 DISTINCT CHANNELS — the same idiom as
/// <c>Social.ContentGeneration.Discovery.CoOccurrenceSignal</c>. Cluster name = the top (most-shared,
/// then alphabetically-first) token; <see cref="UnclassifiedPatternLabel"/> maps to
/// <c>ContentPatternKind.Unclassified</c> via <see cref="ContentPatternKindMapper"/> — this path never
/// legitimately identifies a real content pattern, so it never claims to. No format traits are ever
/// proposed: the lexical path has no basis to judge production format, so <c>AutomationSuitability</c>
/// becomes <c>NotReported</c> downstream rather than guessed. The run still ranks.
/// </summary>
public static class LexicalClusterFallback
{
    public const string UnclassifiedPatternLabel = "Unclassified";

    private const int MinTokenLength = 4;
    private const int ChannelThreshold = 3;

    // Small ES + EN stopword list — cheap, not exhaustive, mirroring CoOccurrenceSignal's own list.
    private static readonly HashSet<string> Stopwords = new(StringComparer.Ordinal)
    {
        "this", "that", "from", "with", "have", "your", "about", "into", "also", "were", "been", "they", "what", "when",
        "para", "como", "pero", "donde", "cuando", "desde", "hacia", "entre", "sobre", "estas", "estos", "este", "esta",
    };

    public static SemanticAnnotation Cluster(IReadOnlyList<SemanticAnnotationCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        if (candidates.Count == 0)
        {
            return new SemanticAnnotation([], [], []);
        }

        var tokensByCandidate = candidates.Select(Tokenize).ToList();

        var channelsByToken = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var candidateIndexesByToken = new Dictionary<string, List<int>>(StringComparer.Ordinal);

        for (var i = 0; i < candidates.Count; i++)
        {
            foreach (var token in tokensByCandidate[i])
            {
                if (!channelsByToken.TryGetValue(token, out var channels))
                {
                    channels = new HashSet<string>(StringComparer.Ordinal);
                    channelsByToken[token] = channels;
                    candidateIndexesByToken[token] = [];
                }

                channels.Add(candidates[i].ChannelExternalRef);
                candidateIndexesByToken[token].Add(i);
            }
        }

        var qualifyingTokens = channelsByToken
            .Where(kv => kv.Value.Count >= ChannelThreshold)
            .Select(kv => kv.Key)
            .OrderByDescending(token => channelsByToken[token].Count)
            .ThenBy(token => token, StringComparer.Ordinal)
            .ToList();

        var claimed = new HashSet<int>();
        var clusters = new List<ClusterProposal>();

        foreach (var token in qualifyingTokens)
        {
            var members = candidateIndexesByToken[token].Where(i => !claimed.Contains(i)).ToList();

            if (members.Count == 0)
            {
                continue;
            }

            foreach (var i in members)
            {
                claimed.Add(i);
            }

            clusters.Add(new ClusterProposal(
                ClusterLabel: token,
                PatternLabel: UnclassifiedPatternLabel,
                VideoExternalRefs: members.Select(i => candidates[i].ExternalRef).ToList()));
        }

        return new SemanticAnnotation(clusters, TopicLabels: [], FormatTraitLabels: []);
    }

    private static HashSet<string> Tokenize(SemanticAnnotationCandidate candidate)
    {
        var text = $"{candidate.Title} {candidate.Description}".ToLowerInvariant();

        var builder = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            builder.Append(char.IsLetterOrDigit(ch) ? ch : ' ');
        }

        return builder.ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token.Length >= MinTokenLength && !Stopwords.Contains(token))
            .ToHashSet(StringComparer.Ordinal);
    }
}
