using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Domain.Social.ClientBrandManuals;
using Incoders.Hive.Domain.Social.Clients;
using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace Incoders.Hive.Infrastructure.Persistence.Postgres;

public sealed class EfClientBrandManualRepository : IClientBrandManualRepository
{
    private readonly AppIdentityDbContext _dbContext;

    public EfClientBrandManualRepository(AppIdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ClientBrandManual?> GetByIdAsync(
        ClientBrandManualId id,
        Guid organizationId,
        CancellationToken ct)
    {
        var row = await _dbContext.ClientBrandManuals
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id.Value && x.OrganizationId == organizationId, ct);

        return row is null ? null : ToDomain(row);
    }

    public async Task<ClientBrandManual?> GetActiveByClientAsync(
        ClientId clientId,
        Guid organizationId,
        CancellationToken ct)
    {
        var row = await _dbContext.ClientBrandManuals
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.ClientId == clientId.Value && x.OrganizationId == organizationId && x.IsActive,
                ct);

        return row is null ? null : ToDomain(row);
    }

    public async Task<(IReadOnlyList<ClientBrandManual> Items, int TotalCount)> ListByClientAsync(
        ClientId clientId,
        Guid organizationId,
        bool includeInactive,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        IQueryable<ClientBrandManualData> source = _dbContext.ClientBrandManuals
            .AsNoTracking()
            .Where(x => x.ClientId == clientId.Value && x.OrganizationId == organizationId);

        if (!includeInactive)
        {
            source = source.Where(x => x.IsActive);
        }

        var totalCount = await source.CountAsync(ct);
        var rows = await source
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (rows.Select(ToDomain).ToList(), totalCount);
    }

    public async Task AddAsync(ClientBrandManual manual, CancellationToken ct)
    {
        await _dbContext.ClientBrandManuals.AddAsync(ToData(manual), ct);
    }

    public void Update(ClientBrandManual manual)
    {
        var entity = _dbContext.ClientBrandManuals.Local.FirstOrDefault(x => x.Id == manual.Id.Value)
                     ?? _dbContext.ClientBrandManuals.First(x => x.Id == manual.Id.Value);

        // Only mutable fields — ClientId and OrganizationId are immutable (R-NFR-002)
        entity.Title = manual.Title;
        // Resolution 6: replace collection refs with new List<T> instances for ValueComparer detection
        entity.ColorPalette = new List<ColorEntry>(manual.ColorPalette);
        entity.Logos = new List<LogoEntry>(manual.Logos);
        entity.BackgroundTemplates = new List<BackgroundEntry>(manual.BackgroundTemplates);
        entity.Typography = new List<TypographyEntry>(manual.Typography);
        entity.VoiceAndTone = manual.VoiceAndTone;
        entity.Notes = manual.Notes;
        entity.IsActive = manual.IsActive;
        entity.UpdatedAtUtc = manual.UpdatedAtUtc;
    }

    public async Task SaveChangesAsync(CancellationToken ct)
    {
        await _dbContext.SaveChangesAsync(ct);
    }

    private static ClientBrandManual ToDomain(ClientBrandManualData d) =>
        ClientBrandManual.Restore(
            new ClientBrandManualId(d.Id),
            new ClientId(d.ClientId),
            d.OrganizationId,
            d.Title,
            d.ColorPalette,
            d.Logos,
            d.BackgroundTemplates,
            d.Typography,
            d.VoiceAndTone,
            d.Notes,
            d.IsActive,
            d.CreatedAtUtc,
            d.UpdatedAtUtc);

    private static ClientBrandManualData ToData(ClientBrandManual m) => new()
    {
        Id = m.Id.Value,
        ClientId = m.ClientId.Value,
        OrganizationId = m.OrganizationId,
        Title = m.Title,
        ColorPalette = new List<ColorEntry>(m.ColorPalette),
        Logos = new List<LogoEntry>(m.Logos),
        BackgroundTemplates = new List<BackgroundEntry>(m.BackgroundTemplates),
        Typography = new List<TypographyEntry>(m.Typography),
        VoiceAndTone = m.VoiceAndTone,
        Notes = m.Notes,
        IsActive = m.IsActive,
        CreatedAtUtc = m.CreatedAtUtc,
        UpdatedAtUtc = m.UpdatedAtUtc,
    };
}
