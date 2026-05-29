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

        _dbContext.ClientStrategies.Add(new ClientStrategyData
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

        _dbContext.ClientMonthlyObjectives.Add(new ClientMonthlyObjectiveData
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            OrganizationId = orgId,
            Year = 2026,
            Month = 5,
            Objectives = "Grow engagement 20%",
            Kpis = new() { "10k views" },
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
}
