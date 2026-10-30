using System.Text.RegularExpressions;
using SocialStudio.Domain.Common;

namespace SocialStudio.Domain.Social.ContentStudio;

/// <summary>
/// A ready-to-publish Instagram caption. Encodes Instagram's concrete publishing limits as a domain
/// invariant NOW (Decision E1/Q3 — Content Studio editing completeness): the domain expresses the
/// publishing contract rather than deferring it to a future adapter.
/// <para>
/// Invariants:
/// <list type="bullet">
/// <item>non-empty after trim</item>
/// <item>at most <see cref="MaxLength"/> characters</item>
/// <item>at most <see cref="MaxHashtags"/> hashtags</item>
/// </list>
/// </para>
/// </summary>
public sealed record Caption
{
    /// <summary>Instagram's caption character ceiling.</summary>
    public const int MaxLength = 2200;

    /// <summary>Instagram's caption hashtag ceiling.</summary>
    public const int MaxHashtags = 30;

    private static readonly Regex HashtagRegex = new(@"#\w+", RegexOptions.Compiled);

    public string Value { get; }

    private Caption(string value)
    {
        Value = value;
    }

    /// <summary>
    /// Validates and creates a <see cref="Caption"/>. Trims surrounding whitespace before validation.
    /// Never throws — returns <see cref="Result{Caption}"/> per the codebase's Result pattern
    /// (matching <see cref="ContentStudioErrors"/> / <c>VisualGuardrailValidator</c> usage).
    /// </summary>
    public static Result<Caption> Create(string? raw)
    {
        var trimmed = raw?.Trim() ?? string.Empty;

        if (trimmed.Length == 0)
        {
            return Result.Failure<Caption>(ContentStudioErrors.CaptionRequired);
        }

        if (trimmed.Length > MaxLength)
        {
            return Result.Failure<Caption>(ContentStudioErrors.CaptionTooLong);
        }

        var hashtagCount = HashtagRegex.Matches(trimmed).Count;
        if (hashtagCount > MaxHashtags)
        {
            return Result.Failure<Caption>(ContentStudioErrors.CaptionTooManyHashtags);
        }

        return Result.Success(new Caption(trimmed));
    }
}
