using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.ContentAnalysis;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Application.Abstractions.Observability;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ProfileAnalyzer.Analyze;
using Incoders.Hive.Infrastructure.Ai;
using Incoders.Hive.Infrastructure.Packaging;
using Incoders.Hive.Infrastructure.Rendering;
using Incoders.Hive.Infrastructure.Social;
using Incoders.Hive.Infrastructure.Social.Canva;
using Incoders.Hive.Infrastructure.Social.ContentGeneration;
using Incoders.Hive.Application.Abstractions.Tenancy;
using Incoders.Hive.Infrastructure.Caching.InMemory;
using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Integrations;
using Incoders.Hive.Infrastructure.Integrations.HealthChecks;
using Incoders.Hive.Infrastructure.Observability;
using Incoders.Hive.Infrastructure.PlatformConfiguration;
using Incoders.Hive.Infrastructure.Persistence;
using Incoders.Hive.Infrastructure.Persistence.Postgres;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Repositories;
using Incoders.Hive.Infrastructure.Social.Providers;
using Incoders.Hive.Infrastructure.Social.Providers.Apify;
using Incoders.Hive.Infrastructure.Social.Workflow;
using Incoders.Hive.Infrastructure.Tenancy;
using Incoders.Hive.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
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
        services.AddObservability();
        services.AddRenderingAndPackaging(configuration);


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
        services.AddScoped<ISocialClientHardDeleteRepository, EfSocialClientRepository>();
        services.AddScoped<ISocialNetworkRepository, EfSocialNetworkRepository>();
        services.AddScoped<IClientReferentAccountRepository, EfClientReferentAccountRepository>();
        services.AddScoped<IClientCompetitorAccountRepository, EfClientCompetitorAccountRepository>();
        services.AddScoped<IClientOwnAccountRepository, EfClientOwnAccountRepository>();
        services.AddScoped<IClientBrandManualRepository, EfClientBrandManualRepository>();
        services.AddScoped<IGeneralStrategyRepository, EfGeneralStrategyRepository>();
        services.AddScoped<IContentPlanRepository, EfContentPlanRepository>();
        services.AddScoped<SocialNetworkCatalogSeeder>();
        services.AddScoped<PlatformSystemModuleSeeder>();
        services.AddScoped<PlatformEmailTemplateSeeder>();
        services.AddScoped<IUserPreferenceRepository, EfUserPreferenceRepository>();
        services.AddScoped<IIntegrationProviderCatalogRepository, EfIntegrationProviderCatalogRepository>();
        services.AddScoped<IOrganizationIntegrationRepository, EfOrganizationIntegrationRepository>();
        services.AddScoped<IOrganizationIntegrationCredentialRepository, EfOrganizationIntegrationCredentialRepository>();
        services.AddScoped<ICanvaUserTokenRepository, EfCanvaUserTokenRepository>();

        // Canva OAuth PKCE service — per-request scoped; owns the HttpClient for token exchanges.
        // Named client "canva-oauth" with a reasonable timeout (30s). Canva's token endpoint
        // is a fast synchronous call, not a long-running job, so no resilience handler removal needed.
        services.AddHttpClient<ICanvaOAuthService, CanvaOAuthService>(
            "canva-oauth",
            client => client.Timeout = TimeSpan.FromSeconds(30));

        // Canva Design Import client — submits PPTX + polls import job (bounded 12 × 5 s = 60 s max).
        // Timeout set to 90 s to accommodate the poll budget plus submit RTT with margin.
        services.AddHttpClient<ICanvaDesignImportClient, CanvaDesignImportClient>(
            "canva-import",
            client => client.Timeout = TimeSpan.FromSeconds(90));
        services.AddScoped<IIntegrationExecutionAttemptRepository, EfIntegrationExecutionAttemptRepository>();
        services.AddScoped<ISingleContentAnalysisRepository, EfSingleContentAnalysisRepository>();
        services.AddScoped<IProfileAnalysisRepository, EfProfileAnalysisRepository>();
        // Profile analysis orchestrator — Application layer (depends on Infrastructure ports)
        services.AddScoped<IProfileAnalyzer, ProfileAnalyzerOrchestrator>();
        services.AddScoped<IProfileMetricCalculator, ProfileMetricCalculator>();
        services.AddScoped<IClientBrandSnapshotLoader, EfClientBrandSnapshotLoader>();
        services.AddScoped<IContentGenerationJobRepository, EfContentGenerationJobRepository>();
        services.AddScoped<IContentGenerationFormatResultRepository, EfContentGenerationFormatResultRepository>();
        services.AddScoped<IGeneratedPackageRepository, EfGeneratedPackageRepository>();
        services.AddScoped<IContentGenerationFormatAssetRepository, EfContentGenerationFormatAssetRepository>();
        // Per-platform Apify scrape adapters (ISocialPlatformScrapeAdapter)
        services.AddScoped<ISocialPlatformScrapeAdapter, TikTokApifyScrapeAdapter>();
        services.AddScoped<ISocialPlatformScrapeAdapter, InstagramApifyScrapeAdapter>();
        services.AddScoped<ISocialPlatformScrapeAdapter, YouTubeApifyScrapeAdapter>();
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
            nameof(Incoders.Hive.Infrastructure.Social.Providers.ApifyProfileScrapeResolver),
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
        // Pre-flight gate: probes all required provider APIs before the analyzer workflow starts.
        services.AddScoped<IContentAnalysisPreflight, ContentAnalysisPreflight>();
        services.AddScoped<IIntegrationResolver, IntegrationResolver>();
        services.AddScoped<IIntegrationCredentialResolver, IntegrationCredentialResolver>();
        services.AddScoped<IProviderFallbackPolicy, ProviderFallbackPolicy>();
        services.AddScoped<IIntegrationExecutionPolicy, IntegrationExecutionPolicy>();
        services.AddScoped<IExternalProviderClientFactory, ExternalProviderClientFactory>();
        // Register the concrete gateway first, then wrap it with the instrumentation decorator.
        // Manual DI decoration (Scrutor not available): inner is registered under a named key via
        // keyed services, outer is resolved as IAiCompletionGateway.
        services.AddScoped<AiCompletionGateway>();
        services.AddScoped<IAiCompletionGateway>(sp =>
        {
            var inner = sp.GetRequiredService<AiCompletionGateway>();
            var ledger = sp.GetRequiredService<IUsageEventLedger>();
            var ctx = sp.GetRequiredService<ProviderCallContext>();
            return new AiCompletionGatewayInstrumentationDecorator(inner, ledger, ctx);
        });

        services.AddScoped<ISocialContentResolver, SocialContentResolver>();
        // Profile scrape resolver — Mundo 2 infrastructure adapter (PA-04, PA-05, Decision 2).
        // Resolves Apify credentials per-org via IIntegrationResolver; does NOT route through
        // ApifySocialContentExtractor (Mundo 1 isolation — PA-REG-01).
        services.AddScoped<IProfileScrapeResolver, ApifyProfileScrapeResolver>();
        // Background job queue for the async single-analyzer pipeline. Singleton: one in-memory
        // bounded channel shared by the POST producer and the hosted worker consumer.
        services.AddSingleton<ISingleAnalysisJobQueue, ChannelSingleAnalysisJobQueue>();

        // Background job queue for the async profile-analysis pipeline.
        // Singleton: one in-memory bounded channel shared by POST producer and hosted worker consumer.
        services.AddSingleton<IProfileAnalysisJobQueue, ChannelProfileAnalysisJobQueue>();

        // Background job queue for the async content-generation pipeline.
        services.AddSingleton<IContentGenerationJobQueue, ChannelContentGenerationJobQueue>();
        services.AddScoped<IntegrationCatalogSeeder>();
        services.AddScoped<ApifyActorMappingBackfill>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();

        return services;
    }


    private static IServiceCollection AddObservability(this IServiceCollection services)
    {
        // In-memory cache for cost rate lookups (shared per DI container — effectively singleton).
        services.AddMemoryCache();

        // Cost rate repository and cached lookup
        services.AddScoped<IProviderCostRateRepository, EfProviderCostRateRepository>();
        services.AddScoped<IProviderCostRateLookup, ProviderCostRateLookup>();

        // Usage event storage (insert-only)
        services.AddScoped<IUsageEventRepository, EfUsageEventRepository>();

        // Safe ledger implementation — wraps all writes in try/catch
        services.AddScoped<IUsageEventLedger, UsageEventLedger>();

        // Ambient provider call context (scoped — one per HTTP request / background job scope)
        services.AddScoped<ProviderCallContext>();

        return services;
    }

    private static IServiceCollection AddRenderingAndPackaging(this IServiceCollection services, IConfiguration configuration)
    {
        // Rendering engine is a GLOBAL application setting, bound from the "Rendering" config section.
        // Default: Chromium (HTML/CSS via Playwright) with SkiaSharp fallback inside the renderer;
        // override Rendering:Engine to "Skia" to fall back to the slot-raster renderer entirely.
        //
        // B1b: registers the configured IContentRenderer (Chromium + pooled browser, or Skia) and the
        // real OpusVisualDesigner (IVisualDesigner, IsActive=true) — REPLACING the Application-layer
        // NoOpVisualDesigner and any default LocalSvgContentRenderer. This is what makes the
        // flyer/carousel designer pipeline genuinely ACTIVE in production.
        services.AddB1bRenderingAndDesigner(configuration);

        // Null adapters for future providers (v1 not supported)
        services.AddScoped<IImageGenerationClient, NullImageGenerationClient>();
        services.AddScoped<IVideoGenerationClient, NullVideoGenerationClient>();

        // Local ephemeral package storage
        // Root path from config key Storage:GeneratedPackagesPath; defaults to App_Data/generated-packages
        var rootPath = configuration["Storage:GeneratedPackagesPath"]
            ?? Path.Combine(AppContext.BaseDirectory, "App_Data", "generated-packages");
        services.AddSingleton<IGeneratedPackageStorage>(_ => new LocalGeneratedPackageStorage(rootPath));

        // Supabase Storage adapter — durable per-asset storage.
        // ServiceRoleKey comes from user-secrets (dev) or environment (prod) — NEVER from appsettings.
        services.AddOptions<SupabaseStorageOptions>()
            .Bind(configuration.GetSection(SupabaseStorageOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpClient<IGeneratedAssetStorage, SupabaseStorageAdapter>((sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<SupabaseStorageOptions>>().Value;
            client.BaseAddress = new Uri(opts.Url);
        });

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
