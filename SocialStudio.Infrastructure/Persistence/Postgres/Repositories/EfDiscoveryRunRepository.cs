using SocialStudio.Application.Abstractions.Social.NicheDiscovery;
using SocialStudio.Domain.Social.NicheDiscovery;
using SocialStudio.Infrastructure.Identity;
using SocialStudio.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace SocialStudio.Infrastructure.Persistence.Postgres.Repositories;

/// <summary>
/// EF implementation of <see cref="IDiscoveryRunRepository"/> over <c>social.discovery_runs</c> — the
/// one NicheDiscovery table with UPDATEs (design §9, ND-16).
/// </summary>
public sealed class EfDiscoveryRunRepository : IDiscoveryRunRepository
{
    private readonly AppIdentityDbContext _db;

    public EfDiscoveryRunRepository(AppIdentityDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(DiscoveryRun run, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(run);

        _db.DiscoveryRuns.Add(ToData(run));
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(DiscoveryRun run, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(run);

        var row = await _db.DiscoveryRuns
            .FirstOrDefaultAsync(x => x.Id == run.Id.Value && x.OrganizationId == run.OrganizationId, ct);

        if (row is null)
        {
            return;
        }

        row.Status = (short)run.Status;
        row.FailureReasonKey = run.FailureReasonKey;
        row.StartedAt = run.StartedAtUtc;
        row.CompletedAt = run.CompletedAtUtc;
        row.WeightsJson = run.WeightsJson;
        row.UnconfiguredSourceKeys = run.UnconfiguredSourceKeys.ToList();
        row.FailedSourceKeys = run.FailedSourceKeys.ToList();
        row.ProviderUnitsSpent = run.ProviderUnitsSpent;
        row.AiCompletionsSpent = run.AiCompletionsSpent;
        row.SemanticDegraded = run.SemanticDegraded;

        await _db.SaveChangesAsync(ct);
    }

    public async Task<DiscoveryRun?> GetByIdAsync(Guid organizationId, DiscoveryRunId id, CancellationToken ct = default)
    {
        var row = await _db.DiscoveryRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id.Value && x.OrganizationId == organizationId, ct);

        return row is null ? null : ToDomain(row);
    }

    public async Task<IReadOnlyList<DiscoveryRun>> ListByOrganizationAsync(
        Guid organizationId, int limit, CancellationToken ct = default)
    {
        if (limit <= 0)
        {
            return [];
        }

        var rows = await _db.DiscoveryRuns
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .OrderByDescending(x => x.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);

        return rows.Select(ToDomain).ToList();
    }

    private static DiscoveryRunData ToData(DiscoveryRun run) => new()
    {
        Id = run.Id.Value,
        OrganizationId = run.OrganizationId,
        RequestedByUserId = run.RequestedByUserId,
        SourceMarketCode = run.SourceMarketCode.Value,
        TargetMarketCode = run.TargetMarketCode.Value,
        NetworkKey = run.NetworkKey,
        ContentFormat = run.ContentFormat,
        ConfigurationSnapshotJson = run.ConfigurationSnapshotJson,
        WindowDays = run.Window.Days,
        Depth = (short)run.Depth,
        Status = (short)run.Status,
        // The scorer weight snapshot (N7, design §7.1) and the execution-outcome fields below are
        // whatever the aggregate already carries at persistence time — "{}"/empty/zero at AddAsync
        // time (before the run has executed), populated by NicheDiscoveryRunProcessor via
        // DiscoveryRun.RecordExecutionSnapshot and round-tripped for real from WU-11a onward.
        WeightsJson = run.WeightsJson,
        OriginSourceKeys = [],
        UnconfiguredSourceKeys = run.UnconfiguredSourceKeys.ToList(),
        FailedSourceKeys = run.FailedSourceKeys.ToList(),
        ProviderUnitsSpent = run.ProviderUnitsSpent,
        AiCompletionsSpent = run.AiCompletionsSpent,
        SemanticDegraded = run.SemanticDegraded,
        FailureReasonKey = run.FailureReasonKey,
        StartedAt = run.StartedAtUtc,
        CompletedAt = run.CompletedAtUtc,
        CreatedAt = run.CreatedAtUtc,
    };

    private static DiscoveryRun ToDomain(DiscoveryRunData row)
    {
        var source = MarketCode.TryParse(row.SourceMarketCode);
        var target = MarketCode.TryParse(row.TargetMarketCode);

        // The aggregate parsed and validated both codes before this row could ever have been
        // written (DiscoveryRun.Create) — a parse failure here means the stored value was
        // corrupted outside the aggregate, which is a hard invariant violation, not a normal
        // "not found"/validation path.
        if (source.IsFailure || target.IsFailure)
        {
            throw new InvalidOperationException(
                $"Persisted DiscoveryRun {row.Id} has a corrupted market code. RunId={row.Id}");
        }

        return DiscoveryRun.Reconstitute(
            new DiscoveryRunId(row.Id),
            row.OrganizationId,
            row.RequestedByUserId,
            source.Value,
            target.Value,
            row.NetworkKey,
            row.ContentFormat,
            DiscoveryWindow.Create(row.WindowDays).Value,
            (DiscoveryDepth)row.Depth,
            row.ConfigurationSnapshotJson,
            nicheId: null,
            (DiscoveryRunStatus)row.Status,
            row.FailureReasonKey,
            row.CreatedAt,
            row.StartedAt,
            row.CompletedAt,
            row.WeightsJson,
            row.UnconfiguredSourceKeys,
            row.FailedSourceKeys,
            row.ProviderUnitsSpent,
            row.AiCompletionsSpent,
            row.SemanticDegraded);
    }
}
