using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.ContentAnalysis;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Application.Abstractions.Observability;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Abstractions.Social.ContentDiscovery;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Abstractions.Social.ContentStudio;
using Incoders.Hive.Application.Abstractions.Social.Publications;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Background;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Batches;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Discovery;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Ideas;
using Incoders.Hive.Application.Features.Social.DiscoveryBankIntelligence;
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
using Incoders.Hive.Infrastructure.Process;
using Incoders.Hive.Infrastructure.Persistence;
using Incoders.Hive.Infrastructure.Persistence.Postgres;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Maintenance;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Repositories;
using Incoders.Hive.Infrastructure.Social.Providers;
using Incoders.Hive.Infrastructure.Social.Providers.Apify;
using Incoders.Hive.Infrastructure.Social.Publications;
using Incoders.Hive.Application.Features.Social.Publications;
using Incoders.Hive.Infrastructure.Social.Workflow;
using Incoders.Hive.Infrastructure.Tenancy;
using Incoders.Hive.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
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
        services.AddSingleton<IProcessRunner, SystemProcessRunner>();

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
        services.AddScoped<ITenantHardDeleteRepository, EfTenantHardDeleteRepository>();
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
        services.AddScoped<ISocialClientDirectoryRepository, EfSocialClientRepository>();
        services.AddScoped<ISocialNetworkRepository, EfSocialNetworkRepository>();
        services.AddScoped<INicheRepository, EfNicheRepository>();
        services.AddScoped<IClientNicheRepository, EfClientNicheRepository>();
        services.AddScoped<IClientReferentAccountRepository, EfClientReferentAccountRepository>();
        services.AddScoped<IClientCompetitorAccountRepository, EfClientCompetitorAccountRepository>();
        services.AddScoped<IClientOwnAccountRepository, EfClientOwnAccountRepository>();
        services.AddScoped<IClientBrandManualRepository, EfClientBrandManualRepository>();
        services.AddScoped<IGeneralStrategyRepository, EfGeneralStrategyRepository>();
        services.AddScoped<IContentPlanRepository, EfContentPlanRepository>();
        services.AddScoped<IContentItemRepository, EfContentItemRepository>();
        services.AddScoped<IContentSetRepository, EfContentSetRepository>();
        services.AddScoped<IPublicationRepository, EfPublicationRepository>();
        services.AddScoped<IClientAccountConnectionRepository, EfClientAccountConnectionRepository>();
        services.AddScoped<IPublicationDestinationResolver, PublicationDestinationResolver>();
        services.AddScoped<IPublicationConnectorSelector, PublicationConnectorSelector>();
        // Shared connector-invocation helper for PublishItemNowCommand/RetryPublicationCommand (WU3).
        services.AddScoped<PublicationPublishAttemptRunner>();
        // Native Instagram Graph API adapter — v1's only registered IPublicationConnector. Typed
        // HttpClient owns the connector; also exposed as IPublicationConnector for the selector's
        // IEnumerable<IPublicationConnector> composition.
        services.AddHttpClient<InstagramGraphPublicationConnector>();
        services.AddScoped<IPublicationConnector>(
            sp => sp.GetRequiredService<InstagramGraphPublicationConnector>());
        services.AddScoped<SocialNetworkCatalogSeeder>();
        services.AddScoped<NicheCatalogSeeder>();
        services.AddScoped<PlatformSystemModuleSeeder>();
        services.AddScoped<PlatformEmailTemplateSeeder>();
        services.AddScoped<IUserPreferenceRepository, EfUserPreferenceRepository>();
        services.AddScoped<IIntegrationProviderCatalogRepository, EfIntegrationProviderCatalogRepository>();
        services.AddScoped<IOrganizationIntegrationRepository, EfOrganizationIntegrationRepository>();
        services.AddScoped<IOrganizationIntegrationCredentialRepository, EfOrganizationIntegrationCredentialRepository>();
        services.AddScoped<IOrganizationAiModelRepository, EfOrganizationAiModelRepository>();
        services.AddScoped<IOrganizationActorRepository, EfOrganizationActorRepository>();
        services.AddScoped<IWorkflowStepConfigurationRepository, EfWorkflowStepConfigurationRepository>();
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
        services.AddScoped<IContentGenerationBatchRepository, EfContentGenerationBatchRepository>();

        // Batch composition: narrow read ports adapted over the existing repositories, plus the
        // platform piece ceiling. The cap lookup is the seam that later becomes billing-aware.
        services.AddScoped<IBatchAnalysisLookup, BatchAnalysisLookup>();
        services.AddScoped<IBatchPieceCapLookup, ClientBatchPieceCapLookup>();

        // Idea sources, in preference order. The ORDER argument decides precedence, never this
        // registration order — see ContentIdeaChain, which sorts on it. Adding a source (Google
        // Trends, market discovery, a second agent) is one more line here and nothing else.
        //
        // Both entries are the SAME generator with a different grounding: the first stands on the
        // strategy the client actually authored, the second on everything else we know about them, so
        // a client with no strategy still gets ideas instead of an empty batch.
        services.AddScoped<IContentIdeaSource>(sp => new AiClientIdeaSource(
            ClientStrategyIdeaGrounding.Key,
            order: 1,
            new ClientStrategyIdeaGrounding(sp.GetRequiredService<IClientBrandSnapshotLoader>()),
            sp.GetRequiredService<IAiCompletionGateway>(),
            // "ai-generation" is an EXISTING routed capability. A new key here would resolve to no
            // provider route and fail at runtime with AiRouteNotConfigured.
            capabilityKey: "ai-generation",
            promptVersionKey: "content-ideas/v1"));

        // Order 2: the only source grounded in EVIDENCE rather than belief — it mines the client's
        // referent accounts for posts that actually performed, so its ideas carry a real signal.
        services.AddScoped<IContentIdeaSource, ReferenceAccountIdeaSource>();

        // Discovery sources, in preference order. The ORDER argument decides precedence, never this
        // registration order — see ContentDiscoveryChain, which sorts on it.
        //
        // Proposal phase of the Content Generator: real content with real metrics for a human to pick
        // from. Same scraping as the idea source above, but it keeps the whole post because a person
        // is going to judge it.
        services.AddScoped<IContentDiscoverySource, ReferenceAccountDiscoverySource>();   // Order 1
        services.AddScoped<IContentDiscoverySource, OwnAccountDiscoverySource>();         // Order 2
        services.AddScoped<IContentDiscoverySource, TikTokNicheSearchSource>();           // Order 3
        // Curated niche hashtags are what the niche search asks for. The provider is pure Application
        // (it only composes the two catalog ports), so it is registered here purely for wiring.
        services.AddScoped<IClientNicheHashtagProvider, ClientNicheHashtagProvider>();
        // Actor resolution is exclusively configuration-driven (CD-12/D13) — see ApifyNicheSearchResolver.
        // The cost/duration guards are bound here so an operator can tune them without a redeploy; when
        // the section is absent the options object's own conservative defaults apply, so a niche-search
        // run is NEVER dispatched without a platform cost cap.
        services.AddOptions<Incoders.Hive.Infrastructure.Social.Providers.Apify.ApifyNicheSearchGuardOptions>()
            .Bind(configuration.GetSection(
                Incoders.Hive.Infrastructure.Social.Providers.Apify.ApifyNicheSearchGuardOptions.SectionName));
        services.AddScoped<INicheSearchResolver, ApifyNicheSearchResolver>();
        // The read-only readiness probe behind the integrations checklist. It shares
        // ApifyNicheSearchActorConfiguration with the resolver above so the UI's "configured" and the
        // dispatcher's "configured" cannot drift apart again.
        services.AddScoped<INicheSearchConfigurationProbe, ApifyNicheSearchConfigurationProbe>();

        // The chain IS the discovery service. Exactly ONE registration of IContentDiscoveryService may
        // exist — a second AddScoped<IContentDiscoveryService, …> would silently win GetRequiredService
        // with no compile or runtime error (see DiscoveryCompositionTests).
        services.AddScoped<IContentDiscoveryService, ContentDiscoveryChain>();

        // WU-4/WU-5: scoring/persistence support for the discovery chain. AccountBaselineProvider is
        // plain (not a port) — it is a read-through cache wrapper the call site uses directly, not an
        // interface with multiple implementations.
        services.AddScoped<IContentCandidateRepository, EfContentCandidateRepository>();
        services.AddScoped<IAccountBaselineRepository, EfAccountBaselineRepository>();
        services.AddScoped<AccountBaselineProvider>();

        // AI Analysis tool: a READ-ONLY view of the same bank, plus the caps that keep one operator
        // question cheap regardless of how large that bank grows. Absent configuration leaves the
        // options record's own conservative defaults in force, so the row cap can never be "unset".
        services.AddScoped<IDiscoveryBankReader, EfDiscoveryBankReader>();
        services.AddOptions<DiscoveryBankIntelligenceOptions>()
            .Bind(configuration.GetSection(DiscoveryBankIntelligenceOptions.SectionName));

        // LOCAL seed import: replays captured Apify run output into the bank through the PRODUCTION
        // adapter. Inert unless explicitly enabled AND running in Development — see
        // DiscoverySeedImportOptions for why both switches exist.
        services.AddOptions<Incoders.Hive.Infrastructure.Social.Discovery.DiscoverySeedImportOptions>()
            .Bind(configuration.GetSection(
                Incoders.Hive.Infrastructure.Social.Discovery.DiscoverySeedImportOptions.SectionName));
        services.AddScoped<Incoders.Hive.Infrastructure.Social.Discovery.DiscoverySeedImporter>();

        // Selection -> generation: turns the proposals the operator ticked into queued pieces.
        services.AddScoped<IProposalGenerationStarter, ProposalGenerationStarter>();

        services.AddScoped<IContentIdeaSource>(sp => new AiClientIdeaSource(
            ClientKnowledgeIdeaGrounding.Key,
            order: 4,
            new ClientKnowledgeIdeaGrounding(sp.GetRequiredService<IClientBrandSnapshotLoader>()),
            sp.GetRequiredService<IAiCompletionGateway>(),
            capabilityKey: "ai-generation",
            promptVersionKey: "content-ideas/v1"));

        services.AddScoped<ContentIdeaChain>();
        services.Configure<ContentGenerationBatchOptions>(
            configuration.GetSection(ContentGenerationBatchOptions.SectionName));
        services.AddScoped(sp => sp.GetRequiredService<IOptions<ContentGenerationBatchOptions>>().Value);
        services.AddScoped<IContentGenerationFormatResultRepository, EfContentGenerationFormatResultRepository>();
        services.AddScoped<IContentGenerationFailurePolicy, EfContentGenerationFailurePolicy>();
        services.AddScoped<IGeneratedPackageRepository, EfGeneratedPackageRepository>();
        services.AddScoped<IContentGenerationFormatAssetRepository, EfContentGenerationFormatAssetRepository>();
        // Change 3 (job 8757e85a-6acd-49b4-af11-a2d19fbcd3e1 incident): sysadmin audit timeline reader.
        services.AddScoped<IContentGenerationAuditReader, EfContentGenerationAuditReader>();
        services.AddScoped<ILegacyPackageReconstructor, EfLegacyPackageReconstructor>();
        // Task 3.6 (Content Studio editing completeness): idempotent draft_caption backfill.
        services.AddScoped<ContentStudioCaptionBackfill>();
        // Per-platform Apify scrape adapters (ISocialPlatformScrapeAdapter)
        services.AddScoped<ISocialPlatformScrapeAdapter, TikTokApifyScrapeAdapter>();
        services.AddScoped<ISocialPlatformScrapeAdapter, InstagramApifyScrapeAdapter>();
        services.AddScoped<ISocialPlatformScrapeAdapter, YouTubeApifyScrapeAdapter>();
        services.AddScoped<ISocialPlatformScrapeAdapter, DefaultApifyScrapeAdapter>();

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
            nameof(Incoders.Hive.Infrastructure.Social.Providers.ApifyNicheSearchResolver),
            // Actor catalog paging can walk several 1000-record pages on a busy account.
            nameof(Incoders.Hive.Infrastructure.Social.Providers.ApifyActorCatalogClient),
            nameof(Incoders.Hive.Infrastructure.Ai.Providers.ClaudeModelClient),
            nameof(Incoders.Hive.Infrastructure.Ai.Providers.ClaudeModelCatalogClient),
            nameof(Incoders.Hive.Infrastructure.Ai.Providers.OpenAiModelClient),
            nameof(Incoders.Hive.Infrastructure.Ai.Providers.OpenAiModelCatalogClient),
            nameof(Incoders.Hive.Infrastructure.Ai.Providers.GeminiModelClient),
            // ComfyUiImageModelClient's own overall Timeout (default 600s) bounds generation via
            // CancellationTokenSource; the underlying HttpClient must not cut the request short first.
            nameof(Incoders.Hive.Infrastructure.Ai.Providers.ComfyUiImageModelClient),
            nameof(Incoders.Hive.Infrastructure.Ai.Providers.ComfyUiModelCatalogClient),
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
        services.AddScoped<IExternalIntegrationHealthCheck, ClaudePromptCliIntegrationHealthCheck>();
        services.AddScoped<IExternalIntegrationHealthCheck, OpenAiIntegrationHealthCheck>();
        services.AddScoped<IExternalIntegrationHealthCheck, ComfyUiIntegrationHealthCheck>();
        services.AddScoped<IIntegrationConnectionTester, DefaultIntegrationConnectionTester>();
        // Pre-flight gate: probes all required provider APIs before the analyzer workflow starts.
        services.AddScoped<IContentAnalysisPreflight, ContentAnalysisPreflight>();
        services.AddScoped<IIntegrationResolver, IntegrationResolver>();
        services.AddScoped<IWorkflowConfigurationResolver, WorkflowConfigurationResolver>();
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

        // Image generation gateway — explicit factory wires the execution policy (CAC-INT-01).
        services.AddScoped<ImageGenerationGateway>(sp => new ImageGenerationGateway(
            sp.GetRequiredService<IWorkflowConfigurationResolver>(),
            sp.GetRequiredService<IIntegrationCredentialResolver>(),
            sp.GetRequiredService<IExternalProviderClientFactory>(),
            sp.GetRequiredService<IIntegrationExecutionPolicy>(),
            sp.GetService<ILogger<ImageGenerationGateway>>()));
        services.AddScoped<IImageGenerationGateway>(sp =>
        {
            var inner = sp.GetRequiredService<ImageGenerationGateway>();
            var ledger = sp.GetRequiredService<IUsageEventLedger>();
            var ctx = sp.GetRequiredService<ProviderCallContext>();
            return new ImageGenerationGatewayInstrumentationDecorator(inner, ledger, ctx);
        });

        // Application services for the creative-background pipeline.
        services.AddScoped<IImagePromptWriter, ImagePromptWriter>();
        services.AddScoped<IClientAestheticImageBackgroundService, ClientAestheticImageBackgroundService>();

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
        services.AddScoped<ClaudePromptCliOrganizationSeeder>();
        services.AddScoped<ComfyUiOrganizationSeeder>();
        services.AddScoped<TikTokNicheSearchWorkflowStepSeeder>();
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
        // real AiVisualDesigner (IVisualDesigner, IsActive=true) — REPLACING the Application-layer
        // NoOpVisualDesigner and any default LocalSvgContentRenderer. This is what makes the
        // story/carousel designer pipeline genuinely ACTIVE in production.
        services.AddB1bRenderingAndDesigner(configuration);

        // WU-9: IImageGenerationClient retired — superseded by IClientAestheticImageBackgroundService
        // (port family: image-prompt-writer → image-render-gateway → image-background-service).
        // NullImageGenerationClient deleted; image generation is now opt-in via the nullable
        // IClientAestheticImageBackgroundService processor dependency (registered separately).

        // Null adapters for future providers (v1 not supported)
        services.AddScoped<IVideoGenerationClient, NullVideoGenerationClient>();

        // Local ephemeral package storage
        // Root path from config key Storage:GeneratedPackagesPath; defaults to App_Data/generated-packages
        var rootPath = configuration["Storage:GeneratedPackagesPath"]
            ?? Path.Combine(AppContext.BaseDirectory, "App_Data", "generated-packages");
        services.AddSingleton<IGeneratedPackageStorage>(_ => new LocalGeneratedPackageStorage(rootPath));

        // Generated-asset storage provider — config key Storage:AssetProvider (default "Supabase").
        // "Local" routes all generated-asset traffic to the local filesystem (dev-only) so
        // iteration consumes ZERO Supabase Storage egress/quota. Anything else keeps the
        // existing Supabase Storage adapter registered exactly as-is.
        var assetProvider = configuration["Storage:AssetProvider"] ?? "Supabase";
        if (string.Equals(assetProvider, "Local", StringComparison.OrdinalIgnoreCase))
        {
            var assetStorageRoot = configuration["Storage:AssetStorageRoot"]
                ?? Path.Combine(AppContext.BaseDirectory, "App_Data", "generated-assets");
            services.AddSingleton<IGeneratedAssetStorage>(
                _ => new LocalFileGeneratedAssetStorage(assetStorageRoot));
        }
        else
        {
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
        }

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
