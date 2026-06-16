using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Persistence.Postgres;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace Incoders.Hive.Tests.Infrastructure.Persistence;

public sealed class SocialClientHardDeleteRepositoryTests
{
    private static readonly DateTime Now = new(2026, 6, 8, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HardDeleteAsync_RemovesClientScopedDataAndKeepsOtherClientsData()
    {
        await using var db = CreateDbContext();
        var repository = new EfSocialClientRepository(db);
        var organizationId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var otherClientId = Guid.NewGuid();
        var clientOnlyAnalysisId = Guid.NewGuid();
        var sharedAnalysisId = Guid.NewGuid();
        var clientOnlyJobId = Guid.NewGuid();
        var sharedClientJobId = Guid.NewGuid();
        var sharedOtherClientJobId = Guid.NewGuid();
        var clientOnlyFormatResultId = Guid.NewGuid();
        var sharedClientFormatResultId = Guid.NewGuid();
        var sharedOtherFormatResultId = Guid.NewGuid();
        var contentPlanId = Guid.NewGuid();

        db.SocialClients.AddRange(
            ClientRow(clientId, organizationId, "Garbage client"),
            ClientRow(otherClientId, organizationId, "Real client"));
        db.ClientReferentAccounts.Add(AccountRow<ClientReferentAccountData>(clientId, organizationId));
        db.ClientCompetitorAccounts.Add(AccountRow<ClientCompetitorAccountData>(clientId, organizationId));
        db.ClientOwnAccounts.Add(AccountRow<ClientOwnAccountData>(clientId, organizationId));
        db.ClientBrandManuals.Add(new ClientBrandManualData
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            OrganizationId = organizationId,
            Title = "Manual",
            IsActive = true,
            CreatedAtUtc = Now,
            UpdatedAtUtc = Now,
        });
        db.GeneralStrategies.Add(new GeneralStrategyData
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            OrganizationId = organizationId,
            ContentPillars = ["education"],
            CreatedAtUtc = Now,
            UpdatedAtUtc = Now,
        });
        db.ContentPlans.Add(new ContentPlanData
        {
            Id = contentPlanId,
            ClientId = clientId,
            OrganizationId = organizationId,
            PeriodKind = 0,
            Year = 2026,
            Month = 6,
            ObjectiveText = "Clean garbage data",
            CreatedAtUtc = Now,
            UpdatedAtUtc = Now,
        });
        db.ContentPlanSlots.Add(new ContentPlanSlotData
        {
            Id = Guid.NewGuid(),
            ContentPlanId = contentPlanId,
            Day = 1,
            ContentFormat = "story",
            CreatedAtUtc = Now,
            UpdatedAtUtc = Now,
        });

        db.SingleContentAnalyses.AddRange(
            AnalysisRow(clientOnlyAnalysisId, organizationId, clientId),
            AnalysisRow(sharedAnalysisId, organizationId, clientId));
        db.ProfileAnalyses.AddRange(
            ProfileRow(Guid.NewGuid(), organizationId, clientId),
            ProfileRow(Guid.NewGuid(), organizationId, otherClientId));
        db.UsageEvents.AddRange(
            UsageRow(organizationId, clientId),
            UsageRow(organizationId, otherClientId));

        db.ContentGenerationJobs.AddRange(
            JobRow(clientOnlyJobId, organizationId, clientId, clientOnlyAnalysisId),
            JobRow(sharedClientJobId, organizationId, clientId, sharedAnalysisId),
            JobRow(sharedOtherClientJobId, organizationId, otherClientId, sharedAnalysisId));
        db.ContentGenerationFormatResults.AddRange(
            FormatResultRow(clientOnlyFormatResultId, clientOnlyJobId),
            FormatResultRow(sharedClientFormatResultId, sharedClientJobId),
            FormatResultRow(sharedOtherFormatResultId, sharedOtherClientJobId));
        db.GeneratedPackages.AddRange(
            PackageRow(Guid.NewGuid(), clientOnlyJobId, "file:///client-only.zip"),
            PackageRow(Guid.NewGuid(), sharedClientJobId, "file:///shared-client.zip"),
            PackageRow(Guid.NewGuid(), sharedOtherClientJobId, "file:///shared-other.zip"));
        db.ContentGenerationFormatAssets.AddRange(
            AssetRow(Guid.NewGuid(), clientOnlyFormatResultId, "org/client/client-only.png"),
            AssetRow(Guid.NewGuid(), sharedClientFormatResultId, "org/client/shared-client.png"),
            AssetRow(Guid.NewGuid(), sharedOtherFormatResultId, "org/other/shared-other.png"));
        await db.SaveChangesAsync();

        var result = await repository.HardDeleteAsync(clientId, CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.NotNull(result);
        Assert.Equal(clientId, result!.ClientId);
        Assert.Equal(
            ["file:///client-only.zip", "file:///shared-client.zip"],
            result.GeneratedPackageStorageUris.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(
            ["org/client/client-only.png", "org/client/shared-client.png"],
            result.GeneratedAssetStoragePaths.Order(StringComparer.Ordinal).ToArray());

        Assert.False(await db.SocialClients.AnyAsync(x => x.Id == clientId));
        Assert.True(await db.SocialClients.AnyAsync(x => x.Id == otherClientId));
        Assert.Empty(await db.ClientReferentAccounts.Where(x => x.ClientId == clientId).ToListAsync());
        Assert.Empty(await db.ClientCompetitorAccounts.Where(x => x.ClientId == clientId).ToListAsync());
        Assert.Empty(await db.ClientOwnAccounts.Where(x => x.ClientId == clientId).ToListAsync());
        Assert.Empty(await db.ClientBrandManuals.Where(x => x.ClientId == clientId).ToListAsync());
        Assert.Empty(await db.GeneralStrategies.Where(x => x.ClientId == clientId).ToListAsync());
        Assert.Empty(await db.ContentPlans.Where(x => x.ClientId == clientId).ToListAsync());
        Assert.Empty(await db.ContentPlanSlots.Where(x => x.ContentPlanId == contentPlanId).ToListAsync());
        Assert.Empty(await db.ProfileAnalyses.Where(x => x.ClientId == clientId).ToListAsync());
        Assert.Empty(await db.UsageEvents.Where(x => x.ClientId == clientId).ToListAsync());
        Assert.False(await db.SingleContentAnalyses.AnyAsync(x => x.Id == clientOnlyAnalysisId));

        var sharedAnalysis = await db.SingleContentAnalyses.SingleAsync(x => x.Id == sharedAnalysisId);
        Assert.Null(sharedAnalysis.ClientId);
        Assert.False(await db.ContentGenerationJobs.AnyAsync(x => x.Id == clientOnlyJobId));
        Assert.False(await db.ContentGenerationJobs.AnyAsync(x => x.Id == sharedClientJobId));
        Assert.True(await db.ContentGenerationJobs.AnyAsync(x => x.Id == sharedOtherClientJobId));
        Assert.False(await db.GeneratedPackages.AnyAsync(x => x.StorageUri == "file:///client-only.zip"));
        Assert.False(await db.GeneratedPackages.AnyAsync(x => x.StorageUri == "file:///shared-client.zip"));
        Assert.True(await db.GeneratedPackages.AnyAsync(x => x.StorageUri == "file:///shared-other.zip"));
        Assert.False(await db.ContentGenerationFormatAssets.AnyAsync(x => x.StoragePath == "org/client/client-only.png"));
        Assert.False(await db.ContentGenerationFormatAssets.AnyAsync(x => x.StoragePath == "org/client/shared-client.png"));
        Assert.True(await db.ContentGenerationFormatAssets.AnyAsync(x => x.StoragePath == "org/other/shared-other.png"));
    }

    [Fact]
    public async Task HardDeleteAsync_WhenClientDoesNotExist_ReturnsNull()
    {
        await using var db = CreateDbContext();
        var repository = new EfSocialClientRepository(db);

        var result = await repository.HardDeleteAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(result);
    }

    private static SocialClientData ClientRow(Guid clientId, Guid organizationId, string name) => new()
    {
        Id = clientId,
        OrganizationId = organizationId,
        Name = name,
        IsActive = true,
        CreatedAtUtc = Now,
        UpdatedAtUtc = Now,
    };

    private static T AccountRow<T>(Guid clientId, Guid organizationId)
        where T : class, new()
    {
        var row = new T();
        typeof(T).GetProperty("Id")!.SetValue(row, Guid.NewGuid());
        typeof(T).GetProperty("ClientId")!.SetValue(row, clientId);
        typeof(T).GetProperty("OrganizationId")!.SetValue(row, organizationId);
        typeof(T).GetProperty("SocialNetworkId")!.SetValue(row, Guid.NewGuid());
        typeof(T).GetProperty("ProfileUrl")!.SetValue(row, "https://example.com/profile");
        typeof(T).GetProperty("IsActive")!.SetValue(row, true);
        typeof(T).GetProperty("CreatedAtUtc")!.SetValue(row, Now);
        typeof(T).GetProperty("UpdatedAtUtc")!.SetValue(row, Now);
        return row;
    }

    private static SingleContentAnalysisData AnalysisRow(
        Guid id,
        Guid organizationId,
        Guid? clientId) => new()
    {
        Id = id,
        OrganizationId = organizationId,
        ClientId = clientId,
        UserId = Guid.NewGuid(),
        SourceUrl = "https://example.com/video",
        Status = "Analyzed",
        CreatedAtUtc = Now,
        UpdatedAtUtc = Now,
    };

    private static ProfileAnalysisData ProfileRow(Guid id, Guid organizationId, Guid clientId) => new()
    {
        Id = id,
        OrganizationId = organizationId,
        ClientId = clientId,
        ProfileUrlOrHandle = "@client",
        Status = "Analyzed",
        CreatedAtUtc = Now,
        UpdatedAtUtc = Now,
    };

    private static UsageEventData UsageRow(Guid organizationId, Guid clientId) => new()
    {
        OrganizationId = organizationId,
        ClientId = clientId,
        WorkflowKey = "single-analyzer",
        PipelineStepKey = "analysis",
        ProviderKey = "test",
        RequestTimestamp = Now,
        CompletedTimestamp = Now,
        DurationMs = 1,
        Status = "Succeeded",
        CorrelationId = Guid.NewGuid(),
        CreatedAtUtc = Now,
    };

    private static ContentGenerationJobData JobRow(
        Guid id,
        Guid organizationId,
        Guid clientId,
        Guid analysisId) => new()
    {
        Id = id,
        OrganizationId = organizationId,
        ClientId = clientId,
        AnalysisId = analysisId,
        RequestedFormats = ["story"],
        Status = "Completed",
        BrandSnapshotJson = "{}",
        CorrelationId = Guid.NewGuid(),
        CreatedAt = Now,
        UpdatedAt = Now,
    };

    private static ContentGenerationFormatResultData FormatResultRow(Guid id, Guid jobId) => new()
    {
        Id = id,
        JobId = jobId,
        FormatKey = "story",
        Status = "Rendered",
        CreatedAt = Now,
        UpdatedAt = Now,
    };

    private static GeneratedPackageData PackageRow(Guid id, Guid jobId, string uri) => new()
    {
        Id = id,
        JobId = jobId,
        StorageUri = uri,
        ManifestJson = "{}",
        SizeBytes = 10,
        CreatedAt = Now,
    };

    private static ContentGenerationFormatAssetData AssetRow(Guid id, Guid resultId, string storagePath) => new()
    {
        Id = id,
        FormatResultId = resultId,
        AssetIndex = 0,
        StoragePath = storagePath,
        Filename = Path.GetFileName(storagePath),
        MimeType = "image/png",
        SizeBytes = 10,
        ContentHash = "hash",
        CreatedAt = Now,
        UpdatedAt = Now,
    };

    private static AppIdentityDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppIdentityDbContext>()
            .UseInMemoryDatabase($"social-client-hard-delete-{Guid.NewGuid():N}")
            .Options;

        return new AppIdentityDbContext(options);
    }
}
