using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Infrastructure.Ai;
using Incoders.Hive.Infrastructure.Social;
using Incoders.Hive.Application.Abstractions.Tenancy;
using Incoders.Hive.Infrastructure.Caching.InMemory;
using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Integrations;
using Incoders.Hive.Infrastructure.Persistence;
using Incoders.Hive.Infrastructure.Persistence.Postgres;
using Incoders.Hive.Infrastructure.Social.Workflow;
using Incoders.Hive.Infrastructure.Tenancy;
using Incoders.Hive.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Incoders.Hive.Infrastructure.DependencyInjection;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.TryAddTimeProvider();
        services.AddHttpContextAccessor();
        services.AddDataProtection();

        services.AddSingleton<ICacheStore, InMemoryCacheStore>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddHttpClient();

        services.TryAddScoped<ICurrentUser, NullCurrentUser>();

        services.AddPersistence(configuration);
        services.AddIdentityAuth(configuration);

        services.AddMultiTenancy(configuration);


        return services;
    }

    private static IServiceCollection AddMultiTenancy(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MultiTenancyOptions>()
            .Bind(configuration.GetSection(MultiTenancyOptions.SectionName));

        services.AddScoped<ITenantRepository, EfTenantRepository>();
        services.AddScoped<ITenantMembershipService, EfTenantMembershipService>();
        services.AddScoped<CurrentTenant>();
        services.AddScoped<ICurrentTenant>(sp => sp.GetRequiredService<CurrentTenant>());
        services.AddScoped<ITenantResolver, HeaderTenantResolver>();
        services.AddSingleton<IContentWorkflowService, InMemoryContentWorkflowService>();

        return services;
    }

    private static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PersistenceOptions>()
            .Bind(configuration.GetSection(PersistenceOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddDbContext<AppIdentityDbContext>((serviceProvider, options) =>
        {
            var persistence = serviceProvider.GetRequiredService<IOptions<PersistenceOptions>>().Value;
            var connectionString = configuration.GetConnectionString(persistence.ConnectionStringName);

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    $"Connection string '{persistence.ConnectionStringName}' is required for Postgres persistence.");
            }

            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(AppIdentityDbContext).Assembly.FullName);
                npgsql.MigrationsHistoryTable(PostgresDatabaseNames.MigrationsHistoryTable);
            });
            // Suppress benign drift between runtime model and snapshot when migrations and snapshot agree.
            // See Migrations\AppIdentityDbContextModelSnapshot.cs vs latest Designer — bodies match.
            options.ConfigureWarnings(w =>
                w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
        });

        services.AddScoped<ISystemSettingRepository, EfSystemSettingRepository>();
        services.AddScoped<ISocialClientRepository, EfSocialClientRepository>();
        services.AddScoped<ISocialNetworkRepository, EfSocialNetworkRepository>();
        services.AddScoped<IClientReferentAccountRepository, EfClientReferentAccountRepository>();
        services.AddScoped<IClientBrandManualRepository, EfClientBrandManualRepository>();
        services.AddScoped<IClientStrategyRepository, EfClientStrategyRepository>();
        services.AddScoped<IMonthlyObjectiveRepository, EfMonthlyObjectiveRepository>();
        services.AddScoped<SocialNetworkCatalogSeeder>();
        services.AddScoped<IUserPreferenceRepository, EfUserPreferenceRepository>();
        services.AddScoped<IIntegrationProviderCatalogRepository, EfIntegrationProviderCatalogRepository>();
        services.AddScoped<IOrganizationIntegrationRepository, EfOrganizationIntegrationRepository>();
        services.AddScoped<IOrganizationIntegrationCredentialRepository, EfOrganizationIntegrationCredentialRepository>();
        services.AddScoped<IIntegrationRoutingPolicyRepository, EfIntegrationRoutingPolicyRepository>();
        services.AddScoped<IIntegrationExecutionAttemptRepository, EfIntegrationExecutionAttemptRepository>();
        services.AddScoped<ISingleContentAnalysisRepository, EfSingleContentAnalysisRepository>();
        services.AddScoped<IIntegrationSecretProtector, DataProtectionIntegrationSecretProtector>();
        services.AddScoped<IIntegrationConnectionTester, DefaultIntegrationConnectionTester>();
        services.AddScoped<IIntegrationResolver, IntegrationResolver>();
        services.AddScoped<IOrganizationIntegrationResolver, OrganizationIntegrationResolver>();
        services.AddScoped<IIntegrationCredentialResolver, IntegrationCredentialResolver>();
        services.AddScoped<IProviderFallbackPolicy, ProviderFallbackPolicy>();
        services.AddScoped<IIntegrationExecutionPolicy, IntegrationExecutionPolicy>();
        services.AddScoped<IExternalProviderClientFactory, ExternalProviderClientFactory>();
        services.AddScoped<IAiCompletionGateway, AiCompletionGateway>();
        services.AddScoped<ISocialContentResolver, SocialContentResolver>();
        services.AddScoped<IntegrationCatalogSeeder>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();

        return services;
    }


    private static void TryAddTimeProvider(this IServiceCollection services)
    {
        if (services.Any(d => d.ServiceType == typeof(TimeProvider)))
        {
            return;
        }

        services.AddSingleton(TimeProvider.System);
    }
}
