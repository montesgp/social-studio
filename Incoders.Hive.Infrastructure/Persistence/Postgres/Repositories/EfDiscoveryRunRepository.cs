using Incoders.Hive.Application.Abstractions.Social.NicheDiscovery;
using Incoders.Hive.Domain.Social.NicheDiscovery;
using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace Incoders.Hive.Infrastructure.Persistence.Postgres.Repositories;

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

        await _db.SaveChangesAsync(ct);
    }

    public async Task<DiscoveryRun?> GetByIdAsync(Guid organizationId, DiscoveryRunId id, CancellationToken ct = default)
    {
        var row = await _db.DiscoveryRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id.Value && x.OrganizationId == organizationId, ct);

        return row is null ? null : ToDomain(row);
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
        // The scorer weight snapshot (N7, design §7.1) is written by the run orchestrator at run
        // start (WU-11a) — this repository only round-trips whatever value is already on the
        // aggregate at persistence time; "{}" is the WU-7b placeholder until that caller exists.
        WeightsJson = "{}",
        OriginSourceKeys = [],
        UnconfiguredSourceKeys = [],
        FailedSourceKeys = [],
        ProviderUnitsSpent = 0,
        AiCompletionsSpent = 0,
        SemanticDegraded = false,
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
            row.CompletedAt);
    }
}
