using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Persistence.Postgres;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;
using Incoders.Hive.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Incoders.Hive.Tests.Infrastructure.Persistence;

public sealed class OrganizationHardDeleteRepositoryTests
{
    private static readonly DateTime Now = new(2026, 6, 8, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HardDeleteAsync_RemovesOrganizationScopedDataUsersAndClientChains()
    {
        await using var db = CreateDbContext();
        var repository = new EfTenantHardDeleteRepository(db);
        var organizationId = Guid.NewGuid();
        var otherOrganizationId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();
        var memberUserId = Guid.NewGuid();
        var sysadminUserId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var secondClientId = Guid.NewGuid();
        var otherClientId = Guid.NewGuid();
        var analysisId = Guid.NewGuid();
        var orgOnlyAnalysisId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var orgOnlyJobId = Guid.NewGuid();
        var otherJobId = Guid.NewGuid();
        var formatResultId = Guid.NewGuid();
        var orgOnlyFormatResultId = Guid.NewGuid();
        var otherFormatResultId = Guid.NewGuid();
        var contentPlanId = Guid.NewGuid();
        var providerId = Guid.NewGuid();
        var integrationId = Guid.NewGuid();
        var moduleId = Guid.NewGuid();
        var sysadminRoleId = Guid.NewGuid();

        db.Tenants.AddRange(
            TenantRow(organizationId, ownerUserId, "Garbage org"),
            TenantRow(otherOrganizationId, otherUserId, "Real org"));
        db.Users.AddRange(
            UserRow(ownerUserId, "owner@example.test"),
            UserRow(memberUserId, "member@example.test"),
            UserRow(sysadminUserId, "sysadmin@example.test"),
            UserRow(otherUserId, "other@example.test"));
        db.Roles.Add(new ApplicationRole(IdentityRoles.Sysadmin)
        {
            Id = sysadminRoleId,
            NormalizedName = IdentityRoles.Sysadmin.ToUpperInvariant(),
        });
        db.UserRoles.Add(new IdentityUserRole<Guid>
        {
            UserId = sysadminUserId,
            RoleId = sysadminRoleId,
        });
        db.TenantMemberships.AddRange(
            MembershipRow(organizationId, ownerUserId),
            MembershipRow(organizationId, memberUserId),
            // Defensive drift case: sysadmins should not have org assignments, but must never be deleted.
            MembershipRow(organizationId, sysadminUserId),
            MembershipRow(otherOrganizationId, otherUserId));
        db.UserPreferences.AddRange(
            UserPreferenceRow(ownerUserId),
            UserPreferenceRow(memberUserId),
            UserPreferenceRow(sysadminUserId),
            UserPreferenceRow(otherUserId));
        db.SystemSettings.AddRange(
            SystemSettingRow(tenantId: organizationId, userId: null, key: "org-theme"),
            SystemSettingRow(tenantId: null, userId: ownerUserId, key: "owner-theme"),
            SystemSettingRow(tenantId: organizationId, userId: sysadminUserId, key: "sysadmin-org-scoped"),
            SystemSettingRow(tenantId: null, userId: sysadminUserId, key: "sysadmin-global"),
            SystemSettingRow(tenantId: otherOrganizationId, userId: null, key: "other-org-theme"),
            SystemSettingRow(tenantId: null, userId: otherUserId, key: "other-user-theme"));

        db.SystemModules.Add(SystemModuleRow(moduleId));
        db.OrganizationModules.AddRange(
            OrganizationModuleRow(organizationId, moduleId),
            OrganizationModuleRow(otherOrganizationId, moduleId));
        db.OrganizationUserModules.AddRange(
            OrganizationUserModuleRow(organizationId, memberUserId, moduleId),
            OrganizationUserModuleRow(otherOrganizationId, otherUserId, moduleId));

        db.IntegrationProviders.Add(ProviderRow(providerId));
        db.OrganizationIntegrations.AddRange(
            OrganizationIntegrationRow(integrationId, organizationId, providerId),
            OrganizationIntegrationRow(Guid.NewGuid(), otherOrganizationId, providerId));
        db.OrganizationIntegrationCredentials.Add(CredentialRow(integrationId));
        db.CanvaUserTokens.Add(new CanvaUserTokenData
        {
            Id = Guid.NewGuid(),
            UserId = memberUserId,
            OrganizationIntegrationId = integrationId,
            EncryptedAccessToken = "access",
            EncryptedRefreshToken = "refresh",
            AccessTokenExpiresAtUtc = Now.AddHours(1),
            Scopes = "design:content:write",
            CreatedAtUtc = Now,
            UpdatedAtUtc = Now,
        });

        db.SocialClients.AddRange(
            ClientRow(clientId, organizationId, "Client 1"),
            ClientRow(secondClientId, organizationId, "Client 2"),
            ClientRow(otherClientId, otherOrganizationId, "Other client"));
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
            ToneKeywords = ["clear"],
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
            ContentFormat = "flyer",
            CreatedAtUtc = Now,
            UpdatedAtUtc = Now,
        });

        db.SingleContentAnalyses.AddRange(
            AnalysisRow(analysisId, organizationId, clientId, memberUserId),
            AnalysisRow(orgOnlyAnalysisId, organizationId, null, memberUserId),
            AnalysisRow(Guid.NewGuid(), otherOrganizationId, otherClientId, otherUserId));
        db.IntegrationExecutionAttempts.AddRange(
            AttemptRow(analysisId),
            AttemptRow(orgOnlyAnalysisId));
        db.ProfileAnalyses.AddRange(
            ProfileRow(Guid.NewGuid(), organizationId, clientId),
            ProfileRow(Guid.NewGuid(), otherOrganizationId, otherClientId));
        db.UsageEvents.AddRange(
            UsageRow(organizationId, clientId),
            UsageRow(organizationId, null),
            UsageRow(otherOrganizationId, otherClientId));

        db.ContentGenerationJobs.AddRange(
            JobRow(jobId, organizationId, clientId, analysisId),
            JobRow(orgOnlyJobId, organizationId, null, orgOnlyAnalysisId),
            JobRow(otherJobId, otherOrganizationId, otherClientId, Guid.NewGuid()));
        db.ContentGenerationFormatResults.AddRange(
            FormatResultRow(formatResultId, jobId),
            FormatResultRow(orgOnlyFormatResultId, orgOnlyJobId),
            FormatResultRow(otherFormatResultId, otherJobId));
        db.GeneratedPackages.AddRange(
            PackageRow(Guid.NewGuid(), jobId, "file:///client-package.zip"),
            PackageRow(Guid.NewGuid(), orgOnlyJobId, "file:///org-package.zip"),
            PackageRow(Guid.NewGuid(), otherJobId, "file:///other-package.zip"));
        db.ContentGenerationFormatAssets.AddRange(
            AssetRow(Guid.NewGuid(), formatResultId, $"{organizationId}/{clientId}/analysis/flyer.png"),
            AssetRow(Guid.NewGuid(), orgOnlyFormatResultId, $"{organizationId}/orphan/generated.png"),
            AssetRow(Guid.NewGuid(), otherFormatResultId, $"{otherOrganizationId}/{otherClientId}/analysis/flyer.png"));
        await db.SaveChangesAsync();

        var result = await repository.HardDeleteAsync(organizationId, CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.NotNull(result);
        Assert.Equal(organizationId, result!.OrganizationId);
        Assert.Equal("Garbage org", result.OrganizationName);
        Assert.Equal(new[] { clientId, secondClientId }.Order().ToArray(), result.ClientIds.Order().ToArray());
        Assert.Equal(new[] { memberUserId, ownerUserId }.Order().ToArray(), result.UserIds.Order().ToArray());
        Assert.Equal(
            ["file:///client-package.zip", "file:///org-package.zip"],
            result.GeneratedPackageStorageUris.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(
            [$"{organizationId}/{clientId}/analysis/flyer.png", $"{organizationId}/orphan/generated.png"],
            result.GeneratedAssetStoragePaths.Order(StringComparer.Ordinal).ToArray());

        Assert.False(await db.Tenants.AnyAsync(x => x.Id == organizationId));
        Assert.True(await db.Tenants.AnyAsync(x => x.Id == otherOrganizationId));
        Assert.False(await db.Users.AnyAsync(x => x.Id == ownerUserId));
        Assert.False(await db.Users.AnyAsync(x => x.Id == memberUserId));
        Assert.True(await db.Users.AnyAsync(x => x.Id == sysadminUserId));
        Assert.True(await db.Users.AnyAsync(x => x.Id == otherUserId));
        Assert.False(await db.UserPreferences.AnyAsync(x => x.UserId == ownerUserId || x.UserId == memberUserId));
        Assert.True(await db.UserPreferences.AnyAsync(x => x.UserId == sysadminUserId));
        Assert.False(await db.TenantMemberships.AnyAsync(x => x.TenantId == organizationId));
        Assert.True(await db.TenantMemberships.AnyAsync(x => x.TenantId == otherOrganizationId));
        Assert.False(await db.OrganizationModules.AnyAsync(x => x.OrganizationId == organizationId));
        Assert.False(await db.OrganizationUserModules.AnyAsync(x => x.OrganizationId == organizationId));
        Assert.True(await db.OrganizationModules.AnyAsync(x => x.OrganizationId == otherOrganizationId));
        Assert.False(await db.SystemSettings.AnyAsync(x => x.TenantId == organizationId));
        Assert.False(await db.SystemSettings.AnyAsync(x => x.UserId == ownerUserId));
        Assert.True(await db.SystemSettings.AnyAsync(x => x.UserId == sysadminUserId && x.TenantId == null));
        Assert.True(await db.SystemSettings.AnyAsync(x => x.TenantId == otherOrganizationId));

        Assert.False(await db.OrganizationIntegrations.AnyAsync(x => x.OrganizationId == organizationId));
        Assert.False(await db.OrganizationIntegrationCredentials.AnyAsync(x => x.OrganizationIntegrationId == integrationId));
        Assert.False(await db.CanvaUserTokens.AnyAsync(x => x.OrganizationIntegrationId == integrationId));
        Assert.True(await db.OrganizationIntegrations.AnyAsync(x => x.OrganizationId == otherOrganizationId));

        Assert.False(await db.SocialClients.AnyAsync(x => x.OrganizationId == organizationId));
        Assert.True(await db.SocialClients.AnyAsync(x => x.OrganizationId == otherOrganizationId));
        Assert.Empty(await db.ClientReferentAccounts.Where(x => x.OrganizationId == organizationId).ToListAsync());
        Assert.Empty(await db.ClientCompetitorAccounts.Where(x => x.OrganizationId == organizationId).ToListAsync());
        Assert.Empty(await db.ClientOwnAccounts.Where(x => x.OrganizationId == organizationId).ToListAsync());
        Assert.Empty(await db.ClientBrandManuals.Where(x => x.OrganizationId == organizationId).ToListAsync());
        Assert.Empty(await db.GeneralStrategies.Where(x => x.OrganizationId == organizationId).ToListAsync());
        Assert.Empty(await db.ContentPlans.Where(x => x.OrganizationId == organizationId).ToListAsync());
        Assert.Empty(await db.ContentPlanSlots.Where(x => x.ContentPlanId == contentPlanId).ToListAsync());
        Assert.Empty(await db.SingleContentAnalyses.Where(x => x.OrganizationId == organizationId).ToListAsync());
        Assert.Empty(await db.IntegrationExecutionAttempts.Where(x => x.AnalysisId == analysisId || x.AnalysisId == orgOnlyAnalysisId).ToListAsync());
        Assert.Empty(await db.ProfileAnalyses.Where(x => x.OrganizationId == organizationId).ToListAsync());
        Assert.Empty(await db.UsageEvents.Where(x => x.OrganizationId == organizationId).ToListAsync());
        Assert.Empty(await db.ContentGenerationJobs.Where(x => x.OrganizationId == organizationId).ToListAsync());
        Assert.False(await db.GeneratedPackages.AnyAsync(x => x.StorageUri == "file:///client-package.zip"));
        Assert.False(await db.GeneratedPackages.AnyAsync(x => x.StorageUri == "file:///org-package.zip"));
        Assert.True(await db.GeneratedPackages.AnyAsync(x => x.StorageUri == "file:///other-package.zip"));
        Assert.False(await db.ContentGenerationFormatAssets.AnyAsync(x => x.StoragePath == $"{organizationId}/{clientId}/analysis/flyer.png"));
        Assert.True(await db.ContentGenerationFormatAssets.AnyAsync(x => x.StoragePath == $"{otherOrganizationId}/{otherClientId}/analysis/flyer.png"));
    }

    [Fact]
    public async Task HardDeleteAsync_WhenOrganizationDoesNotExist_ReturnsNull()
    {
        await using var db = CreateDbContext();
        var repository = new EfTenantHardDeleteRepository(db);

        var result = await repository.HardDeleteAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(result);
    }

    private static TenantData TenantRow(Guid organizationId, Guid ownerUserId, string name) => new()
    {
        Id = organizationId,
        Name = name,
        OwnerUserId = ownerUserId,
        IsActive = true,
        CreatedAtUtc = Now,
        UpdatedAtUtc = Now,
    };

    private static ApplicationUser UserRow(Guid userId, string email) => new()
    {
        Id = userId,
        UserName = email,
        NormalizedUserName = email.ToUpperInvariant(),
        Email = email,
        NormalizedEmail = email.ToUpperInvariant(),
        EmailConfirmed = true,
        Status = UserStatus.Active,
    };

    private static TenantMembershipData MembershipRow(Guid organizationId, Guid userId) => new()
    {
        TenantId = organizationId,
        UserId = userId,
        Role = "Member",
        CreatedAtUtc = Now,
    };

    private static UserPreferenceData UserPreferenceRow(Guid userId) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        ThemeKey = "system",
        CreatedAtUtc = Now,
        UpdatedAtUtc = Now,
    };

    private static SystemSettingData SystemSettingRow(Guid? tenantId, Guid? userId, string key) => new()
    {
        Id = Guid.NewGuid(),
        Key = key,
        Value = "{}",
        Scope = tenantId.HasValue ? "organization" : userId.HasValue ? "user" : "global",
        TenantId = tenantId,
        UserId = userId,
        CreatedAtUtc = Now,
        UpdatedAtUtc = Now,
    };

    private static SystemModuleData SystemModuleRow(Guid moduleId) => new()
    {
        Id = moduleId,
        ModuleKey = "social",
        DisplayName = "Social",
        Description = "Social workflows",
        ShortLabel = "Social",
        IsActive = true,
        CreatedAtUtc = Now,
        UpdatedAtUtc = Now,
    };

    private static OrganizationModuleData OrganizationModuleRow(Guid organizationId, Guid moduleId) => new()
    {
        OrganizationId = organizationId,
        ModuleId = moduleId,
        SortOrder = 0,
        CreatedAtUtc = Now,
        UpdatedAtUtc = Now,
    };

    private static OrganizationUserModuleData OrganizationUserModuleRow(
        Guid organizationId,
        Guid userId,
        Guid moduleId) => new()
    {
        OrganizationId = organizationId,
        UserId = userId,
        ModuleId = moduleId,
        SortOrder = 0,
        CreatedAtUtc = Now,
        UpdatedAtUtc = Now,
    };

    private static IntegrationProviderData ProviderRow(Guid providerId) => new()
    {
        Id = providerId,
        Key = "canva",
        DisplayName = "Canva",
        Category = "design",
        IconKey = "canva",
        SupportsBringYourOwnKey = true,
        SupportsManagedByIncoders = false,
        SupportsSystemDefault = false,
        CredentialFieldsJson = "[]",
        AvailableModelsJson = "[]",
        IsActive = true,
        CreatedAtUtc = Now,
        UpdatedAtUtc = Now,
    };

    private static OrganizationIntegrationData OrganizationIntegrationRow(
        Guid integrationId,
        Guid organizationId,
        Guid providerId) => new()
    {
        Id = integrationId,
        OrganizationId = organizationId,
        ProviderId = providerId,
        ConnectionMode = "BringYourOwnKey",
        Status = "Connected",
        CreatedAtUtc = Now,
        UpdatedAtUtc = Now,
    };

    private static OrganizationIntegrationCredentialData CredentialRow(Guid integrationId) => new()
    {
        Id = Guid.NewGuid(),
        OrganizationIntegrationId = integrationId,
        Key = "apiKey",
        EncryptedValue = "secret",
        CreatedAtUtc = Now,
        UpdatedAtUtc = Now,
    };

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
        Guid? clientId,
        Guid userId) => new()
    {
        Id = id,
        OrganizationId = organizationId,
        ClientId = clientId,
        UserId = userId,
        SourceUrl = "https://example.com/video",
        Status = "Analyzed",
        CreatedAtUtc = Now,
        UpdatedAtUtc = Now,
    };

    private static IntegrationExecutionAttemptData AttemptRow(Guid analysisId) => new()
    {
        Id = Guid.NewGuid(),
        AnalysisId = analysisId,
        CapabilityKey = "single-content-analysis",
        ProviderKey = "test",
        Status = "Succeeded",
        AttemptNumber = 1,
        StartedAtUtc = Now,
        CompletedAtUtc = Now,
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

    private static UsageEventData UsageRow(Guid organizationId, Guid? clientId) => new()
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
        Guid? clientId,
        Guid analysisId) => new()
    {
        Id = id,
        OrganizationId = organizationId,
        ClientId = clientId,
        AnalysisId = analysisId,
        RequestedFormats = ["flyer"],
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
        FormatKey = "flyer",
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
            .UseInMemoryDatabase($"organization-hard-delete-{Guid.NewGuid():N}")
            .Options;

        return new AppIdentityDbContext(options);
    }
}
