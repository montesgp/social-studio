using System.Text.RegularExpressions;
using SocialStudio.Domain.Common;

namespace SocialStudio.Domain.Social.Niches;

/// <summary>
/// Platform-managed catalog entry for a sector/niche (e.g. "Software &amp; Technology",
/// "Medical &amp; Health"). Sysadmin-curated, so there is no OrganizationId column — clients
/// associate to entries from this shared catalog rather than typing their own free text.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SearchHashtags"/> is a curated list, deliberately stored instead of derived. Deriving
/// hashtags lexically from a display name produces tags that do not exist on the target network
/// ("Medical &amp; health" → a hashtag nobody uses), and every client sharing that niche inherits
/// the same bad key. Curating them here lets a sysadmin fix one hashtag once, for everyone, and
/// makes the discovery key intentional rather than a side effect of spelling.
/// </para>
/// </remarks>
public sealed class Niche : Entity<NicheId>
{
    public const int CodeMinLength = 2;
    public const int CodeMaxLength = 64;
    public const int DisplayNameMaxLength = 120;
    public const int IconKeyMaxLength = 32;
    public const int HashtagMinLength = 2;
    public const int HashtagMaxLength = 64;
    public const int MaxSearchHashtags = 20;

    private static readonly Regex CodePattern = new(@"^[a-z0-9\-]+$", RegexOptions.Compiled);
    private static readonly Regex IconKeyPattern = new(@"^[a-z0-9\-]+$", RegexOptions.Compiled);
    private static readonly Regex HashtagPattern = new(@"^[a-z0-9_]+$", RegexOptions.Compiled);

    private List<string> _searchHashtags = [];

    private Niche(
        NicheId id,
        string code,
        string displayName,
        string? iconKey,
        IEnumerable<string> searchHashtags,
        bool isActive,
        DateTime createdAtUtc,
        DateTime updatedAtUtc)
        : base(id)
    {
        Code = code;
        DisplayName = displayName;
        IconKey = iconKey;
        _searchHashtags = searchHashtags.ToList();
        IsActive = isActive;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
    }

    // Parameterless constructor for EF Core re-hydration
    private Niche() : base(default) { }

    public string Code { get; private set; } = default!;

    public string DisplayName { get; private set; } = default!;

    /// <summary>
    /// Optional icon key. <c>null</c> means the UI renders the generic fallback niche icon.
    /// </summary>
    public string? IconKey { get; private set; }

    /// <summary>
    /// Curated, normalized hashtags (no leading '#', lowercase) used as the intentional discovery
    /// key for this niche. Empty is valid — a niche with no curated hashtags simply contributes none.
    /// </summary>
    public IReadOnlyList<string> SearchHashtags => _searchHashtags;

    public bool IsActive { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public static Result<Niche> Create(
        string code,
        string displayName,
        string? iconKey,
        IReadOnlyCollection<string> searchHashtags,
        DateTime nowUtc)
    {
        var validated = ValidateAll(code, displayName, iconKey, searchHashtags);
        if (validated.IsFailure)
        {
            return Result.Failure<Niche>(validated.Error);
        }

        var parts = validated.Value;

        return Result.Success(new Niche(
            NicheId.New(),
            parts.Code,
            parts.DisplayName,
            parts.IconKey,
            parts.Hashtags,
            true,
            nowUtc,
            nowUtc));
    }

    /// <summary>
    /// Re-hydration factory used by the persistence adapter. Bypasses validation — data coming back
    /// from the store is trusted.
    /// </summary>
    public static Niche Restore(
        NicheId id,
        string code,
        string displayName,
        string? iconKey,
        IEnumerable<string> searchHashtags,
        bool isActive,
        DateTime createdAtUtc,
        DateTime updatedAtUtc)
    {
        return new Niche(id, code, displayName, iconKey, searchHashtags, isActive, createdAtUtc, updatedAtUtc);
    }

    /// <summary>
    /// Updates the display name, icon and curated hashtags. <see cref="Code"/> is immutable, mirroring
    /// the social network catalog: it is the stable key downstream data points at.
    /// </summary>
    public Result Update(
        string displayName,
        string? iconKey,
        IReadOnlyCollection<string> searchHashtags,
        DateTime nowUtc)
    {
        var validated = ValidateAll(Code, displayName, iconKey, searchHashtags);
        if (validated.IsFailure)
        {
            return Result.Failure(validated.Error);
        }

        DisplayName = validated.Value.DisplayName;
        IconKey = validated.Value.IconKey;
        _searchHashtags = validated.Value.Hashtags;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    public Result Deactivate(DateTime nowUtc)
    {
        if (!IsActive)
        {
            return Result.Failure(NicheErrors.AlreadyInactive);
        }

        IsActive = false;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    public Result Activate(DateTime nowUtc)
    {
        if (IsActive)
        {
            return Result.Failure(NicheErrors.AlreadyActive);
        }

        IsActive = true;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    private static Result<NicheParts> ValidateAll(
        string code,
        string displayName,
        string? iconKey,
        IReadOnlyCollection<string> searchHashtags)
    {
        var normalizedCode = (code ?? string.Empty).Trim().ToLowerInvariant();

        if (normalizedCode.Length < CodeMinLength || normalizedCode.Length > CodeMaxLength)
        {
            return Result.Failure<NicheParts>(NicheErrors.InvalidCode(
                $"Code must be between {CodeMinLength} and {CodeMaxLength} characters."));
        }

        if (!CodePattern.IsMatch(normalizedCode))
        {
            return Result.Failure<NicheParts>(NicheErrors.InvalidCode(
                "Code must be lowercase alphanumeric with optional hyphens."));
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            return Result.Failure<NicheParts>(NicheErrors.InvalidDisplayName("DisplayName is required."));
        }

        var normalizedDisplayName = displayName.Trim();
        if (normalizedDisplayName.Length > DisplayNameMaxLength)
        {
            return Result.Failure<NicheParts>(NicheErrors.InvalidDisplayName(
                $"DisplayName must not exceed {DisplayNameMaxLength} characters."));
        }

        string? normalizedIconKey = null;
        if (!string.IsNullOrWhiteSpace(iconKey))
        {
            normalizedIconKey = iconKey.Trim().ToLowerInvariant();

            if (normalizedIconKey.Length > IconKeyMaxLength)
            {
                return Result.Failure<NicheParts>(NicheErrors.InvalidIconKey(
                    $"IconKey must not exceed {IconKeyMaxLength} characters."));
            }

            if (!IconKeyPattern.IsMatch(normalizedIconKey))
            {
                return Result.Failure<NicheParts>(NicheErrors.InvalidIconKey(
                    "Must be lowercase alphanumeric (a-z, 0-9) with optional hyphens."));
            }
        }

        var hashtags = new List<string>();
        foreach (var raw in searchHashtags ?? [])
        {
            var normalized = NormalizeHashtag(raw);
            if (normalized.Length == 0)
            {
                continue;
            }

            if (normalized.Length < HashtagMinLength || normalized.Length > HashtagMaxLength)
            {
                return Result.Failure<NicheParts>(NicheErrors.InvalidHashtag(
                    $"Each hashtag must be between {HashtagMinLength} and {HashtagMaxLength} characters."));
            }

            if (!HashtagPattern.IsMatch(normalized))
            {
                return Result.Failure<NicheParts>(NicheErrors.InvalidHashtag(
                    "Each hashtag must be lowercase alphanumeric or underscore, with no spaces."));
            }

            if (!hashtags.Contains(normalized, StringComparer.Ordinal))
            {
                hashtags.Add(normalized);
            }
        }

        if (hashtags.Count > MaxSearchHashtags)
        {
            return Result.Failure<NicheParts>(NicheErrors.InvalidHashtag(
                $"A niche may curate at most {MaxSearchHashtags} hashtags."));
        }

        return Result.Success(new NicheParts(normalizedCode, normalizedDisplayName, normalizedIconKey, hashtags));
    }

    private static string NormalizeHashtag(string? raw) =>
        (raw ?? string.Empty).Trim().TrimStart('#').Trim().ToLowerInvariant();

    private sealed record NicheParts(string Code, string DisplayName, string? IconKey, List<string> Hashtags);
}
