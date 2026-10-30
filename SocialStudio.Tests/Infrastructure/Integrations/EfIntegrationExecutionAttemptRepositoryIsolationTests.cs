using SocialStudio.Application.Abstractions.Integrations;
using SocialStudio.Infrastructure.Identity;
using SocialStudio.Infrastructure.Persistence.Postgres;
using SocialStudio.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace SocialStudio.Tests.Infrastructure.Integrations;

/// <summary>
/// Isolation guarantees for <see cref="EfIntegrationExecutionAttemptRepository.AddAsync"/>.
///
/// Telemetry attempt rows are best-effort observability and MUST NEVER fail the caller's
/// unit of work. The earlier implementation only enqueued the entity on the shared
/// (scoped) <see cref="AppIdentityDbContext"/> without saving, so a telemetry failure was
/// deferred to the next unrelated <c>SaveChanges</c> on that shared context — escaping the
/// caller's try/catch and aborting the whole content-generation job (observed as runtime
/// 42703 once correlation columns existed in the model but not the DB).
///
/// The write must therefore be fully self-contained: its own short-lived context, its own
/// transaction, committed inside <c>AddAsync</c> so failures are catchable and the caller's
/// context is never touched.
/// </summary>
public sealed class EfIntegrationExecutionAttemptRepositoryIsolationTests
{
    [Fact]
    public async Task AddAsync_PersistsAttempt_WithoutCallerCallingSaveChanges()
    {
        using var provider = BuildProvider();
        using var callerScope = provider.CreateScope();
        var callerContext = callerScope.ServiceProvider.GetRequiredService<AppIdentityDbContext>();

        var repo = new EfIntegrationExecutionAttemptRepository(
            callerContext,
            provider.GetRequiredService<IServiceScopeFactory>());

        var record = NewRecord(out var id);

        // The caller deliberately never calls SaveChanges — the write must persist on its own.
        await repo.AddAsync(record);

        using var verifyScope = provider.CreateScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<AppIdentityDbContext>();
        var read = await verifyContext.IntegrationExecutionAttempts.FindAsync(id);

        Assert.NotNull(read);
        Assert.Equal("content-generation/generate-image", read!.CorrelationKind);
    }

    [Fact]
    public async Task AddAsync_DoesNotEnqueueAnythingOnCallerContext()
    {
        using var provider = BuildProvider();
        using var callerScope = provider.CreateScope();
        var callerContext = callerScope.ServiceProvider.GetRequiredService<AppIdentityDbContext>();

        var repo = new EfIntegrationExecutionAttemptRepository(
            callerContext,
            provider.GetRequiredService<IServiceScopeFactory>());

        await repo.AddAsync(NewRecord(out _));

        // Isolation: nothing telemetry-related may be tracked on the caller's context,
        // otherwise a later SaveChanges on that shared context would flush (and could fail on) it.
        Assert.Empty(callerContext.ChangeTracker.Entries<IntegrationExecutionAttemptData>());
    }

    private static ServiceProvider BuildProvider()
    {
        var databaseName = "attempt-isolation-" + Guid.NewGuid().ToString("N");
        var services = new ServiceCollection();
        services.AddDbContext<AppIdentityDbContext>(options => options.UseInMemoryDatabase(databaseName));
        return services.BuildServiceProvider();
    }

    private static IntegrationExecutionAttemptRecord NewRecord(out Guid id)
    {
        id = Guid.NewGuid();
        return new IntegrationExecutionAttemptRecord(
            Id: id,
            AnalysisId: null,
            CapabilityKey: "generate-image",
            ProviderKey: "carousel-background-service",
            Status: IntegrationExecutionAttemptStatus.Succeeded,
            AttemptNumber: 1,
            FailureCode: null,
            RetryCount: 0,
            ElapsedMs: 42,
            DiagnosticsJson: "{\"slideIndex\":3}",
            StartedAtUtc: DateTime.UtcNow,
            CompletedAtUtc: DateTime.UtcNow,
            CorrelationId: Guid.NewGuid(),
            CorrelationKind: "content-generation/generate-image");
    }
}
