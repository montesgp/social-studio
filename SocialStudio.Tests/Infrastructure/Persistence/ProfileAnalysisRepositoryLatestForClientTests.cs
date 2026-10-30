using SocialStudio.Infrastructure.Identity;
using SocialStudio.Infrastructure.Persistence.Postgres;
using SocialStudio.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace SocialStudio.Tests.Infrastructure.Persistence;

/// <summary>
/// client-analysis-data admin panel: <see cref="EfProfileAnalysisRepository.GetLatestForClientAsync"/>
/// returns the newest (by <c>CreatedAtUtc</c>) analysis row for a client regardless of status,
/// or null when none exists — the read side of the admin "what was captured last" panel.
/// </summary>
public sealed class ProfileAnalysisRepositoryLatestForClientTests
{
    private static AppIdentityDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppIdentityDbContext>()
            .UseInMemoryDatabase($"latest-for-client-{Guid.NewGuid():N}")
            .Options);

    [Fact]
    public async Task ReturnsNull_WhenNoAnalysisExistsForClient()
    {
        await using var db = CreateDb();
        var repo = new EfProfileAnalysisRepository(db);

        var result = await repo.GetLatestForClientAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ReturnsNewestByCreatedAtUtc_RegardlessOfStatus()
    {
        var org = Guid.NewGuid();
        var clientId = Guid.NewGuid();

        await using var db = CreateDb();
        db.ProfileAnalyses.AddRange(
            new ProfileAnalysisData
            {
                Id = Guid.NewGuid(),
                OrganizationId = org,
                ClientId = clientId,
                ProfileUrlOrHandle = "@older",
                Status = "Analyzed",
                AestheticProfileJson = """{"styleDescriptors":["old"]}""",
                CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                CompletedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            },
            new ProfileAnalysisData
            {
                Id = Guid.NewGuid(),
                OrganizationId = org,
                ClientId = clientId,
                ProfileUrlOrHandle = "@newer",
                Status = "Analyzing",
                CreatedAtUtc = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAtUtc = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            });
        await db.SaveChangesAsync();

        var repo = new EfProfileAnalysisRepository(db);
        var result = await repo.GetLatestForClientAsync(org, clientId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("@newer", result!.ProfileUrlOrHandle);
        Assert.Equal("Analyzing", result.Status);
        Assert.Null(result.CompletedAtUtc);
    }

    [Fact]
    public async Task ScopesByOrganization_DoesNotLeakOtherOrgsAnalysis()
    {
        var clientId = Guid.NewGuid();
        var org1 = Guid.NewGuid();
        var org2 = Guid.NewGuid();

        await using var db = CreateDb();
        db.ProfileAnalyses.Add(new ProfileAnalysisData
        {
            Id = Guid.NewGuid(),
            OrganizationId = org1,
            ClientId = clientId,
            ProfileUrlOrHandle = "@org1",
            Status = "Analyzed",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var repo = new EfProfileAnalysisRepository(db);
        var result = await repo.GetLatestForClientAsync(org2, clientId, CancellationToken.None);

        Assert.Null(result);
    }
}
