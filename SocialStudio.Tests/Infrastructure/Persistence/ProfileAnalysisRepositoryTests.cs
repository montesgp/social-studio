using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Domain.Social.ProfileAnalysis;
using SocialStudio.Infrastructure.Identity;
using SocialStudio.Infrastructure.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;

namespace SocialStudio.Tests.Infrastructure.Persistence;

/// <summary>
/// EF InMemory round-trip tests for EfProfileAnalysisRepository.
/// Covers: Add/Get, org-scoping, paging, URL-path nullability, status persistence.
/// Spec: PA-13, PA-15.
/// </summary>
public sealed class ProfileAnalysisRepositoryTests
{
    private static readonly DateTime Now = new(2026, 6, 2, 10, 0, 0, DateTimeKind.Utc);

    // ── AddAsync + GetByIdAsync ──────────────────────────────────────────────

    [Fact]
    public async Task AddAsync_Then_GetByIdAsync_RoundTripsAllJsonColumns()
    {
        await using var db = CreateDbContext();
        var repo = new EfProfileAnalysisRepository(db);

        var analysis = ProfileAnalysis.CreateFromClient(
            organizationId: Guid.NewGuid(),
            clientId: Guid.NewGuid(),
            ownAccountId: Guid.NewGuid(),
            profileUrlOrHandle: "@testprofile",
            createdAtUtc: Now);

        analysis.SetRawData(
            rawPostsJson: """[{"id":"p1"}]""",
            statsJson: """{"engagementRate":0.05}""",
            diagnosticsJson: """{"actor_id":"abc"}""");

        await repo.AddAsync(analysis);
        await db.SaveChangesAsync();

        var loaded = await repo.GetByIdAsync(analysis.Id, analysis.OrganizationId);

        Assert.NotNull(loaded);
        Assert.Equal(analysis.Id, loaded.Id);
        Assert.Equal(analysis.OrganizationId, loaded.OrganizationId);
        Assert.Equal(analysis.ClientId, loaded.ClientId);
        Assert.Equal(analysis.OwnAccountId, loaded.OwnAccountId);
        Assert.Equal("@testprofile", loaded.ProfileUrlOrHandle);
        Assert.Equal(ProfileAnalysisStatus.Requested, loaded.Status);
        Assert.Equal("""[{"id":"p1"}]""", loaded.RawPostsDatasetJson);
        Assert.Equal("""{"engagementRate":0.05}""", loaded.ServerDerivedStatsJson);
        Assert.Equal("""{"actor_id":"abc"}""", loaded.ProviderDiagnosticsJson);
        Assert.Null(loaded.AiAnalysisJson);
        Assert.Null(loaded.FailureCode);
        Assert.Null(loaded.CompletedAtUtc);
    }

    [Fact]
    public async Task AddAsync_Then_GetByIdAsync_RoundTripsAiAnalysisJson()
    {
        await using var db = CreateDbContext();
        var repo = new EfProfileAnalysisRepository(db);

        var analysis = ProfileAnalysis.CreateFromUrl(
            organizationId: Guid.NewGuid(),
            profileUrl: "https://www.instagram.com/testaccount",
            createdAtUtc: Now);

        analysis.SetRawData("""[]""", """{}""", """{}""");
        // Simulate AI stage completion JSON columns
        analysis.SetAiAnalysis(
            aiAnalysisJson: """{"summary":"great account"}""",
            diagnosticsJson: """{"ai_provider_key":"claude"}""");

        await repo.AddAsync(analysis);
        await db.SaveChangesAsync();

        var loaded = await repo.GetByIdAsync(analysis.Id, analysis.OrganizationId);

        Assert.NotNull(loaded);
        Assert.Equal("""{"summary":"great account"}""", loaded!.AiAnalysisJson);
        Assert.Equal("""{"ai_provider_key":"claude"}""", loaded.ProviderDiagnosticsJson);
    }

    // ── Org-scoping ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_WithWrongOrganizationId_ReturnsNull()
    {
        await using var db = CreateDbContext();
        var repo = new EfProfileAnalysisRepository(db);

        var analysis = ProfileAnalysis.CreateFromClient(
            organizationId: Guid.NewGuid(),
            clientId: Guid.NewGuid(),
            ownAccountId: Guid.NewGuid(),
            profileUrlOrHandle: "@profile",
            createdAtUtc: Now);

        await repo.AddAsync(analysis);
        await db.SaveChangesAsync();

        var wrongOrgId = Guid.NewGuid();
        var loaded = await repo.GetByIdAsync(analysis.Id, wrongOrgId);

        Assert.Null(loaded);
    }

    [Fact]
    public async Task GetByOrganizationAsync_ReturnsOnlyRecordsMatchingOrganizationId()
    {
        await using var db = CreateDbContext();
        var repo = new EfProfileAnalysisRepository(db);

        var orgA = Guid.NewGuid();
        var orgB = Guid.NewGuid();

        var a1 = ProfileAnalysis.CreateFromUrl(orgA, "https://instagram.com/a1", Now);
        var a2 = ProfileAnalysis.CreateFromUrl(orgA, "https://instagram.com/a2", Now.AddSeconds(1));
        var b1 = ProfileAnalysis.CreateFromUrl(orgB, "https://instagram.com/b1", Now);

        await repo.AddAsync(a1);
        await repo.AddAsync(a2);
        await repo.AddAsync(b1);
        await db.SaveChangesAsync();

        var pageA = await repo.GetByOrganizationAsync(orgA, page: 1, pageSize: 10);

        Assert.Equal(2, pageA.TotalCount);
        Assert.Equal(2, pageA.Items.Count);
        Assert.All(pageA.Items, item => Assert.Equal(orgA, item.OrganizationId));
    }

    // ── URL-path nullability ────────────────────────────────────────────────

    [Fact]
    public async Task UrlPathAnalysis_HasNullClientIdAndOwnAccountId()
    {
        await using var db = CreateDbContext();
        var repo = new EfProfileAnalysisRepository(db);

        var analysis = ProfileAnalysis.CreateFromUrl(
            organizationId: Guid.NewGuid(),
            profileUrl: "https://www.instagram.com/urlpath",
            createdAtUtc: Now);

        await repo.AddAsync(analysis);
        await db.SaveChangesAsync();

        var loaded = await repo.GetByIdAsync(analysis.Id, analysis.OrganizationId);

        Assert.NotNull(loaded);
        Assert.Null(loaded!.ClientId);
        Assert.Null(loaded.OwnAccountId);
        Assert.Equal("https://www.instagram.com/urlpath", loaded.ProfileUrlOrHandle);
    }

    // ── UpdateAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_PersistsStatusChange()
    {
        await using var db = CreateDbContext();
        var repo = new EfProfileAnalysisRepository(db);

        var analysis = ProfileAnalysis.CreateFromClient(
            organizationId: Guid.NewGuid(),
            clientId: Guid.NewGuid(),
            ownAccountId: Guid.NewGuid(),
            profileUrlOrHandle: "@updateme",
            createdAtUtc: Now);

        await repo.AddAsync(analysis);
        await db.SaveChangesAsync();

        // Transition to scraping
        analysis.StartScraping(Now.AddSeconds(1));
        analysis.SetRawData("""[{"id":"p1"}]""", """{"stat":1}""", """{"d":1}""");
        analysis.CompleteScrape(isPartial: false, updatedAtUtc: Now.AddSeconds(2));
        analysis.StartAnalyzing(Now.AddSeconds(3));

        await repo.UpdateAsync(analysis);
        await db.SaveChangesAsync();

        // Clear the tracking cache so the next GetByIdAsync re-reads from the store
        db.ChangeTracker.Clear();

        var loaded = await repo.GetByIdAsync(analysis.Id, analysis.OrganizationId);

        Assert.NotNull(loaded);
        Assert.Equal(ProfileAnalysisStatus.Analyzing, loaded!.Status);
        Assert.Equal("""[{"id":"p1"}]""", loaded.RawPostsDatasetJson);
    }

    // ── DeleteAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_RemovesRecord()
    {
        await using var db = CreateDbContext();
        var repo = new EfProfileAnalysisRepository(db);

        var analysis = ProfileAnalysis.CreateFromUrl(
            organizationId: Guid.NewGuid(),
            profileUrl: "https://instagram.com/deleteme",
            createdAtUtc: Now);

        await repo.AddAsync(analysis);
        await db.SaveChangesAsync();

        var deleted = await repo.DeleteAsync(analysis.Id, analysis.OrganizationId);
        await db.SaveChangesAsync();

        Assert.True(deleted);

        var loaded = await repo.GetByIdAsync(analysis.Id, analysis.OrganizationId);
        Assert.Null(loaded);
    }

    [Fact]
    public async Task DeleteAsync_WrongOrg_ReturnsFalse()
    {
        await using var db = CreateDbContext();
        var repo = new EfProfileAnalysisRepository(db);

        var analysis = ProfileAnalysis.CreateFromUrl(
            organizationId: Guid.NewGuid(),
            profileUrl: "https://instagram.com/protected",
            createdAtUtc: Now);

        await repo.AddAsync(analysis);
        await db.SaveChangesAsync();

        var deleted = await repo.DeleteAsync(analysis.Id, Guid.NewGuid());

        Assert.False(deleted);
    }

    // ── Paging ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByOrganizationAsync_ReturnsPagedNewestFirst()
    {
        await using var db = CreateDbContext();
        var repo = new EfProfileAnalysisRepository(db);

        var orgId = Guid.NewGuid();
        for (var i = 0; i < 5; i++)
        {
            var a = ProfileAnalysis.CreateFromUrl(orgId, $"https://instagram.com/u{i}", Now.AddMinutes(i));
            await repo.AddAsync(a);
        }
        await db.SaveChangesAsync();

        var page1 = await repo.GetByOrganizationAsync(orgId, page: 1, pageSize: 3);
        var page2 = await repo.GetByOrganizationAsync(orgId, page: 2, pageSize: 3);

        Assert.Equal(5, page1.TotalCount);
        Assert.Equal(3, page1.Items.Count);
        Assert.Equal(2, page2.Items.Count);

        // Newest first — page1 should have u4 (added last) first
        Assert.Equal("https://instagram.com/u4", page1.Items[0].ProfileUrlOrHandle);
    }

    // ── Status string persistence ────────────────────────────────────────────

    [Fact]
    public async Task StatusPersistsAndReadsBackCorrectly()
    {
        await using var db = CreateDbContext();
        var repo = new EfProfileAnalysisRepository(db);

        var analysis = ProfileAnalysis.CreateFromUrl(
            organizationId: Guid.NewGuid(),
            profileUrl: "https://instagram.com/statustest",
            createdAtUtc: Now);

        await repo.AddAsync(analysis);
        await db.SaveChangesAsync();

        var loaded = await repo.GetByIdAsync(analysis.Id, analysis.OrganizationId);

        Assert.NotNull(loaded);
        Assert.Equal(ProfileAnalysisStatus.Requested, loaded!.Status);
        Assert.Equal("Requested", loaded.Status.ToString());
    }

    // ── Helper ───────────────────────────────────────────────────────────────

    private static AppIdentityDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppIdentityDbContext>()
            .UseInMemoryDatabase($"profile-analysis-repo-{Guid.NewGuid():N}")
            .Options;

        return new AppIdentityDbContext(options);
    }
}
