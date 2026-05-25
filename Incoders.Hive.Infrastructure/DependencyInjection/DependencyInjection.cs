using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.ContentAnalysis;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Infrastructure.Ai;
using Incoders.Hive.Infrastructure.Social;
using Incoders.Hive.Application.Abstractions.Tenancy;
using Incoders.Hive.Infrastructure.Caching.InMemory;
using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Integrations;
using Incoders.Hive.Infrastructure.Integrations.HealthChecks;
using Incoders.Hive.Infrastructure.PlatformConfiguration;
using Incoders.Hive.Infrastructure.Persistence;
using Incoders.Hive.Infrastructure.Persistence.Postgres;
using Incoders.Hive.Infrastructure.Social.Providers.Apify;
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
        services.AddClientAssetStorage(configuration);


        return services;
    }

    private static IServiceCollection AddClientAssetStorage(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ClientAssetStorageOptions>()
            .Bind(configuration.GetSection(ClientAssetStorageOptions.SectionName));

        services.AddScoped<IClientAssetStorage, LocalClientAssetStorage>();
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
        services.AddScoped<PlatformSystemModuleSeeder>();
        services.AddScoped<PlatformEmailTemplateSeeder>();
        services.AddScoped<IUserPreferenceRepository, EfUserPreferenceRepository>();
        services.AddScoped<IIntegrationProviderCatalogRepository, EfIntegrationProviderCatalogRepository>();
        services.AddScoped<IOrganizationIntegrationRepository, EfOrganizationIntegrationRepository>();
        services.AddScoped<IOrganizationIntegrationCredentialRepository, EfOrganizationIntegrationCredentialRepository>();
        services.AddScoped<IIntegrationRoutingPolicyRepository, EfIntegrationRoutingPolicyRepository>();
        services.AddScoped<IIntegrationExecutionAttemptRepository, EfIntegrationExecutionAttemptRepository>();
        services.AddScoped<ISingleContentAnalysisRepository, EfSingleContentAnalysisRepository>();
        // Per-platform Apify scrape adapters (ISocialPlatformScrapeAdapter)
        services.AddScoped<ISocialPlatformScrapeAdapter, TikTokApifyScrapeAdapter>();
        services.AddScoped<ISocialPlatformScrapeAdapter, InstagramApifyScrapeAdapter>();
        services.AddScoped<ISocialPlatformScrapeAdapter, DefaultApifyScrapeAdapter>();

        // AI provider selection — thin adapter over IIntegrationResolver.ResolveDefaultAiProviderAsync
        services.AddScoped<IDefaultAiSelector, DefaultAiSelector>();

        // Long, single-shot outbound calls (Apify run-sync; LLM completions) regularly exceed the
        // Aspire standard resilience handler's ~10s per-attempt timeout that AddServiceDefaults()
        // applies to ALL HttpClients — which CANCELS them and retries (each retry restarts the
        // upstream actor run / LLM generation). Opt these clients OUT of the global resilience and
        // use a plain long timeout. Deliberate retry/fallback is a business-layer concern
        // (IIntegrationExecutionPolicy), not the HTTP layer.
        // RemoveAllResilienceHandlers is [Experimental] (EXTEXP0001) — the documented per-client opt-out.
        string[] longRunningHttpClients =
        [
            nameof(Incoders.Hive.Infrastructure.Social.Providers.ApifySocialContentExtractor),
            nameof(Incoders.Hive.Infrastructure.Ai.Providers.ClaudeModelClient),
            nameof(Incoders.Hive.Infrastructure.Ai.Providers.OpenAiModelClient),
            nameof(Incoders.Hive.Infrastructure.Ai.Providers.GeminiModelClient),
        ];
#pragma warning disable EXTEXP0001
        foreach (var clientName in longRunningHttpClients)
        {
            services.AddHttpClient(clientName, client => client.Timeout = TimeSpan.FromSeconds(120))
                .RemoveAllResilienceHandlers();
        }
#pragma warning restore EXTEXP0001

        services.AddScoped<IIntegrationSecretProtector, DataProtectionIntegrationSecretProtector>();
        // Real per-provider connectivity probes used by "Test connection".
        services.AddScoped<IExternalIntegrationHealthCheck, ApifyIntegrationHealthCheck>();
        services.AddScoped<IExternalIntegrationHealthCheck, ClaudeIntegrationHealthCheck>();
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
