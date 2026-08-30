using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;

namespace Incoders.Hive.Tests.Infrastructure.Persistence;

/// <summary>
/// Persistence contract for the synced Apify actor catalog.
///
/// The in-memory provider does NOT enforce unique indexes, so the "one row per
/// (organization, provider, actor)" rule is asserted through the repository boundary — the real
/// unique index still ships in the migration.
/// </summary>
public sealed class EfOrganizationActorRepositoryTests
{
    private static readonly DateTime Now = new(2026, 3, 4, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task UpsertManyAsync_ThenList_ReturnsAvailableActorsOrderedByDisplayName()
    {
        await using var db = CreateDbContext();
        var repository = new EfOrganizationActorRepository(db);
        var organizationId = Guid.NewGuid();
        var providerId = Guid.NewGuid();

        await repository.UpsertManyAsync(
            [
                Record(organizationId, providerId, "zZ9YyXxWwVvUuTtS2", "beta", "vendor", "Zeta Scraper", isOwned: false),
                Record(organizationId, providerId, "aBcDeFgHiJkLmNoP1", "alpha", "acme", "Alpha Scraper", isOwned: true)
            ],
            CancellationToken.None);
        await db.SaveChangesAsync();

        var rows = await repository.ListByProviderAsync(organizationId, providerId, CancellationToken.None);

        Assert.Equal(["Alpha Scraper", "Zeta Scraper"], rows.Select(x => x.DisplayName));
        Assert.True(rows.Single(x => x.ActorId == "aBcDeFgHiJkLmNoP1").IsOwned);
        Assert.Equal("vendor", rows.Single(x => x.ActorId == "zZ9YyXxWwVvUuTtS2").Username);
    }

    [Fact]
    public async Task UpsertManyAsync_SameActorTwice_UpdatesInPlaceInsteadOfDuplicating()
    {
        await using var db = CreateDbContext();
        var repository = new EfOrganizationActorRepository(db);
        var organizationId = Guid.NewGuid();
        var providerId = Guid.NewGuid();

        await repository.UpsertManyAsync(
            [Record(organizationId, providerId, "aBcDeFgHiJkLmNoP1", "alpha", "acme", "Old Title", isOwned: true)],
            CancellationToken.None);
        await db.SaveChangesAsync();

        await repository.UpsertManyAsync(
            [Record(organizationId, providerId, "aBcDeFgHiJkLmNoP1", "alpha", "acme", "New Title", isOwned: true)],
            CancellationToken.None);
        await db.SaveChangesAsync();

        var rows = await repository.ListByProviderAsync(organizationId, providerId, CancellationToken.None);

        var only = Assert.Single(rows);
        Assert.Equal("New Title", only.DisplayName);
    }

    [Fact]
    public async Task UpsertManyAsync_ActorNoLongerReturned_IsMarkedUnavailableAndDroppedFromTheList()
    {
        await using var db = CreateDbContext();
        var repository = new EfOrganizationActorRepository(db);
        var organizationId = Guid.NewGuid();
        var providerId = Guid.NewGuid();

        await repository.UpsertManyAsync(
            [
                Record(organizationId, providerId, "aBcDeFgHiJkLmNoP1", "alpha", "acme", "Alpha", isOwned: true),
                Record(organizationId, providerId, "zZ9YyXxWwVvUuTtS2", "beta", "acme", "Beta", isOwned: true)
            ],
            CancellationToken.None);
        await db.SaveChangesAsync();

        await repository.UpsertManyAsync(
            [Record(organizationId, providerId, "aBcDeFgHiJkLmNoP1", "alpha", "acme", "Alpha", isOwned: true)],
            CancellationToken.None);
        await db.SaveChangesAsync();

        var rows = await repository.ListByProviderAsync(organizationId, providerId, CancellationToken.None);

        Assert.Equal(["Alpha"], rows.Select(x => x.DisplayName));
    }

    [Fact]
    public async Task ListByProviderAsync_IsScopedToTheOrganizationAndProvider()
    {
        await using var db = CreateDbContext();
        var repository = new EfOrganizationActorRepository(db);
        var organizationA = Guid.NewGuid();
        var organizationB = Guid.NewGuid();
        var providerId = Guid.NewGuid();

        await repository.UpsertManyAsync(
            [Record(organizationA, providerId, "aBcDeFgHiJkLmNoP1", "alpha", "acme", "Alpha", isOwned: true)],
            CancellationToken.None);
        await repository.UpsertManyAsync(
            [Record(organizationB, providerId, "zZ9YyXxWwVvUuTtS2", "beta", "other", "Beta", isOwned: true)],
            CancellationToken.None);
        await db.SaveChangesAsync();

        var rows = await repository.ListByProviderAsync(organizationA, providerId, CancellationToken.None);

        Assert.Equal(["Alpha"], rows.Select(x => x.DisplayName));
        Assert.Empty(await repository.ListByProviderAsync(organizationA, Guid.NewGuid(), CancellationToken.None));
    }

    private static OrganizationActorRecord Record(
        Guid organizationId,
        Guid providerId,
        string actorId,
        string name,
        string username,
        string displayName,
        bool isOwned) =>
        new(
            Guid.NewGuid(),
            organizationId,
            Guid.NewGuid(),
            providerId,
            "apify",
            actorId,
            name,
            username,
            displayName,
            "{}",
            isOwned,
            IsAvailable: true,
            SyncedAtUtc: Now,
            CreatedAtUtc: Now,
            UpdatedAtUtc: Now);

    private static AppIdentityDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppIdentityDbContext>()
            .UseInMemoryDatabase($"organization-actors-{Guid.NewGuid():N}")
            .Options;

        return new AppIdentityDbContext(options);
    }
}
