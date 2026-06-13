using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Domain.Social.ClientBrandManuals;
using Incoders.Hive.Domain.Social.ContentGeneration;
using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace Incoders.Hive.Infrastructure.Social.ContentGeneration;

/// <summary>
/// EF Core adapter for <see cref="IClientBrandSnapshotLoader"/>.
/// Loads the client profile, active brand manual, general strategy, latest content plan objective,
/// and active referent/competitor accounts for the client at job-start time.
/// Returns a fully-inferred <see cref="BrandSnapshot"/> when any source is absent.
///
/// Source changes (PR 3 — loader re-sourcing):
///   - Strategy: now reads <c>GeneralStrategies</c> (was <c>ClientStrategies</c>).
///   - Monthly objective: now reads the <c>ContentPlans</c> objective node for the current period
///     (was <c>ClientMonthlyObjectives</c>). Prefers the current month; falls back to latest by
///     year desc, month desc.
///
/// <see cref="BrandSnapshot"/>, <see cref="StrategySnapshot"/>, and
/// <see cref="ClientMonthlyObjectiveSnapshot"/> output shapes are FROZEN — they are shared with
/// the in-flight generate-social-content-workflow. Do not change their constructors or property names.
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
        var clientRow = await _dbContext.SocialClients
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Id == clientId && x.OrganizationId == organizationId,
                cancellationToken);

        // Load active brand manual
        var brandRow = await _dbContext.ClientBrandManuals
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.ClientId == clientId && x.OrganizationId == organizationId && x.IsActive,
                cancellationToken);

        // Load general strategy (one per client — was ClientStrategies, now GeneralStrategies)
        var strategyRow = await _dbContext.GeneralStrategies
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.ClientId == clientId && x.OrganizationId == organizationId,
                cancellationToken);

        // Load content plan: prefer current month, fall back to latest by year/month desc.
        // Include Slots so the calendar is eager-loaded (no lazy loading in this context).
        // (was ClientMonthlyObjectives, now ContentPlans — same ordering semantics)
        var now = DateTime.UtcNow;
        var contentPlanRow = await _dbContext.ContentPlans
            .AsNoTracking()
            .Include(x => x.Slots)
            .Where(x => x.ClientId == clientId && x.OrganizationId == organizationId && x.PeriodKind == 0)
            .OrderByDescending(x => x.Year == now.Year && x.Month == now.Month)
            .ThenByDescending(x => x.Year)
            .ThenByDescending(x => x.Month)
            .FirstOrDefaultAsync(cancellationToken);

        var networkRows = await _dbContext.SocialNetworks
            .AsNoTracking()
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var referentRows = await _dbContext.ClientReferentAccounts
            .AsNoTracking()
            .Where(x => x.ClientId == clientId && x.OrganizationId == organizationId && x.IsActive)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var competitorRows = await _dbContext.ClientCompetitorAccounts
            .AsNoTracking()
            .Where(x => x.ClientId == clientId && x.OrganizationId == organizationId && x.IsActive)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        // Map to domain snapshots
        ClientProfileSnapshot? clientSnapshot = clientRow is null
            ? null
            : new ClientProfileSnapshot(
                Name: clientRow.Name,
                Description: clientRow.Description,
                SectorOrNiche: clientRow.SectorOrNiche);

        BrandManualSnapshot? brandSnapshot = brandRow is null
            ? null
            : new BrandManualSnapshot(
                ColorPalette: brandRow.ColorPalette.Select(c => c.Hex).ToArray(),
                Logos: brandRow.Logos.Select(l => l.Url).ToArray(),
                BackgroundTemplates: brandRow.BackgroundTemplates
                    .Select(background => new BrandBackgroundTemplateSnapshot(
                        background.Name,
                        background.Url,
                        background.UseCase,
                        background.Tier,
                        background.FormatKey,
                        background.IsFormatDefault))
                    .ToArray(),
                Typography: brandRow.Typography.Select(t => t.FontFamily).ToArray(),
                VoiceAndTone: brandRow.VoiceAndTone,
                Notes: brandRow.Notes);

        StrategySnapshot? strategySnapshot = strategyRow is null
            ? null
            : new StrategySnapshot(
                ContentPillars: strategyRow.ContentPillars.ToArray(),
                TargetAudience: strategyRow.TargetAudience,
                PostingFrequency: strategyRow.PostingFrequency);

        // Map ContentPlan objective node → ClientMonthlyObjectiveSnapshot (frozen contract shape)
        ClientMonthlyObjectiveSnapshot? objectiveSnapshot = contentPlanRow is null
            ? null
            : new ClientMonthlyObjectiveSnapshot(
                Objectives: contentPlanRow.ObjectiveText,
                Kpis: contentPlanRow.ObjectiveKpis.ToArray());

        // Map ContentPlan content-strategy node → ContentStrategySnapshot (new, optional)
        ContentStrategySnapshot? contentStrategySnapshot = contentPlanRow is null
            ? null
            : new ContentStrategySnapshot(
                StrategyText: contentPlanRow.ContentStrategyText,
                Source: contentPlanRow.ContentStrategyProvenanceSource,
                Status: contentPlanRow.ContentStrategyProvenanceStatus);

        // Map ContentPlan slots → CalendarSnapshot (new, optional)
        CalendarSnapshot? calendarSnapshot = contentPlanRow is null
            ? null
            : new CalendarSnapshot(
                Slots: contentPlanRow.Slots
                    .OrderBy(s => s.SortOrder)
                    .Select(s => new CalendarSlotSnapshot(
                        Day: s.Day,
                        ContentFormat: s.ContentFormat,
                        SortOrder: s.SortOrder))
                    .ToArray());

        // Map period triple → PeriodSnapshot (new, optional)
        PeriodSnapshot? periodSnapshot = contentPlanRow is null
            ? null
            : new PeriodSnapshot(
                Year: contentPlanRow.Year,
                Month: contentPlanRow.Month,
                PeriodKind: contentPlanRow.PeriodKind);

        var referentSnapshots = referentRows
            .Select(row => ToAccountSnapshot(row.SocialNetworkId, row.ProfileUrl, row.Handle, row.Label, row.Notes, networkRows))
            .ToArray();

        var competitorSnapshots = competitorRows
            .Select(row => ToAccountSnapshot(row.SocialNetworkId, row.ProfileUrl, row.Handle, row.Label, row.Notes, networkRows))
            .ToArray();

        return BrandSnapshot.FromBrandData(
            brandSnapshot,
            strategySnapshot,
            objectiveSnapshot,
            clientSnapshot,
            referentSnapshots,
            competitorSnapshots,
            contentStrategySnapshot,
            calendarSnapshot,
            periodSnapshot);
    }

    private static ClientAccountSnapshot ToAccountSnapshot(
        Guid socialNetworkId,
        string profileUrl,
        string? handle,
        string? label,
        string? notes,
        IReadOnlyDictionary<Guid, SocialNetworkData> networks)
    {
        var hasNetwork = networks.TryGetValue(socialNetworkId, out var network);
        return new ClientAccountSnapshot(
            NetworkCode: hasNetwork ? network!.Code : socialNetworkId.ToString("N"),
            NetworkName: hasNetwork ? network!.DisplayName : socialNetworkId.ToString("N"),
            ProfileUrl: profileUrl,
            Handle: handle,
            Label: label,
            Notes: notes);
    }
}
