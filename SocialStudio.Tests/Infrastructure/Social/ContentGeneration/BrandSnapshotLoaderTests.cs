using SocialStudio.Infrastructure.Identity;
using SocialStudio.Infrastructure.Persistence.Postgres.Entities;
using SocialStudio.Infrastructure.Social.ContentGeneration;
using Microsoft.EntityFrameworkCore;

namespace SocialStudio.Tests.Infrastructure.Social.ContentGeneration;

/// <summary>
/// RED tests for WU-5, task 5.3:
/// Asserts that EfClientBrandSnapshotLoader loads ClientOwnAccount.Handle into
/// ClientProfileSnapshot.Handle (CAC-CG-04).
///
/// Architecture assertions:
/// - ClientProfileSnapshot is a Domain record with no Infrastructure or provider types.
/// - The loader port signature (IClientBrandSnapshotLoader.LoadAsync) returns BrandSnapshot,
///   which also contains no Infrastructure types.
/// </summary>
public sealed class BrandSnapshotLoaderTests : IDisposable
{
    private readonly AppIdentityDbContext _dbContext;
    private readonly EfClientBrandSnapshotLoader _loader;

    public BrandSnapshotLoaderTests()
    {
        var options = new DbContextOptionsBuilder<AppIdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new AppIdentityDbContext(options);
        _loader = new EfClientBrandSnapshotLoader(_dbContext);
    }

    public void Dispose() => _dbContext.Dispose();

    // --- CAC-CG-04-a: own account with handle → ClientProfileSnapshot.Handle populated ---

    [Fact]
    public async Task Load_WithOwnAccountHandle_PopulatesClientProfileSnapshotHandle()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var clientId = Guid.NewGuid();

        _dbContext.SocialClients.Add(new SocialClientData
        {
            Id = clientId,
            OrganizationId = orgId,
            Name = "Test Brand",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        });

        _dbContext.ClientOwnAccounts.Add(new ClientOwnAccountData
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            OrganizationId = orgId,
            SocialNetworkId = Guid.NewGuid(),
            ProfileUrl = "https://instagram.com/brand",
            Handle = "@brand",
            IsActive = true,
            SortOrder = 0,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        });

        await _dbContext.SaveChangesAsync();

        // Act
        var snapshot = await _loader.LoadAsync(orgId, clientId);

        // Assert: CAC-CG-04-a — handle flows from own account into client profile snapshot.
        Assert.NotNull(snapshot.Client);
        Assert.Equal("@brand", snapshot.Client!.Handle);
    }

    // --- CAC-CG-04-b: no own account → Handle null; snapshot assembles successfully ---

    [Fact]
    public async Task Load_WithNoOwnAccount_HandleIsNull_AndSnapshotSucceeds()
    {
        // Arrange: client row exists but no own-account row
        var orgId = Guid.NewGuid();
        var clientId = Guid.NewGuid();

        _dbContext.SocialClients.Add(new SocialClientData
        {
            Id = clientId,
            OrganizationId = orgId,
            Name = "No Handle Brand",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        });

        await _dbContext.SaveChangesAsync();

        // Act
        var exception = await Record.ExceptionAsync(() => _loader.LoadAsync(orgId, clientId));
        var snapshot = await _loader.LoadAsync(orgId, clientId);

        // Assert: no exception; Handle is null; client is present
        Assert.Null(exception);
        Assert.NotNull(snapshot.Client);
        Assert.Null(snapshot.Client!.Handle);
    }

    // --- Architecture: ClientProfileSnapshot carries no Infrastructure types ---

    [Fact]
    public void ClientProfileSnapshot_HasNoInfrastructureOrProviderTypes()
    {
        // ClientProfileSnapshot lives in Domain and must not reference Infrastructure, EF, or provider types.
        var type = typeof(SocialStudio.Domain.Social.ContentGeneration.ClientProfileSnapshot);
        Assert.StartsWith("SocialStudio.Domain", type.Assembly.GetName().Name);

        // All property types must be either primitives, string, nullable-of-primitive,
        // or other Domain types — no EF Core, Npgsql, or Infrastructure concrete types.
        foreach (var prop in type.GetProperties())
        {
            var propType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
            var ns = propType.Namespace ?? string.Empty;
            Assert.False(
                ns.StartsWith("Microsoft.EntityFrameworkCore") ||
                ns.StartsWith("Npgsql") ||
                ns.StartsWith("SocialStudio.Infrastructure"),
                $"Property '{prop.Name}' has type '{prop.PropertyType}' which leaks an Infrastructure type.");
        }
    }

    // --- Multiple own accounts: the first by SortOrder is preferred ---

    [Fact]
    public async Task Load_WithMultipleOwnAccounts_LoadsHandleFromLowestSortOrder()
    {
        var orgId = Guid.NewGuid();
        var clientId = Guid.NewGuid();

        _dbContext.SocialClients.Add(new SocialClientData
        {
            Id = clientId,
            OrganizationId = orgId,
            Name = "Multi Account Brand",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        });

        _dbContext.ClientOwnAccounts.AddRange(
            new ClientOwnAccountData
            {
                Id = Guid.NewGuid(),
                ClientId = clientId,
                OrganizationId = orgId,
                SocialNetworkId = Guid.NewGuid(),
                ProfileUrl = "https://instagram.com/secondary",
                Handle = "@secondary",
                IsActive = true,
                SortOrder = 1,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
            },
            new ClientOwnAccountData
            {
                Id = Guid.NewGuid(),
                ClientId = clientId,
                OrganizationId = orgId,
                SocialNetworkId = Guid.NewGuid(),
                ProfileUrl = "https://instagram.com/primary",
                Handle = "@primary",
                IsActive = true,
                SortOrder = 0,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
            });

        await _dbContext.SaveChangesAsync();

        var snapshot = await _loader.LoadAsync(orgId, clientId);

        // Lowest SortOrder is the primary account.
        Assert.Equal("@primary", snapshot.Client!.Handle);
    }

    // --- Inactive own account is ignored ---

    [Fact]
    public async Task Load_WithInactiveOwnAccount_HandleIsNull()
    {
        var orgId = Guid.NewGuid();
        var clientId = Guid.NewGuid();

        _dbContext.SocialClients.Add(new SocialClientData
        {
            Id = clientId,
            OrganizationId = orgId,
            Name = "Inactive Handle Brand",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        });

        _dbContext.ClientOwnAccounts.Add(new ClientOwnAccountData
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            OrganizationId = orgId,
            SocialNetworkId = Guid.NewGuid(),
            ProfileUrl = "https://instagram.com/old",
            Handle = "@old_handle",
            IsActive = false, // inactive — must not be loaded
            SortOrder = 0,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        });

        await _dbContext.SaveChangesAsync();

        var snapshot = await _loader.LoadAsync(orgId, clientId);

        Assert.Null(snapshot.Client!.Handle);
    }
}
