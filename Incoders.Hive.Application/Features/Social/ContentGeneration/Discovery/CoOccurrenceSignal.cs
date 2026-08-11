using System.Text;
using Incoders.Hive.Application.Abstractions.Social.ContentDiscovery;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Discovery;

/// <summary>
/// The lexical co-occurrence signal (design §6.4, OQ-7) — a cheap proxy for "several different
/// authors are independently covering the same topic right now", explicitly NOT clustering or NLP.
///
/// A candidate's raw signal is the MAX, over its own distinct tokens, of the number of distinct
/// author handles anywhere in the set whose candidate contains that token. Below <c>K = 3</c>
/// distinct authors, the raw signal is honestly <c>0</c> — available, not missing (it participates
/// in normalization like any other signal). Normalized by the set's distinct author count so the
/// value stays comparable across differently-sized discovery runs.
/// </summary>
public static class CoOccurrenceSignal
{
    private const int MinTokenLength = 4;

    /// <summary>Assumed product value (design §0): K = 3 distinct authors.</summary>
    private const int AuthorThreshold = 3;

    // Small ES + EN stopword list — cheap, not exhaustive; the point is to keep obviously
    // content-free connector words from driving a false co-occurrence match.
    private static readonly HashSet<string> Stopwords = new(StringComparer.Ordinal)
    {
        // English
        "this", "that", "from", "with", "have", "your", "about", "into", "also", "were", "been", "they", "what", "when",
        // Spanish
        "para", "como", "pero", "donde", "cuando", "desde", "hacia", "entre", "sobre", "estas", "estos", "este", "esta",
    };

    public static IReadOnlyList<double> Compute(IReadOnlyList<DiscoveredContent> set)
    {
        ArgumentNullException.ThrowIfNull(set);

        if (set.Count == 0)
        {
            return [];
        }

        var tokensByCandidate = set.Select(Tokenize).ToList();

        var authorsByToken = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        for (var i = 0; i < set.Count; i++)
        {
            var author = NormalizeHandle(set[i].SourceHandle);

            foreach (var token in tokensByCandidate[i])
            {
                if (!authorsByToken.TryGetValue(token, out var authors))
                {
                    authors = new HashSet<string>(StringComparer.Ordinal);
                    authorsByToken[token] = authors;
                }

                authors.Add(author);
            }
        }

        var distinctAuthorCount = set.Select(c => NormalizeHandle(c.SourceHandle)).Distinct().Count();

        var results = new List<double>(set.Count);

        for (var i = 0; i < set.Count; i++)
        {
            var tokens = tokensByCandidate[i];

            var raw = tokens.Count == 0
                ? 0
                : tokens.Max(token => authorsByToken[token].Count);

            // Below-threshold is an honest, available zero — not an excluded/missing signal.
            if (raw < AuthorThreshold)
            {
                raw = 0;
            }

            results.Add(distinctAuthorCount == 0 ? 0.0 : (double)raw / distinctAuthorCount);
        }

        return results;
    }

    private static string NormalizeHandle(string handle) => handle.Trim().ToLowerInvariant();

    private static HashSet<string> Tokenize(DiscoveredContent candidate)
    {
        var text = $"{candidate.Title} {candidate.Summary}".ToLowerInvariant();

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
