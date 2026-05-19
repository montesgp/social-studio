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

    public DbSet<SystemSettingData> SystemSettings => Set<SystemSettingData>();

    public DbSet<SocialClientData> SocialClients => Set<SocialClientData>();

    public DbSet<UserPreferenceData> UserPreferences => Set<UserPreferenceData>();

    public DbSet<IntegrationProviderData> IntegrationProviders => Set<IntegrationProviderData>();

    public DbSet<OrganizationIntegrationData> OrganizationIntegrations => Set<OrganizationIntegrationData>();

    public DbSet<OrganizationIntegrationCredentialData> OrganizationIntegrationCredentials => Set<OrganizationIntegrationCredentialData>();

    public DbSet<SocialNetworkData> SocialNetworks => Set<SocialNetworkData>();

    public DbSet<ClientReferentAccountData> ClientReferentAccounts => Set<ClientReferentAccountData>();

    public DbSet<ClientBrandManualData> ClientBrandManuals => Set<ClientBrandManualData>();

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
            ConfigureJsonbCollection<ColorEntry>(entity, x => x.ColorPalette, "color_palette");
            ConfigureJsonbCollection<LogoEntry>(entity, x => x.Logos, "logos");
            ConfigureJsonbCollection<BackgroundEntry>(entity, x => x.BackgroundTemplates, "background_templates");
            ConfigureJsonbCollection<TypographyEntry>(entity, x => x.Typography, "typography");

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
    }

    /// <summary>
    /// Configures a jsonb column with ValueConverter (System.Text.Json) and ValueComparer (deep-equal).
    /// Resolution 4: null deserializes to empty list. Resolution 5: pinned options via BrandManualJsonOptions.Default.
    /// Resolution 6: comparer snapshot uses ToList() so Update replaces refs with new List instances.
    /// </summary>
    private static void ConfigureJsonbCollection<T>(
        Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<ClientBrandManualData> entity,
        System.Linq.Expressions.Expression<Func<ClientBrandManualData, List<T>>> propertyExpression,
        string columnName)
        where T : class
    {
        var converter = new ValueConverter<List<T>, string>(
            v => JsonSerializer.Serialize(v, BrandManualJsonOptions.Default),
            v => JsonSerializer.Deserialize<List<T>>(v, BrandManualJsonOptions.Default) ?? new List<T>());

        var comparer = new ValueComparer<List<T>>(
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
