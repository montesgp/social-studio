using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Domain.Social.ClientMonthlyObjectives;
using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace Incoders.Hive.Infrastructure.Persistence.Postgres;

public sealed class EfClientMonthlyObjectiveRepository : IClientMonthlyObjectiveRepository
{
    private readonly AppIdentityDbContext _dbContext;

    public EfClientMonthlyObjectiveRepository(AppIdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ClientMonthlyObjective?> GetByClientAndMonthAsync(
        Guid clientId,
        Guid organizationId,
        int year,
        int month,
        CancellationToken ct)
    {
        var row = await _dbContext.ClientMonthlyObjectives
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.ClientId == clientId &&
                x.OrganizationId == organizationId &&
                x.Year == year &&
                x.Month == month,
                ct);

        return row is null ? null : ToDomain(row);
    }

    public async Task<IReadOnlyList<ClientMonthlyObjective>> ListByClientAsync(
        Guid clientId,
        Guid organizationId,
        CancellationToken ct)
    {
        var rows = await _dbContext.ClientMonthlyObjectives
            .AsNoTracking()
            .Where(x => x.ClientId == clientId && x.OrganizationId == organizationId)
            .OrderByDescending(x => x.Year)
            .ThenByDescending(x => x.Month)
            .ToListAsync(ct);

        return rows.Select(ToDomain).ToList();
    }

    public void Add(ClientMonthlyObjective objective)
    {
        var existing = _dbContext.ClientMonthlyObjectives.Local.FirstOrDefault(x => x.Id == objective.Id.Value);
        if (existing is null)
        {
            _dbContext.ClientMonthlyObjectives.Add(ToData(objective));
        }
    }

    public void Update(ClientMonthlyObjective objective)
    {
        var entity = _dbContext.ClientMonthlyObjectives.Local.FirstOrDefault(x => x.Id == objective.Id.Value)
                     ?? _dbContext.ClientMonthlyObjectives.First(x => x.Id == objective.Id.Value);

        // Only mutable fields — ClientId, OrganizationId, Year, Month are immutable
        // Resolution 6: replace collection ref with new List instance for ValueComparer detection
        entity.Objectives = objective.Objectives;
        entity.Kpis = new List<string>(objective.Kpis);
        entity.UpdatedAtUtc = objective.UpdatedAtUtc;
    }

    public async Task SaveChangesAsync(CancellationToken ct)
    {
        await _dbContext.SaveChangesAsync(ct);
    }

    private static ClientMonthlyObjective ToDomain(ClientMonthlyObjectiveData d) =>
        ClientMonthlyObjective.Restore(
            new ClientMonthlyObjectiveId(d.Id),
            d.ClientId,
            d.OrganizationId,
            d.Year,
            d.Month,
            d.Objectives,
            d.Kpis,
            d.CreatedAtUtc,
            d.UpdatedAtUtc);

    private static ClientMonthlyObjectiveData ToData(ClientMonthlyObjective o) => new()
    {
        Id = o.Id.Value,
        ClientId = o.ClientId,
        OrganizationId = o.OrganizationId,
        Year = o.Year,
        Month = o.Month,
        Objectives = o.Objectives,
        Kpis = new List<string>(o.Kpis),
        CreatedAtUtc = o.CreatedAtUtc,
        UpdatedAtUtc = o.UpdatedAtUtc,
    };
}
