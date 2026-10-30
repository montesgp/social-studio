using SocialStudio.Application.Abstractions;
using SocialStudio.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace SocialStudio.Infrastructure.Persistence.Postgres;

public sealed class EfUnitOfWork : IUnitOfWork
{
    private readonly AppIdentityDbContext _dbContext;

    public EfUnitOfWork(AppIdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" } pg)
        {
            // 23505 = unique_violation. Translate to a provider-neutral exception so Application
            // code (e.g. the single-analyzer org-scoped duplicate gate's DB-level backstop) can
            // react to a specific expected race without depending on Npgsql types.
            throw new UniqueConstraintViolationException(pg.ConstraintName ?? string.Empty, ex);
        }
    }
}
