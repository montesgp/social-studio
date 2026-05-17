using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Infrastructure.Identity;

namespace Incoders.Hive.Infrastructure.Persistence.Postgres;

public sealed class EfUnitOfWork : IUnitOfWork
{
    private readonly AppIdentityDbContext _dbContext;

    public EfUnitOfWork(AppIdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _dbContext.SaveChangesAsync(cancellationToken);
}
