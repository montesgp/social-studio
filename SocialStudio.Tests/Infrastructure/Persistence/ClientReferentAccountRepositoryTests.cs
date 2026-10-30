using SocialStudio.Domain.Social.ClientReferentAccounts;
using SocialStudio.Domain.Social.Clients;
using SocialStudio.Domain.Social.Networks;
using SocialStudio.Infrastructure.Identity;
using SocialStudio.Infrastructure.Persistence.Postgres;
using SocialStudio.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace SocialStudio.Tests.Infrastructure.Persistence;

public sealed class ClientReferentAccountRepositoryTests
{
    private static readonly DateTime Now = new(2026, 5, 22, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ListByClientAsync_FiltersByOrganizationClientAndActiveState()
    {
        await using var db = CreateDbContext();
        var (organizationId, clientId, networkId) = await SeedClientAndNetworkAsync(db);
        var otherOrganizationId = Guid.NewGuid();
        var otherClientId = Guid.NewGuid();

        db.Tenants.Add(new TenantData
        {
            Id = otherOrganizationId,
            Name = "Other organization",
            OwnerUserId = Guid.NewGuid(),
            IsActive = true,
            CreatedAtUtc = Now,
            UpdatedAtUtc = Now,
        });
        db.SocialClients.Add(new SocialClientData
        {
            Id = otherClientId,
            OrganizationId = otherOrganizationId,
            Name = "Other client",
            IsActive = true,
            CreatedAtUtc = Now,
            UpdatedAtUtc = Now,
        });
        db.ClientReferentAccounts.AddRange(
            AccountData(clientId, organizationId, networkId, "https://example.com/active", true, 0),
            AccountData(clientId, organizationId, networkId, "https://example.com/inactive", false, 1),
            AccountData(otherClientId, otherOrganizationId, networkId, "https://example.com/other", true, 0));
        await db.SaveChangesAsync();

        var repository = new EfClientReferentAccountRepository(db);

        var activeOnly = await repository.ListByClientAsync(
            new ClientId(clientId),
            organizationId,
            includeInactive: false,
            page: 1,
            pageSize: 20,
            CancellationToken.None);

        var includeInactive = await repository.ListByClientAsync(
            new ClientId(clientId),
            organizationId,
            includeInactive: true,
            page: 1,
            pageSize: 20,
            CancellationToken.None);

        Assert.Single(activeOnly.Items);
        Assert.Equal("https://example.com/active", activeOnly.Items[0].ProfileUrl);
        Assert.Equal(2, includeInactive.TotalCount);
        Assert.Equal(
            ["https://example.com/active", "https://example.com/inactive"],
            includeInactive.Items.Select(x => x.ProfileUrl).ToArray());
    }

    [Fact]
    public async Task ReorderAsync_PersistsSortOrderForClientAccounts()
    {
        await using var db = CreateDbContext();
        var (organizationId, clientId, networkId) = await SeedClientAndNetworkAsync(db);
        var first = AccountData(clientId, organizationId, networkId, "https://example.com/first", true, 0);
        var second = AccountData(clientId, organizationId, networkId, "https://example.com/second", true, 1);
        db.ClientReferentAccounts.AddRange(first, second);
        await db.SaveChangesAsync();

        var repository = new EfClientReferentAccountRepository(db);

        await repository.ReorderAsync(
            new ClientId(clientId),
            organizationId,
            [second.Id, first.Id],
            CancellationToken.None);

        var reordered = await repository.ListByClientAsync(
            new ClientId(clientId),
            organizationId,
            includeInactive: true,
            page: 1,
            pageSize: 20,
            CancellationToken.None);

        Assert.Equal(["https://example.com/second", "https://example.com/first"], reordered.Items.Select(x => x.ProfileUrl).ToArray());
    }

    [Fact]
    public async Task AddAndUpdateAsync_RoundTripsMutableFields()
    {
        await using var db = CreateDbContext();
        var (organizationId, clientId, networkId) = await SeedClientAndNetworkAsync(db);
        var repository = new EfClientReferentAccountRepository(db);
        var created = ClientReferentAccount.Create(
            ClientReferentAccountId.New(),
            new ClientId(clientId),
            organizationId,
            new SocialNetworkId(networkId),
            "https://example.com/original",
            "@original",
            "Original",
            "Initial note",
            Now).Value;

        await repository.AddAsync(created, CancellationToken.None);
        await repository.SaveChangesAsync(CancellationToken.None);

        var updated = created.Update("https://example.com/updated", "@updated", "Updated", "Updated note", Now.AddMinutes(1));
        Assert.True(updated.IsSuccess);
        repository.Update(created);
        await repository.SaveChangesAsync(CancellationToken.None);

        var saved = await repository.GetByIdAsync(created.Id, organizationId, CancellationToken.None);

        Assert.NotNull(saved);
        Assert.Equal("https://example.com/updated", saved.ProfileUrl);
        Assert.Equal("@updated", saved.Handle);
        Assert.Equal("Updated", saved.Label);
        Assert.Equal("Updated note", saved.Notes);
    }

    private static AppIdentityDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppIdentityDbContext>()
            .UseInMemoryDatabase($"client-referent-accounts-{Guid.NewGuid():N}")
            .Options;

        return new AppIdentityDbContext(options);
    }

    private static async Task<(Guid OrganizationId, Guid ClientId, Guid NetworkId)> SeedClientAndNetworkAsync(
        AppIdentityDbContext db)
    {
        var organizationId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var networkId = Guid.NewGuid();

        db.Tenants.Add(new TenantData
        {
            Id = organizationId,
            Name = "Test organization",
            OwnerUserId = Guid.NewGuid(),
            IsActive = true,
            CreatedAtUtc = Now,
            UpdatedAtUtc = Now,
        });
        db.SocialClients.Add(new SocialClientData
        {
            Id = clientId,
            OrganizationId = organizationId,
            Name = "Test client",
            IsActive = true,
            CreatedAtUtc = Now,
            UpdatedAtUtc = Now,
        });
        db.SocialNetworks.Add(new SocialNetworkData
        {
            Id = networkId,
            Code = "instagram",
            DisplayName = "Instagram",
            IconKey = "instagram",
            IsActive = true,
            CreatedAtUtc = Now,
            UpdatedAtUtc = Now,
        });
        await db.SaveChangesAsync();

        return (organizationId, clientId, networkId);
    }

    private static ClientReferentAccountData AccountData(
        Guid clientId,
        Guid organizationId,
        Guid networkId,
        string profileUrl,
        bool isActive,
        int sortOrder) => new()
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            OrganizationId = organizationId,
            SocialNetworkId = networkId,
            ProfileUrl = profileUrl,
            IsActive = isActive,
            SortOrder = sortOrder,
            CreatedAtUtc = Now.AddMinutes(sortOrder),
            UpdatedAtUtc = Now.AddMinutes(sortOrder),
        };
}
