using SocialStudio.Domain.Common;

namespace SocialStudio.Domain.Social.NicheDiscovery;

/// <summary>
/// Aggregate root for a Niche Discovery run: operator-supplied parameters plus an immutable
/// configuration snapshot and an explicit status lifecycle (ND-01, ND-02).
/// </summary>
/// <remarks>
/// <see cref="ConfigurationSnapshotJson"/> is an opaque JSON string, mirroring
/// <c>ProfileAnalysis.RawPostsDatasetJson</c>: the Application-layer shape it captures (origin
/// mapping, scorer weights, depth ceiling) is not built until later work units, and the Domain
/// aggregate must not depend on it. Two runs with identical parameters resolve to identical
/// snapshots, which is what makes them comparable (ND-01 scenario 2).
/// </remarks>
public sealed class DiscoveryRun : Entity<DiscoveryRunId>
{
    private DiscoveryRun(
        DiscoveryRunId id,
        Guid organizationId,
        Guid requestedByUserId,
        MarketCode sourceMarketCode,
        MarketCode targetMarketCode,
        string networkKey,
        string contentFormat,
        DiscoveryWindow window,
        DiscoveryDepth depth,
        string configurationSnapshotJson,
        Guid? nicheId,
        DateTime createdAtUtc)
        : base(id)
    {
        OrganizationId = organizationId;
        RequestedByUserId = requestedByUserId;
        SourceMarketCode = sourceMarketCode;
        TargetMarketCode = targetMarketCode;
        NetworkKey = networkKey;
        ContentFormat = contentFormat;
        Window = window;
        Depth = depth;
        ConfigurationSnapshotJson = configurationSnapshotJson;
        NicheId = nicheId;
        Status = DiscoveryRunStatus.Requested;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid OrganizationId { get; }

    /// <summary>The operator who requested the run — <c>discovery_runs.requested_by_user_id</c> (design §9).</summary>
    public Guid RequestedByUserId { get; }

    /// <summary>
    /// Parsed at construction, never stored raw: an unparseable code cannot reach persistence, and
    /// only <see cref="MarketCode.Language"/> is ever read downstream, so no algorithm can branch on
    /// a market's identity.
    /// </summary>
    public MarketCode SourceMarketCode { get; }

    /// <inheritdoc cref="SourceMarketCode"/>
    public MarketCode TargetMarketCode { get; }

    public string NetworkKey { get; }

    public string ContentFormat { get; }

    public DiscoveryWindow Window { get; }

    public DiscoveryDepth Depth { get; }

    /// <summary>Immutable snapshot of the resolved configuration used at execution time (ND-01).</summary>
    public string ConfigurationSnapshotJson { get; }

    /// <summary>Optional niche the operator scoped the run to; null means the run is unscoped.</summary>
    public Guid? NicheId { get; }

    public DiscoveryRunStatus Status { get; private set; }

    /// <summary>Populated when <see cref="Status"/> is <see cref="DiscoveryRunStatus.Failed"/>.</summary>
    public string? FailureReasonKey { get; private set; }

    public DateTime CreatedAtUtc { get; }

    public DateTime? StartedAtUtc { get; private set; }

    public DateTime? CompletedAtUtc { get; private set; }

    /// <summary>
    /// The scorer weights snapshotted at run start (N7, design §7.1) — the effective weight set the
    /// run was actually ranked with, independent of any later change to
    /// <c>OpportunityScoreWeights.Default</c>. <c>"{}"</c> until <see cref="RecordExecutionSnapshot"/>
    /// runs. GAP FIX (WU-11a): WU-7b's <c>EfDiscoveryRunRepository</c> hardcoded this and the fields
    /// below to placeholder values because this aggregate had no way to carry them — the run
    /// orchestrator (this work unit) is the first caller that actually produces them.
    /// </summary>
    public string WeightsJson { get; private set; } = "{}";

    public IReadOnlyList<string> UnconfiguredSourceKeys { get; private set; } = [];

    public IReadOnlyList<string> FailedSourceKeys { get; private set; } = [];

    public int ProviderUnitsSpent { get; private set; }

    public int AiCompletionsSpent { get; private set; }

    /// <summary>Set when the semantic layer fell back to <c>LexicalClusterFallback</c> (N11) — the run
    /// still ranks, but this is surfaced so the operator knows the AI naming/grouping degraded.</summary>
    public bool SemanticDegraded { get; private set; }

    public static Result<DiscoveryRun> Create(
        Guid organizationId,
        Guid requestedByUserId,
        string sourceMarketCode,
        string targetMarketCode,
        string networkKey,
        string contentFormat,
        DiscoveryWindow window,
        DiscoveryDepth depth,
        string configurationSnapshotJson,
        Guid? nicheId,
        DateTime nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(networkKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentFormat);
        ArgumentNullException.ThrowIfNull(configurationSnapshotJson);

        var source = MarketCode.TryParse(sourceMarketCode);
        if (source.IsFailure)
        {
            return Result.Failure<DiscoveryRun>(source.Error);
        }

        var target = MarketCode.TryParse(targetMarketCode);
        if (target.IsFailure)
        {
            return Result.Failure<DiscoveryRun>(target.Error);
        }

        return Result.Success(new DiscoveryRun(
            DiscoveryRunId.New(),
            organizationId,
            requestedByUserId,
            source.Value,
            target.Value,
            networkKey,
            contentFormat,
            window,
            depth,
            configurationSnapshotJson,
            nicheId,
            nowUtc));
    }

    /// <summary>
    /// Reconstitutes a <see cref="DiscoveryRun"/> aggregate from persisted column values. For use
    /// EXCLUSIVELY by Infrastructure persistence mapping — DO NOT call from Application or Domain.
    /// Bypasses the status-machine guard because the data is already in a valid persisted state.
    /// </summary>
    public static DiscoveryRun Reconstitute(
        DiscoveryRunId id,
        Guid organizationId,
        Guid requestedByUserId,
        MarketCode sourceMarketCode,
        MarketCode targetMarketCode,
        string networkKey,
        string contentFormat,
        DiscoveryWindow window,
        DiscoveryDepth depth,
        string configurationSnapshotJson,
        Guid? nicheId,
        DiscoveryRunStatus status,
        string? failureReasonKey,
        DateTime createdAtUtc,
        DateTime? startedAtUtc,
        DateTime? completedAtUtc,
        string weightsJson = "{}",
        IReadOnlyList<string>? unconfiguredSourceKeys = null,
        IReadOnlyList<string>? failedSourceKeys = null,
        int providerUnitsSpent = 0,
        int aiCompletionsSpent = 0,
        bool semanticDegraded = false)
    {
        var run = new DiscoveryRun(
            id,
            organizationId,
            requestedByUserId,
            sourceMarketCode,
            targetMarketCode,
            networkKey,
            contentFormat,
            window,
            depth,
            configurationSnapshotJson,
            nicheId,
            createdAtUtc)
        {
            Status = status,
            FailureReasonKey = failureReasonKey,
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = completedAtUtc,
            WeightsJson = weightsJson,
            UnconfiguredSourceKeys = unconfiguredSourceKeys ?? [],
            FailedSourceKeys = failedSourceKeys ?? [],
            ProviderUnitsSpent = providerUnitsSpent,
            AiCompletionsSpent = aiCompletionsSpent,
            SemanticDegraded = semanticDegraded,
        };

        return run;
    }

    /// <summary>
    /// Records the execution outcome the orchestrator observed (design §7.1 N7, §9) — the weights the
    /// run was actually scored with, which origins were unconfigured/failed, provider/AI spend, and
    /// whether the semantic layer degraded. Called by <c>NicheDiscoveryRunProcessor</c> once sourcing
    /// and scoring are complete, immediately before the terminal status transition, so
    /// <c>GET runs/{id}</c> reflects the true execution shape rather than WU-7b's placeholders.
    /// </summary>
    public void RecordExecutionSnapshot(
        string weightsJson,
        IReadOnlyList<string> unconfiguredSourceKeys,
        IReadOnlyList<string> failedSourceKeys,
        int providerUnitsSpent,
        int aiCompletionsSpent,
        bool semanticDegraded)
    {
        ArgumentNullException.ThrowIfNull(weightsJson);
        ArgumentNullException.ThrowIfNull(unconfiguredSourceKeys);
        ArgumentNullException.ThrowIfNull(failedSourceKeys);

        WeightsJson = weightsJson;
        UnconfiguredSourceKeys = unconfiguredSourceKeys;
        FailedSourceKeys = failedSourceKeys;
        ProviderUnitsSpent = providerUnitsSpent;
        AiCompletionsSpent = aiCompletionsSpent;
        SemanticDegraded = semanticDegraded;
    }

    /// <summary>Transitions the run to <see cref="DiscoveryRunStatus.Running"/>.</summary>
    public void Start(DateTime nowUtc)
    {
        EnsureTransition(DiscoveryRunStatus.Running);
        StartedAtUtc = nowUtc;
    }

    /// <summary>
    /// Transitions the run to <see cref="DiscoveryRunStatus.Completed"/> (terminal success, every
    /// origin ran and the semantic layer did not degrade).
    /// </summary>
    public void Complete(DateTime nowUtc)
    {
        EnsureTransition(DiscoveryRunStatus.Completed);
        CompletedAtUtc = nowUtc;
    }

    /// <summary>
    /// Transitions the run to <see cref="DiscoveryRunStatus.PartiallyCompleted"/> — the run still
    /// ranked, but degraded (e.g. semantic fallback engaged, N11).
    /// </summary>
    public void CompletePartially(DateTime nowUtc)
    {
        EnsureTransition(DiscoveryRunStatus.PartiallyCompleted);
        CompletedAtUtc = nowUtc;
    }

    /// <summary>
    /// Transitions the run to <see cref="DiscoveryRunStatus.Failed"/> (terminal failure) with a
    /// structured reason. Used both for an unconfigured origin (ND-02 scenario 1) and for a
    /// depth/quota ceiling exhausted mid-run (ND-02 scenario 2) — never silently reported as
    /// "completed with zero opportunities".
    /// </summary>
    public void Fail(string failureReasonKey, DateTime nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureReasonKey);

        EnsureTransition(DiscoveryRunStatus.Failed);
        FailureReasonKey = failureReasonKey;
        CompletedAtUtc = nowUtc;
    }

    private void EnsureTransition(DiscoveryRunStatus target)
    {
        if (!IsValidTransition(Status, target))
        {
            throw new InvalidOperationException(
                $"Invalid DiscoveryRun status transition: {Status} -> {target}. RunId={Id.Value}");
        }

        Status = target;
    }

    private static bool IsValidTransition(DiscoveryRunStatus from, DiscoveryRunStatus to) =>
        (from, to) switch
        {
            (DiscoveryRunStatus.Requested, DiscoveryRunStatus.Running) => true,
            (DiscoveryRunStatus.Requested, DiscoveryRunStatus.Failed) => true,
            (DiscoveryRunStatus.Running, DiscoveryRunStatus.Completed) => true,
            (DiscoveryRunStatus.Running, DiscoveryRunStatus.PartiallyCompleted) => true,
            (DiscoveryRunStatus.Running, DiscoveryRunStatus.Failed) => true,
            _ => false,
        };
}
