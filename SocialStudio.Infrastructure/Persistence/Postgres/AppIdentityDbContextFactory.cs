using SocialStudio.Infrastructure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SocialStudio.Infrastructure.Persistence.Postgres;

public sealed class AppIdentityDbContextFactory : IDesignTimeDbContextFactory<AppIdentityDbContext>
{
    public AppIdentityDbContext CreateDbContext(string[] args)
    {
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? "Development";

        var apiServiceDirectory = FindApiServiceDirectory();
        var configuration = new ConfigurationBuilder()
            .SetBasePath(apiServiceDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .AddUserSecrets("social-studio-apiservice-0b4b0ca9")
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("Postgres");
        if (string.IsNullOrWhiteSpace(connectionString) || connectionString == "__SET_IN_USER_SECRETS__")
        {
            throw new InvalidOperationException(
                "Connection string 'Postgres' must be configured in user-secrets or environment variables.");
        }

        var builder = new DbContextOptionsBuilder<AppIdentityDbContext>();
        builder.UseNpgsql(connectionString, options =>
        {
            options.MigrationsAssembly(typeof(AppIdentityDbContext).Assembly.FullName);
            options.MigrationsHistoryTable(PostgresDatabaseNames.MigrationsHistoryTable);
        });

        return new AppIdentityDbContext(builder.Options);
    }

    private static string FindApiServiceDirectory()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "SocialStudio.ApiService");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the SocialStudio.ApiService project directory.");
    }
}
