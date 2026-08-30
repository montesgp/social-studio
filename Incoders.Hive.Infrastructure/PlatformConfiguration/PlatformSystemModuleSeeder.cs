using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Features.PlatformConfiguration.WorkspaceModules;
using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace Incoders.Hive.Infrastructure.PlatformConfiguration;

public sealed class PlatformSystemModuleSeeder
{
    private readonly AppIdentityDbContext _dbContext;
    private readonly IClock _clock;

    public PlatformSystemModuleSeeder(
        AppIdentityDbContext dbContext,
        IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var hasChanges = false;

        foreach (var module in WorkspaceModuleCatalog.All)
        {
            var existing = await _dbContext.SystemModules
                .FirstOrDefaultAsync(x => x.ModuleKey == module.Key, cancellationToken);
            if (existing is not null)
            {
                continue;
            }

            _dbContext.SystemModules.Add(new SystemModuleData
            {
                Id = module.Id,
                ModuleKey = module.Key,
                DisplayName = module.Label,
                Description = module.Description,
                ShortLabel = module.ShortLabel,
                IsActive = true,
                CreatedAtUtc = module.CreatedAtUtc,
                UpdatedAtUtc = _clock.UtcNow,
            });
            hasChanges = true;
        }

        if (hasChanges)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
