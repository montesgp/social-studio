using System.Text.Json;
using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;
using Incoders.Hive.Infrastructure.Social.ContentGeneration;
using Microsoft.EntityFrameworkCore;

namespace Incoders.Hive.Tests.Infrastructure.Social.ContentGeneration;

/// <summary>
/// Unit tests for EfClientAestheticDescriptorReader using an in-memory EF context.
/// Refs: design §4.4; tasks 5.3; PA-SEAM-01a/b/c/d, PA-SEAM-03.
/// </summary>
public sealed class EfClientAestheticDescriptorReaderTests : IDisposable
{
    private readonly AppIdentityDbContext _db;
    private readonly EfClientAestheticDescriptorReader _reader;
    private readonly Guid _orgId = Guid.NewGuid();
    private readonly Guid _clientId = Guid.NewGuid();

    public EfClientAestheticDescriptorReaderTests()
    {
        var options = new DbContextOptionsBuilder<AppIdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppIdentityDbContext(options);
        _reader = new EfClientAestheticDescriptorReader(_db);
    }

    public void Dispose() => _db.Dispose();

    // -----------------------------------------------------------------------
    // PA-SEAM-01a: returns DetectedBackgroundImages from the most recent Analyzed record
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetDescriptorsAsync_WithSingleAnalyzedRecord_ReturnsDescriptors()
    {
        var descriptors = new[] { "bright studio backgrounds", "minimal white walls" };
        SeedAnalysis(_orgId, _clientId, "Analyzed", descriptors, DateTime.UtcNow.AddDays(-1));
        await _db.SaveChangesAsync();

        var result = await _reader.GetDescriptorsAsync(_orgId, _clientId, CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Contains("bright studio backgrounds", result);
        Assert.Contains("minimal white walls", result);
    }

    // -----------------------------------------------------------------------
    // PA-SEAM-01b: returns empty list when no completed record exists
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetDescriptorsAsync_WithNoAnalyzedRecord_ReturnsEmpty()
    {
        // No rows inserted

        var result = await _reader.GetDescriptorsAsync(_orgId, _clientId, CancellationToken.None);

        Assert.Empty(result);
    }

    // -----------------------------------------------------------------------
    // PA-SEAM-01c: selects the most recent of two completed records by CompletedAtUtc desc
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetDescriptorsAsync_WithTwoAnalyzedRecords_ReturnsMostRecentByCompletedAt()
    {
        var older = new[] { "old descriptors" };
        var newer = new[] { "new descriptor A", "new descriptor B" };

        SeedAnalysis(_orgId, _clientId, "Analyzed", older, DateTime.UtcNow.AddDays(-5));
        SeedAnalysis(_orgId, _clientId, "Analyzed", newer, DateTime.UtcNow.AddDays(-1));
        await _db.SaveChangesAsync();

        var result = await _reader.GetDescriptorsAsync(_orgId, _clientId, CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Contains("new descriptor A", result);
        Assert.Contains("new descriptor B", result);
    }

    // -----------------------------------------------------------------------
    // PA-SEAM-01d: in-progress / failed records are excluded
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetDescriptorsAsync_WithOnlyFailedOrInProgressRecords_ReturnsEmpty()
    {
        SeedAnalysis(_orgId, _clientId, "Failed", new[] { "irrelevant" }, DateTime.UtcNow.AddDays(-2));
        SeedAnalysis(_orgId, _clientId, "Analyzing", null, null);
        await _db.SaveChangesAsync();

        var result = await _reader.GetDescriptorsAsync(_orgId, _clientId, CancellationToken.None);

        Assert.Empty(result);
    }

    // -----------------------------------------------------------------------
    // PA-SEAM-03: IApifyGateway is NOT in the call chain (constructor check)
    // -----------------------------------------------------------------------

    [Fact]
    public void EfClientAestheticDescriptorReader_ConstructorHasNoApifyParameter()
    {
        var readerType = typeof(EfClientAestheticDescriptorReader);
        var ctors = readerType.GetConstructors();

        foreach (var ctor in ctors)
        {
            foreach (var param in ctor.GetParameters())
            {
                Assert.DoesNotMatch(
                    @"Apify|IApifyGateway",
                    param.ParameterType.FullName ?? string.Empty);
            }
        }
    }

    // -----------------------------------------------------------------------
    // Cross-org isolation
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetDescriptorsAsync_CrossOrgIsolation_ReturnsOnlyOwnOrgDescriptors()
    {
        var otherOrg = Guid.NewGuid();
        SeedAnalysis(otherOrg, _clientId, "Analyzed", new[] { "other org descriptor" }, DateTime.UtcNow.AddDays(-1));
        await _db.SaveChangesAsync();

        var result = await _reader.GetDescriptorsAsync(_orgId, _clientId, CancellationToken.None);

        Assert.Empty(result);
    }

    // -----------------------------------------------------------------------
    // Null/empty AiAnalysisJson — returns empty gracefully
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetDescriptorsAsync_WithNullAiAnalysisJson_ReturnsEmpty()
    {
        // Analyzed record but AiAnalysisJson is null (edge case)
        _db.ProfileAnalyses.Add(new ProfileAnalysisData
        {
            Id = Guid.NewGuid(),
            OrganizationId = _orgId,
            ClientId = _clientId,
            ProfileUrlOrHandle = "@test",
            Status = "Analyzed",
            AiAnalysisJson = null,
            CompletedAtUtc = DateTime.UtcNow.AddDays(-1),
            CreatedAtUtc = DateTime.UtcNow.AddDays(-2),
            UpdatedAtUtc = DateTime.UtcNow.AddDays(-1),
        });
        await _db.SaveChangesAsync();

        var result = await _reader.GetDescriptorsAsync(_orgId, _clientId, CancellationToken.None);

        Assert.Empty(result);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private void SeedAnalysis(
        Guid orgId,
        Guid clientId,
        string status,
        string[]? backgroundDescriptors,
        DateTime? completedAt)
    {
        var aiJson = backgroundDescriptors is not null
            ? JsonSerializer.Serialize(new { DetectedBackgroundImages = backgroundDescriptors })
            : null;

        _db.ProfileAnalyses.Add(new ProfileAnalysisData
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            ClientId = clientId,
            ProfileUrlOrHandle = "@handle",
            Status = status,
            AiAnalysisJson = aiJson,
            CompletedAtUtc = completedAt,
            CreatedAtUtc = completedAt?.AddHours(-1) ?? DateTime.UtcNow.AddDays(-3),
            UpdatedAtUtc = completedAt ?? DateTime.UtcNow.AddDays(-3),
        });
    }
}
