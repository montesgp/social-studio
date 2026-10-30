using SocialStudio.Domain.Social.Clients;
using SocialStudio.Infrastructure.Identity;
using SocialStudio.Infrastructure.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;

namespace SocialStudio.Tests.Infrastructure.Persistence;

/// <summary>
/// Round-trip regression tests for <c>Client.AestheticRefreshThresholdDays</c> through
/// the EfSocialClientRepository mapping: ToData (create), Update method body, and ToDomain (read).
///
/// <para>
/// BUG GUARD (predecessor pattern): a previous change shipped with new Client fields mapped in
/// ToData and ToDomain but silently DROPPED in the Update method body. All THREE methods
/// must map the field or it is lost on the first update. This test set proves all three paths.
/// </para>
/// Refs: design §2.3; CAG-CS-01; tasks 6.4.
/// </summary>
public sealed class EfSocialClientRepositoryThresholdTests
{
    private static readonly DateTime Now = new(2026, 6, 21, 12, 0, 0, DateTimeKind.Utc);

    // -----------------------------------------------------------------------
    // create → reload: ToData + ToDomain
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AddAsync_WithThreshold14_GetByIdReturns14()
    {
        var store = $"threshold-add-{Guid.NewGuid():N}";
        var orgId = Guid.NewGuid();
        var clientId = ClientId.New();

        await using (var db = CreateDbContext(store))
        {
            var repo = new EfSocialClientRepository(db);
            var client = Client.Create(clientId, orgId, "Threshold Co", null, Now).Value;
            client.SetAestheticRefreshThresholdDays(14, Now);

            await repo.AddAsync(client);
            await db.SaveChangesAsync();
        }

        await using (var db = CreateDbContext(store))
        {
            var loaded = await new EfSocialClientRepository(db).GetByIdAsync(clientId, orgId);

            Assert.NotNull(loaded);
            Assert.Equal(14, loaded!.AestheticRefreshThresholdDays);
        }
    }

    // -----------------------------------------------------------------------
    // create with null → reload: round-trip null via ToData + ToDomain
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AddAsync_WithNullThreshold_GetByIdReturnsNull()
    {
        var store = $"threshold-add-null-{Guid.NewGuid():N}";
        var orgId = Guid.NewGuid();
        var clientId = ClientId.New();

        await using (var db = CreateDbContext(store))
        {
            var repo = new EfSocialClientRepository(db);
            var client = Client.Create(clientId, orgId, "Default Co", null, Now).Value;
            // No call to SetAestheticRefreshThresholdDays → stays null

            await repo.AddAsync(client);
            await db.SaveChangesAsync();
        }

        await using (var db = CreateDbContext(store))
        {
            var loaded = await new EfSocialClientRepository(db).GetByIdAsync(clientId, orgId);

            Assert.NotNull(loaded);
            Assert.Null(loaded!.AestheticRefreshThresholdDays);
        }
    }

    // -----------------------------------------------------------------------
    // create → update → reload: Update method body guard (the predecessor bug)
    // create with 14 → update to 7 → reload → must see 7 (not null, not 14)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Update_ChangesThresholdFrom14To7_ReloadReturns7()
    {
        var store = $"threshold-update-{Guid.NewGuid():N}";
        var orgId = Guid.NewGuid();
        var clientId = ClientId.New();

        // Step 1: create with threshold=14
        await using (var db = CreateDbContext(store))
        {
            var repo = new EfSocialClientRepository(db);
            var client = Client.Create(clientId, orgId, "Threshold Co", null, Now).Value;
            client.SetAestheticRefreshThresholdDays(14, Now);
            await repo.AddAsync(client);
            await db.SaveChangesAsync();
        }

        // Step 2: reload, update to 7
        await using (var db = CreateDbContext(store))
        {
            var repo = new EfSocialClientRepository(db);
            var loaded = await repo.GetByIdAsync(clientId, orgId);
            loaded!.SetAestheticRefreshThresholdDays(7, Now);
            repo.Update(loaded);
            await db.SaveChangesAsync();
        }

        // Step 3: reload again — must see 7
        await using (var db = CreateDbContext(store))
        {
            var loaded = await new EfSocialClientRepository(db).GetByIdAsync(clientId, orgId);

            Assert.NotNull(loaded);
            Assert.Equal(7, loaded!.AestheticRefreshThresholdDays);
        }
    }

    // -----------------------------------------------------------------------
    // create with threshold → update to null → reload: clears to null
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Update_ClearsThresholdToNull_ReloadReturnsNull()
    {
        var store = $"threshold-clear-{Guid.NewGuid():N}";
        var orgId = Guid.NewGuid();
        var clientId = ClientId.New();

        await using (var db = CreateDbContext(store))
        {
            var repo = new EfSocialClientRepository(db);
            var client = Client.Create(clientId, orgId, "Threshold Co", null, Now).Value;
            client.SetAestheticRefreshThresholdDays(30, Now);
            await repo.AddAsync(client);
            await db.SaveChangesAsync();
        }

        await using (var db = CreateDbContext(store))
        {
            var repo = new EfSocialClientRepository(db);
            var loaded = await repo.GetByIdAsync(clientId, orgId);
            loaded!.SetAestheticRefreshThresholdDays(null, Now);
            repo.Update(loaded);
            await db.SaveChangesAsync();
        }

        await using (var db = CreateDbContext(store))
        {
            var loaded = await new EfSocialClientRepository(db).GetByIdAsync(clientId, orgId);

            Assert.NotNull(loaded);
            Assert.Null(loaded!.AestheticRefreshThresholdDays);
        }
    }

    private static AppIdentityDbContext CreateDbContext(string store)
    {
        var options = new DbContextOptionsBuilder<AppIdentityDbContext>()
            .UseInMemoryDatabase(store)
            .Options;

        return new AppIdentityDbContext(options);
    }
}
