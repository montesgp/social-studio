using System.Text.Json.Serialization;
using Incoders.Hive.ApiService.Endpoints.Admin;
using Incoders.Hive.ApiService.Endpoints.Auth;
using Incoders.Hive.ApiService.Endpoints.Common;
using Incoders.Hive.ApiService.Endpoints.Integrations;
using Incoders.Hive.ApiService.Endpoints.PlatformConfiguration;
using Incoders.Hive.ApiService.Endpoints.Social;
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

var app = builder.Build();

var clientAssetStorageOptions = app.Services.GetRequiredService<IOptions<ClientAssetStorageOptions>>().Value;
var clientAssetStorageRootPath = clientAssetStorageOptions.ResolveRootPath(app.Environment.ContentRootPath);
Directory.CreateDirectory(clientAssetStorageRootPath);

if (app.Configuration.GetValue("StartupTasks:ApplyMigrations", app.Environment.IsDevelopment()))
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<AppIdentityDbContext>();
    dbContext.Database.Migrate();
}

if (app.Configuration.GetValue("StartupTasks:SeedData", true))
{
    await app.Services.SeedIntegrationCatalogAsync();
    await app.Services.SeedSocialNetworkCatalogAsync();
    await app.Services.SeedPlatformSystemModulesAsync();
    await app.Services.SeedPlatformEmailTemplatesAsync();
    await app.Services.SeedIdentityRolesAsync();
    await app.Services.SeedSysAdminUserAsync();
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
app.MapClientReferentAccountsEndpoints();
app.MapClientAssetEndpoints();
app.MapClientBrandManualsEndpoints();
app.MapClientStrategyEndpoints();
app.MapMonthlyObjectivesEndpoints();
app.MapSocialIntegrationsEndpoints();
app.MapSocialContentWorkflowEndpoints();
app.MapSingleAnalyzerEndpoints();
app.MapUserPreferencesEndpoints();

app.MapDefaultEndpoints();

app.Run();
