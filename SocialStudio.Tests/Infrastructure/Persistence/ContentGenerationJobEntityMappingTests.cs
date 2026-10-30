using SocialStudio.Infrastructure.Identity;
using SocialStudio.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace SocialStudio.Tests.Infrastructure.Persistence;

/// <summary>
/// EF mapping tests for <see cref="ContentGenerationJobData"/> → social.content_generation_jobs.
/// </summary>
public sealed class ContentGenerationJobEntityMappingTests
{
    private static readonly DateTime Now = new(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ContentGenerationJobData_RoundTrip_AllRequiredFieldsPersistCorrectly()
    {
        await using var db = CreateDbContext();

        var analysisId = Guid.NewGuid();
        var entity = new ContentGenerationJobData
        {
            Id = Guid.NewGuid(),
            OrganizationId = Guid.NewGuid(),
            ClientId = Guid.NewGuid(),
            AnalysisId = analysisId,
            RequestedFormats = new List<string> { "carousel", "story" },
            Status = "Queued",
            StatusReason = null,
            StartedAt = null,
            CompletedAt = null,
            ErrorCode = null,
            BrandSnapshotJson = "{}",
            ManifestJson = null,
            CorrelationId = Guid.NewGuid(),
            CreatedAt = Now,
            UpdatedAt = Now,
        };

        db.ContentGenerationJobs.Add(entity);
        await db.SaveChangesAsync();

        var read = await db.ContentGenerationJobs.FindAsync(entity.Id);
        Assert.NotNull(read);
        Assert.Equal(entity.Id, read!.Id);
        Assert.Equal(entity.OrganizationId, read.OrganizationId);
        Assert.Equal(entity.Status, read.Status);
        Assert.Equal(entity.AnalysisId, read.AnalysisId);
        Assert.Equal(2, read.RequestedFormats.Count);
        Assert.Null(read.StartedAt);
        Assert.Null(read.ManifestJson);
    }

    [Fact]
    public async Task ContentGenerationJobData_NullableFields_AllowNull()
    {
        await using var db = CreateDbContext();

        var entity = new ContentGenerationJobData
        {
            Id = Guid.NewGuid(),
            OrganizationId = Guid.NewGuid(),
            ClientId = null,
            AnalysisId = Guid.NewGuid(),
            RequestedFormats = new List<string> { "hook" },
            Status = "Failed",
            StatusReason = "All formats failed",
            StartedAt = Now.AddMinutes(-5),
            CompletedAt = Now,
            ErrorCode = "all-formats-failed",
            BrandSnapshotJson = "{}",
            ManifestJson = null,
            CorrelationId = Guid.NewGuid(),
            CreatedAt = Now,
            UpdatedAt = Now,
        };

        db.ContentGenerationJobs.Add(entity);
        await db.SaveChangesAsync();

        var read = await db.ContentGenerationJobs.FindAsync(entity.Id);
        Assert.NotNull(read);
        Assert.Null(read!.ClientId);
        Assert.NotNull(read.StartedAt);
        Assert.NotNull(read.CompletedAt);
        Assert.Equal("all-formats-failed", read.ErrorCode);
    }

    private static AppIdentityDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppIdentityDbContext>()
            .UseInMemoryDatabase($"content-gen-jobs-{Guid.NewGuid():N}")
            .Options;
        return new AppIdentityDbContext(options);
    }
}
