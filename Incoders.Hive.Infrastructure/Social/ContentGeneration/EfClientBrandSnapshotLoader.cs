using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Domain.Social.ClientBrandManuals;
using Incoders.Hive.Domain.Social.ContentGeneration;
using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace Incoders.Hive.Infrastructure.Social.ContentGeneration;

/// <summary>
/// EF Core adapter for <see cref="IClientBrandSnapshotLoader"/>.
/// Loads the client profile, active brand manual, strategy, latest monthly objective,
/// and active referent/competitor accounts for the client at job-start time.
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

        // Load strategy (one per client)
        var strategyRow = await _dbContext.ClientStrategies
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.ClientId == clientId && x.OrganizationId == organizationId,
                cancellationToken);

        // Load current monthly objective first, then latest by year desc, month desc.
        var now = DateTime.UtcNow;
        var objectiveRow = await _dbContext.ClientMonthlyObjectives
            .AsNoTracking()
            .Where(x => x.ClientId == clientId && x.OrganizationId == organizationId)
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
                        background.UseCase))
                    .ToArray(),
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

        ClientMonthlyObjectiveSnapshot? objectiveSnapshot = objectiveRow is null
            ? null
            : new ClientMonthlyObjectiveSnapshot(
                Objectives: objectiveRow.Objectives,
                Kpis: objectiveRow.Kpis.ToArray());

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
            competitorSnapshots);
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
