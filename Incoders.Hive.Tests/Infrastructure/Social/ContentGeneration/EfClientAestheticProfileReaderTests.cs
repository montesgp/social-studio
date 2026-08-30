using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;
using Incoders.Hive.Infrastructure.Social.ContentGeneration;
using Microsoft.EntityFrameworkCore;

namespace Incoders.Hive.Tests.Infrastructure.Social.ContentGeneration;

/// <summary>
/// Unit tests for EfClientAestheticProfileReader using an in-memory EF context.
/// Validates the richer IClientAestheticProfileReader contract (CAG-CG-05):
/// BackgroundDescriptors + VoiceTone from ai_analysis_json;
/// AverageSlidesPerCarousel from server_derived_stats_json;
/// AnalyzedAtUtc from CompletedAtUtc.
/// Refs: design §2.3; CAG-PA-04, CAG-PA-05; tasks 5.7.
/// </summary>
public sealed class EfClientAestheticProfileReaderTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly AppIdentityDbContext _db;
    private readonly EfClientAestheticProfileReader _reader;
    private readonly Guid _orgId = Guid.NewGuid();
    private readonly Guid _clientId = Guid.NewGuid();

    public EfClientAestheticProfileReaderTests()
    {
        var options = new DbContextOptionsBuilder<AppIdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppIdentityDbContext(options);
        _reader = new EfClientAestheticProfileReader(_db);
    }

    public void Dispose() => _db.Dispose();

    // -----------------------------------------------------------------------
    // Returns all four fields from the most recent Analyzed record
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetProfileAsync_WithAnalyzedRecord_ReturnsAllFourFields()
    {
        var completedAt = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        var descriptors = new[] { "bright studio backgrounds", "minimal white walls" };
        var voiceTone = "professional and inspiring";
        var avgSlides = 4.5;

        SeedAnalysis(
            _orgId, _clientId, "Analyzed",
            descriptors, voiceTone, avgSlides,
            completedAt);
        await _db.SaveChangesAsync();

        var profile = await _reader.GetProfileAsync(_orgId, _clientId, CancellationToken.None);

        Assert.NotNull(profile);
        Assert.Equal(2, profile!.BackgroundDescriptors.Count);
        Assert.Contains("bright studio backgrounds", profile.BackgroundDescriptors);
        Assert.Contains("minimal white walls", profile.BackgroundDescriptors);
        Assert.Equal(voiceTone, profile.VoiceTone);
        Assert.Equal(avgSlides, profile.AverageSlidesPerCarousel);
        Assert.Equal(completedAt, profile.AnalyzedAtUtc);
    }

    // -----------------------------------------------------------------------
    // Returns null when no Analyzed row exists
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetProfileAsync_WithNoAnalyzedRecord_ReturnsNull()
    {
        var profile = await _reader.GetProfileAsync(_orgId, _clientId, CancellationToken.None);

        Assert.Null(profile);
    }

    // -----------------------------------------------------------------------
    // Failed / in-progress records are excluded; only Analyzed rows returned
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetProfileAsync_WithOnlyFailedOrInProgressRecords_ReturnsNull()
    {
        SeedAnalysis(_orgId, _clientId, "Failed", null, null, null, DateTime.UtcNow.AddDays(-2));
        SeedAnalysis(_orgId, _clientId, "Analyzing", null, null, null, null);
        await _db.SaveChangesAsync();

        var profile = await _reader.GetProfileAsync(_orgId, _clientId, CancellationToken.None);

        Assert.Null(profile);
    }

    // -----------------------------------------------------------------------
    // Selects the most recent of two Analyzed rows by CompletedAtUtc
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetProfileAsync_WithTwoAnalyzedRecords_ReturnsMostRecent()
    {
        var older = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        var newer = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        SeedAnalysis(_orgId, _clientId, "Analyzed", new[] { "old bg" }, "old tone", null, older);
        SeedAnalysis(_orgId, _clientId, "Analyzed", new[] { "new bg" }, "new tone", 5.0, newer);
        await _db.SaveChangesAsync();

        var profile = await _reader.GetProfileAsync(_orgId, _clientId, CancellationToken.None);

        Assert.NotNull(profile);
        Assert.Equal("new tone", profile!.VoiceTone);
        Assert.Equal(newer, profile.AnalyzedAtUtc);
    }

    // -----------------------------------------------------------------------
    // Null/absent averageSlidesPerCarousel → null on returned profile
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetProfileAsync_WithNoAverageSlidesInStats_ReturnsNullAvgSlides()
    {
        SeedAnalysis(
            _orgId, _clientId, "Analyzed",
            new[] { "descriptor" }, "tone", avgSlides: null,
            new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        await _db.SaveChangesAsync();

        var profile = await _reader.GetProfileAsync(_orgId, _clientId, CancellationToken.None);

        Assert.NotNull(profile);
        Assert.Null(profile!.AverageSlidesPerCarousel);
    }

    // -----------------------------------------------------------------------
    // Return value has no EF/Infrastructure types in its shape (contract test)
    // -----------------------------------------------------------------------

    [Fact]
    public void GetProfileAsync_ReturnType_HasNoInfrastructureTypes()
    {
        var readerType = typeof(EfClientAestheticProfileReader);
        var method = readerType.GetMethod(nameof(IClientAestheticProfileReader.GetProfileAsync))!;

        // Parameter types must not reference EF or Npgsql
        foreach (var param in method.GetParameters())
        {
            var typeName = param.ParameterType.FullName ?? string.Empty;
            Assert.DoesNotMatch(@"EntityFramework|Npgsql|EF\.", typeName);
        }

        // Return type is Task<ClientAestheticProfile?> — no infra type
        var returnType = method.ReturnType.FullName ?? string.Empty;
        Assert.DoesNotMatch(@"EntityFramework|Npgsql|EF\.", returnType);
    }

    // -----------------------------------------------------------------------
    // Cross-org isolation
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetProfileAsync_CrossOrgIsolation_ReturnsOnlyOwnOrg()
    {
        var otherOrg = Guid.NewGuid();
        SeedAnalysis(otherOrg, _clientId, "Analyzed", new[] { "other" }, "other tone", null,
            DateTime.UtcNow.AddDays(-1));
        await _db.SaveChangesAsync();

        var profile = await _reader.GetProfileAsync(_orgId, _clientId, CancellationToken.None);

        Assert.Null(profile);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private void SeedAnalysis(
        Guid orgId,
        Guid clientId,
        string status,
        string[]? backgroundDescriptors,
        string? voiceTone,
        double? avgSlides,
        DateTime? completedAt)
    {
        // ai_analysis_json holds DetectedBackgroundImages + VoiceTone
        string? aiJson = null;
        if (backgroundDescriptors is not null || voiceTone is not null)
        {
            aiJson = JsonSerializer.Serialize(new
            {
                DetectedBackgroundImages = backgroundDescriptors ?? Array.Empty<string>(),
                VoiceTone = voiceTone
            }, JsonOpts);
        }

        // server_derived_stats_json holds AverageSlidesPerCarousel
        string? statsJson = null;
        if (avgSlides.HasValue)
        {
            statsJson = JsonSerializer.Serialize(new
            {
                AverageSlidesPerCarousel = avgSlides.Value
            }, JsonOpts);
        }

        _db.ProfileAnalyses.Add(new ProfileAnalysisData
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            ClientId = clientId,
            ProfileUrlOrHandle = "@handle",
            Status = status,
            AiAnalysisJson = aiJson,
            ServerDerivedStatsJson = statsJson,
            CompletedAtUtc = completedAt,
            CreatedAtUtc = completedAt?.AddHours(-1) ?? DateTime.UtcNow.AddDays(-3),
            UpdatedAtUtc = completedAt ?? DateTime.UtcNow.AddDays(-3),
        });
    }
}
