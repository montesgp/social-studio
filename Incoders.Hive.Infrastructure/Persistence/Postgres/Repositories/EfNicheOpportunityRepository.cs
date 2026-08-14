using Incoders.Hive.Application.Abstractions.Social.NicheDiscovery;
using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace Incoders.Hive.Infrastructure.Persistence.Postgres.Repositories;

/// <summary>
/// EF implementation of <see cref="INicheOpportunityRepository"/> over <c>social.niche_opportunities</c>
/// (design §9, ND-11). <c>AppendAsync</c> + readers only, mirroring the port — no update or delete path
/// exists here either.
/// </summary>
public sealed class EfNicheOpportunityRepository : INicheOpportunityRepository
{
    private readonly AppIdentityDbContext _db;

    public EfNicheOpportunityRepository(AppIdentityDbContext db)
    {
        _db = db;
    }

    public async Task AppendAsync(IReadOnlyList<NicheOpportunityRecord> opportunities, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(opportunities);

        if (opportunities.Count == 0)
        {
            return;
        }

        // A double-write of the same (run_id, rank) is a hard error — never a silent dedup — since a
        // duplicate rank inside one run's ranked output is a scoring-bug symptom, not a legitimate
        // re-observation (unlike social.discovered_candidates' append semantics). Checked at the
        // application boundary because the in-memory test provider does not enforce
        // ux_niche_opportunities_run_rank the way Postgres does in production.
        var incomingKeys = opportunities.Select(o => (o.RunId, o.Rank)).ToList();
        if (incomingKeys.Distinct().Count() != incomingKeys.Count)
        {
            throw new InvalidOperationException(
                "Duplicate (run_id, rank) inside one AppendAsync call — a hard error under ux_niche_opportunities_run_rank.");
        }

        var runIds = opportunities.Select(o => o.RunId).Distinct().ToList();
        var existingKeys = await _db.NicheOpportunities
            .Where(x => runIds.Contains(x.RunId))
            .Select(x => new { x.RunId, x.Rank })
            .ToListAsync(ct);
        var existingKeySet = existingKeys.Select(x => (x.RunId, x.Rank)).ToHashSet();

        if (incomingKeys.Any(existingKeySet.Contains))
        {
            throw new InvalidOperationException(
                "Duplicate (run_id, rank) against an already-persisted opportunity — a hard error under ux_niche_opportunities_run_rank.");
        }

        foreach (var opportunity in opportunities)
        {
            _db.NicheOpportunities.Add(ToData(opportunity));
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<NicheOpportunityRecord>> ListByRunAsync(
        Guid organizationId, Guid runId, CancellationToken ct = default)
    {
        var rows = await _db.NicheOpportunities
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.RunId == runId)
            .OrderBy(x => x.Rank)
            .ToListAsync(ct);

        return rows.Select(ToDomain).ToList();
    }

    public async Task<NicheOpportunityRecord?> GetByIdAsync(
        Guid organizationId, Guid opportunityId, CancellationToken ct = default)
    {
        var row = await _db.NicheOpportunities
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == opportunityId, ct);

        return row is null ? null : ToDomain(row);
    }

    private static NicheOpportunityData ToData(NicheOpportunityRecord r) => new()
    {
        Id = r.Id,
        RunId = r.RunId,
        OrganizationId = r.OrganizationId,
        NicheId = r.NicheId,
        SubNicheLabel = r.SubNicheLabel,
        ContentPattern = r.ContentPattern,
        TopicLabels = r.TopicLabels.ToList(),
        OpportunityScore = r.OpportunityScore,
        ConfidenceScore = r.ConfidenceScore,
        EffectiveWeightDenominator = r.EffectiveWeightDenominator,
        ExcludedSignalsJson = r.ExcludedSignalsJson,
        MedianViews = r.MedianViews,
        OutlierRatio = r.OutlierRatio,
        OutlierDensity = r.OutlierDensity,
        GrowthProxy = r.GrowthProxy,
        CrossChannelRepetition = r.CrossChannelRepetition,
        TargetMarketDemand = r.TargetMarketDemand,
        TargetMarketCompetition = r.TargetMarketCompetition,
        TargetMarketGap = r.TargetMarketGap,
        AutomationSuitability = r.AutomationSuitability,
        ChannelSampleSize = r.ChannelSampleSize,
        VideoSampleSize = r.VideoSampleSize,
        Rank = r.Rank,
        ObservedAt = r.ObservedAtUtc.UtcDateTime,
        CreatedAt = DateTime.UtcNow,
    };

    private static NicheOpportunityRecord ToDomain(NicheOpportunityData e) => new(
        Id: e.Id,
        RunId: e.RunId,
        OrganizationId: e.OrganizationId,
        NicheId: e.NicheId,
        SubNicheLabel: e.SubNicheLabel,
        ContentPattern: e.ContentPattern,
        TopicLabels: e.TopicLabels,
        OpportunityScore: e.OpportunityScore,
        ConfidenceScore: e.ConfidenceScore,
        EffectiveWeightDenominator: e.EffectiveWeightDenominator,
        ExcludedSignalsJson: e.ExcludedSignalsJson,
        MedianViews: e.MedianViews,
        OutlierRatio: e.OutlierRatio,
        OutlierDensity: e.OutlierDensity,
        GrowthProxy: e.GrowthProxy,
        CrossChannelRepetition: e.CrossChannelRepetition,
        TargetMarketDemand: e.TargetMarketDemand,
        TargetMarketCompetition: e.TargetMarketCompetition,
        TargetMarketGap: e.TargetMarketGap,
        AutomationSuitability: e.AutomationSuitability,
        ChannelSampleSize: e.ChannelSampleSize,
        VideoSampleSize: e.VideoSampleSize,
        Rank: e.Rank,
        ObservedAtUtc: new DateTimeOffset(e.ObservedAt, TimeSpan.Zero));
}
