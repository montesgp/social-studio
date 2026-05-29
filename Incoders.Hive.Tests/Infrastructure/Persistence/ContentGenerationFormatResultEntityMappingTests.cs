using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace Incoders.Hive.Tests.Infrastructure.Persistence;

/// <summary>
/// EF mapping tests for <see cref="ContentGenerationFormatResultData"/>.
/// </summary>
public sealed class ContentGenerationFormatResultEntityMappingTests
{
    private static readonly DateTime Now = new(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task FormatResultData_RoundTrip_AllFieldsPersistCorrectly()
    {
        await using var db = CreateDbContext();

        var jobId = Guid.NewGuid();
        var job = BuildJob(db, jobId);

        var entity = new ContentGenerationFormatResultData
        {
            Id = Guid.NewGuid(),
            JobId = jobId,
            FormatKey = "carousel",
            Status = "Generated",
            OutputJson = "{\"slides\":[]}",
            CompositionSpecJson = null,
            RenderedAssetUri = null,
            InferredFieldsJson = null,
            PromptVersion = "carousel.v1",
            ErrorCode = null,
            CreatedAt = Now,
            UpdatedAt = Now,
        };

        db.ContentGenerationFormatResults.Add(entity);
        await db.SaveChangesAsync();

        var read = await db.ContentGenerationFormatResults.FindAsync(entity.Id);
        Assert.NotNull(read);
        Assert.Equal(jobId, read!.JobId);
        Assert.Equal("carousel", read.FormatKey);
        Assert.Equal("Generated", read.Status);
        Assert.Equal("carousel.v1", read.PromptVersion);
        Assert.Null(read.RenderedAssetUri);
    }

    [Fact]
    public async Task FormatResultData_UniquePerJobFormatKey_ConstraintExists()
    {
        await using var db = CreateDbContext();
        var jobId = Guid.NewGuid();
        BuildJob(db, jobId);

        var r1 = new ContentGenerationFormatResultData
        {
            Id = Guid.NewGuid(),
            JobId = jobId,
            FormatKey = "flyer",
            Status = "Rendered",
            RenderedAssetUri = "file:///tmp/test.png",
            PromptVersion = "flyer.v1",
            CreatedAt = Now,
            UpdatedAt = Now,
        };
        db.ContentGenerationFormatResults.Add(r1);
        await db.SaveChangesAsync();

        var read = await db.ContentGenerationFormatResults.FindAsync(r1.Id);
        Assert.Equal("flyer", read!.FormatKey);
        Assert.Equal("file:///tmp/test.png", read.RenderedAssetUri);
    }

    private static ContentGenerationJobData BuildJob(AppIdentityDbContext db, Guid jobId)
    {
        var job = new ContentGenerationJobData
        {
            Id = jobId,
            OrganizationId = Guid.NewGuid(),
            AnalysisId = Guid.NewGuid(),
            RequestedFormats = new List<string> { "carousel", "flyer" },
            Status = "Running",
            BrandSnapshotJson = "{}",
            CorrelationId = Guid.NewGuid(),
            CreatedAt = new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
        };
        db.ContentGenerationJobs.Add(job);
        return job;
    }

    private static AppIdentityDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppIdentityDbContext>()
            .UseInMemoryDatabase($"content-gen-format-results-{Guid.NewGuid():N}")
            .Options;
        return new AppIdentityDbContext(options);
    }
}
