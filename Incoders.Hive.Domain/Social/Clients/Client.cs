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

    public const int MaxPiecesPerGenerationMin = 1;
    /// <summary>
    /// Generous ceiling so the cap does not have to move every time the format catalog grows.
    /// </summary>
    public const int MaxPiecesPerGenerationMax = 20;

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
        int? maxPostsToAnalyze = null,
        bool signsOwnSlides = false,
        int? maxPiecesPerGeneration = null)
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
        SignsOwnSlides = signsOwnSlides;
        MaxPiecesPerGeneration = maxPiecesPerGeneration;
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

    /// <summary>
    /// Per-client cap on how many pieces (format results) one generation job may build.
    /// Null means no cap — every requested format is generated.
    /// Valid range when set: 1–<see cref="MaxPiecesPerGenerationMax"/>.
    /// </summary>
    /// <remarks>
    /// The dominant cost of a generation run is per-slide AI image rendering, so capping the number
    /// of pieces is a coarse but effective brake for cheap test runs. Which formats survive the cap
    /// is the CALLER's choice: <see cref="ApplyPieceCap"/> preserves the requested order, so an
    /// operator who asks for the cheap formats first gets exactly those.
    /// </remarks>
    public int? MaxPiecesPerGeneration { get; private set; }

    /// <summary>
    /// Whether the client signs their own slides with an @handle attribution (issue #70).
    /// Plain boolean, no tri-state/detection. Defaults to <c>false</c> (not signed) for both
    /// new and existing clients — an intended behavior change: clients that previously received
    /// an inferred/deterministic signature now get none until explicitly opted in.
    /// </summary>
    public bool SignsOwnSlides { get; private set; }

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
        int? maxPostsToAnalyze = null,
        bool signsOwnSlides = false,
        int? maxPiecesPerGeneration = null)
    {
        return new Client(
            id, organizationId, name, description, isActive,
            createdAtUtc, updatedAtUtc, sectorOrNiche, policy,
            aestheticRefreshThresholdDays, maxPostsToAnalyze, signsOwnSlides,
            maxPiecesPerGeneration);
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

    /// <summary>
    /// Sets the per-client cap on pieces built per generation job. Pass null for no cap.
    /// Valid non-null range: 1–<see cref="MaxPiecesPerGenerationMax"/>.
    /// </summary>
    public Result SetMaxPiecesPerGeneration(int? pieces, DateTime nowUtc)
    {
        if (pieces.HasValue
            && (pieces.Value < MaxPiecesPerGenerationMin || pieces.Value > MaxPiecesPerGenerationMax))
        {
            return Result.Failure(ClientErrors.MaxPiecesPerGenerationOutOfRange);
        }

        MaxPiecesPerGeneration = pieces;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    /// <summary>
    /// Trims a requested format list to this client's piece cap, preserving the caller's order so
    /// the highest-priority formats survive. Returns the list unchanged when no cap is set.
    /// </summary>
    public IReadOnlyList<string> ApplyPieceCap(IReadOnlyList<string> requestedFormats)
    {
        if (MaxPiecesPerGeneration is not { } cap || requestedFormats.Count <= cap)
        {
            return requestedFormats;
        }

        return requestedFormats.Take(cap).ToList();
    }

    /// <summary>
    /// Sets whether this client signs their own slides with an @handle attribution (issue #70).
    /// Plain boolean toggle — no tri-state, no profile-analysis detection.
    /// </summary>
    public void SetSignsOwnSlides(bool signsOwnSlides, DateTime nowUtc)
    {
        SignsOwnSlides = signsOwnSlides;
        UpdatedAtUtc = nowUtc;
    }

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
