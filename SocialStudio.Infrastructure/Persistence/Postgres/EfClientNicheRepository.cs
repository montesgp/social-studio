using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Domain.Social.Clients;
using SocialStudio.Domain.Social.Niches;
using SocialStudio.Infrastructure.Identity;
using SocialStudio.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace SocialStudio.Infrastructure.Persistence.Postgres;

public sealed class EfClientNicheRepository : IClientNicheRepository
{
    private readonly AppIdentityDbContext _dbContext;

    public EfClientNicheRepository(AppIdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<NicheId>> GetNicheIdsAsync(
        ClientId clientId,
        CancellationToken cancellationToken)
    {
        var rows = await _dbContext.ClientNiches
            .AsNoTracking()
            .Where(x => x.ClientId == clientId.Value)
            .Select(x => x.NicheId)
            .ToListAsync(cancellationToken);

        return rows.Select(id => new NicheId(id)).ToList();
    }

    public async Task ReplaceAsync(
        ClientId clientId,
        IReadOnlyList<NicheId> nicheIds,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var existing = await _dbContext.ClientNiches
            .Where(x => x.ClientId == clientId.Value)
            .ToListAsync(cancellationToken);

        var desired = nicheIds.Select(x => x.Value).Distinct().ToList();

        // Delete/insert only the delta so CreatedAtUtc survives a no-op save: that timestamp is the
        // only record of when the client actually declared the niche.
        var toRemove = existing.Where(x => !desired.Contains(x.NicheId)).ToList();
        if (toRemove.Count > 0)
        {
            _dbContext.ClientNiches.RemoveRange(toRemove);
        }

        var existingIds = existing.Select(x => x.NicheId).ToHashSet();
        foreach (var nicheId in desired.Where(id => !existingIds.Contains(id)))
        {
            _dbContext.ClientNiches.Add(new ClientNicheData
            {
                ClientId = clientId.Value,
                NicheId = nicheId,
                CreatedAtUtc = nowUtc,
            });
        }
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        _dbContext.SaveChangesAsync(cancellationToken);
}
