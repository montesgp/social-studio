using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Domain.Social.GeneralStrategies;

/// <summary>
/// Aggregate root representing the general social media strategy for a client.
/// Sibling aggregate to Client — owns its own repository, keyed by ClientId FK.
/// One strategy per client is enforced at the application layer and backed
/// by a Postgres unique index on client_id.
/// Renamed from ClientStrategy; identical structure and invariants.
/// </summary>
public sealed class GeneralStrategy : Entity<GeneralStrategyId>
{
    public const int ContentPillarsMaxCount = 10;
    public const int ToneKeywordsMaxCount = 20;
    public const int TargetAudienceMaxLength = 1000;
    public const int PostingFrequencyMaxLength = 200;
    public const int NotesMaxLength = 2000;

    private GeneralStrategy(
        GeneralStrategyId id,
        Guid clientId,
        Guid organizationId,
        IReadOnlyList<string> contentPillars,
        IReadOnlyList<string> toneKeywords,
        string? targetAudience,
        string? postingFrequency,
        string? notes,
        DateTime createdAtUtc,
        DateTime updatedAtUtc)
        : base(id)
    {
        ClientId = clientId;
        OrganizationId = organizationId;
        ContentPillars = contentPillars;
        ToneKeywords = toneKeywords;
        TargetAudience = targetAudience;
        PostingFrequency = postingFrequency;
        Notes = notes;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
    }

    // Parameterless constructor for EF Core
    private GeneralStrategy() : base(default) { }

    public Guid ClientId { get; private set; }

    public Guid OrganizationId { get; private set; }

    public IReadOnlyList<string> ContentPillars { get; private set; } = Array.Empty<string>();

    public IReadOnlyList<string> ToneKeywords { get; private set; } = Array.Empty<string>();

    public string? TargetAudience { get; private set; }

    public string? PostingFrequency { get; private set; }

    public string? Notes { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>
    /// Factory for creating a new GeneralStrategy. Validates all fields including
    /// the non-empty signal rule — a fully empty strategy is rejected.
    /// </summary>
    public static Result<GeneralStrategy> Create(
        GeneralStrategyId id,
        Guid clientId,
        Guid organizationId,
        IReadOnlyList<string>? contentPillars,
        IReadOnlyList<string>? toneKeywords,
        string? targetAudience,
        string? postingFrequency,
        string? notes,
        DateTime nowUtc)
    {
        var pillars = contentPillars ?? Array.Empty<string>();
        var keywords = toneKeywords ?? Array.Empty<string>();

        if (pillars.Count > ContentPillarsMaxCount)
        {
            return Result.Failure<GeneralStrategy>(GeneralStrategyErrors.Validation(
                $"ContentPillars must not exceed {ContentPillarsMaxCount} items."));
        }

        if (keywords.Count > ToneKeywordsMaxCount)
        {
            return Result.Failure<GeneralStrategy>(GeneralStrategyErrors.Validation(
                $"ToneKeywords must not exceed {ToneKeywordsMaxCount} items."));
        }

        var audienceResult = ValidateOptionalText(targetAudience, TargetAudienceMaxLength, "TargetAudience");
        if (audienceResult.IsFailure)
        {
            return Result.Failure<GeneralStrategy>(audienceResult.Error);
        }

        var frequencyResult = ValidateOptionalText(postingFrequency, PostingFrequencyMaxLength, "PostingFrequency");
        if (frequencyResult.IsFailure)
        {
            return Result.Failure<GeneralStrategy>(frequencyResult.Error);
        }

        var notesResult = ValidateOptionalText(notes, NotesMaxLength, "Notes");
        if (notesResult.IsFailure)
        {
            return Result.Failure<GeneralStrategy>(notesResult.Error);
        }

        if (!HasSomeSignal(pillars, keywords, audienceResult.Value, frequencyResult.Value, notesResult.Value))
        {
            return Result.Failure<GeneralStrategy>(GeneralStrategyErrors.EmptyStrategy);
        }

        return Result.Success(new GeneralStrategy(
            id,
            clientId,
            organizationId,
            pillars,
            keywords,
            audienceResult.Value,
            frequencyResult.Value,
            notesResult.Value,
            nowUtc,
            nowUtc));
    }

    /// <summary>
    /// Re-hydration factory used by EF mapping. Bypasses validation — data is trusted from persistence.
    /// </summary>
    public static GeneralStrategy Restore(
        GeneralStrategyId id,
        Guid clientId,
        Guid organizationId,
        IReadOnlyList<string> contentPillars,
        IReadOnlyList<string> toneKeywords,
        string? targetAudience,
        string? postingFrequency,
        string? notes,
        DateTime createdAtUtc,
        DateTime updatedAtUtc)
    {
        return new GeneralStrategy(
            id,
            clientId,
            organizationId,
            contentPillars,
            toneKeywords,
            targetAudience,
            postingFrequency,
            notes,
            createdAtUtc,
            updatedAtUtc);
    }

    /// <summary>
    /// Updates all editable fields. ClientId and OrganizationId are immutable.
    /// Resolution 6: collection refs replaced with new List instances for EF ValueComparer.
    /// </summary>
    public Result<GeneralStrategy> Update(
        IReadOnlyList<string>? contentPillars,
        IReadOnlyList<string>? toneKeywords,
        string? targetAudience,
        string? postingFrequency,
        string? notes,
        DateTime nowUtc)
    {
        var pillars = contentPillars is null ? new List<string>() : new List<string>(contentPillars);
        var keywords = toneKeywords is null ? new List<string>() : new List<string>(toneKeywords);

        if (pillars.Count > ContentPillarsMaxCount)
        {
            return Result.Failure<GeneralStrategy>(GeneralStrategyErrors.Validation(
                $"ContentPillars must not exceed {ContentPillarsMaxCount} items."));
        }

        if (keywords.Count > ToneKeywordsMaxCount)
        {
            return Result.Failure<GeneralStrategy>(GeneralStrategyErrors.Validation(
                $"ToneKeywords must not exceed {ToneKeywordsMaxCount} items."));
        }

        var audienceResult = ValidateOptionalText(targetAudience, TargetAudienceMaxLength, "TargetAudience");
        if (audienceResult.IsFailure)
        {
            return Result.Failure<GeneralStrategy>(audienceResult.Error);
        }

        var frequencyResult = ValidateOptionalText(postingFrequency, PostingFrequencyMaxLength, "PostingFrequency");
        if (frequencyResult.IsFailure)
        {
            return Result.Failure<GeneralStrategy>(frequencyResult.Error);
        }

        var notesResult = ValidateOptionalText(notes, NotesMaxLength, "Notes");
        if (notesResult.IsFailure)
        {
            return Result.Failure<GeneralStrategy>(notesResult.Error);
        }

        if (!HasSomeSignal(pillars, keywords, audienceResult.Value, frequencyResult.Value, notesResult.Value))
        {
            return Result.Failure<GeneralStrategy>(GeneralStrategyErrors.EmptyStrategy);
        }

        ContentPillars = pillars;
        ToneKeywords = keywords;
        TargetAudience = audienceResult.Value;
        PostingFrequency = frequencyResult.Value;
        Notes = notesResult.Value;
        UpdatedAtUtc = nowUtc;

        return Result.Success(this);
    }

    private static bool HasSomeSignal(
        IReadOnlyList<string> pillars,
        IReadOnlyList<string> keywords,
        string? targetAudience,
        string? postingFrequency,
        string? notes)
    {
        return pillars.Count > 0
            || keywords.Count > 0
            || !string.IsNullOrWhiteSpace(targetAudience)
            || !string.IsNullOrWhiteSpace(postingFrequency)
            || !string.IsNullOrWhiteSpace(notes);
    }

    private static Result<string?> ValidateOptionalText(string? value, int maxLength, string fieldName)
    {
        if (value is null)
        {
            return Result.Success<string?>(null);
        }

        var trimmed = value.Trim();

        if (trimmed.Length == 0)
        {
            return Result.Success<string?>(null);
        }

        if (trimmed.Length > maxLength)
        {
            return Result.Failure<string?>(GeneralStrategyErrors.Validation(
                $"{fieldName} must not exceed {maxLength} characters."));
        }

        return Result.Success<string?>(trimmed);
    }
}
