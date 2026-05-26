using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Domain.Social.ClientBrandManuals;
using Incoders.Hive.Domain.Social.ContentGeneration;
using Incoders.Hive.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace Incoders.Hive.Infrastructure.Social.ContentGeneration;

/// <summary>
/// EF Core adapter for <see cref="IClientBrandSnapshotLoader"/>.
/// Loads <see cref="ClientBrandManualData"/>, <see cref="ClientStrategyData"/>,
/// and the most recent <see cref="MonthlyObjectiveData"/> for the client at job-start time.
/// Returns a fully-inferred <see cref="BrandSnapshot"/> when any source is absent.
/// </summary>
public sealed class EfClientBrandSnapshotLoader : IClientBrandSnapshotLoader
{
    private readonly AppIdentityDbContext _dbContext;

    public EfClientBrandSnapshotLoader(AppIdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<BrandSnapshot> LoadAsync(
        Guid organizationId,
        Guid clientId,
        CancellationToken cancellationToken = default)
    {
        // Load active brand manual
        var brandRow = await _dbContext.ClientBrandManuals
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.ClientId == clientId && x.OrganizationId == organizationId && x.IsActive,
                cancellationToken);

        // Load strategy (one per client)
        var strategyRow = await _dbContext.ClientStrategies
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.ClientId == clientId && x.OrganizationId == organizationId,
                cancellationToken);

        // Load most recent monthly objective (current year/month first, then latest by year desc, month desc)
        var now = DateTime.UtcNow;
        var objectiveRow = await _dbContext.MonthlyObjectives
            .AsNoTracking()
            .Where(x => x.ClientId == clientId && x.OrganizationId == organizationId)
            .OrderByDescending(x => x.Year)
            .ThenByDescending(x => x.Month)
            .FirstOrDefaultAsync(cancellationToken);

        // Map to domain snapshots
        BrandManualSnapshot? brandSnapshot = brandRow is null
            ? null
            : new BrandManualSnapshot(
                ColorPalette: brandRow.ColorPalette.Select(c => c.Hex).ToArray(),
                Logos: brandRow.Logos.Select(l => l.Url).ToArray(),
                Typography: brandRow.Typography.Select(t => t.FontFamily).ToArray(),
                VoiceAndTone: brandRow.VoiceAndTone,
                Notes: brandRow.Notes);

        StrategySnapshot? strategySnapshot = strategyRow is null
            ? null
            : new StrategySnapshot(
                ContentPillars: strategyRow.ContentPillars.ToArray(),
                ToneKeywords: strategyRow.ToneKeywords.ToArray(),
                TargetAudience: strategyRow.TargetAudience,
                PostingFrequency: strategyRow.PostingFrequency);

        MonthlyObjectiveSnapshot? objectiveSnapshot = objectiveRow is null
            ? null
            : new MonthlyObjectiveSnapshot(
                Objectives: objectiveRow.Objectives,
                Kpis: objectiveRow.Kpis.ToArray());

        return BrandSnapshot.FromBrandData(brandSnapshot, strategySnapshot, objectiveSnapshot);
    }
}
