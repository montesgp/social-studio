using Incoders.Hive.ApiService.Endpoints.Auth;
using Incoders.Hive.ApiService.Endpoints.Common;
using Incoders.Hive.ApiService.Endpoints.Integrations;
using Incoders.Hive.ApiService.Endpoints.Social;
using Incoders.Hive.ApiService.Endpoints.SystemSettings;
using Incoders.Hive.ApiService.Endpoints.Tenants;
using Incoders.Hive.ApiService.Endpoints.UserPreferences;
using Incoders.Hive.Application;
using Incoders.Hive.Infrastructure.DependencyInjection;
using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Integrations;
using Incoders.Hive.Infrastructure.Social;
using Incoders.Hive.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

const string FrontendCorsPolicy = "FrontendCors";

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ValidationExceptionHandler>();

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

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<AppIdentityDbContext>();
    dbContext.Database.Migrate();
}

await app.Services.SeedIntegrationCatalogAsync();
await app.Services.SeedSocialNetworkCatalogAsync();
await app.Services.SeedIdentityRolesAsync();
await app.Services.SeedSysAdminUserAsync();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();
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
app.MapMeEndpoints();
app.MapSystemSettingsEndpoints();
app.MapTenantsEndpoints();
app.MapIntegrationsEndpoints();
app.MapSocialClientsEndpoints();
app.MapSocialNetworksEndpoints();
app.MapClientReferentAccountsEndpoints();
app.MapClientBrandManualsEndpoints();
app.MapSocialIntegrationsEndpoints();
app.MapSocialContentWorkflowEndpoints();
app.MapUserPreferencesEndpoints();

app.MapDefaultEndpoints();

app.Run();
