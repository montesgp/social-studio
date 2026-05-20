using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Domain.Social.MonthlyObjectives;
using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace Incoders.Hive.Infrastructure.Persistence.Postgres;

public sealed class EfMonthlyObjectiveRepository : IMonthlyObjectiveRepository
{
    private readonly AppIdentityDbContext _dbContext;

    public EfMonthlyObjectiveRepository(AppIdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<MonthlyObjective?> GetByClientAndMonthAsync(
        Guid clientId,
        Guid organizationId,
        int year,
        int month,
        CancellationToken ct)
    {
        var row = await _dbContext.MonthlyObjectives
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.ClientId == clientId &&
                x.OrganizationId == organizationId &&
                x.Year == year &&
                x.Month == month,
                ct);

        return row is null ? null : ToDomain(row);
    }

    public async Task<IReadOnlyList<MonthlyObjective>> ListByClientAsync(
        Guid clientId,
        Guid organizationId,
        CancellationToken ct)
    {
        var rows = await _dbContext.MonthlyObjectives
            .AsNoTracking()
            .Where(x => x.ClientId == clientId && x.OrganizationId == organizationId)
            .OrderByDescending(x => x.Year)
            .ThenByDescending(x => x.Month)
            .ToListAsync(ct);

        return rows.Select(ToDomain).ToList();
    }

    public void Add(MonthlyObjective objective)
    {
        var existing = _dbContext.MonthlyObjectives.Local.FirstOrDefault(x => x.Id == objective.Id.Value);
        if (existing is null)
        {
            _dbContext.MonthlyObjectives.Add(ToData(objective));
        }
    }

    public void Update(MonthlyObjective objective)
    {
        var entity = _dbContext.MonthlyObjectives.Local.FirstOrDefault(x => x.Id == objective.Id.Value)
                     ?? _dbContext.MonthlyObjectives.First(x => x.Id == objective.Id.Value);

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

    private static MonthlyObjective ToDomain(MonthlyObjectiveData d) =>
        MonthlyObjective.Restore(
            new MonthlyObjectiveId(d.Id),
            d.ClientId,
            d.OrganizationId,
            d.Year,
            d.Month,
            d.Objectives,
            d.Kpis,
            d.CreatedAtUtc,
            d.UpdatedAtUtc);

    private static MonthlyObjectiveData ToData(MonthlyObjective o) => new()
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
