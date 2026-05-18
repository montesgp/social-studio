using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Domain.Social.SingleAnalyzer;
using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Persistence.Postgres;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace Incoders.Hive.Tests.Infrastructure.Persistence;

public sealed class SingleAnalyzerRoutingPersistenceTests
{
    private static readonly DateTime Now = new(2026, 5, 18, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task RoutingPolicyRepository_ReturnsEnabledCandidatesInPriorityOrder()
    {
        await using var db = CreateDbContext();
        var organizationId = Guid.NewGuid();
        var apifyId = Guid.NewGuid();
        var data365Id = Guid.NewGuid();
        db.IntegrationProviders.AddRange(
            Provider(apifyId, "apify"),
            Provider(data365Id, "data365"));
        await db.SaveChangesAsync();

        var repo = new EfIntegrationRoutingPolicyRepository(db);
        var policy = IntegrationRoutingPolicy.Create(
            Guid.NewGuid(),
            organizationId,
            "single-analyzer",
            "social-extraction",
            "tiktok",
            isEnabled: true,
            [
                Candidate(apifyId, "apify", priority: 20, allowManagedFallback: true),
                Candidate(data365Id, "data365", priority: 10, allowManagedFallback: false)
            ],
            Now).Value;

        await repo.UpsertAsync(policy, CancellationToken.None);
        await db.SaveChangesAsync();

        var loaded = await repo.GetEnabledPolicyAsync(
            organizationId,
            "single-analyzer",
            "social-extraction",
            "tiktok",
            CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(["data365", "apify"], loaded!.GetEnabledCandidates().Select(x => x.ProviderKey).ToArray());
    }

    [Fact]
    public async Task RoutingPolicyRepository_MapsManagedFallbackFlagAndConnectionMode()
    {
        await using var db = CreateDbContext();
        var organizationId = Guid.NewGuid();
        var openAiId = Guid.NewGuid();
        db.IntegrationProviders.Add(Provider(openAiId, "openai"));
        await db.SaveChangesAsync();

        var repo = new EfIntegrationRoutingPolicyRepository(db);
        var policy = IntegrationRoutingPolicy.Create(
            Guid.NewGuid(),
            organizationId,
            "single-analyzer",
            "ai-analysis",
            platformKey: null,
            isEnabled: true,
            [IntegrationRouteCandidate.Create(
                openAiId,
                "openai",
                IntegrationConnectionMode.ManagedByIncoders,
                priority: 10,
                allowManagedFallback: true,
                maxAttempts: 2,
                timeoutMs: 4_000,
                selectedModel: "gpt-4.1-mini",
                isEnabled: true).Value],
            Now).Value;

        await repo.UpsertAsync(policy, CancellationToken.None);
        await db.SaveChangesAsync();

        var loaded = await repo.GetEnabledPolicyAsync(
            organizationId,
            "single-analyzer",
            "ai-analysis",
            platformKey: null,
            CancellationToken.None);
        var candidate = Assert.Single(loaded!.GetEnabledCandidates());

        Assert.True(candidate.AllowManagedFallback);
        Assert.True(candidate.IsManagedFallback);
        Assert.Equal(IntegrationConnectionMode.ManagedByIncoders, candidate.ConnectionMode);
        Assert.Equal("gpt-4.1-mini", candidate.SelectedModel);
    }

    [Fact]
    public async Task SingleContentAnalysisRepository_PersistsStatusTransitions()
    {
        await using var db = CreateDbContext();
        var repo = new EfSingleContentAnalysisRepository(db);
        var organizationId = Guid.NewGuid();
        var analysisId = Guid.NewGuid();

        await repo.AddAsync(new SingleContentAnalysisRecord(
            analysisId,
            organizationId,
            ClientId: null,
            UserId: Guid.NewGuid(),
            SourceUrl: "https://www.tiktok.com/@brand/video/123",
            NormalizedUrl: null,
            SourcePlatformKey: null,
            SingleContentAnalysisStatus.Requested,
            ContentSnapshotJson: null,
            AnalysisJson: null,
            ProviderDiagnosticsJson: null,
            FailureCode: null,
            FailureMessageKey: null,
            CreatedAtUtc: Now,
            UpdatedAtUtc: Now,
            CompletedAtUtc: null),
            CancellationToken.None);
        await db.SaveChangesAsync();

        await repo.UpdateStatusAsync(
            analysisId,
            organizationId,
            SingleContentAnalysisStatus.ResolvingContent,
            Now.AddMinutes(1),
            CancellationToken.None);
        await db.SaveChangesAsync();

        var loaded = await repo.GetByIdAsync(analysisId, organizationId, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(SingleContentAnalysisStatus.ResolvingContent, loaded!.Status);
        Assert.Equal(Now.AddMinutes(1), loaded.UpdatedAtUtc);
    }

    [Fact]
    public async Task IntegrationExecutionAttemptRepository_PersistsAttemptsWithFailureDiagnostics()
    {
        await using var db = CreateDbContext();
        var analysisRepo = new EfSingleContentAnalysisRepository(db);
        var attemptRepo = new EfIntegrationExecutionAttemptRepository(db);
        var organizationId = Guid.NewGuid();
        var analysisId = Guid.NewGuid();

        await analysisRepo.AddAsync(new SingleContentAnalysisRecord(
            analysisId,
            organizationId,
            ClientId: null,
            UserId: Guid.NewGuid(),
            SourceUrl: "https://x.com/incoders/status/123",
            NormalizedUrl: "https://x.com/incoders/status/123",
            SourcePlatformKey: "x",
            SingleContentAnalysisStatus.Analyzing,
            ContentSnapshotJson: "{}",
            AnalysisJson: null,
            ProviderDiagnosticsJson: null,
            FailureCode: null,
            FailureMessageKey: null,
            CreatedAtUtc: Now,
            UpdatedAtUtc: Now,
            CompletedAtUtc: null),
            CancellationToken.None);

        await attemptRepo.AddAsync(new IntegrationExecutionAttemptRecord(
            Guid.NewGuid(),
            analysisId,
            "ai-analysis",
            "openai",
            IntegrationExecutionAttemptStatus.Failed,
            AttemptNumber: 1,
            FailureCode: "timeout",
            RetryCount: 2,
            ElapsedMs: 1_250,
            DiagnosticsJson: "{\"fallback\":true}",
            StartedAtUtc: Now,
            CompletedAtUtc: Now.AddMilliseconds(1250)),
            CancellationToken.None);
        await db.SaveChangesAsync();

        var attempts = await attemptRepo.ListByAnalysisIdAsync(analysisId, CancellationToken.None);
        var attempt = Assert.Single(attempts);

        Assert.Equal("ai-analysis", attempt.CapabilityKey);
        Assert.Equal("openai", attempt.ProviderKey);
        Assert.Equal(IntegrationExecutionAttemptStatus.Failed, attempt.Status);
        Assert.Equal("timeout", attempt.FailureCode);
        Assert.Equal(2, attempt.RetryCount);
        Assert.Equal(1_250, attempt.ElapsedMs);
    }

    [Fact]
    public void RoutingPolicyModel_EnforcesUniqueGlobalAndPlatformSpecificPolicies()
    {
        using var db = CreateDbContext();
        var entity = db.Model.FindEntityType(typeof(OrganizationIntegrationRoutingPolicyData));

        Assert.NotNull(entity);
        Assert.Contains(entity!.GetIndexes(), index =>
            index.IsUnique
            && index.GetDatabaseName() == "ix_org_routing_policies_org_flow_capability_global"
            && index.GetFilter() == "platform_key IS NULL"
            && index.Properties.Select(property => property.Name).SequenceEqual(
                [
                    nameof(OrganizationIntegrationRoutingPolicyData.OrganizationId),
                    nameof(OrganizationIntegrationRoutingPolicyData.FlowKey),
                    nameof(OrganizationIntegrationRoutingPolicyData.CapabilityKey),
                ]));
        Assert.Contains(entity.GetIndexes(), index =>
            index.IsUnique
            && index.GetDatabaseName() == "ix_org_routing_policies_org_flow_capability_platform"
            && index.GetFilter() == "platform_key IS NOT NULL"
            && index.Properties.Select(property => property.Name).SequenceEqual(
                [
                    nameof(OrganizationIntegrationRoutingPolicyData.OrganizationId),
                    nameof(OrganizationIntegrationRoutingPolicyData.FlowKey),
                    nameof(OrganizationIntegrationRoutingPolicyData.CapabilityKey),
                    nameof(OrganizationIntegrationRoutingPolicyData.PlatformKey),
                ]));
    }

    [Fact]
    public void ProviderCapabilityModel_EnforcesUniqueGlobalAndPlatformSpecificCapabilities()
    {
        using var db = CreateDbContext();
        var entity = db.Model.FindEntityType(typeof(IntegrationProviderCapabilityData));

        Assert.NotNull(entity);
        Assert.Contains(entity!.GetIndexes(), index =>
            index.IsUnique
            && index.GetDatabaseName() == "ix_integration_provider_capabilities_provider_capability_global"
            && index.GetFilter() == "platform_key IS NULL"
            && index.Properties.Select(property => property.Name).SequenceEqual(
                [
                    nameof(IntegrationProviderCapabilityData.ProviderId),
                    nameof(IntegrationProviderCapabilityData.CapabilityKey),
                ]));
        Assert.Contains(entity.GetIndexes(), index =>
            index.IsUnique
            && index.GetDatabaseName() == "ix_integration_provider_capabilities_provider_capability_platform"
            && index.GetFilter() == "platform_key IS NOT NULL"
            && index.Properties.Select(property => property.Name).SequenceEqual(
                [
                    nameof(IntegrationProviderCapabilityData.ProviderId),
                    nameof(IntegrationProviderCapabilityData.CapabilityKey),
                    nameof(IntegrationProviderCapabilityData.PlatformKey),
                ]));
    }

    private static AppIdentityDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppIdentityDbContext>()
            .UseInMemoryDatabase($"single-analyzer-routing-{Guid.NewGuid():N}")
            .Options;
        return new AppIdentityDbContext(options);
    }

    private static IntegrationProviderData Provider(Guid id, string key) => new()
    {
        Id = id,
        Key = key,
        DisplayName = key,
        Category = IntegrationProviderCategory.DataExtraction.ToString(),
        IconKey = key,
        SupportsBringYourOwnKey = true,
        SupportsManagedByIncoders = true,
        SupportsSystemDefault = false,
        SystemDefaultReadOnly = false,
        CredentialFieldsJson = "[]",
        IsActive = true,
        CreatedAtUtc = Now,
        UpdatedAtUtc = Now,
    };

    private static IntegrationRouteCandidate Candidate(
        Guid providerId,
        string providerKey,
        int priority,
        bool allowManagedFallback) =>
        IntegrationRouteCandidate.Create(
            providerId,
            providerKey,
            IntegrationConnectionMode.BringYourOwnKey,
            priority,
            allowManagedFallback,
            maxAttempts: 1,
            timeoutMs: 2_000,
            selectedModel: null,
            isEnabled: true).Value;
}
