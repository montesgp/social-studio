using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Domain.Social.Clients;
using Incoders.Hive.Domain.Social.Niches;
using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;

namespace Incoders.Hive.Tests.Infrastructure.Persistence;

/// <summary>
/// Covers the persistence adapters for the niche catalog and the client association.
/// Search-term filtering relies on <c>ILike</c>, which is provider-specific, so it is exercised by
/// the in-memory port double instead; everything provider-agnostic is verified here.
/// </summary>
public sealed class EfNicheRepositoryTests
{
    private static readonly DateTime Now = new(2026, 8, 13, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task AddAndGetById_RoundTripsTheCuratedHashtags()
    {
        await using var db = CreateDbContext();
        var repository = new EfNicheRepository(db);

        var niche = Niche.Create("software", "Software", "cpu", ["software", "technology"], Now).Value;
        await repository.AddAsync(niche, CancellationToken.None);
        await repository.SaveChangesAsync(CancellationToken.None);

        var reread = await repository.GetByIdAsync(niche.Id, CancellationToken.None);

        Assert.NotNull(reread);
        Assert.Equal("software", reread!.Code);
        Assert.Equal("cpu", reread.IconKey);
        Assert.Equal(new[] { "software", "technology" }, reread.SearchHashtags);
    }

    [Fact]
    public async Task GetByCodeAsync_NormalizesTheLookup()
    {
        await using var db = CreateDbContext();
        var repository = new EfNicheRepository(db);
        await Seed(repository, "medical-health", "Medical & Health");

        var found = await repository.GetByCodeAsync("  MEDICAL-HEALTH  ", CancellationToken.None);

        Assert.NotNull(found);
    }

    [Fact]
    public async Task GetByDisplayNameAsync_IsCaseInsensitive()
    {
        await using var db = CreateDbContext();
        var repository = new EfNicheRepository(db);
        await Seed(repository, "software", "Software");

        var found = await repository.GetByDisplayNameAsync("software", CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal("Software", found!.DisplayName);
    }

    [Fact]
    public async Task GetByDisplayNameAsync_UnknownName_ReturnsNull()
    {
        await using var db = CreateDbContext();
        var repository = new EfNicheRepository(db);

        Assert.Null(await repository.GetByDisplayNameAsync("Nothing Here", CancellationToken.None));
    }

    [Fact]
    public async Task ListAsync_WithoutSearchTerm_ReturnsMostRecentlyCreatedFirst()
    {
        await using var db = CreateDbContext();
        var repository = new EfNicheRepository(db);
        await Seed(repository, "oldest", "Oldest", Now);
        await Seed(repository, "middle", "Middle", Now.AddHours(1));
        await Seed(repository, "newest", "Newest", Now.AddHours(2));

        var page = await repository.ListAsync(
            new NicheListQuery(false, 1, 20, null),
            CancellationToken.None);

        Assert.Equal(new[] { "newest", "middle", "oldest" }, page.Items.Select(x => x.Code).ToArray());
        Assert.Equal(3, page.TotalCount);
    }

    [Fact]
    public async Task ListAsync_HonoursThePageSizeAndReportsTheTotal()
    {
        await using var db = CreateDbContext();
        var repository = new EfNicheRepository(db);
        for (var i = 0; i < 7; i++)
        {
            await Seed(repository, $"niche-{i}", $"Niche {i}", Now.AddMinutes(i));
        }

        var page = await repository.ListAsync(
            new NicheListQuery(false, 1, 3, null),
            CancellationToken.None);

        Assert.Equal(3, page.Items.Count);
        Assert.Equal(7, page.TotalCount);
    }

    [Fact]
    public async Task ListAsync_ExcludesInactiveUnlessAsked()
    {
        await using var db = CreateDbContext();
        var repository = new EfNicheRepository(db);
        var active = await Seed(repository, "software", "Software");
        var inactive = await Seed(repository, "gastronomy", "Gastronomy");

        Assert.True(inactive.Deactivate(Now).IsSuccess);
        repository.Update(inactive);
        await repository.SaveChangesAsync(CancellationToken.None);

        var activeOnly = await repository.ListAsync(new NicheListQuery(false, 1, 20, null), CancellationToken.None);
        var all = await repository.ListAsync(new NicheListQuery(true, 1, 20, null), CancellationToken.None);

        Assert.Equal(active.Code, Assert.Single(activeOnly.Items).Code);
        Assert.Equal(2, all.TotalCount);
    }

    [Fact]
    public async Task Update_PersistsHashtagsAndLeavesCodeAlone()
    {
        await using var db = CreateDbContext();
        var repository = new EfNicheRepository(db);
        var niche = await Seed(repository, "medical-health", "Medical & Health");

        Assert.True(niche.Update("Medical & Health Care", "stethoscope", ["medical", "health"], Now).IsSuccess);
        repository.Update(niche);
        await repository.SaveChangesAsync(CancellationToken.None);

        var reread = await repository.GetByIdAsync(niche.Id, CancellationToken.None);

        Assert.Equal("medical-health", reread!.Code);
        Assert.Equal("Medical & Health Care", reread.DisplayName);
        Assert.Equal(new[] { "medical", "health" }, reread.SearchHashtags);
    }

    [Fact]
    public async Task GetByIdsAsync_ReturnsOnlyTheKnownIds()
    {
        await using var db = CreateDbContext();
        var repository = new EfNicheRepository(db);
        var first = await Seed(repository, "software", "Software");
        var second = await Seed(repository, "gastronomy", "Gastronomy");

        var found = await repository.GetByIdsAsync(
            [first.Id, second.Id, NicheId.New()],
            CancellationToken.None);

        Assert.Equal(2, found.Count);
    }

    [Fact]
    public async Task ClientNiches_ReplaceThenGet_RoundTripsTheSelection()
    {
        await using var db = CreateDbContext();
        var niches = new EfNicheRepository(db);
        var links = new EfClientNicheRepository(db);

        var software = await Seed(niches, "software", "Software");
        var gastronomy = await Seed(niches, "gastronomy", "Gastronomy");
        var clientId = ClientId.New();

        await links.ReplaceAsync(clientId, [software.Id, gastronomy.Id], Now, CancellationToken.None);
        await links.SaveChangesAsync(CancellationToken.None);

        var stored = await links.GetNicheIdsAsync(clientId, CancellationToken.None);

        Assert.Equal(2, stored.Count);
        Assert.Contains(software.Id, stored);
        Assert.Contains(gastronomy.Id, stored);
    }

    [Fact]
    public async Task ClientNiches_Replace_DropsWhatIsNoLongerSelected()
    {
        await using var db = CreateDbContext();
        var niches = new EfNicheRepository(db);
        var links = new EfClientNicheRepository(db);

        var software = await Seed(niches, "software", "Software");
        var gastronomy = await Seed(niches, "gastronomy", "Gastronomy");
        var clientId = ClientId.New();

        await links.ReplaceAsync(clientId, [software.Id], Now, CancellationToken.None);
        await links.SaveChangesAsync(CancellationToken.None);

        await links.ReplaceAsync(clientId, [gastronomy.Id], Now, CancellationToken.None);
        await links.SaveChangesAsync(CancellationToken.None);

        var stored = await links.GetNicheIdsAsync(clientId, CancellationToken.None);

        Assert.Equal(gastronomy.Id, Assert.Single(stored));
    }

    [Fact]
    public async Task ClientNiches_ReplaceWithEmpty_ClearsEverything()
    {
        await using var db = CreateDbContext();
        var niches = new EfNicheRepository(db);
        var links = new EfClientNicheRepository(db);

        var software = await Seed(niches, "software", "Software");
        var clientId = ClientId.New();

        await links.ReplaceAsync(clientId, [software.Id], Now, CancellationToken.None);
        await links.SaveChangesAsync(CancellationToken.None);

        await links.ReplaceAsync(clientId, [], Now, CancellationToken.None);
        await links.SaveChangesAsync(CancellationToken.None);

        Assert.Empty(await links.GetNicheIdsAsync(clientId, CancellationToken.None));
    }

    [Fact]
    public async Task ClientNiches_ReplaceKeepingASelection_PreservesItsCreationTimestamp()
    {
        await using var db = CreateDbContext();
        var niches = new EfNicheRepository(db);
        var links = new EfClientNicheRepository(db);

        var software = await Seed(niches, "software", "Software");
        var gastronomy = await Seed(niches, "gastronomy", "Gastronomy");
        var clientId = ClientId.New();

        await links.ReplaceAsync(clientId, [software.Id], Now, CancellationToken.None);
        await links.SaveChangesAsync(CancellationToken.None);

        await links.ReplaceAsync(clientId, [software.Id, gastronomy.Id], Now.AddDays(1), CancellationToken.None);
        await links.SaveChangesAsync(CancellationToken.None);

        var rows = await db.ClientNiches.AsNoTracking()
            .Where(x => x.ClientId == clientId.Value)
            .ToListAsync(CancellationToken.None);

        Assert.Equal(Now, rows.Single(x => x.NicheId == software.Id.Value).CreatedAtUtc);
        Assert.Equal(Now.AddDays(1), rows.Single(x => x.NicheId == gastronomy.Id.Value).CreatedAtUtc);
    }

    [Fact]
    public async Task ClientNiches_AreScopedPerClient()
    {
        await using var db = CreateDbContext();
        var niches = new EfNicheRepository(db);
        var links = new EfClientNicheRepository(db);

        var software = await Seed(niches, "software", "Software");
        var first = ClientId.New();
        var second = ClientId.New();

        await links.ReplaceAsync(first, [software.Id], Now, CancellationToken.None);
        await links.SaveChangesAsync(CancellationToken.None);

        Assert.Empty(await links.GetNicheIdsAsync(second, CancellationToken.None));
    }

    private static async Task<Niche> Seed(
        INicheRepository repository,
        string code,
        string displayName,
        DateTime? createdAtUtc = null)
    {
        var niche = Niche.Create(code, displayName, null, [], createdAtUtc ?? Now).Value;
        await repository.AddAsync(niche, CancellationToken.None);
        await repository.SaveChangesAsync(CancellationToken.None);
        return niche;
    }

    private static AppIdentityDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppIdentityDbContext>()
            .UseInMemoryDatabase($"niches-{Guid.NewGuid():N}")
            .Options;
        return new AppIdentityDbContext(options);
    }
}
