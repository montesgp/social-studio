using SocialStudio.Domain.Social.ContentGeneration;
using SocialStudio.Infrastructure.Persistence.Postgres.Entities;
using SocialStudio.Infrastructure.Social.ContentGeneration;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SocialStudio.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace SocialStudio.Tests.Infrastructure.Social.ContentGeneration;

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
            ColorPalette = new() { new("Primary", "#FF0000", null, 0) },
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

    // --- NEW: Content Strategy and Calendar threading ---

    [Fact]
    public async Task Load_WithContentStrategyText_ExposesContentStrategyOnSnapshot()
    {
        // Arrange: seed a ContentPlan with a content strategy text
        var orgId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var planId = Guid.NewGuid();

        _dbContext.ContentPlans.Add(new ContentPlanData
        {
            Id = planId,
            ClientId = clientId,
            OrganizationId = orgId,
            PeriodKind = 0,
            Year = now.Year,
            Month = now.Month,
            ObjectiveText = "Increase brand awareness",
            ObjectiveKpis = new() { "10k impressions" },
            ObjectiveProvenanceSource = 1,
            ObjectiveProvenanceStatus = 1,
            ContentStrategyText = "Focus on educational reels and carousel posts this month.",
            ContentStrategyProvenanceSource = 1,
            ContentStrategyProvenanceStatus = 2,
            CalendarProvenanceSource = 0,
            CalendarProvenanceStatus = 0,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        });

        await _dbContext.SaveChangesAsync();

        // Act
        var snapshot = await _loader.LoadAsync(orgId, clientId);

        // Assert: ContentStrategy is populated and not inferred
        Assert.NotNull(snapshot.ContentStrategy);
        Assert.Equal("Focus on educational reels and carousel posts this month.", snapshot.ContentStrategy!.StrategyText);
        Assert.Equal((short)1, snapshot.ContentStrategy.Source);
        Assert.Equal((short)2, snapshot.ContentStrategy.Status);
        Assert.False(snapshot.InferredFields["ContentStrategy"]);
    }

    [Fact]
    public async Task Load_WithCalendarSlots_ExposesCalendarAndPeriodOnSnapshot()
    {
        // Arrange: seed a ContentPlan with 3 slots
        var orgId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var planId = Guid.NewGuid();

        var plan = new ContentPlanData
        {
            Id = planId,
            ClientId = clientId,
            OrganizationId = orgId,
            PeriodKind = 0,
            Year = now.Year,
            Month = now.Month,
            ObjectiveText = "Grow reach",
            ObjectiveKpis = new() { "5k reach" },
            ObjectiveProvenanceSource = 1,
            ObjectiveProvenanceStatus = 1,
            ContentStrategyProvenanceSource = 0,
            ContentStrategyProvenanceStatus = 0,
            CalendarProvenanceSource = 1,
            CalendarProvenanceStatus = 2,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };

        _dbContext.ContentPlans.Add(plan);
        await _dbContext.SaveChangesAsync();

        _dbContext.ContentPlanSlots.AddRange(
            new ContentPlanSlotData
            {
                Id = Guid.NewGuid(),
                ContentPlanId = planId,
                Day = 3,
                ContentFormat = "caption",
                SortOrder = 0,
                SlotProvenanceSource = 1,
                SlotProvenanceStatus = 2,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
            },
            new ContentPlanSlotData
            {
                Id = Guid.NewGuid(),
                ContentPlanId = planId,
                Day = 10,
                ContentFormat = "carousel",
                SortOrder = 1,
                SlotProvenanceSource = 1,
                SlotProvenanceStatus = 2,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
            },
            new ContentPlanSlotData
            {
                Id = Guid.NewGuid(),
                ContentPlanId = planId,
                Day = 20,
                ContentFormat = "reel",
                SortOrder = 2,
                SlotProvenanceSource = 1,
                SlotProvenanceStatus = 2,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
            });

        await _dbContext.SaveChangesAsync();

        // Act
        var snapshot = await _loader.LoadAsync(orgId, clientId);

        // Assert: Calendar is populated with all 3 slots in SortOrder
        Assert.NotNull(snapshot.Calendar);
        Assert.Equal(3, snapshot.Calendar!.Slots.Count);
        Assert.Equal(3, snapshot.Calendar.Slots[0].Day);
        Assert.Equal("caption", snapshot.Calendar.Slots[0].ContentFormat);
        Assert.Equal(0, snapshot.Calendar.Slots[0].SortOrder);
        Assert.Equal(10, snapshot.Calendar.Slots[1].Day);
        Assert.Equal("carousel", snapshot.Calendar.Slots[1].ContentFormat);
        Assert.Equal(20, snapshot.Calendar.Slots[2].Day);
        Assert.Equal("reel", snapshot.Calendar.Slots[2].ContentFormat);
        Assert.False(snapshot.InferredFields["CalendarSlots"]);

        // Assert: Period is populated
        Assert.NotNull(snapshot.Period);
        Assert.Equal(now.Year, snapshot.Period!.Year);
        Assert.Equal(now.Month, snapshot.Period.Month);
        Assert.Equal((short)0, snapshot.Period.PeriodKind);
    }

    [Fact]
    public async Task Load_WithContentPlanButNullStrategyTextAndNoSlots_StrategyAndCalendarAreInferred()
    {
        // Arrange: ContentPlan exists but ContentStrategyText is null and no slots
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
            ObjectiveText = "Build community",
            ObjectiveKpis = new() { "Followers +1k" },
            ObjectiveProvenanceSource = 1,
            ObjectiveProvenanceStatus = 1,
            ContentStrategyText = null,          // absent
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

        // Assert: strategy and calendar inferred; period still populated
        Assert.True(snapshot.InferredFields["ContentStrategy"]);
        Assert.True(snapshot.InferredFields["CalendarSlots"]);

        // ContentStrategy snapshot is created (provenance captured) but text is null
        Assert.NotNull(snapshot.ContentStrategy);
        Assert.Null(snapshot.ContentStrategy!.StrategyText);

        // Calendar snapshot is created but empty
        Assert.NotNull(snapshot.Calendar);
        Assert.Empty(snapshot.Calendar!.Slots);

        // Period is still populated from the plan row
        Assert.NotNull(snapshot.Period);
        Assert.Equal(now.Year, snapshot.Period!.Year);
        Assert.Equal(now.Month, snapshot.Period.Month);
    }

    [Fact]
    public async Task Load_WithNoContentPlan_ContentStrategyAndCalendarAndPeriodAreNull()
    {
        // Arrange: no content plan at all
        var orgId = Guid.NewGuid();
        var clientId = Guid.NewGuid();

        // Act
        var snapshot = await _loader.LoadAsync(orgId, clientId);

        // Assert: new optional fields are all null; their inferred flags are set
        Assert.Null(snapshot.ContentStrategy);
        Assert.Null(snapshot.Calendar);
        Assert.Null(snapshot.Period);
        Assert.True(snapshot.InferredFields["ContentStrategy"]);
        Assert.True(snapshot.InferredFields["CalendarSlots"]);
    }
}
