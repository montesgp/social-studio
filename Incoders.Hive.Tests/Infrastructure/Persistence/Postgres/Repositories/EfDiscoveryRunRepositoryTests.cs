using Incoders.Hive.Domain.Social.NicheDiscovery;
using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Incoders.Hive.Tests.Infrastructure.Persistence.Postgres.Repositories;

/// <summary>
/// WU-7b: ND-11, ND-16. <c>social.discovery_runs</c> is the ONE NicheDiscovery table with UPDATEs (the
/// status lifecycle, design §9) — round-trip, the UPDATE path, and tenant scoping (ND-16).
/// </summary>
public sealed class EfDiscoveryRunRepositoryTests
{
    private readonly Guid _orgId = Guid.NewGuid();
    private readonly Guid _otherOrgId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 8, 14, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task AddAsync_ThenGetByIdAsync_RoundTripsTheAggregate()
    {
        await using var db = CreateDb();
        var repo = new EfDiscoveryRunRepository(db);
        var run = CreateRun();

        await repo.AddAsync(run, CancellationToken.None);

        var loaded = await repo.GetByIdAsync(_orgId, run.Id, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(run.Id, loaded!.Id);
        Assert.Equal("es-AR", loaded.SourceMarketCode.Value);
        Assert.Equal("pt-BR", loaded.TargetMarketCode.Value);
        Assert.Equal("youtube", loaded.NetworkKey);
        Assert.Equal(DiscoveryRunStatus.Requested, loaded.Status);
    }

    [Fact]
    public async Task UpdateAsync_PersistsTheStatusLifecycleTransition()
    {
        await using var db = CreateDb();
        var repo = new EfDiscoveryRunRepository(db);
        var run = CreateRun();
        await repo.AddAsync(run, CancellationToken.None);

        run.Start(Now.AddMinutes(1));
        await repo.UpdateAsync(run, CancellationToken.None);

        run.Complete(Now.AddMinutes(5));
        await repo.UpdateAsync(run, CancellationToken.None);

        var loaded = await repo.GetByIdAsync(_orgId, run.Id, CancellationToken.None);

        Assert.Equal(DiscoveryRunStatus.Completed, loaded!.Status);
        Assert.NotNull(loaded.StartedAtUtc);
        Assert.NotNull(loaded.CompletedAtUtc);
    }

    [Fact]
    public async Task GetByIdAsync_IsTenantScoped_CrossOrgReadReturnsNothing()
    {
        await using var db = CreateDb();
        var repo = new EfDiscoveryRunRepository(db);
        var run = CreateRun();
        await repo.AddAsync(run, CancellationToken.None);

        var crossTenant = await repo.GetByIdAsync(_otherOrgId, run.Id, CancellationToken.None);

        Assert.Null(crossTenant);
    }

    private DiscoveryRun CreateRun()
    {
        var result = DiscoveryRun.Create(
            _orgId,
            _userId,
            "es-AR",
            "pt-BR",
            "youtube",
            "video",
            DiscoveryWindow.Default(),
            DiscoveryDepth.Standard,
            configurationSnapshotJson: "{}",
            nicheId: null,
            Now);

        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static AppIdentityDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppIdentityDbContext>()
            .UseInMemoryDatabase($"discovery-runs-{Guid.NewGuid():N}")
            .Options;

        return new AppIdentityDbContext(options);
    }
}
