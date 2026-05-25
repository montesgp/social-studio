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

    public DbSet<OrganizationIntegrationRoutingPolicyData> OrganizationIntegrationRoutingPolicies => Set<OrganizationIntegrationRoutingPolicyData>();

    public DbSet<OrganizationIntegrationRoutingPolicyItemData> OrganizationIntegrationRoutingPolicyItems => Set<OrganizationIntegrationRoutingPolicyItemData>();

    public DbSet<SocialNetworkData> SocialNetworks => Set<SocialNetworkData>();

    public DbSet<ClientReferentAccountData> ClientReferentAccounts => Set<ClientReferentAccountData>();

    public DbSet<ClientBrandManualData> ClientBrandManuals => Set<ClientBrandManualData>();

    public DbSet<ClientStrategyData> ClientStrategies => Set<ClientStrategyData>();

    public DbSet<MonthlyObjectiveData> MonthlyObjectives => Set<MonthlyObjectiveData>();

    public DbSet<SingleContentAnalysisData> SingleContentAnalyses => Set<SingleContentAnalysisData>();

    public DbSet<IntegrationExecutionAttemptData> IntegrationExecutionAttempts => Set<IntegrationExecutionAttemptData>();

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

        builder.Entity<OrganizationIntegrationRoutingPolicyData>(entity =>
        {
            entity.ToTable("organization_integration_routing_policies", PostgresDatabaseNames.PlatformSchema);
            entity.HasKey(x => x.Id);
            entity.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired();
            entity.Property(x => x.FlowKey).HasMaxLength(64).IsRequired();
            entity.Property(x => x.CapabilityKey).HasMaxLength(64).IsRequired();
            entity.Property(x => x.PlatformKey).HasMaxLength(64);
            entity.Property(x => x.IsEnabled).IsRequired();
            entity.HasIndex(x => new { x.OrganizationId, x.FlowKey, x.CapabilityKey })
                .IsUnique()
                .HasFilter("platform_key IS NULL")
                .HasDatabaseName("ix_org_routing_policies_org_flow_capability_global");
            entity.HasIndex(x => new { x.OrganizationId, x.FlowKey, x.CapabilityKey, x.PlatformKey })
                .IsUnique()
                .HasFilter("platform_key IS NOT NULL")
                .HasDatabaseName("ix_org_routing_policies_org_flow_capability_platform");

            entity.HasOne(x => x.Organization)
                .WithMany()
                .HasForeignKey(x => x.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<OrganizationIntegrationRoutingPolicyItemData>(entity =>
        {
            entity.ToTable("organization_integration_routing_policy_items", PostgresDatabaseNames.PlatformSchema);
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ConnectionMode).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Priority).IsRequired();
            entity.Property(x => x.AllowManagedFallback).IsRequired();
            entity.Property(x => x.MaxAttempts).IsRequired();
            entity.Property(x => x.TimeoutMs).IsRequired();
            entity.Property(x => x.SelectedModel).HasMaxLength(200);
            entity.Property(x => x.IsEnabled).IsRequired();
            entity.HasIndex(x => new { x.PolicyId, x.Priority })
                .IsUnique()
                .HasDatabaseName("ix_org_routing_policy_items_policy_priority");

            entity.HasOne(x => x.Policy)
                .WithMany(x => x.Items)
                .HasForeignKey(x => x.PolicyId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Provider)
                .WithMany(x => x.RoutingPolicyItems)
                .HasForeignKey(x => x.ProviderId)
                .OnDelete(DeleteBehavior.Restrict);
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

        builder.Entity<ClientStrategyData>(entity =>
        {
            entity.ToTable("client_strategies", PostgresDatabaseNames.SocialSchema);
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.ClientId).HasColumnName("client_id").IsRequired();
            entity.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired();
            entity.Property(x => x.TargetAudience).HasColumnName("target_audience").HasMaxLength(1000);
            entity.Property(x => x.PostingFrequency).HasColumnName("posting_frequency").HasMaxLength(200);
            entity.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(2000);
            entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
            entity.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();

            // jsonb columns for list fields
            ConfigureJsonbCollection<ClientStrategyData, string>(entity, x => x.ContentPillars, "content_pillars");
            ConfigureJsonbCollection<ClientStrategyData, string>(entity, x => x.ToneKeywords, "tone_keywords");

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

            // UNIQUE: one strategy per client
            entity.HasIndex(x => x.ClientId)
                .IsUnique()
                .HasDatabaseName("ix_client_strategies_one_per_client");

            entity.HasIndex(x => x.OrganizationId)
                .HasDatabaseName("ix_client_strategies_organization_id");
        });

        builder.Entity<MonthlyObjectiveData>(entity =>
        {
            entity.ToTable("monthly_objectives", PostgresDatabaseNames.SocialSchema);
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.ClientId).HasColumnName("client_id").IsRequired();
            entity.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired();
            entity.Property(x => x.Year).HasColumnName("year").IsRequired();
            entity.Property(x => x.Month).HasColumnName("month").IsRequired();
            entity.Property(x => x.Objectives).HasColumnName("objectives").HasMaxLength(3000).IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
            entity.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();

            // jsonb column for KPIs list
            ConfigureJsonbCollection<MonthlyObjectiveData, string>(entity, x => x.Kpis, "kpis");

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

            // UNIQUE: one objective per (client, year, month)
            entity.HasIndex(x => new { x.ClientId, x.Year, x.Month })
                .IsUnique()
                .HasDatabaseName("ix_monthly_objectives_client_year_month");

            entity.HasIndex(x => x.ClientId)
                .HasDatabaseName("ix_monthly_objectives_client_id");

            entity.HasIndex(x => x.OrganizationId)
                .HasDatabaseName("ix_monthly_objectives_organization_id");
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
