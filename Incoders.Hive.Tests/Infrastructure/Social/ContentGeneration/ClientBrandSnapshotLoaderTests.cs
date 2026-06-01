using Incoders.Hive.Domain.Social.ContentGeneration;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;
using Incoders.Hive.Infrastructure.Social.ContentGeneration;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Incoders.Hive.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Incoders.Hive.Tests.Infrastructure.Social.ContentGeneration;

/// <summary>
/// Unit tests for EfClientBrandSnapshotLoader using an in-memory EF context.
/// Each test exercises the BrandSnapshot.FromBrandData inference rules at the adapter boundary.
/// The loader now reads GeneralStrategyData (was ClientStrategyData) and ContentPlanData (was
/// ClientMonthlyObjectiveData). BrandSnapshot / StrategySnapshot / ClientMonthlyObjectiveSnapshot
/// output shapes are FROZEN — this file is the regression guard for that contract.
/// </summary>
public sealed class ClientBrandSnapshotLoaderTests : IDisposable
{
    private readonly AppIdentityDbContext _dbContext;
    private readonly EfClientBrandSnapshotLoader _loader;

    public ClientBrandSnapshotLoaderTests()
    {
        var options = new DbContextOptionsBuilder<AppIdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new AppIdentityDbContext(options);
        _loader = new EfClientBrandSnapshotLoader(_dbContext);
    }

    public void Dispose() => _dbContext.Dispose();

    // --- Full brand: fields with data are not inferred ---

    [Fact]
    public async Task Load_WithAllThreeSources_ColorPaletteAndVoiceAreNotInferred()
    {
        var orgId = Guid.NewGuid();
        var clientId = Guid.NewGuid();

        _dbContext.ClientBrandManuals.Add(new ClientBrandManualData
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            OrganizationId = orgId,
            Title = "Brand 2026",
            ColorPalette = new() { new("Primary", "#FF0000", null) },
            Logos = new(),
            BackgroundTemplates = new()
            {
                new("Story", "/storage/client-assets/org/client/story.png", "story")
            },
            Typography = new(),
            VoiceAndTone = "Friendly and professional",
            Notes = null,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        });

        // Re-sourced: seed GeneralStrategyData instead of ClientStrategyData
        _dbContext.GeneralStrategies.Add(new GeneralStrategyData
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            OrganizationId = orgId,
            ContentPillars = new() { "Education", "Entertainment" },
            ToneKeywords = new() { "bold" },
            TargetAudience = "Young professionals",
            PostingFrequency = "Daily",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        });

        // Re-sourced: seed ContentPlanData instead of ClientMonthlyObjectiveData
        var now = DateTime.UtcNow;
        _dbContext.ContentPlans.Add(new ContentPlanData
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            OrganizationId = orgId,
            PeriodKind = 0,
            Year = now.Year,
            Month = now.Month,
            ObjectiveText = "Grow engagement 20%",
            ObjectiveKpis = new() { "10k views" },
            ObjectiveProvenanceSource = 1, // Manual
            ObjectiveProvenanceStatus = 1, // Draft
            ContentStrategyProvenanceSource = 0,
            ContentStrategyProvenanceStatus = 0,
            CalendarProvenanceSource = 0,
            CalendarProvenanceStatus = 0,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        });

        await _dbContext.SaveChangesAsync();

        var snapshot = await _loader.LoadAsync(orgId, clientId);

        Assert.NotNull(snapshot);
        // Fields with actual data must NOT be inferred
        Assert.False(snapshot.InferredFields["ColorPalette"]);
        Assert.False(snapshot.InferredFields["VoiceAndTone"]);
        Assert.False(snapshot.InferredFields["BackgroundTemplates"]);
        Assert.Equal(
            "/storage/client-assets/org/client/story.png",
            snapshot.Brand!.BackgroundTemplates.Single().Url);
        Assert.False(snapshot.InferredFields["ContentPillars"]);
        Assert.False(snapshot.InferredFields["TargetAudience"]);
        Assert.False(snapshot.InferredFields["MonthlyObjectives"]);
        Assert.False(snapshot.InferredFields["MonthlyKpis"]);
        // Empty collections (Logos, Typography) are correctly inferred
        Assert.True(snapshot.InferredFields["Logos"]);
        Assert.True(snapshot.InferredFields["Typography"]);
    }

    // --- Missing color palette: ColorPalette is inferred ---

    [Fact]
    public async Task Load_WithMissingColorPalette_ColorPaletteIsInferred()
    {
        var orgId = Guid.NewGuid();
        var clientId = Guid.NewGuid();

        _dbContext.ClientBrandManuals.Add(new ClientBrandManualData
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            OrganizationId = orgId,
            Title = "Voice Only Brand",
            ColorPalette = new(),   // empty = missing
            Logos = new(),
            BackgroundTemplates = new(),
            Typography = new(),
            VoiceAndTone = "Bold and direct",
            Notes = null,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        });

        await _dbContext.SaveChangesAsync();

        var snapshot = await _loader.LoadAsync(orgId, clientId);

        Assert.True(snapshot.InferredFields["ColorPalette"]);
        Assert.False(snapshot.InferredFields["VoiceAndTone"]);
    }

    // --- No brand manual at all: all fields inferred ---

    [Fact]
    public async Task Load_WithNoBrandData_AllFieldsAreInferred()
    {
        var orgId = Guid.NewGuid();
        var clientId = Guid.NewGuid();

        // No rows inserted for this client/org

        var snapshot = await _loader.LoadAsync(orgId, clientId);

        Assert.NotNull(snapshot);
        Assert.True(snapshot.InferredFields.All(kv => kv.Value));
    }

    [Fact]
    public async Task Load_WithNoBrandData_DoesNotThrow()
    {
        // Ensures the port contract "MUST NOT fail" when no brand data exists
        var orgId = Guid.NewGuid();
        var clientId = Guid.NewGuid();

        var exception = await Record.ExceptionAsync(() => _loader.LoadAsync(orgId, clientId));

        Assert.Null(exception);
    }

    // --- BS-02: Loader reads Content Plan Objective for period ---

    [Fact]
    public async Task Load_WithContentPlanForCurrentMonth_ReadsObjectiveFromContentPlan()
    {
        // Arrange: seed a ContentPlan for the current period — the loader should read from it
        var orgId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        _dbContext.ContentPlans.Add(new ContentPlanData
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            OrganizationId = orgId,
            PeriodKind = 0,
            Year = now.Year,
            Month = now.Month,
            ObjectiveText = "Reach 50k followers",
            ObjectiveKpis = new() { "Followers +5k", "Engagement rate 8%" },
            ObjectiveProvenanceSource = 1,
            ObjectiveProvenanceStatus = 1,
            ContentStrategyProvenanceSource = 0,
            ContentStrategyProvenanceStatus = 0,
            CalendarProvenanceSource = 0,
            CalendarProvenanceStatus = 0,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        });

        await _dbContext.SaveChangesAsync();

        // Act
        var snapshot = await _loader.LoadAsync(orgId, clientId);

        // Assert: objective snapshot is sourced from ContentPlan, not ClientMonthlyObjective
        Assert.NotNull(snapshot.MonthlyObjective);
        Assert.Equal("Reach 50k followers", snapshot.MonthlyObjective!.Objectives);
        Assert.Equal(2, snapshot.MonthlyObjective.Kpis.Count);
        Assert.Equal("Followers +5k", snapshot.MonthlyObjective.Kpis[0]);
        Assert.Equal("Engagement rate 8%", snapshot.MonthlyObjective.Kpis[1]);
        Assert.False(snapshot.InferredFields["MonthlyObjectives"]);
        Assert.False(snapshot.InferredFields["MonthlyKpis"]);
    }

    [Fact]
    public async Task Load_WithContentPlanForPastMonth_FallsBackToLatestPlan()
    {
        // Arrange: no current-month plan; one plan for a past period
        var orgId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var pastYear = now.Month == 1 ? now.Year - 1 : now.Year;
        var pastMonth = now.Month == 1 ? 12 : now.Month - 1;

        _dbContext.ContentPlans.Add(new ContentPlanData
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            OrganizationId = orgId,
            PeriodKind = 0,
            Year = pastYear,
            Month = pastMonth,
            ObjectiveText = "Past month objective",
            ObjectiveKpis = new() { "Old KPI" },
            ObjectiveProvenanceSource = 1,
            ObjectiveProvenanceStatus = 1,
            ContentStrategyProvenanceSource = 0,
            ContentStrategyProvenanceStatus = 0,
            CalendarProvenanceSource = 0,
            CalendarProvenanceStatus = 0,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        });

        await _dbContext.SaveChangesAsync();

        // Act
        var snapshot = await _loader.LoadAsync(orgId, clientId);

        // Assert: loader falls back to most recent plan instead of returning null
        Assert.NotNull(snapshot.MonthlyObjective);
        Assert.Equal("Past month objective", snapshot.MonthlyObjective!.Objectives);
        Assert.False(snapshot.InferredFields["MonthlyObjectives"]);
    }

    // --- BS-02: Missing Content Plan yields inferred objective without throwing ---

    [Fact]
    public async Task Load_WithNoContentPlan_YieldsInferredObjectiveWithoutThrowing()
    {
        // Arrange: brand manual and strategy present but no ContentPlan
        var orgId = Guid.NewGuid();
        var clientId = Guid.NewGuid();

        _dbContext.GeneralStrategies.Add(new GeneralStrategyData
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            OrganizationId = orgId,
            ContentPillars = new() { "Lifestyle" },
            ToneKeywords = new() { "warm" },
            TargetAudience = "Families",
            PostingFrequency = "3x/week",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        });

        await _dbContext.SaveChangesAsync();

        // Act
        var snapshot = await _loader.LoadAsync(orgId, clientId);

        // Assert: no exception; objective fields are inferred; strategy fields are not inferred
        Assert.Null(snapshot.MonthlyObjective);
        Assert.True(snapshot.InferredFields["MonthlyObjectives"]);
        Assert.True(snapshot.InferredFields["MonthlyKpis"]);
        Assert.False(snapshot.InferredFields["ContentPillars"]);
        Assert.False(snapshot.InferredFields["TargetAudience"]);
    }
}
