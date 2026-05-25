var builder = DistributedApplication.CreateBuilder(args);

// F5 entrypoint: starts only the API service.
// Run the Next.js frontend separately via VS Code: cd apps/web && npm run dev
var apiService = builder.AddProject<Projects.Incoders_Hive_ApiService>("apiservice")
    .WithHttpHealthCheck("/health");

if (string.Equals(builder.Configuration["HIVE_FAST_STARTUP"], "true", StringComparison.OrdinalIgnoreCase))
{
    apiService
        .WithEnvironment("StartupTasks__ApplyMigrations", "false")
        .WithEnvironment("StartupTasks__SeedData", "false")
        .WithEnvironment("FileLogging__Enabled", "false");
}

builder.Build().Run();
