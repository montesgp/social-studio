using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Persistence.Postgres;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace Incoders.Hive.Tests.Infrastructure.Persistence;

public sealed class EfSingleContentAnalysisRepositoryHistoryTests
{
    [Fact]
    public async Task ListByUserAsync_PreservesEarlierPackageWhenLatestRegenerationFailed()
    {
        await using var db = CreateDbContext();
        var organizationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var analysisId = Guid.NewGuid();
        var packageJobId = Guid.NewGuid();
        var failedJobId = Guid.NewGuid();
        var now = new DateTime(2026, 7, 3, 12, 0, 0, DateTimeKind.Utc);

        db.SingleContentAnalyses.Add(new SingleContentAnalysisData
        {
            Id = analysisId,
            OrganizationId = organizationId,
            UserId = userId,
            SourceUrl = "https://www.instagram.com/reel/example",
            Status = "Analyzed",
            CreatedAtUtc = now.AddMinutes(-10),
            UpdatedAtUtc = now,
        });
        db.ContentGenerationJobs.AddRange(
            BuildJob(packageJobId, analysisId, organizationId, "Completed", now.AddMinutes(-5)),
            BuildJob(failedJobId, analysisId, organizationId, "Failed", now));
        db.GeneratedPackages.Add(new GeneratedPackageData
        {
            Id = Guid.NewGuid(),
            JobId = packageJobId,
            StorageUri = "file:///packages/success.zip",
            ManifestJson = "{}",
            CreatedAt = now.AddMinutes(-4),
        });
        await db.SaveChangesAsync();

        var repository = new EfSingleContentAnalysisRepository(db);
        var page = await repository.ListByUserAsync(organizationId, userId, 1, 20);

        var summary = Assert.Single(page.Items).LatestContentGeneration;
        Assert.NotNull(summary);
        Assert.Equal(failedJobId, summary!.JobId);
        Assert.Equal("Failed", summary.Status);
        Assert.True(summary.HasPackage);
        Assert.Equal(packageJobId, summary.PackageJobId);
    }

    private static ContentGenerationJobData BuildJob(
        Guid id, Guid analysisId, Guid organizationId, string status, DateTime createdAt) => new()
    {
        Id = id,
        AnalysisId = analysisId,
        OrganizationId = organizationId,
        RequestedFormats = ["reel-script"],
        Status = status,
        BrandSnapshotJson = "{}",
        CorrelationId = Guid.NewGuid(),
        CreatedAt = createdAt,
        UpdatedAt = createdAt,
        CompletedAt = createdAt,
    };

    private static AppIdentityDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppIdentityDbContext>()
            .UseInMemoryDatabase($"single-analysis-history-{Guid.NewGuid():N}")
            .Options;
        return new AppIdentityDbContext(options);
    }
}
