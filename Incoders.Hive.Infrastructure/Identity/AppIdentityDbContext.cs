using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Incoders.Hive.Domain.Social.ClientBrandManuals;
using Incoders.Hive.Infrastructure.Persistence.Postgres;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Json;

namespace Incoders.Hive.Infrastructure.Identity;

/// <summary>
/// Application persistence context. Owns both ASP.NET Identity tables (in the <c>identity</c>
/// schema) and platform/product business entities. The backend is the single source of truth
/// for authentication; Supabase is used only as the underlying Postgres host.
/// </summary>
public sealed class AppIdentityDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>
{
    public AppIdentityDbContext(DbContextOptions<AppIdentityDbContext> options) : base(options) { }

    public DbSet<TenantData> Tenants => Set<TenantData>();

    public DbSet<TenantMembershipData> TenantMemberships => Set<TenantMembershipData>();

    public DbSet<SystemModuleData> SystemModules => Set<SystemModuleData>();

    public DbSet<OrganizationModuleData> OrganizationModules => Set<OrganizationModuleData>();

    public DbSet<OrganizationUserModuleData> OrganizationUserModules => Set<OrganizationUserModuleData>();

    public DbSet<SystemSettingData> SystemSettings => Set<SystemSettingData>();

    public DbSet<SocialClientData> SocialClients => Set<SocialClientData>();

    public DbSet<UserPreferenceData> UserPreferences => Set<UserPreferenceData>();

    public DbSet<IntegrationProviderData> IntegrationProviders => Set<IntegrationProviderData>();

    public DbSet<OrganizationIntegrationData> OrganizationIntegrations => Set<OrganizationIntegrationData>();

    public DbSet<OrganizationIntegrationCredentialData> OrganizationIntegrationCredentials => Set<OrganizationIntegrationCredentialData>();

    public DbSet<IntegrationProviderCapabilityData> IntegrationProviderCapabilities => Set<IntegrationProviderCapabilityData>();

    public DbSet<SocialNetworkData> SocialNetworks => Set<SocialNetworkData>();

    public DbSet<ClientReferentAccountData> ClientReferentAccounts => Set<ClientReferentAccountData>();

    public DbSet<ClientCompetitorAccountData> ClientCompetitorAccounts => Set<ClientCompetitorAccountData>();

    public DbSet<ClientBrandManualData> ClientBrandManuals => Set<ClientBrandManualData>();

    public DbSet<GeneralStrategyData> GeneralStrategies => Set<GeneralStrategyData>();

    public DbSet<ContentPlanData> ContentPlans => Set<ContentPlanData>();

    public DbSet<ContentPlanSlotData> ContentPlanSlots => Set<ContentPlanSlotData>();

    public DbSet<SingleContentAnalysisData> SingleContentAnalyses => Set<SingleContentAnalysisData>();

    public DbSet<IntegrationExecutionAttemptData> IntegrationExecutionAttempts => Set<IntegrationExecutionAttemptData>();

    public DbSet<UsageEventData> UsageEvents => Set<UsageEventData>();

    public DbSet<ProviderCostRateData> ProviderCostRates => Set<ProviderCostRateData>();

    public DbSet<ContentGenerationJobData> ContentGenerationJobs => Set<ContentGenerationJobData>();

    public DbSet<ContentGenerationFormatResultData> ContentGenerationFormatResults => Set<ContentGenerationFormatResultData>();

    public DbSet<GeneratedPackageData> GeneratedPackages => Set<GeneratedPackageData>();

    public DbSet<ContentGenerationFormatAssetData> ContentGenerationFormatAssets => Set<ContentGenerationFormatAssetData>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        ConfigureIdentityTables(builder);
        ConfigureApplicationTables(builder);

        builder.UsePostgresSnakeCaseNames();
    }

    private static void ConfigureIdentityTables(ModelBuilder builder)
    {
        const string schema = PostgresDatabaseNames.IdentitySchema;

        builder.Entity<ApplicationUser>().ToTable("users", schema);
        builder.Entity<ApplicationRole>().ToTable("roles", schema);
        builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserRole<Guid>>().ToTable("user_roles", schema);
        builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserClaim<Guid>>().ToTable("user_claims", schema);
        builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserLogin<Guid>>().ToTable("user_logins", schema);
        builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserToken<Guid>>().ToTable("user_tokens", schema);
        builder.Entity<Microsoft.AspNetCore.Identity.IdentityRoleClaim<Guid>>().ToTable("role_claims", schema);
    }

    private static void ConfigureApplicationTables(ModelBuilder builder)
    {
        builder.Entity<TenantData>(entity =>
        {
            entity.ToTable("organizations", PostgresDatabaseNames.PlatformSchema);
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.IsActive).IsRequired();
            entity.HasIndex(x => x.Name);
        });

        builder.Entity<TenantMembershipData>(entity =>
        {
            entity.ToTable("organization_memberships", PostgresDatabaseNames.PlatformSchema);
            entity.HasKey(x => new { x.TenantId, x.UserId });
            entity.Property(x => x.TenantId).HasColumnName("organization_id");
            entity.Property(x => x.Role).HasMaxLength(32).IsRequired();
            entity.HasIndex(x => x.UserId)
                .IsUnique()
                .HasDatabaseName("ix_organization_memberships_user_id_unique");
            entity.HasOne(x => x.Tenant)
                .WithMany()
                .HasForeignKey(x => x.TenantId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<SystemModuleData>(entity =>
        {
            entity.ToTable("system_modules", PostgresDatabaseNames.PlatformSchema);
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ModuleKey).HasColumnName("key_module").HasMaxLength(64).IsRequired();
            entity.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(120).IsRequired();
            entity.Property(x => x.Description).HasColumnName("description").HasMaxLength(280).IsRequired();
            entity.Property(x => x.ShortLabel).HasColumnName("short_label").HasMaxLength(64).IsRequired();
            entity.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
            entity.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();
            entity.HasIndex(x => x.ModuleKey).IsUnique();
            entity.HasIndex(x => x.IsActive).HasDatabaseName("ix_system_modules_is_active");
        });

        builder.Entity<OrganizationModuleData>(entity =>
        {
            entity.ToTable("organization_modules", PostgresDatabaseNames.PlatformSchema);
            entity.HasKey(x => new { x.OrganizationId, x.ModuleId });
            entity.Property(x => x.OrganizationId).HasColumnName("organization_id");
            entity.Property(x => x.ModuleId).HasColumnName("module_id");
            entity.Property(x => x.SortOrder).HasColumnName("sort_order").IsRequired().HasDefaultValue(0);
            entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
            entity.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();

            entity.HasOne(x => x.Organization)
                .WithMany()
                .HasForeignKey(x => x.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Module)
                .WithMany()
                .HasForeignKey(x => x.ModuleId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => new { x.OrganizationId, x.SortOrder })
                .HasDatabaseName("ix_organization_modules_org_sort_order");
        });

        builder.Entity<OrganizationUserModuleData>(entity =>
        {
            entity.ToTable("organization_user_modules", PostgresDatabaseNames.PlatformSchema);
            entity.HasKey(x => new { x.OrganizationId, x.UserId, x.ModuleId });
            entity.Property(x => x.OrganizationId).HasColumnName("organization_id");
            entity.Property(x => x.ModuleId).HasColumnName("module_id");
            entity.Property(x => x.SortOrder).HasColumnName("sort_order").IsRequired().HasDefaultValue(0);
            entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
            entity.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();

            entity.HasOne(x => x.Organization)
                .WithMany()
                .HasForeignKey(x => x.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<TenantMembershipData>()
                .WithMany()
                .HasForeignKey(x => new { x.OrganizationId, x.UserId })
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_organization_user_modules_membership");

            entity.HasOne(x => x.Module)
                .WithMany()
                .HasForeignKey(x => x.ModuleId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => x.UserId)
                .HasDatabaseName("ix_organization_user_modules_user_id");

            entity.HasIndex(x => new { x.OrganizationId, x.UserId, x.SortOrder })
                .HasDatabaseName("ix_organization_user_modules_org_user_sort_order");
        });

        builder.Entity<SystemSettingData>(entity =>
        {
            entity.ToTable("system_settings", PostgresDatabaseNames.PlatformSchema);
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Key).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Value).IsRequired();
            entity.Property(x => x.Scope).HasMaxLength(32).IsRequired();
            entity.Property(x => x.TenantId).HasColumnName("organization_id");
            entity.HasIndex(x => new { x.Scope, x.TenantId, x.UserId, x.Key })
                .IsUnique();
        });

        builder.Entity<SocialClientData>(entity =>
        {
            entity.ToTable("clients", PostgresDatabaseNames.SocialSchema);
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(1_000);
            entity.Property(x => x.SectorOrNiche).HasMaxLength(300);
            entity.Property(x => x.IsActive).IsRequired();
            entity.HasIndex(x => new { x.OrganizationId, x.Name });
            entity.HasOne(x => x.Organization)
                .WithMany()
                .HasForeignKey(x => x.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<UserPreferenceData>(entity =>
        {
            entity.ToTable("user_preferences", PostgresDatabaseNames.PlatformSchema);
            entity.HasKey(x => x.Id);
            entity.Property(x => x.UserId).IsRequired();
            entity.Property(x => x.ThemeKey).HasMaxLength(32).IsRequired();
            entity.Property(x => x.AccentTheme).HasMaxLength(64);
            entity.HasIndex(x => x.UserId).IsUnique();
        });

        builder.Entity<IntegrationProviderData>(entity =>
        {
            entity.ToTable("integration_providers", PostgresDatabaseNames.PlatformSchema);
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Key).HasMaxLength(64).IsRequired();
            entity.Property(x => x.DisplayName).HasMaxLength(120).IsRequired();
            entity.Property(x => x.Category).HasMaxLength(64).IsRequired();
            entity.Property(x => x.IconKey).HasMaxLength(64).IsRequired();
            entity.Property(x => x.CredentialFieldsJson).IsRequired();
            entity.Property(x => x.IsActive).IsRequired();
            entity.Property(x => x.SupportsBringYourOwnKey).IsRequired();
            entity.Property(x => x.SupportsManagedByIncoders).IsRequired();
            entity.Property(x => x.SupportsSystemDefault).IsRequired();
            entity.Property(x => x.SystemDefaultReadOnly).IsRequired();
            entity.HasIndex(x => x.Key).IsUnique();
        });

        builder.Entity<OrganizationIntegrationData>(entity =>
        {
            entity.ToTable("organization_integrations", PostgresDatabaseNames.PlatformSchema);
            entity.HasKey(x => x.Id);
            entity.Property(x => x.OrganizationId).HasColumnName("organization_id");
            entity.Property(x => x.ConnectionMode).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(32).IsRequired();
            entity.Property(x => x.SelectedAiModel).HasMaxLength(200);
            entity.Property(x => x.IsReadOnly).IsRequired();
            entity.Property(x => x.IsDefaultAi).IsRequired();
            entity.HasIndex(x => new { x.OrganizationId, x.ProviderId }).IsUnique();
            entity.HasIndex(x => new { x.OrganizationId, x.Status });

            entity.HasOne(x => x.Organization)
                .WithMany()
                .HasForeignKey(x => x.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Provider)
                .WithMany(x => x.OrganizationIntegrations)
                .HasForeignKey(x => x.ProviderId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<OrganizationIntegrationCredentialData>(entity =>
        {
            entity.ToTable("organization_integration_credentials", PostgresDatabaseNames.PlatformSchema);
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Key).HasMaxLength(64).IsRequired();
            entity.Property(x => x.EncryptedValue).IsRequired();
            entity.HasIndex(x => new { x.OrganizationIntegrationId, x.Key }).IsUnique();

            entity.HasOne(x => x.OrganizationIntegration)
                .WithMany(x => x.Credentials)
                .HasForeignKey(x => x.OrganizationIntegrationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<IntegrationProviderCapabilityData>(entity =>
        {
            entity.ToTable("integration_provider_capabilities", PostgresDatabaseNames.PlatformSchema);
            entity.HasKey(x => x.Id);
            entity.Property(x => x.CapabilityKey).HasMaxLength(64).IsRequired();
            entity.Property(x => x.PlatformKey).HasMaxLength(64);
            entity.Property(x => x.IsActive).IsRequired();
            entity.HasIndex(x => new { x.ProviderId, x.CapabilityKey })
                .IsUnique()
                .HasFilter("platform_key IS NULL")
                .HasDatabaseName("ix_integration_provider_capabilities_provider_capability_global");
            entity.HasIndex(x => new { x.ProviderId, x.CapabilityKey, x.PlatformKey })
                .IsUnique()
                .HasFilter("platform_key IS NOT NULL")
                .HasDatabaseName("ix_integration_provider_capabilities_provider_capability_platform");

            entity.HasOne(x => x.Provider)
                .WithMany(x => x.Capabilities)
                .HasForeignKey(x => x.ProviderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ClientReferentAccountData>(entity =>
        {
            entity.ToTable("client_referent_accounts", PostgresDatabaseNames.SocialSchema);
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.ClientId).HasColumnName("client_id").IsRequired();
            entity.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired();
            entity.Property(x => x.SocialNetworkId).HasColumnName("social_network_id").IsRequired();
            entity.Property(x => x.ProfileUrl).HasColumnName("profile_url").HasMaxLength(2048).IsRequired();
            entity.Property(x => x.Handle).HasColumnName("handle").HasMaxLength(100);
            entity.Property(x => x.Label).HasColumnName("label").HasMaxLength(80);
            entity.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(500);
            entity.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
            entity.Property(x => x.SortOrder).HasColumnName("sort_order").IsRequired().HasDefaultValue(0);
            entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
            entity.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();

            // FK: client_id → social.clients(id) ON DELETE CASCADE
            entity.HasOne<SocialClientData>()
                .WithMany()
                .HasForeignKey(x => x.ClientId)
                .OnDelete(DeleteBehavior.Cascade);

            // FK: social_network_id → platform.social_networks(id) ON DELETE RESTRICT (cross-schema)
            entity.HasOne<SocialNetworkData>()
                .WithMany()
                .HasForeignKey(x => x.SocialNetworkId)
                .OnDelete(DeleteBehavior.Restrict);

            // FK: organization_id → platform.organizations(id) ON DELETE CASCADE (cross-schema)
            entity.HasOne<TenantData>()
                .WithMany()
                .HasForeignKey(x => x.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);

            // Composite unique index — core deduplication constraint
            entity.HasIndex(x => new { x.ClientId, x.SocialNetworkId, x.ProfileUrl })
                .IsUnique()
                .HasDatabaseName("ix_client_referent_accounts_client_network_url");

            entity.HasIndex(x => x.ClientId)
                .HasDatabaseName("ix_client_referent_accounts_client_id");

            entity.HasIndex(x => new { x.ClientId, x.SortOrder })
                .HasDatabaseName("ix_client_referent_accounts_client_sort_order");

            entity.HasIndex(x => x.OrganizationId)
                .HasDatabaseName("ix_client_referent_accounts_organization_id");

            entity.HasIndex(x => x.SocialNetworkId)
                .HasDatabaseName("ix_client_referent_accounts_social_network_id");
        });

        builder.Entity<ClientCompetitorAccountData>(entity =>
        {
            entity.ToTable("client_competitor_accounts", PostgresDatabaseNames.SocialSchema);
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.ClientId).HasColumnName("client_id").IsRequired();
            entity.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired();
            entity.Property(x => x.SocialNetworkId).HasColumnName("social_network_id").IsRequired();
            entity.Property(x => x.ProfileUrl).HasColumnName("profile_url").HasMaxLength(2048).IsRequired();
            entity.Property(x => x.Handle).HasColumnName("handle").HasMaxLength(100);
            entity.Property(x => x.Label).HasColumnName("label").HasMaxLength(80);
            entity.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(500);
            entity.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
            entity.Property(x => x.SortOrder).HasColumnName("sort_order").IsRequired().HasDefaultValue(0);
            entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
            entity.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();

            entity.HasOne<SocialClientData>()
                .WithMany()
                .HasForeignKey(x => x.ClientId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<SocialNetworkData>()
                .WithMany()
                .HasForeignKey(x => x.SocialNetworkId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<TenantData>()
                .WithMany()
                .HasForeignKey(x => x.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => new { x.ClientId, x.SocialNetworkId, x.ProfileUrl })
                .IsUnique()
                .HasDatabaseName("ix_client_competitor_accounts_client_network_url");

            entity.HasIndex(x => x.ClientId)
                .HasDatabaseName("ix_client_competitor_accounts_client_id");

            entity.HasIndex(x => new { x.ClientId, x.SortOrder })
                .HasDatabaseName("ix_client_competitor_accounts_client_sort_order");

            entity.HasIndex(x => x.OrganizationId)
                .HasDatabaseName("ix_client_competitor_accounts_organization_id");

            entity.HasIndex(x => x.SocialNetworkId)
                .HasDatabaseName("ix_client_competitor_accounts_social_network_id");
        });

        builder.Entity<ClientBrandManualData>(entity =>
        {
            entity.ToTable("client_brand_manuals", PostgresDatabaseNames.SocialSchema);
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.ClientId).HasColumnName("client_id").IsRequired();
            entity.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired();
            entity.Property(x => x.Title).HasColumnName("title").HasMaxLength(80).IsRequired();
            entity.Property(x => x.VoiceAndTone).HasColumnName("voice_and_tone").HasMaxLength(2000);
            entity.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(4000);
            entity.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
            entity.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();

            // jsonb columns — ADR 3/4/5
            ConfigureJsonbCollection<ClientBrandManualData, ColorEntry>(entity, x => x.ColorPalette, "color_palette");
            ConfigureJsonbCollection<ClientBrandManualData, LogoEntry>(entity, x => x.Logos, "logos");
            ConfigureJsonbCollection<ClientBrandManualData, BackgroundEntry>(entity, x => x.BackgroundTemplates, "background_templates");
            ConfigureJsonbCollection<ClientBrandManualData, TypographyEntry>(entity, x => x.Typography, "typography");

            // FK: client_id → social.clients(id) ON DELETE CASCADE
            entity.HasOne<SocialClientData>()
                .WithMany()
                .HasForeignKey(x => x.ClientId)
                .OnDelete(DeleteBehavior.Cascade);

            // FK: organization_id → platform.organizations(id) ON DELETE CASCADE
            entity.HasOne<TenantData>()
                .WithMany()
                .HasForeignKey(x => x.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => x.ClientId)
                .HasDatabaseName("ix_client_brand_manuals_client_id");

            entity.HasIndex(x => x.OrganizationId)
                .HasDatabaseName("ix_client_brand_manuals_organization_id");

            // Partial unique index: at most one active manual per client (ADR 6)
            entity.HasIndex(x => x.ClientId)
                .IsUnique()
                .HasFilter("is_active = true")
                .HasDatabaseName("ix_client_brand_manuals_one_active_per_client");
        });

        builder.Entity<SocialNetworkData>(entity =>
        {
            entity.ToTable("social_networks", PostgresDatabaseNames.PlatformSchema);
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.Code).HasColumnName("code").HasMaxLength(32).IsRequired();
            entity.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(80).IsRequired();
            entity.Property(x => x.IconKey).HasColumnName("icon_key").HasMaxLength(32).IsRequired();
            entity.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
            entity.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();
            entity.HasIndex(x => x.Code).IsUnique().HasDatabaseName("ix_social_networks_code");
        });

        builder.Entity<SingleContentAnalysisData>(entity =>
        {
            entity.ToTable("single_content_analyses", PostgresDatabaseNames.SocialSchema);
            entity.HasKey(x => x.Id);
            entity.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired();
            entity.Property(x => x.ClientId).HasColumnName("client_id");
            entity.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
            entity.Property(x => x.SourceUrl).HasMaxLength(2048).IsRequired();
            entity.Property(x => x.NormalizedUrl).HasMaxLength(2048);
            entity.Property(x => x.SourcePlatformKey).HasMaxLength(64);
            entity.Property(x => x.Status).HasMaxLength(32).IsRequired();
            entity.Property(x => x.FailureCode).HasMaxLength(128);
            entity.Property(x => x.FailureMessageKey).HasMaxLength(200);
            entity.Property(x => x.PatternSetJson)
                .HasColumnName("pattern_set_json")
                .HasColumnType("jsonb");
            entity.HasIndex(x => new { x.OrganizationId, x.Status })
                .HasDatabaseName("ix_single_content_analyses_org_status");
            entity.HasIndex(x => new { x.OrganizationId, x.SourcePlatformKey })
                .HasDatabaseName("ix_single_content_analyses_org_platform");

            entity.HasOne(x => x.Organization)
                .WithMany()
                .HasForeignKey(x => x.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<IntegrationExecutionAttemptData>(entity =>
        {
            entity.ToTable("integration_execution_attempts", PostgresDatabaseNames.SocialSchema);
            entity.HasKey(x => x.Id);
            entity.Property(x => x.CapabilityKey).HasMaxLength(64).IsRequired();
            entity.Property(x => x.ProviderKey).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(32).IsRequired();
            entity.Property(x => x.FailureCode).HasMaxLength(128);
            entity.HasIndex(x => new { x.AnalysisId, x.CapabilityKey, x.ProviderKey, x.AttemptNumber })
                .HasDatabaseName("ix_integration_attempts_analysis_capability_provider_attempt");

            entity.HasOne(x => x.Analysis)
                .WithMany(x => x.Attempts)
                .HasForeignKey(x => x.AnalysisId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<UsageEventData>(entity =>
        {
            entity.ToTable("usage_events", PostgresDatabaseNames.PlatformSchema);
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedOnAdd();
            entity.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired();
            entity.Property(x => x.ClientId).HasColumnName("client_id");
            entity.Property(x => x.WorkflowKey).HasColumnName("workflow_key").HasMaxLength(128).IsRequired();
            entity.Property(x => x.PipelineStepKey).HasColumnName("pipeline_step_key").HasMaxLength(128).IsRequired();
            entity.Property(x => x.ProviderKey).HasColumnName("provider_key").HasMaxLength(64).IsRequired();
            entity.Property(x => x.Model).HasColumnName("model").HasMaxLength(128);
            entity.Property(x => x.PromptVersion).HasColumnName("prompt_version").HasMaxLength(64);
            entity.Property(x => x.RequestTimestamp).HasColumnName("request_timestamp").IsRequired();
            entity.Property(x => x.CompletedTimestamp).HasColumnName("completed_timestamp").IsRequired();
            entity.Property(x => x.DurationMs).HasColumnName("duration_ms").IsRequired();
            entity.Property(x => x.InputTokens).HasColumnName("input_tokens");
            entity.Property(x => x.OutputTokens).HasColumnName("output_tokens");
            entity.Property(x => x.EstimatedCostUsd).HasColumnName("estimated_cost_usd").HasColumnType("numeric(18,6)");
            entity.Property(x => x.RequestSizeBytes).HasColumnName("request_size_bytes");
            entity.Property(x => x.ResponseSizeBytes).HasColumnName("response_size_bytes");
            entity.Property(x => x.RetryCount).HasColumnName("retry_count").IsRequired().HasDefaultValue(0);
            entity.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
            entity.Property(x => x.FailureCode).HasColumnName("failure_code").HasMaxLength(128);
            entity.Property(x => x.CorrelationId).HasColumnName("correlation_id").IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();

            entity.HasIndex(x => new { x.OrganizationId, x.RequestTimestamp })
                .HasDatabaseName("ix_usage_events_org_timestamp");
            entity.HasIndex(x => new { x.OrganizationId, x.ClientId, x.RequestTimestamp })
                .HasDatabaseName("ix_usage_events_org_client_timestamp");
            entity.HasIndex(x => new { x.ProviderKey, x.Model, x.RequestTimestamp })
                .HasDatabaseName("ix_usage_events_provider_model_timestamp");
            entity.HasIndex(x => x.CorrelationId)
                .HasDatabaseName("ix_usage_events_correlation_id");
        });

        builder.Entity<ProviderCostRateData>(entity =>
        {
            entity.ToTable("provider_cost_rates", PostgresDatabaseNames.PlatformSchema);
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ProviderKey).HasColumnName("provider_key").HasMaxLength(64).IsRequired();
            entity.Property(x => x.Model).HasColumnName("model").HasMaxLength(128);
            entity.Property(x => x.CostPer1KInputTokens).HasColumnName("cost_per_1k_input_tokens").HasColumnType("numeric(18,8)");
            entity.Property(x => x.CostPer1KOutputTokens).HasColumnName("cost_per_1k_output_tokens").HasColumnType("numeric(18,8)");
            entity.Property(x => x.CostPerRequest).HasColumnName("cost_per_request").HasColumnType("numeric(18,8)");
            entity.Property(x => x.EffectiveFrom).HasColumnName("effective_from").IsRequired();
            entity.Property(x => x.EffectiveTo).HasColumnName("effective_to");
            entity.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(500);

            entity.HasIndex(x => new { x.ProviderKey, x.Model, x.EffectiveFrom })
                .IsUnique()
                .HasDatabaseName("ix_provider_cost_rates_provider_model_effective_from");
        });

        builder.Entity<ContentGenerationJobData>(entity =>
        {
            entity.ToTable("content_generation_jobs", PostgresDatabaseNames.SocialSchema);
            entity.HasKey(x => x.Id);
            entity.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired();
            entity.Property(x => x.ClientId).HasColumnName("client_id");
            entity.Property(x => x.AnalysisId).HasColumnName("analysis_id").IsRequired();
            entity.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
            entity.Property(x => x.StatusReason).HasColumnName("status_reason").HasMaxLength(500);
            entity.Property(x => x.StartedAt).HasColumnName("started_at");
            entity.Property(x => x.CompletedAt).HasColumnName("completed_at");
            entity.Property(x => x.ErrorCode).HasColumnName("error_code").HasMaxLength(128);
            entity.Property(x => x.BrandSnapshotJson).HasColumnName("brand_snapshot_json").HasColumnType("jsonb").IsRequired();
            entity.Property(x => x.ManifestJson).HasColumnName("manifest_json").HasColumnType("jsonb");
            entity.Property(x => x.RegenerationObjection).HasColumnName("regeneration_objection").HasMaxLength(1000);
            entity.Property(x => x.CorrelationId).HasColumnName("correlation_id").IsRequired();
            entity.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();

            // RequestedFormats as jsonb text[]
            var formatsConverter = new ValueConverter<List<string>, string>(
                v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                v => System.Text.Json.JsonSerializer.Deserialize<List<string>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new List<string>());
            var formatsComparer = new ValueComparer<List<string>>(
                (a, b) => a!.SequenceEqual(b!),
                x => x.Aggregate(0, (h, e) => HashCode.Combine(h, e.GetHashCode())),
                x => x.ToList());
            entity.Property(x => x.RequestedFormats)
                .HasColumnName("requested_formats")
                .HasColumnType("jsonb")
                .IsRequired()
                .HasDefaultValueSql("'[]'")
                .HasConversion(formatsConverter)
                .Metadata.SetValueComparer(formatsComparer);

            entity.HasIndex(x => new { x.OrganizationId, x.CreatedAt })
                .HasDatabaseName("ix_content_generation_jobs_org_created");
            entity.HasIndex(x => x.AnalysisId)
                .HasDatabaseName("ix_content_generation_jobs_analysis_id");
            entity.HasIndex(x => x.Status)
                .HasDatabaseName("ix_content_generation_jobs_status");

            entity.HasOne<TenantData>()
                .WithMany()
                .HasForeignKey(x => x.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(x => x.FormatResults)
                .WithOne(x => x.Job)
                .HasForeignKey(x => x.JobId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ContentGenerationFormatResultData>(entity =>
        {
            entity.ToTable("content_generation_format_results", PostgresDatabaseNames.SocialSchema);
            entity.HasKey(x => x.Id);
            entity.Property(x => x.JobId).HasColumnName("job_id").IsRequired();
            entity.Property(x => x.FormatKey).HasColumnName("format_key").HasMaxLength(64).IsRequired();
            entity.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
            entity.Property(x => x.OutputJson).HasColumnName("output_json").HasColumnType("jsonb");
            entity.Property(x => x.CompositionSpecJson).HasColumnName("composition_spec_json").HasColumnType("jsonb");
            entity.Property(x => x.RenderedAssetUri).HasColumnName("rendered_asset_uri").HasMaxLength(2048);
            entity.Property(x => x.InferredFieldsJson).HasColumnName("inferred_fields_json").HasColumnType("jsonb");
            entity.Property(x => x.PromptVersion).HasColumnName("prompt_version").HasMaxLength(64);
            entity.Property(x => x.ErrorCode).HasColumnName("error_code").HasMaxLength(128);
            entity.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();

            entity.HasIndex(x => new { x.JobId, x.FormatKey })
                .IsUnique()
                .HasDatabaseName("ix_content_generation_format_results_job_format");

            entity.HasOne(x => x.Job)
                .WithMany(x => x.FormatResults)
                .HasForeignKey(x => x.JobId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<GeneratedPackageData>(entity =>
        {
            entity.ToTable("generated_packages", PostgresDatabaseNames.SocialSchema);
            entity.HasKey(x => x.Id);
            entity.Property(x => x.JobId).HasColumnName("job_id").IsRequired();
            entity.Property(x => x.StorageUri).HasColumnName("storage_uri").HasMaxLength(2048).IsRequired();
            entity.Property(x => x.SizeBytes).HasColumnName("size_bytes").IsRequired();
            entity.Property(x => x.ManifestJson).HasColumnName("manifest_json").HasColumnType("jsonb").IsRequired();
            entity.Property(x => x.ExpiresAt).HasColumnName("expires_at");
            entity.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();

            // 1:1 with job — unique FK
            entity.HasIndex(x => x.JobId)
                .IsUnique()
                .HasDatabaseName("ix_generated_packages_job_id");

            entity.HasOne(x => x.Job)
                .WithOne(x => x.Package)
                .HasForeignKey<GeneratedPackageData>(x => x.JobId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ContentGenerationFormatAssetData>(entity =>
        {
            entity.ToTable("content_generation_format_assets", PostgresDatabaseNames.SocialSchema);
            entity.HasKey(x => x.Id);
            entity.Property(x => x.FormatResultId).HasColumnName("format_result_id").IsRequired();
            entity.Property(x => x.AssetIndex).HasColumnName("asset_index").IsRequired();
            entity.Property(x => x.StoragePath).HasColumnName("storage_path").IsRequired();
            entity.Property(x => x.Filename).HasColumnName("filename").HasMaxLength(500).IsRequired();
            entity.Property(x => x.MimeType).HasColumnName("mime_type").HasMaxLength(128).IsRequired();
            entity.Property(x => x.SizeBytes).HasColumnName("size_bytes").IsRequired();
            entity.Property(x => x.ContentHash).HasColumnName("content_hash").HasMaxLength(64).IsRequired();
            entity.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();

            // UNIQUE: one asset per (format_result_id, asset_index)
            entity.HasIndex(x => new { x.FormatResultId, x.AssetIndex })
                .IsUnique()
                .HasDatabaseName("ix_content_generation_format_assets_result_index");

            entity.HasIndex(x => x.FormatResultId)
                .HasDatabaseName("ix_content_generation_format_assets_format_result_id");

            entity.HasIndex(x => x.StoragePath)
                .HasDatabaseName("ix_content_generation_format_assets_storage_path");

            entity.HasOne(x => x.FormatResult)
                .WithMany(x => x.Assets)
                .HasForeignKey(x => x.FormatResultId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<GeneralStrategyData>(entity =>
        {
            entity.ToTable("general_strategies", PostgresDatabaseNames.SocialSchema);
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.ClientId).HasColumnName("client_id").IsRequired();
            entity.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired();
            entity.Property(x => x.TargetAudience).HasColumnName("target_audience").HasMaxLength(1000);
            entity.Property(x => x.PostingFrequency).HasColumnName("posting_frequency").HasMaxLength(200);
            entity.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(2000);
            entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
            entity.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();

            ConfigureJsonbCollection<GeneralStrategyData, string>(entity, x => x.ContentPillars, "content_pillars");
            ConfigureJsonbCollection<GeneralStrategyData, string>(entity, x => x.ToneKeywords, "tone_keywords");

            entity.HasOne<SocialClientData>()
                .WithMany()
                .HasForeignKey(x => x.ClientId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<TenantData>()
                .WithMany()
                .HasForeignKey(x => x.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => x.ClientId)
                .IsUnique()
                .HasDatabaseName("ix_general_strategies_one_per_client");

            entity.HasIndex(x => x.OrganizationId)
                .HasDatabaseName("ix_general_strategies_organization_id");
        });

        builder.Entity<ContentPlanData>(entity =>
        {
            entity.ToTable("content_plans", PostgresDatabaseNames.SocialSchema);
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.ClientId).HasColumnName("client_id").IsRequired();
            entity.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired();

            // Period columns
            entity.Property(x => x.PeriodKind).HasColumnName("period_kind").HasColumnType("smallint").IsRequired();
            entity.Property(x => x.Year).HasColumnName("year").IsRequired();
            entity.Property(x => x.Month).HasColumnName("month").IsRequired();

            // Objective node
            entity.Property(x => x.ObjectiveText).HasColumnName("objective_text").HasMaxLength(3000).IsRequired();
            ConfigureJsonbCollection<ContentPlanData, string>(entity, x => x.ObjectiveKpis, "objective_kpis");

            // Objective Provenance (flat owned columns with prefix)
            entity.Property(x => x.ObjectiveProvenanceSource)
                .HasColumnName("objective_provenance_source").HasColumnType("smallint").IsRequired();
            entity.Property(x => x.ObjectiveProvenanceStatus)
                .HasColumnName("objective_provenance_status").HasColumnType("smallint").IsRequired();
            entity.Property(x => x.ObjectiveProvenanceGeneratedAtUtc)
                .HasColumnName("objective_provenance_generated_at_utc");
            entity.Property(x => x.ObjectiveProvenancePromptVersion)
                .HasColumnName("objective_provenance_prompt_version").HasMaxLength(64);

            // Content Strategy node
            entity.Property(x => x.ContentStrategyText).HasColumnName("content_strategy_text").HasMaxLength(10000);

            // Content Strategy Provenance
            entity.Property(x => x.ContentStrategyProvenanceSource)
                .HasColumnName("content_strategy_provenance_source").HasColumnType("smallint").IsRequired();
            entity.Property(x => x.ContentStrategyProvenanceStatus)
                .HasColumnName("content_strategy_provenance_status").HasColumnType("smallint").IsRequired();
            entity.Property(x => x.ContentStrategyProvenanceGeneratedAtUtc)
                .HasColumnName("content_strategy_provenance_generated_at_utc");
            entity.Property(x => x.ContentStrategyProvenancePromptVersion)
                .HasColumnName("content_strategy_provenance_prompt_version").HasMaxLength(64);

            // Calendar-level Provenance
            entity.Property(x => x.CalendarProvenanceSource)
                .HasColumnName("calendar_provenance_source").HasColumnType("smallint").IsRequired();
            entity.Property(x => x.CalendarProvenanceStatus)
                .HasColumnName("calendar_provenance_status").HasColumnType("smallint").IsRequired();
            entity.Property(x => x.CalendarProvenanceGeneratedAtUtc)
                .HasColumnName("calendar_provenance_generated_at_utc");
            entity.Property(x => x.CalendarProvenancePromptVersion)
                .HasColumnName("calendar_provenance_prompt_version").HasMaxLength(64);

            entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
            entity.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();

            // FK: client_id → social.clients(id) ON DELETE CASCADE
            entity.HasOne<SocialClientData>()
                .WithMany()
                .HasForeignKey(x => x.ClientId)
                .OnDelete(DeleteBehavior.Cascade);

            // FK: organization_id → platform.organizations(id) ON DELETE CASCADE
            entity.HasOne<TenantData>()
                .WithMany()
                .HasForeignKey(x => x.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);

            // Unique constraint on (client_id, period_kind, year, month)
            entity.HasIndex(x => new { x.ClientId, x.PeriodKind, x.Year, x.Month })
                .IsUnique()
                .HasDatabaseName("ix_content_plans_client_period");

            entity.HasIndex(x => x.ClientId)
                .HasDatabaseName("ix_content_plans_client_id");

            entity.HasIndex(x => x.OrganizationId)
                .HasDatabaseName("ix_content_plans_organization_id");

            // Slots collection — cascade-loaded for upsert, navigation for EF cascade
            entity.HasMany(x => x.Slots)
                .WithOne(x => x.ContentPlan)
                .HasForeignKey(x => x.ContentPlanId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ContentPlanSlotData>(entity =>
        {
            entity.ToTable("content_plan_slots", PostgresDatabaseNames.SocialSchema);
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.ContentPlanId).HasColumnName("content_plan_id").IsRequired();
            entity.Property(x => x.Day).HasColumnName("day").IsRequired();
            entity.Property(x => x.ContentFormat).HasColumnName("content_format").HasMaxLength(200).IsRequired();
            entity.Property(x => x.SortOrder).HasColumnName("sort_order").IsRequired().HasDefaultValue(0);
            entity.Property(x => x.ProducedPackageId).HasColumnName("produced_package_id");

            // Slot Provenance
            entity.Property(x => x.SlotProvenanceSource)
                .HasColumnName("slot_provenance_source").HasColumnType("smallint").IsRequired();
            entity.Property(x => x.SlotProvenanceStatus)
                .HasColumnName("slot_provenance_status").HasColumnType("smallint").IsRequired();
            entity.Property(x => x.SlotProvenanceGeneratedAtUtc)
                .HasColumnName("slot_provenance_generated_at_utc");
            entity.Property(x => x.SlotProvenancePromptVersion)
                .HasColumnName("slot_provenance_prompt_version").HasMaxLength(64);

            entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
            entity.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();

            // FK: content_plan_id → social.content_plans(id) ON DELETE CASCADE
            entity.HasOne(x => x.ContentPlan)
                .WithMany(x => x.Slots)
                .HasForeignKey(x => x.ContentPlanId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => x.ContentPlanId)
                .HasDatabaseName("ix_content_plan_slots_plan_id");

            entity.HasIndex(x => new { x.ContentPlanId, x.SortOrder })
                .HasDatabaseName("ix_content_plan_slots_plan_sort_order");
        });
    }

    /// <summary>
    /// Configures a jsonb column with ValueConverter (System.Text.Json) and ValueComparer (deep-equal).
    /// Resolution 4: null deserializes to empty list. Resolution 5: pinned options via SocialJsonOptions.Default.
    /// Resolution 6: comparer snapshot uses ToList() so Update replaces refs with new List instances.
    /// Generic over both the entity type and the item type so it can be reused for all social aggregates (ADR-3).
    /// </summary>
    private static void ConfigureJsonbCollection<TEntity, TItem>(
        Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<TEntity> entity,
        System.Linq.Expressions.Expression<Func<TEntity, List<TItem>>> propertyExpression,
        string columnName)
        where TEntity : class
        where TItem : class
    {
        var converter = new ValueConverter<List<TItem>, string>(
            v => JsonSerializer.Serialize(v, SocialJsonOptions.Default),
            v => JsonSerializer.Deserialize<List<TItem>>(v, SocialJsonOptions.Default) ?? new List<TItem>());

        var comparer = new ValueComparer<List<TItem>>(
            (a, b) => a!.SequenceEqual(b!),
            x => x.Aggregate(0, (h, e) => HashCode.Combine(h, e!.GetHashCode())),
            x => x.ToList());

        entity.Property(propertyExpression)
            .HasColumnName(columnName)
            .HasColumnType("jsonb")
            .IsRequired()
            .HasDefaultValueSql("'[]'")
            .HasConversion(converter)
            .Metadata.SetValueComparer(comparer);
    }
}

internal static class ModelBuilderPostgresNamingExtensions
{
    public static void UsePostgresSnakeCaseNames(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var tableName = entityType.GetTableName();
            if (!string.IsNullOrWhiteSpace(tableName))
            {
                entityType.SetTableName(ToSnakeCase(tableName));
            }

            var schema = entityType.GetSchema();
            if (!string.IsNullOrWhiteSpace(schema))
            {
                entityType.SetSchema(ToSnakeCase(schema));
            }

            tableName = entityType.GetTableName();
            if (string.IsNullOrWhiteSpace(tableName))
            {
                continue;
            }

            var table = StoreObjectIdentifier.Table(tableName, entityType.GetSchema());

            foreach (var property in entityType.GetProperties())
            {
                var columnName = property.GetColumnName(table) ?? property.Name;
                property.SetColumnName(ToSnakeCase(columnName));
            }

            foreach (var key in entityType.GetKeys())
            {
                var keyName = key.GetName();
                if (!string.IsNullOrWhiteSpace(keyName))
                {
                    key.SetName(ToSnakeCase(keyName));
                }
            }

            foreach (var foreignKey in entityType.GetForeignKeys())
            {
                var constraintName = foreignKey.GetConstraintName();
                if (!string.IsNullOrWhiteSpace(constraintName))
                {
                    foreignKey.SetConstraintName(ToSnakeCase(constraintName));
                }
            }

            foreach (var index in entityType.GetIndexes())
            {
                var databaseName = index.GetDatabaseName();
                if (!string.IsNullOrWhiteSpace(databaseName))
                {
                    index.SetDatabaseName(ToSnakeCase(databaseName));
                }
            }
        }
    }

    private static string ToSnakeCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var builder = new StringBuilder(value.Length + Math.Min(2, value.Length / 5));

        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];

            if (current is '-' or ' ')
            {
                AppendUnderscore(builder);
                continue;
            }

            if (current == '_')
            {
                AppendUnderscore(builder);
                continue;
            }

            if (char.IsUpper(current))
            {
                if (ShouldInsertSeparator(value, index))
                {
                    AppendUnderscore(builder);
                }

                builder.Append(char.ToLowerInvariant(current));
                continue;
            }

            builder.Append(current);
        }

        return builder.ToString();
    }

    private static bool ShouldInsertSeparator(string value, int index)
    {
        if (index == 0)
        {
            return false;
        }

        var previous = value[index - 1];
        if (previous is '_' or '-' or ' ')
        {
            return false;
        }

        if (char.IsLower(previous) || char.IsDigit(previous))
        {
            return true;
        }

        return index + 1 < value.Length && char.IsLower(value[index + 1]);
    }

    private static void AppendUnderscore(StringBuilder builder)
    {
        if (builder.Length > 0 && builder[^1] != '_')
        {
            builder.Append('_');
        }
    }
}
