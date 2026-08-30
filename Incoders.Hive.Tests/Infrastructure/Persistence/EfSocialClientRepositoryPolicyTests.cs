using Incoders.Hive.Domain.Social.Clients;
using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;

namespace Incoders.Hive.Tests.Infrastructure.Persistence;

/// <summary>
/// Round-trips the per-client <see cref="BackgroundSourcingPolicy"/> through the repository's
/// domain ↔ entity mapping (ToData / Update / ToDomain). Regression guard: the policy was being
/// dropped on create, update, and read, so a saved selection never persisted (BSP-07).
/// </summary>
public sealed class EfSocialClientRepositoryPolicyTests
{
    private static readonly DateTime Now = new(2026, 6, 20, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task AddAsync_PersistsPolicy_AndGetByIdReturnsIt()
    {
        var store = $"social-client-policy-add-{Guid.NewGuid():N}";
        var orgId = Guid.NewGuid();
        var clientId = ClientId.New();

        await using (var db = CreateDbContext(store))
        {
            var repository = new EfSocialClientRepository(db);
            var client = Client.Create(clientId, orgId, "Aesthetic Co", null, Now).Value;
            client.SetBackgroundSourcingPolicy(BackgroundSourcingPolicy.ClientAesthetic, Now);

            await repository.AddAsync(client);
            await db.SaveChangesAsync();
        }

        await using (var db = CreateDbContext(store))
        {
            var loaded = await new EfSocialClientRepository(db).GetByIdAsync(clientId, orgId);

            Assert.NotNull(loaded);
            Assert.Equal(BackgroundSourcingPolicy.ClientAesthetic, loaded!.BackgroundSourcingPolicy);
        }
    }

    [Fact]
    public async Task Update_PersistsChangedPolicy_AndGetByIdReturnsIt()
    {
        var store = $"social-client-policy-update-{Guid.NewGuid():N}";
        var orgId = Guid.NewGuid();
        var clientId = ClientId.New();

        await using (var db = CreateDbContext(store))
        {
            var repository = new EfSocialClientRepository(db);
            var client = Client.Create(clientId, orgId, "Aesthetic Co", null, Now).Value;
            await repository.AddAsync(client);
            await db.SaveChangesAsync();
        }

        await using (var db = CreateDbContext(store))
        {
            var repository = new EfSocialClientRepository(db);
            var loaded = await repository.GetByIdAsync(clientId, orgId);
            loaded!.SetBackgroundSourcingPolicy(BackgroundSourcingPolicy.ClientDefinedSet, Now);

            repository.Update(loaded);
            await db.SaveChangesAsync();
        }

        await using (var db = CreateDbContext(store))
        {
            var loaded = await new EfSocialClientRepository(db).GetByIdAsync(clientId, orgId);

            Assert.NotNull(loaded);
            Assert.Equal(BackgroundSourcingPolicy.ClientDefinedSet, loaded!.BackgroundSourcingPolicy);
        }
    }

    [Fact]
    public async Task AddAsync_WithNullPolicy_GetByIdReturnsNull()
    {
        var store = $"social-client-policy-null-{Guid.NewGuid():N}";
        var orgId = Guid.NewGuid();
        var clientId = ClientId.New();

        await using (var db = CreateDbContext(store))
        {
            var repository = new EfSocialClientRepository(db);
            var client = Client.Create(clientId, orgId, "Legacy Co", null, Now).Value;
            await repository.AddAsync(client);
            await db.SaveChangesAsync();
        }

        await using (var db = CreateDbContext(store))
        {
            var loaded = await new EfSocialClientRepository(db).GetByIdAsync(clientId, orgId);

            Assert.NotNull(loaded);
            Assert.Null(loaded!.BackgroundSourcingPolicy);
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
