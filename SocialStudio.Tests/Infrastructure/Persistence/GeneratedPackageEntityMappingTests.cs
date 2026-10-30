using SocialStudio.Infrastructure.Identity;
using SocialStudio.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace SocialStudio.Tests.Infrastructure.Persistence;

/// <summary>
/// EF mapping tests for <see cref="GeneratedPackageData"/> → social.generated_packages.
/// </summary>
public sealed class GeneratedPackageEntityMappingTests
{
    private static readonly DateTime Now = new(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task GeneratedPackageData_RoundTrip_AllFieldsPersistCorrectly()
    {
        await using var db = CreateDbContext();
        var jobId = Guid.NewGuid();
        BuildJob(db, jobId);

        var entity = new GeneratedPackageData
        {
            Id = Guid.NewGuid(),
            JobId = jobId,
            StorageUri = "file:///App_Data/generated-packages/org/job/package.zip",
            SizeBytes = 4096,
            ManifestJson = "{\"formats\":{}}",
            ExpiresAt = null, // ephemeral v1 — no TTL
            CreatedAt = Now,
        };

        db.GeneratedPackages.Add(entity);
        await db.SaveChangesAsync();

        var read = await db.GeneratedPackages.FindAsync(entity.Id);
        Assert.NotNull(read);
        Assert.Equal(jobId, read!.JobId);
        Assert.Equal(4096, read.SizeBytes);
        Assert.Equal("{\"formats\":{}}", read.ManifestJson);
        Assert.Null(read.ExpiresAt);
    }

    [Fact]
    public async Task GeneratedPackageData_ExpiresAt_Nullable_AllowsNull()
    {
        await using var db = CreateDbContext();
        var jobId = Guid.NewGuid();
        BuildJob(db, jobId);

        var entity = new GeneratedPackageData
        {
            Id = Guid.NewGuid(),
            JobId = jobId,
            StorageUri = "file:///tmp/pkg.zip",
            SizeBytes = 1024,
            ManifestJson = "{}",
            ExpiresAt = null, // v1: no expiry
            CreatedAt = Now,
        };

        db.GeneratedPackages.Add(entity);
        await db.SaveChangesAsync();

        var read = await db.GeneratedPackages.FindAsync(entity.Id);
        Assert.Null(read!.ExpiresAt);
    }

    private static ContentGenerationJobData BuildJob(AppIdentityDbContext db, Guid jobId)
    {
        var job = new ContentGenerationJobData
        {
            Id = jobId,
            OrganizationId = Guid.NewGuid(),
            AnalysisId = Guid.NewGuid(),
            RequestedFormats = new List<string> { "carousel" },
            Status = "Completed",
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
            .UseInMemoryDatabase($"generated-packages-{Guid.NewGuid():N}")
            .Options;
        return new AppIdentityDbContext(options);
    }
}
