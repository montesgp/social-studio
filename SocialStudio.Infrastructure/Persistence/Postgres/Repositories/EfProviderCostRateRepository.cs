using SocialStudio.Application.Abstractions.Observability;
using SocialStudio.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace SocialStudio.Infrastructure.Persistence.Postgres.Repositories;

public sealed class EfProviderCostRateRepository : IProviderCostRateRepository
{
    private readonly AppIdentityDbContext _db;

    public EfProviderCostRateRepository(AppIdentityDbContext db)
    {
        _db = db;
    }

    public async Task<ProviderCostRate?> GetEffectiveRateAsync(
        string providerKey,
        string? model,
        DateTime atUtc,
        CancellationToken cancellationToken)
    {
        var query = _db.ProviderCostRates
            .Where(r => r.ProviderKey == providerKey
                        && r.Model == model
                        && r.EffectiveFrom <= atUtc
                        && (r.EffectiveTo == null || r.EffectiveTo > atUtc))
            .OrderByDescending(r => r.EffectiveFrom);

        var row = await query.FirstOrDefaultAsync(cancellationToken);
        if (row is null) return null;

        return new ProviderCostRate(
            row.ProviderKey,
            row.Model,
            row.CostPer1KInputTokens,
            row.CostPer1KOutputTokens,
            row.CostPerRequest,
            row.EffectiveFrom,
            row.EffectiveTo);
    }
}
