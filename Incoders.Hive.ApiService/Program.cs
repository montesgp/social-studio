using System.Text.Json.Serialization;
using Incoders.Hive.ApiService.BackgroundServices;
using Incoders.Hive.Infrastructure.Social.Workflow;
using Incoders.Hive.ApiService.Endpoints.Admin;
using Incoders.Hive.ApiService.Endpoints.Auth;
using Incoders.Hive.ApiService.Endpoints.Common;
using Incoders.Hive.ApiService.Endpoints.Integrations;
using Incoders.Hive.ApiService.Endpoints.PlatformConfiguration;
using Incoders.Hive.ApiService.Endpoints.Social;
using Incoders.Hive.ApiService.Endpoints.Social.ContentGeneration;
using Incoders.Hive.ApiService.Endpoints.SystemSettings;
using Incoders.Hive.ApiService.Endpoints.Tenants;
using Incoders.Hive.ApiService.Endpoints.UserPreferences;
using Incoders.Hive.ApiService.Logging;
using Incoders.Hive.Application;
using Incoders.Hive.Infrastructure.DependencyInjection;
using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Integrations;
using Incoders.Hive.Infrastructure.PlatformConfiguration;
using Incoders.Hive.Infrastructure.Social;
using Incoders.Hive.Infrastructure.Social.Discovery;
using Incoders.Hive.Infrastructure.Social.NicheDiscovery;
using Incoders.Hive.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

const string FrontendCorsPolicy = "FrontendCors";

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddProvider(new LocalFileLoggerProvider(
    builder.Configuration.GetSection("FileLogging"),
    builder.Environment.ContentRootPath));

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ValidationExceptionHandler>();
// Backstop: any exception the validation handler doesn't claim is logged and returned as a
// friendly, code-carrying problem — never a raw 500 to the user.
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddCors(options =>
{
    options.AddPolicy(FrontendCorsPolicy, policy =>
    {
        var configuredOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
        if (configuredOrigins is { Length: > 0 })
        {
            policy.WithOrigins(configuredOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod();
            return;
        }

        policy.SetIsOriginAllowed(origin =>
            Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            && uri.IsLoopback
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// Consumes queued single-analysis jobs off the request thread (async/poll pipeline).
builder.Services.AddHostedService<SingleAnalysisBackgroundWorker>();

// Consumes queued profile-analysis jobs (async scrape + AI pipeline — Mundo 2).
builder.Services.AddHostedService<ProfileAnalysisBackgroundWorker>();

// Consumes queued content-generation jobs (async multi-step orchestration pipeline).
builder.Services.AddHostedService<ContentGenerationBackgroundWorker>();

// Consumes queued niche-discovery runs (async sourcing + analytics + semantics + scoring pipeline).
builder.Services.AddHostedService<Incoders.Hive.Infrastructure.Social.NicheDiscovery.NicheDiscoveryRunBackgroundService>();

// One-time, idempotent reconstruction of legacy packages to the current format taxonomy
// (story → story-single, feed-post → feed-single, carousel → feed-carousel). No-op once canonical.
builder.Services.AddHostedService<LegacyPackageReconstructionStartupJob>();

// One-time, idempotent backfill of draft_caption from draft_text for pre-existing visual Content
// Studio items (task 3.6). No-op once every visual item already has a caption.
builder.Services.AddHostedService<ContentStudioCaptionBackfillStartupJob>();

var app = builder.Build();

var clientAssetStorageOptions = app.Services.GetRequiredService<IOptions<ClientAssetStorageOptions>>().Value;
var clientAssetStorageRootPath = clientAssetStorageOptions.ResolveRootPath(app.Environment.ContentRootPath);
Directory.CreateDirectory(clientAssetStorageRootPath);

// Slice 1 (issue #57): default stays IsDevelopment(), but "Development" now targets the local
// Docker Postgres from docker-compose.dev.yml (see appsettings.Development.json), never the
// shared cloud DB. Any environment that is not local dev MUST set
// StartupTasks__ApplyMigrations=false explicitly once it points at a shared/cloud database, so
// the migration-delivery CI pipeline (not app startup) is the sole apply authority there.
// Slice 2 (issue #57) enforces this default at the config level for every non-dev environment.
if (app.Configuration.GetValue("StartupTasks:ApplyMigrations", app.Environment.IsDevelopment()))
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<AppIdentityDbContext>();
    dbContext.Database.Migrate();
}

if (app.Configuration.GetValue("StartupTasks:SeedData", true))
{
    await app.Services.SeedIntegrationCatalogAsync();
    // Auto-provision the host-local claude-p connector for every active organization (idempotent).
    await app.Services.SeedClaudePromptCliOrganizationIntegrationsAsync();
    // Auto-provision the local comfyui connector for every active organization (idempotent).
    await app.Services.SeedComfyUiOrganizationIntegrationsAsync();
    await app.Services.SeedSocialNetworkCatalogAsync();
    await app.Services.SeedNicheCatalogAsync();
    await app.Services.SeedMarketCatalogAsync();
    // Auto-enables the niche-discovery steps for any org whose YouTube data origin is connected:
    // without them every run dies with origin_unconfigured on a toggle the operator never saw.
    await app.Services.SeedNicheDiscoveryWorkflowStepsAsync();
    await app.Services.SeedPlatformSystemModulesAsync();
    await app.Services.SeedPlatformEmailTemplatesAsync();
    await app.Services.SeedIdentityRolesAsync();
    await app.Services.SeedSysAdminUserAsync();
    // Idempotent backfill: re-tags legacy "default" actor_mappings_json entries
    // to "profile" / "social-extraction" with isPrimary flags.
    await app.Services.RunApifyActorMappingBackfillAsync();
    // Idempotent: seeds a disabled, unassigned discover-niche-content step configuration for every
    // organization with a connected Apify integration, so the discovery stage is pre-created in
    // settings without ever forcing an actor (INT-DISC-02, D13).
    await app.Services.SeedTikTokNicheSearchWorkflowStepsAsync();
    // Idempotent, Development-only, opt-in: replays the captured Apify discovery output under
    // seed-data/discovery into the candidate bank through the PRODUCTION adapter, so a local
    // environment has real candidates without buying another run.
    await app.Services.ImportDiscoverySeedAsync();
}

// Configure the HTTP request pipeline.
app.UseExceptionHandler();
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(clientAssetStorageRootPath),
    RequestPath = clientAssetStorageOptions.NormalizedPublicBasePath(),
});
app.UseRouting();
app.UseCors(FrontendCorsPolicy);

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthentication();
app.UseTenantResolution();
app.UseAuthorization();

app.MapGet("/", () => "API service is running.");

app.MapAuthEndpoints();
app.MapAdminUsersEndpoints();
app.MapMeEndpoints();
app.MapSystemSettingsEndpoints();
app.MapPlatformConfigurationEndpoints();
app.MapTenantsEndpoints();
app.MapIntegrationsEndpoints();
app.MapSocialClientsEndpoints();
app.MapSocialNetworksEndpoints();
app.MapNichesEndpoints();
app.MapClientReferentAccountsEndpoints();
app.MapClientCompetitorAccountsEndpoints();
app.MapClientOwnAccountsEndpoints();
app.MapClientAestheticProfileEndpoints();
app.MapClientAssetEndpoints();
app.MapClientBrandManualsEndpoints();
app.MapGeneralStrategyEndpoints();
app.MapContentPlanEndpoints();
app.MapSocialContentWorkflowEndpoints();
app.MapSingleAnalyzerEndpoints();
app.MapDiscoveryBankEndpoints();
app.MapProfileAnalyzerEndpoints();
app.MapNicheDiscoveryEndpoints();
app.MapContentGenerationEndpoints();
app.MapContentGenerationBatches();
app.MapContentStudioEndpoints();
app.MapPublicationConnectionEndpoints();
app.MapContentBankEndpoints();
app.MapPublicationEndpoints();
app.MapUserPreferencesEndpoints();

app.MapDefaultEndpoints();

app.Run();
