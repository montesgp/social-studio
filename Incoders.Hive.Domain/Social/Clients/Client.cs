using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Domain.Social.Clients;

/// <summary>
/// Organization-owned customer/account managed inside the Hive Social module.
/// </summary>
public sealed class Client : Entity<ClientId>
{
    public const int NameMaxLength = 200;
    public const int DescriptionMaxLength = 1_000;
    public const int SectorOrNicheMaxLength = 300;

    public const int AestheticRefreshThresholdMinDays = 1;
    public const int AestheticRefreshThresholdMaxDays = 365;

    public const int MaxPostsToAnalyzeMin = 1;
    public const int MaxPostsToAnalyzeMax = 50;
    /// <summary>Default number of profile posts analysed per run when the client sets no override.</summary>
    public const int DefaultPostsToAnalyze = 12;

    private Client(
        ClientId id,
        Guid organizationId,
        string name,
        string? description,
        bool isActive,
        DateTime createdAtUtc,
        DateTime updatedAtUtc,
        string? sectorOrNiche,
        BackgroundSourcingPolicy? policy = null,
        int? aestheticRefreshThresholdDays = null,
        int? maxPostsToAnalyze = null)
        : base(id)
    {
        OrganizationId = organizationId;
        Name = name;
        Description = description;
        IsActive = isActive;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
        SectorOrNiche = sectorOrNiche;
        BackgroundSourcingPolicy = policy;
        AestheticRefreshThresholdDays = aestheticRefreshThresholdDays;
        MaxPostsToAnalyze = maxPostsToAnalyze;
    }

    public Guid OrganizationId { get; private set; }

    public string Name { get; private set; }

    public string? Description { get; private set; }

    public string? SectorOrNiche { get; private set; }

    public bool IsActive { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>
    /// Nullable background sourcing policy for this client.
    /// Null (or Unset) means the legacy 3-tier selection is applied (BSP-05).
    /// </summary>
    public BackgroundSourcingPolicy? BackgroundSourcingPolicy { get; private set; }

    /// <summary>
    /// Per-client staleness threshold for aesthetic analysis refresh, in days.
    /// Null means the system default (30 days) applies.
    /// Valid range when set: 1–365 days (CAG-CS-01, design §2.1, OQ-1).
    /// </summary>
    public int? AestheticRefreshThresholdDays { get; private set; }

    /// <summary>
    /// Per-client number of most-recent posts to fetch on each profile analysis run.
    /// Null means the system default (<see cref="DefaultPostsToAnalyze"/> = 12) applies.
    /// Valid range when set: 1–50. Higher values capture a richer aesthetic sample at more Apify cost.
    /// </summary>
    public int? MaxPostsToAnalyze { get; private set; }

    public static Result<Client> Create(
        ClientId id,
        Guid organizationId,
        string name,
        string? description,
        DateTime nowUtc,
        string? sectorOrNiche = null)
    {
        var validation = Validate(organizationId, name, description, sectorOrNiche);
        if (validation.IsFailure)
        {
            return Result.Failure<Client>(validation.Error);
        }

        return Result.Success(new Client(
            id,
            organizationId,
            name.Trim(),
            NormalizeDescription(description),
            true,
            nowUtc,
            nowUtc,
            NormalizeSectorOrNiche(sectorOrNiche),
            policy: null));
    }

    public static Client Restore(
        ClientId id,
        Guid organizationId,
        string name,
        string? description,
        bool isActive,
        DateTime createdAtUtc,
        DateTime updatedAtUtc,
        string? sectorOrNiche = null,
        BackgroundSourcingPolicy? policy = null,
        int? aestheticRefreshThresholdDays = null,
        int? maxPostsToAnalyze = null)
    {
        return new Client(
            id, organizationId, name, description, isActive,
            createdAtUtc, updatedAtUtc, sectorOrNiche, policy,
            aestheticRefreshThresholdDays, maxPostsToAnalyze);
    }

    public Result Rename(string name, string? description, DateTime nowUtc, string? sectorOrNiche = null)
    {
        var validation = Validate(OrganizationId, name, description, sectorOrNiche);
        if (validation.IsFailure)
        {
            return Result.Failure(validation.Error);
        }

        Name = name.Trim();
        Description = NormalizeDescription(description);
        SectorOrNiche = NormalizeSectorOrNiche(sectorOrNiche);
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    public Result Deactivate(DateTime nowUtc)
    {
        if (!IsActive)
        {
            return Result.Failure(ClientErrors.AlreadyInactive);
        }

        IsActive = false;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    public Result Activate(DateTime nowUtc)
    {
        if (IsActive)
        {
            return Result.Failure(ClientErrors.AlreadyActive);
        }

        IsActive = true;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    /// <summary>
    /// Sets (or clears) the background sourcing policy for this client.
    /// Pass null to revert to the legacy UNSET behavior.
    /// </summary>
    public void SetBackgroundSourcingPolicy(BackgroundSourcingPolicy? policy, DateTime nowUtc)
    {
        BackgroundSourcingPolicy = policy;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>
    /// Sets (or clears) the per-client aesthetic refresh staleness threshold.
    /// Pass null to revert to the 30-day system default.
    /// Valid non-null range: 1–365 days (OQ-1, CAG-CS-01).
    /// </summary>
    public Result SetAestheticRefreshThresholdDays(int? days, DateTime nowUtc)
    {
        if (days.HasValue)
        {
            if (days.Value < AestheticRefreshThresholdMinDays)
                return Result.Failure(ClientErrors.AestheticRefreshThresholdTooLow);

            if (days.Value > AestheticRefreshThresholdMaxDays)
                return Result.Failure(ClientErrors.AestheticRefreshThresholdTooHigh);
        }

        AestheticRefreshThresholdDays = days;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    /// <summary>
    /// Sets the per-client number of profile posts analysed per run. Pass null to use the
    /// system default (<see cref="DefaultPostsToAnalyze"/>). Valid non-null range: 1–50.
    /// </summary>
    public Result SetMaxPostsToAnalyze(int? posts, DateTime nowUtc)
    {
        if (posts.HasValue && (posts.Value < MaxPostsToAnalyzeMin || posts.Value > MaxPostsToAnalyzeMax))
        {
            return Result.Failure(ClientErrors.MaxPostsToAnalyzeOutOfRange);
        }

        MaxPostsToAnalyze = posts;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    /// <summary>Effective posts-to-analyze for this client (override or the system default).</summary>
    public int EffectivePostsToAnalyze => MaxPostsToAnalyze ?? DefaultPostsToAnalyze;

    private static Result Validate(Guid organizationId, string name, string? description, string? sectorOrNiche = null)
    {
        if (organizationId == Guid.Empty)
        {
            return Result.Failure(ClientErrors.OrganizationRequired);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure(ClientErrors.NameRequired);
        }

        if (name.Length > NameMaxLength)
        {
            return Result.Failure(ClientErrors.NameTooLong);
        }

        if (description?.Length > DescriptionMaxLength)
        {
            return Result.Failure(ClientErrors.DescriptionTooLong);
        }

        if (sectorOrNiche?.Length > SectorOrNicheMaxLength)
        {
            return Result.Failure(ClientErrors.SectorOrNicheTooLong);
        }

        return Result.Success();
    }

    private static string? NormalizeDescription(string? description) =>
        string.IsNullOrWhiteSpace(description) ? null : description.Trim();

    private static string? NormalizeSectorOrNiche(string? sectorOrNiche) =>
        string.IsNullOrWhiteSpace(sectorOrNiche) ? null : sectorOrNiche.Trim();
}
