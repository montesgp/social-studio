using System.Globalization;
using System.Text;

namespace SocialStudio.Application.Features.Social.ContentGeneration.Discovery;

/// <summary>
/// Turns a client's niche — FREE TEXT such as <c>"Medical &amp; health"</c> — into the small set of
/// plausible hashtags a niche search can actually ask a network for.
///
/// WHY THIS EXISTS: a niche phrase is not a hashtag. Removing only whitespace produced
/// <c>Medical&amp;health</c>, which no network can host (hashtags carry neither spaces nor
/// punctuation), so the provider answered "this profile/hashtag does not exist".
///
/// WHY IN APPLICATION: this is provider-agnostic text handling. Instagram and YouTube niche search
/// will need exactly the same derivation, so it lives beside the other discovery helpers rather than
/// inside one network's adapter. It is pure — no HTTP, no persistence, no provider types.
///
/// WHY SEVERAL HASHTAGS: both forms of a two-word niche are plausible real tags (<c>#medical</c> and
/// <c>#medicalhealth</c>) and we cannot know in advance which one exists on the network. The provider
/// input is an array, so the candidates travel in ONE call instead of one guess per call.
/// </summary>
public static class NicheHashtagDerivation
{
    /// <summary>
    /// Upper bound on the hashtags emitted for one niche. Each extra hashtag is extra work for the
    /// provider — and on usage-billed actors, extra money — so a long phrase must not fan one search
    /// out into an unbounded run. Five keeps the common one-to-three-word niche intact whole.
    /// </summary>
    public const int MaxHashtags = 5;

    /// <summary>A single character is never a searchable hashtag on its own.</summary>
    private const int MinTokenLength = 2;

    /// <summary>
    /// Pragmatic connector/stopword list, EN + ES — deliberately small, explicit and hand-picked, not
    /// linguistics. It only has to stop a connector from being emitted as a hashtag of its own
    /// (<c>#and</c>, <c>#de</c>), which no client ever means by their niche.
    /// </summary>
    private static readonly HashSet<string> Connectors = new(StringComparer.Ordinal)
    {
        // English
        "and", "or", "the", "of", "for",
        // Spanish
        "y", "e", "o", "de", "del", "la", "el", "los", "las", "en", "para", "con",
        // A bare ampersand survives the letter/digit filter as an empty token, but the written-out
        // forms above do not — keeping "&" listed documents the intent at the call site.
        "&",
    };

    /// <summary>
    /// Derives the hashtag candidates for <paramref name="nicheTerm"/>, order-stable and deduplicated:
    /// the surviving individual tokens first, then the full concatenation of the phrase.
    ///
    /// Returns an EMPTY list when nothing survives (blank input, punctuation only, or connectors only).
    /// The caller must then report the search as unrunnable rather than calling the provider with a
    /// meaningless tag — an empty result is an answer, not a reason to guess.
    /// </summary>
    public static IReadOnlyList<string> Derive(string? nicheTerm)
    {
        if (string.IsNullOrWhiteSpace(nicheTerm))
        {
            return [];
        }

        var tokens = nicheTerm
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(Sanitize)
            .Where(token => token.Length > 0 && !Connectors.Contains(token))
            .ToList();

        if (tokens.Count == 0)
        {
            return [];
        }

        // The concatenation keeps every surviving token, including the short ones: "plan b coaching"
        // is the phrase the client wrote, so #planbcoaching stays a plausible tag even though #b is not.
        var concatenated = string.Concat(tokens);

        var candidates = tokens
            .Where(token => token.Length >= MinTokenLength)
            .Append(concatenated)
            .Distinct(StringComparer.Ordinal)
            .Take(MaxHashtags)
            .ToList();

        return candidates;
    }

    /// <summary>
    /// Lowercases, strips diacritics (<c>nutrición</c> → <c>nutricion</c>) and removes every character
    /// that is not a letter or a digit — which also removes a leading <c>#</c>, so an operator who
    /// already typed a hashtag gets that hashtag back.
    /// </summary>
    private static string Sanitize(string token)
    {
        var decomposed = token.ToLowerInvariant().Normalize(NormalizationForm.FormD);

        var builder = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
