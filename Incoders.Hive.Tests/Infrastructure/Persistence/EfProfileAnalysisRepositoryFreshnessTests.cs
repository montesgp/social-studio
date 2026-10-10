using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Persistence.Postgres;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;
using Incoders.Hive.Domain.Social.ProfileAnalysis;
using Microsoft.EntityFrameworkCore;

namespace Incoders.Hive.Tests.Infrastructure.Persistence;

/// <summary>
/// Integration tests for the freshness/debounce methods on EfProfileAnalysisRepository.
/// Uses an in-memory EF context — no real Postgres connection needed.
/// Refs: CAG-PA-04, CAG-PA-05; design §4; tasks 5.9.
/// </summary>
public sealed class EfProfileAnalysisRepositoryFreshnessTests
{
    private readonly Guid _orgId = Guid.NewGuid();
    private readonly Guid _clientId = Guid.NewGuid();

    // -----------------------------------------------------------------------
    // GetLatestAnalyzedCompletedAtAsync — returns MAX CompletedAtUtc of Analyzed rows
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetLatestAnalyzedCompletedAtAsync_WithSingleAnalyzedRow_ReturnsCompletedAt()
    {
        var completedAt = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        await using var db = CreateDb();
        Seed(db, _orgId, _clientId, "Analyzed", completedAt);
        await db.SaveChangesAsync();

        var repo = new EfProfileAnalysisRepository(db);
        var result = await repo.GetLatestAnalyzedCompletedAtAsync(_orgId, _clientId, CancellationToken.None);

        Assert.Equal(completedAt, result);
    }

    [Fact]
    public async Task GetLatestAnalyzedCompletedAtAsync_WithTwoAnalyzedRows_ReturnsMaxCompletedAt()
    {
        var older = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        var newer = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        await using var db = CreateDb();
        Seed(db, _orgId, _clientId, "Analyzed", older);
        Seed(db, _orgId, _clientId, "Analyzed", newer);
        await db.SaveChangesAsync();

        var repo = new EfProfileAnalysisRepository(db);
        var result = await repo.GetLatestAnalyzedCompletedAtAsync(_orgId, _clientId, CancellationToken.None);

        Assert.Equal(newer, result);
    }

    [Fact]
    public async Task GetLatestAnalyzedCompletedAtAsync_WithNoAnalyzedRows_ReturnsNull()
    {
        await using var db = CreateDb();
        // Only a Failed row — no Analyzed
        Seed(db, _orgId, _clientId, "Failed", DateTime.UtcNow.AddDays(-1));
        await db.SaveChangesAsync();

        var repo = new EfProfileAnalysisRepository(db);
        var result = await repo.GetLatestAnalyzedCompletedAtAsync(_orgId, _clientId, CancellationToken.None);

        Assert.Null(result);
    }

    // -----------------------------------------------------------------------
    // HasNonTerminalAnalysisForClientAsync — true when Requested/Scraping/Analyzing row exists
    // -----------------------------------------------------------------------

    [Fact]
    public async Task HasNonTerminalAnalysisForClientAsync_WithRequestedRow_ReturnsTrue()
    {
        await using var db = CreateDb();
        Seed(db, _orgId, _clientId, "Requested", null);
        await db.SaveChangesAsync();

        var repo = new EfProfileAnalysisRepository(db);
        var result = await repo.HasNonTerminalAnalysisForClientAsync(_orgId, _clientId, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task HasNonTerminalAnalysisForClientAsync_AbandonedRow_IsNotInFlight()
    {
        // A restart or a suspended host leaves the row "Analyzing" with nothing running it; it must
        // not keep debouncing every future refresh for the client.
        await using var db = CreateDb();
        Seed(db, _orgId, _clientId, "Analyzing", null, updatedAt: DateTime.UtcNow.AddHours(-11));
        await db.SaveChangesAsync();

        var repo = new EfProfileAnalysisRepository(db);
        var result = await repo.HasNonTerminalAnalysisForClientAsync(_orgId, _clientId, CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task HasNonTerminalAnalysisForClientAsync_WithScrapingRow_ReturnsTrue()
    {
        await using var db = CreateDb();
        Seed(db, _orgId, _clientId, "Scraping", null);
        await db.SaveChangesAsync();

        var repo = new EfProfileAnalysisRepository(db);
        var result = await repo.HasNonTerminalAnalysisForClientAsync(_orgId, _clientId, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task HasNonTerminalAnalysisForClientAsync_WithOnlyAnalyzedOrFailedRows_ReturnsFalse()
    {
        var completedAt = DateTime.UtcNow.AddDays(-1);
        await using var db = CreateDb();
        Seed(db, _orgId, _clientId, "Analyzed", completedAt);
        Seed(db, _orgId, _clientId, "Failed", completedAt.AddDays(-2));
        await db.SaveChangesAsync();

        var repo = new EfProfileAnalysisRepository(db);
        var result = await repo.HasNonTerminalAnalysisForClientAsync(_orgId, _clientId, CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task HasNonTerminalAnalysisForClientAsync_WithNoRows_ReturnsFalse()
    {
        await using var db = CreateDb();

        var repo = new EfProfileAnalysisRepository(db);
        var result = await repo.HasNonTerminalAnalysisForClientAsync(_orgId, _clientId, CancellationToken.None);

        Assert.False(result);
    }

    // -----------------------------------------------------------------------
    // LatestAnalyzedHasAestheticAsync — issue #84: "usable" requires real aesthetic content
    // (ColorPairings and/or BackgroundPattern), not just a non-empty JSON blob.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task LatestAnalyzedHasAestheticAsync_StaleEmptyAestheticProfile_ReturnsFalse()
    {
        // Non-empty JSON, but no ColorPairings and no BackgroundPattern — the stale/empty shape
        // that used to trivially satisfy the old IsNullOrWhiteSpace check.
        const string json =
            """{"palette":[],"styleDescriptors":["editorial"],"motifs":[],"captionVoice":"playful","referenceImages":[],"promptVersion":"aesthetic-extractor/v2"}""";
        await using var db = CreateDb();
        SeedWithAesthetic(db, _orgId, _clientId, "Analyzed", DateTime.UtcNow, json);
        await db.SaveChangesAsync();

        var repo = new EfProfileAnalysisRepository(db);
        var result = await repo.LatestAnalyzedHasAestheticAsync(_orgId, _clientId, CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task LatestAnalyzedHasAestheticAsync_RichAestheticProfile_ReturnsTrue()
    {
        const string json =
            """
            {"palette":[],"styleDescriptors":[],"motifs":[],"captionVoice":null,
             "referenceImages":[{"sourcePostId":"p1","assetIndex":0,"storagePath":"org/c/p1.jpg","contentHash":"h1"}],
             "backgroundPattern":["Photo","FlatColor"],
             "colorPairings":[{"backgroundHex":"#111","textHex":"#FFF","rank":1}],
             "promptVersion":"aesthetic-extractor/v3"}
            """;
        await using var db = CreateDb();
        SeedWithAesthetic(db, _orgId, _clientId, "Analyzed", DateTime.UtcNow, json);
        await db.SaveChangesAsync();

        var repo = new EfProfileAnalysisRepository(db);
        var result = await repo.LatestAnalyzedHasAestheticAsync(_orgId, _clientId, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task LatestAnalyzedHasAestheticAsync_OnlyPromptVersionAndReferenceImagesShape_ReturnsFalse()
    {
        // Exact real-world shape observed on job 6b51cbd5-ab01-459d-9c4f-fbb10a8d7eb6 (issue #84):
        // only promptVersion + referenceImages populated, everything else null/absent.
        const string json =
            """{"referenceImages":[{"sourcePostId":"p1","assetIndex":0,"storagePath":"org/c/p1.jpg","contentHash":"h1"}],"promptVersion":"aesthetic-extractor/v2"}""";
        await using var db = CreateDb();
        SeedWithAesthetic(db, _orgId, _clientId, "Analyzed", DateTime.UtcNow, json);
        await db.SaveChangesAsync();

        var repo = new EfProfileAnalysisRepository(db);
        var result = await repo.LatestAnalyzedHasAestheticAsync(_orgId, _clientId, CancellationToken.None);

        Assert.False(result);
    }

    // -----------------------------------------------------------------------
    // GetLatestAestheticFreshnessAsync — parses the latest Analyzed row's aesthetic JSON
    // (aesthetic-freshness-versioning)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetLatestAestheticFreshnessAsync_CompleteV3Blob_ReturnsAllPillarsTrue()
    {
        const string json =
            """
            {"palette":[],"styleDescriptors":[],"motifs":[],"captionVoice":null,
             "referenceImages":[{"sourcePostId":"p1","assetIndex":0,"storagePath":"org/c/p1.jpg","contentHash":"h1"}],
             "backgroundPattern":["Photo","FlatColor"],
             "colorPairings":[{"backgroundHex":"#111","textHex":"#FFF","rank":1}],
             "promptVersion":"aesthetic-extractor/v3"}
            """;
        await using var db = CreateDb();
        SeedWithAesthetic(db, _orgId, _clientId, "Analyzed", DateTime.UtcNow, json);
        await db.SaveChangesAsync();

        var repo = new EfProfileAnalysisRepository(db);
        var snapshot = await repo.GetLatestAestheticFreshnessAsync(_orgId, _clientId, CancellationToken.None);

        Assert.Equal("aesthetic-extractor/v3", snapshot.SchemaVersion);
        Assert.True(snapshot.HasColorPairings);
        Assert.True(snapshot.HasBackgroundPattern);
        Assert.True(snapshot.HasReferenceImages);
    }

    [Fact]
    public async Task GetLatestAestheticFreshnessAsync_OldV2BlobMissingPillars_ReturnsFalsePillarsAndStoredVersion()
    {
        // Pre-v3 blob captured before ColorPairings/BackgroundPattern shipped: no aesthetic
        // signal for those keys, and no reference images either (the Atlas bug scenario).
        const string json =
            """{"palette":[],"styleDescriptors":["editorial"],"motifs":[],"captionVoice":"playful","referenceImages":[],"promptVersion":"aesthetic-extractor/v2"}""";
        await using var db = CreateDb();
        SeedWithAesthetic(db, _orgId, _clientId, "Analyzed", DateTime.UtcNow, json);
        await db.SaveChangesAsync();

        var repo = new EfProfileAnalysisRepository(db);
        var snapshot = await repo.GetLatestAestheticFreshnessAsync(_orgId, _clientId, CancellationToken.None);

        Assert.Equal("aesthetic-extractor/v2", snapshot.SchemaVersion);
        Assert.False(snapshot.HasColorPairings);
        Assert.False(snapshot.HasBackgroundPattern);
        Assert.False(snapshot.HasReferenceImages);
    }

    [Fact]
    public async Task GetLatestAestheticFreshnessAsync_NoAnalyzedRow_ReturnsNullVersionAndFalsePillars()
    {
        await using var db = CreateDb();

        var repo = new EfProfileAnalysisRepository(db);
        var snapshot = await repo.GetLatestAestheticFreshnessAsync(_orgId, _clientId, CancellationToken.None);

        Assert.Null(snapshot.SchemaVersion);
        Assert.False(snapshot.HasColorPairings);
        Assert.False(snapshot.HasBackgroundPattern);
        Assert.False(snapshot.HasReferenceImages);
    }

    [Fact]
    public async Task GetLatestAestheticFreshnessAsync_PicksMostRecentAnalyzedRow()
    {
        var older = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        var newer = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        const string olderJson = """{"palette":[],"styleDescriptors":[],"motifs":[],"captionVoice":null,"referenceImages":[],"promptVersion":"aesthetic-extractor/v2"}""";
        const string newerJson = """{"palette":[],"styleDescriptors":[],"motifs":[],"captionVoice":null,"referenceImages":[],"promptVersion":"aesthetic-extractor/v3"}""";

        await using var db = CreateDb();
        SeedWithAesthetic(db, _orgId, _clientId, "Analyzed", older, olderJson);
        SeedWithAesthetic(db, _orgId, _clientId, "Analyzed", newer, newerJson);
        await db.SaveChangesAsync();

        var repo = new EfProfileAnalysisRepository(db);
        var snapshot = await repo.GetLatestAestheticFreshnessAsync(_orgId, _clientId, CancellationToken.None);

        Assert.Equal("aesthetic-extractor/v3", snapshot.SchemaVersion);
    }

    [Fact]
    public async Task UpdateAsync_AfterSetAestheticProfileJsonAsync_PreservesAestheticJson()
    {
        var now = new DateTime(2026, 7, 15, 10, 33, 33, DateTimeKind.Utc);
        var ownAccountId = Guid.NewGuid();
        const string aestheticJson =
            """{"palette":[],"styleDescriptors":["editorial gradients"],"motifs":[],"captionVoice":null,"referenceImages":[],"promptVersion":"aesthetic-extractor/v2"}""";

        var analysis = ProfileAnalysis.CreateFromClient(_orgId, _clientId, ownAccountId, "@handle", now.AddMinutes(-5));
        analysis.StartScraping(now.AddMinutes(-4));
        analysis.CompleteScrape(isPartial: false, now.AddMinutes(-3));
        analysis.StartAnalyzing(now.AddMinutes(-2));
        analysis.SetAiAnalysis("""{"summary":"ok"}""", """{"aiProviderKey":"test"}""");

        await using var db = CreateDb();
        db.ProfileAnalyses.Add(new ProfileAnalysisData
        {
            Id = analysis.Id,
            OrganizationId = _orgId,
            ClientId = _clientId,
            OwnAccountId = ownAccountId,
            ProfileUrlOrHandle = "@handle",
            Status = analysis.Status.ToString(),
            CreatedAtUtc = analysis.CreatedAtUtc,
            UpdatedAtUtc = analysis.UpdatedAtUtc,
        });
        await db.SaveChangesAsync();

        var repo = new EfProfileAnalysisRepository(db);
        await repo.SetAestheticProfileJsonAsync(analysis.Id, _orgId, aestheticJson, CancellationToken.None);

        analysis.CompleteAnalysis(BuildAiAnalysis(), now);
        await repo.UpdateAsync(analysis, CancellationToken.None);
        await db.SaveChangesAsync();

        var row = await db.ProfileAnalyses
            .AsNoTracking()
            .SingleAsync(x => x.Id == analysis.Id);
        Assert.Equal("Analyzed", row.Status);
        Assert.Equal(aestheticJson, row.AestheticProfileJson);
        // Issue #84: this blob has no ColorPairings/BackgroundPattern (pre-v3 shape) — no longer
        // "usable" under the corrected richness check, even though the JSON itself is non-empty.
        Assert.False(await repo.LatestAnalyzedHasAestheticAsync(_orgId, _clientId, CancellationToken.None));
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static AppIdentityDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppIdentityDbContext>()
            .UseInMemoryDatabase($"profile-analysis-freshness-{Guid.NewGuid():N}")
            .Options;
        return new AppIdentityDbContext(options);
    }

    private static void Seed(
        AppIdentityDbContext db,
        Guid orgId,
        Guid clientId,
        string status,
        DateTime? completedAt,
        DateTime? updatedAt = null)
    {
        db.ProfileAnalyses.Add(new ProfileAnalysisData
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            ClientId = clientId,
            ProfileUrlOrHandle = "@handle",
            Status = status,
            CompletedAtUtc = completedAt,
            CreatedAtUtc = completedAt?.AddHours(-2) ?? DateTime.UtcNow.AddMinutes(-5),
            // An in-flight analysis was touched moments ago; a finished one when it completed.
            UpdatedAtUtc = updatedAt ?? completedAt ?? DateTime.UtcNow.AddMinutes(-2),
        });
    }

    private static void SeedWithAesthetic(
        AppIdentityDbContext db,
        Guid orgId,
        Guid clientId,
        string status,
        DateTime? completedAt,
        string aestheticProfileJson)
    {
        db.ProfileAnalyses.Add(new ProfileAnalysisData
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            ClientId = clientId,
            ProfileUrlOrHandle = "@handle",
            Status = status,
            AestheticProfileJson = aestheticProfileJson,
            CompletedAtUtc = completedAt,
            CreatedAtUtc = completedAt?.AddHours(-2) ?? DateTime.UtcNow.AddHours(-3),
            UpdatedAtUtc = completedAt ?? DateTime.UtcNow.AddHours(-2),
        });
    }

    private static ProfileAiAnalysis BuildAiAnalysis() =>
        new(
            Summary: "Summary",
            AudienceProfile: "Audience",
            ContentPillars: ["Education"],
            TopContentTypes: ["Carousel"],
            PostingDays: ["Monday"],
            DetectedBrandColors: [],
            DetectedFonts: [],
            DetectedBackgroundImages: ["Gradient"],
            VoiceTone: "Direct",
            RecommendedKpis: ["Reach"],
            MonthlyObjectiveSuggestion: "Grow",
            ContentStrategySuggestion: "Teach",
            Strengths: ["Consistent"],
            Weaknesses: [],
            Opportunities: [],
            Confidence: 0.9,
            Warnings: []);
}
