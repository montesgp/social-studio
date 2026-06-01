using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Domain.Social.GeneralStrategies;
using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace Incoders.Hive.Infrastructure.Persistence.Postgres;

public sealed class EfGeneralStrategyRepository : IGeneralStrategyRepository
{
    private readonly AppIdentityDbContext _dbContext;

    public EfGeneralStrategyRepository(AppIdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<GeneralStrategy?> GetByClientIdAsync(
        Guid clientId,
        Guid organizationId,
        CancellationToken ct)
    {
        var row = await _dbContext.GeneralStrategies
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ClientId == clientId && x.OrganizationId == organizationId, ct);

        return row is null ? null : ToDomain(row);
    }

    public void Add(GeneralStrategy strategy)
    {
        var existing = _dbContext.GeneralStrategies.Local.FirstOrDefault(x => x.Id == strategy.Id.Value);
        if (existing is null)
        {
            _dbContext.GeneralStrategies.Add(ToData(strategy));
        }
    }

    public void Update(GeneralStrategy strategy)
    {
        var entity = _dbContext.GeneralStrategies.Local.FirstOrDefault(x => x.Id == strategy.Id.Value)
                     ?? _dbContext.GeneralStrategies.First(x => x.Id == strategy.Id.Value);

        // Only mutable fields — ClientId and OrganizationId are immutable
        entity.ContentPillars = new List<string>(strategy.ContentPillars);
        entity.ToneKeywords = new List<string>(strategy.ToneKeywords);
        entity.TargetAudience = strategy.TargetAudience;
        entity.PostingFrequency = strategy.PostingFrequency;
        entity.Notes = strategy.Notes;
        entity.UpdatedAtUtc = strategy.UpdatedAtUtc;
    }

    public async Task SaveChangesAsync(CancellationToken ct)
    {
        await _dbContext.SaveChangesAsync(ct);
    }

    private static GeneralStrategy ToDomain(GeneralStrategyData d) =>
        GeneralStrategy.Restore(
            new GeneralStrategyId(d.Id),
            d.ClientId,
            d.OrganizationId,
            d.ContentPillars,
            d.ToneKeywords,
            d.TargetAudience,
            d.PostingFrequency,
            d.Notes,
            d.CreatedAtUtc,
            d.UpdatedAtUtc);

    private static GeneralStrategyData ToData(GeneralStrategy s) => new()
    {
        Id = s.Id.Value,
        ClientId = s.ClientId,
        OrganizationId = s.OrganizationId,
        ContentPillars = new List<string>(s.ContentPillars),
        ToneKeywords = new List<string>(s.ToneKeywords),
        TargetAudience = s.TargetAudience,
        PostingFrequency = s.PostingFrequency,
        Notes = s.Notes,
        CreatedAtUtc = s.CreatedAtUtc,
        UpdatedAtUtc = s.UpdatedAtUtc,
    };
}
