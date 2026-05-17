var builder = DistributedApplication.CreateBuilder(args);

// F5 entrypoint: starts only the API service.
// Run the Next.js frontend separately via VS Code: cd apps/web && npm run dev
builder.AddProject<Projects.Incoders_Hive_ApiService>("apiservice")
    .WithHttpHealthCheck("/health");

builder.Build().Run();
