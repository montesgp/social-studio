using Incoders.Hive.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Incoders.Hive.Infrastructure.Persistence.Postgres;

public sealed class AppIdentityDbContextFactory : IDesignTimeDbContextFactory<AppIdentityDbContext>
{
    public AppIdentityDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? "Host=localhost;Port=5432;Database=incoders_hive;Username=postgres;Password=postgres";

        var builder = new DbContextOptionsBuilder<AppIdentityDbContext>();
        builder.UseNpgsql(connectionString, options =>
        {
            options.MigrationsAssembly(typeof(AppIdentityDbContext).Assembly.FullName);
            options.MigrationsHistoryTable(PostgresDatabaseNames.MigrationsHistoryTable);
        });

        return new AppIdentityDbContext(builder.Options);
    }
}
