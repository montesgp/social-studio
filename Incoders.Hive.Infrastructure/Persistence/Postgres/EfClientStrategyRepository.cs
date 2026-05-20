using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Domain.Social.ClientStrategies;
using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace Incoders.Hive.Infrastructure.Persistence.Postgres;

public sealed class EfClientStrategyRepository : IClientStrategyRepository
{
    private readonly AppIdentityDbContext _dbContext;

    public EfClientStrategyRepository(AppIdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ClientStrategy?> GetByClientIdAsync(
        Guid clientId,
        Guid organizationId,
        CancellationToken ct)
    {
        var row = await _dbContext.ClientStrategies
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ClientId == clientId && x.OrganizationId == organizationId, ct);

        return row is null ? null : ToDomain(row);
    }

    public void Add(ClientStrategy strategy)
    {
        // Use Local dedup pattern consistent with sibling repos
        var existing = _dbContext.ClientStrategies.Local.FirstOrDefault(x => x.Id == strategy.Id.Value);
        if (existing is null)
        {
            _dbContext.ClientStrategies.Add(ToData(strategy));
        }
    }

    public void Update(ClientStrategy strategy)
    {
        var entity = _dbContext.ClientStrategies.Local.FirstOrDefault(x => x.Id == strategy.Id.Value)
                     ?? _dbContext.ClientStrategies.First(x => x.Id == strategy.Id.Value);

        // Only mutable fields — ClientId and OrganizationId are immutable
        // Resolution 6: replace collection refs with new List<string> instances for ValueComparer detection
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

    private static ClientStrategy ToDomain(ClientStrategyData d) =>
        ClientStrategy.Restore(
            new ClientStrategyId(d.Id),
            d.ClientId,
            d.OrganizationId,
            d.ContentPillars,
            d.ToneKeywords,
            d.TargetAudience,
            d.PostingFrequency,
            d.Notes,
            d.CreatedAtUtc,
            d.UpdatedAtUtc);

    private static ClientStrategyData ToData(ClientStrategy s) => new()
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
