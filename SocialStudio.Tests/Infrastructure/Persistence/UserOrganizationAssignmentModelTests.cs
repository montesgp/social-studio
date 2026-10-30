using SocialStudio.Infrastructure.Identity;
using SocialStudio.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace SocialStudio.Tests.Infrastructure.Persistence;

public class UserOrganizationAssignmentModelTests
{
    [Fact]
    public void OrganizationMembership_EnforcesOneOrganizationPerUser()
    {
        using var db = CreateDbContext();

        var membershipType = db.Model.FindEntityType(typeof(TenantMembershipData));
        Assert.NotNull(membershipType);

        var userIndex = Assert.Single(
            membershipType!.GetIndexes(),
            index => index.GetDatabaseName() == "ix_organization_memberships_user_id_unique");

        Assert.True(userIndex.IsUnique);
        Assert.Equal(["UserId"], userIndex.Properties.Select(property => property.Name));
    }

    [Fact]
    public void OrganizationUserModules_CascadeFromMembership()
    {
        using var db = CreateDbContext();

        var moduleType = db.Model.FindEntityType(typeof(OrganizationUserModuleData));
        Assert.NotNull(moduleType);

        var membershipForeignKey = Assert.Single(
            moduleType!.GetForeignKeys(),
            foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(TenantMembershipData));

        Assert.Equal(DeleteBehavior.Cascade, membershipForeignKey.DeleteBehavior);
        Assert.Equal(["OrganizationId", "UserId"], membershipForeignKey.Properties.Select(property => property.Name));
    }

    private static AppIdentityDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppIdentityDbContext>()
            .UseInMemoryDatabase($"user-organization-assignment-{Guid.NewGuid():N}")
            .Options;

        return new AppIdentityDbContext(options);
    }
}
