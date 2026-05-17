using System.Text;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Incoders.Hive.Infrastructure.Persistence.Postgres;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;

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
