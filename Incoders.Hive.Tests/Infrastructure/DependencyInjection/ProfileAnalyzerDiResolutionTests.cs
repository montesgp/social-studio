using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Features.Social.ProfileAnalyzer.Analyze;
using Incoders.Hive.Infrastructure.DependencyInjection;
using Incoders.Hive.Infrastructure.Persistence.Postgres;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Repositories;
using Incoders.Hive.Infrastructure.Social.Providers.Apify;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Incoders.Hive.Tests.Infrastructure.DependencyInjection;

/// <summary>
/// Regression guard: ensures every service in the ProfileAnalyzerOrchestrator DI graph
/// is registered so the API does not blow up at builder.Build() with an
/// AggregateException / InvalidOperationException.
///
/// WHY descriptor-presence instead of full-graph resolution:
/// Building the real container requires IConfiguration with a live connection-string,
/// EF Core database options, and external HTTP clients (Apify, AI providers).
/// Those deps are impractical to supply in a unit test without Docker / Aspire.
/// Descriptor-presence is the strongest feasible guard:
///   - It fails the moment a registration is removed or renamed.
///   - It passes only when all required service types are present.
///
/// This test WOULD HAVE CAUGHT the original bug: IProfileMetricCalculator was absent
/// from the ServiceCollection, which this test asserts for explicitly.
///
/// To confirm it catches the bug: temporarily comment out the
///   services.AddScoped&lt;IProfileMetricCalculator, ProfileMetricCalculator&gt;()
/// line in DependencyInjection.cs and run this suite — it will fail.
/// Restore the line and it passes.
/// </summary>
public sealed class ProfileAnalyzerDiResolutionTests
{
    // ── Orchestrator constructor ports ────────────────────────────────────────

    [Fact]
    public void AddInfrastructure_RegistersIProfileAnalyzer()
    {
        var services = BuildServices();
        AssertDescriptorExists(services, typeof(IProfileAnalyzer));
    }

    [Fact]
    public void AddInfrastructure_RegistersIProfileMetricCalculator()
    {
        // This is the service that was MISSING and caused the AggregateException at boot.
        var services = BuildServices();
        AssertDescriptorExists(services, typeof(IProfileMetricCalculator));
    }

    [Fact]
    public void AddInfrastructure_RegistersIProfileScrapeResolver()
    {
        var services = BuildServices();
        AssertDescriptorExists(services, typeof(IProfileScrapeResolver));
    }

    [Fact]
    public void AddInfrastructure_RegistersIProfileAnalysisRepository()
    {
        var services = BuildServices();
        AssertDescriptorExists(services, typeof(IProfileAnalysisRepository));
    }

    [Fact]
    public void AddInfrastructure_RegistersIProfileAnalysisJobQueue()
    {
        var services = BuildServices();
        AssertDescriptorExists(services, typeof(IProfileAnalysisJobQueue));
    }

    // ── Implementation-type checks (catches rename/move regressions) ─────────

    [Fact]
    public void AddInfrastructure_IProfileAnalyzer_MapsToProfileAnalyzerOrchestrator()
    {
        var services = BuildServices();

        var descriptor = services.Single(d => d.ServiceType == typeof(IProfileAnalyzer));

        Assert.Equal(typeof(ProfileAnalyzerOrchestrator), descriptor.ImplementationType);
    }

    [Fact]
    public void AddInfrastructure_IProfileMetricCalculator_MapsToProfileMetricCalculator()
    {
        var services = BuildServices();

        var descriptor = services.Single(d => d.ServiceType == typeof(IProfileMetricCalculator));

        Assert.Equal(typeof(ProfileMetricCalculator), descriptor.ImplementationType);
    }

    // -------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------

    /// <summary>
    /// Calls AddInfrastructure with a minimal in-memory IConfiguration that satisfies
    /// the Options-binding guard checks (ValidateOnStart options require keys to exist
    /// even when the container is never built to a ServiceProvider).
    ///
    /// We do NOT call BuildServiceProvider() because that would trigger
    /// ValidateOnStart validators and require live connection strings.
    /// Descriptor-presence checks operate on the IServiceCollection directly.
    /// </summary>
    private static IServiceCollection BuildServices()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Required by PersistenceOptions.ValidateDataAnnotations
                ["Persistence:ConnectionStringName"] = "DefaultConnection",
                // Required by MultiTenancyOptions binding
                ["MultiTenancy:Mode"] = "Header",
                // Required by SupabaseStorageOptions.ValidateDataAnnotations
                ["SupabaseStorage:Url"] = "https://placeholder.supabase.co",
                ["SupabaseStorage:Bucket"] = "placeholder",
                ["SupabaseStorage:ServiceRoleKey"] = "placeholder",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        return services;
    }

    private static void AssertDescriptorExists(IServiceCollection services, Type serviceType)
    {
        var descriptor = services.FirstOrDefault(d => d.ServiceType == serviceType);
        Assert.True(
            descriptor is not null,
            $"No DI descriptor found for {serviceType.FullName}. " +
            $"The service must be registered in AddInfrastructure (DependencyInjection.cs).");
    }
}
