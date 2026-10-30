using SocialStudio.Infrastructure.Identity;
using SocialStudio.Infrastructure.Persistence.Postgres;
using SocialStudio.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace SocialStudio.Tests.Infrastructure.Persistence;

/// <summary>Phase 2 (CAG2-PA-04): SetAestheticProfileJsonAsync persists the enriched payload column.</summary>
public sealed class ProfileAnalysisRepositoryAestheticTests
{
    private static AppIdentityDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppIdentityDbContext>()
            .UseInMemoryDatabase($"aesthetic-write-{Guid.NewGuid():N}")
            .Options);

    [Fact]
    public async Task SetAestheticProfileJsonAsync_PersistsColumn()
    {
        var id = Guid.NewGuid();
        var org = Guid.NewGuid();

        await using var db = CreateDb();
        db.ProfileAnalyses.Add(new ProfileAnalysisData
        {
            Id = id,
            OrganizationId = org,
            ProfileUrlOrHandle = "@client",
            Status = "Analyzed",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var repo = new EfProfileAnalysisRepository(db);
        await repo.SetAestheticProfileJsonAsync(id, org, """{"palette":[]}""", CancellationToken.None);

        var row = await db.ProfileAnalyses.AsNoTracking().FirstAsync(x => x.Id == id);
        Assert.Equal("""{"palette":[]}""", row.AestheticProfileJson);
    }

    [Fact]
    public async Task SetAestheticProfileJsonAsync_NoOp_WhenAnalysisNotFound()
    {
        await using var db = CreateDb();
        var repo = new EfProfileAnalysisRepository(db);

        // Should not throw when the analysis does not exist.
        await repo.SetAestheticProfileJsonAsync(Guid.NewGuid(), Guid.NewGuid(), "{}", CancellationToken.None);
    }
}
