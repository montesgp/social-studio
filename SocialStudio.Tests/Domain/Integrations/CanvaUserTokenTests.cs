using SocialStudio.Domain.Integrations;

namespace SocialStudio.Tests.Domain.Integrations;

/// <summary>
/// WU-4 RED tests for <see cref="CanvaUserToken"/> domain entity and <see cref="CanvaUserTokenId"/> value object.
/// These tests fail until the domain entity is created (TDD RED phase).
/// </summary>
public sealed class CanvaUserTokenTests
{
    private static readonly DateTime Now = new(2026, 6, 6, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void CanvaUserToken_Create_SetsAllFields()
    {
        var id = CanvaUserTokenId.New();
        var userId = Guid.NewGuid();
        var orgIntegrationId = Guid.NewGuid();
        const string encryptedAccess = "enc-access-token";
        const string encryptedRefresh = "enc-refresh-token";
        var expiresAt = Now.AddHours(1);
        const string scopes = "design:content:read design:content:write";

        var token = CanvaUserToken.Create(
            id,
            userId,
            orgIntegrationId,
            encryptedAccess,
            encryptedRefresh,
            expiresAt,
            scopes,
            Now);

        Assert.Equal(id, token.Id);
        Assert.Equal(userId, token.UserId);
        Assert.Equal(orgIntegrationId, token.OrganizationIntegrationId);
        Assert.Equal(encryptedAccess, token.EncryptedAccessToken);
        Assert.Equal(encryptedRefresh, token.EncryptedRefreshToken);
        Assert.Equal(expiresAt, token.AccessTokenExpiresAtUtc);
        Assert.Equal(scopes, token.Scopes);
        Assert.Equal(Now, token.CreatedAtUtc);
        Assert.Equal(Now, token.UpdatedAtUtc);
    }

    [Fact]
    public void CanvaUserTokenId_IsValueObject_EqualityByValue()
    {
        var guid = Guid.NewGuid();
        var idA = new CanvaUserTokenId(guid);
        var idB = new CanvaUserTokenId(guid);
        var idC = CanvaUserTokenId.New();

        Assert.Equal(idA, idB);
        Assert.NotEqual(idA, idC);
        Assert.Equal(idA.GetHashCode(), idB.GetHashCode());
    }

    [Fact]
    public void CanvaUserTokenId_New_GeneratesUniqueGuids()
    {
        var idA = CanvaUserTokenId.New();
        var idB = CanvaUserTokenId.New();

        Assert.NotEqual(idA, idB);
        Assert.NotEqual(Guid.Empty, idA.Value);
    }

    [Fact]
    public void CanvaUserToken_Rotate_UpdatesTokensAndBumpsUpdatedAt()
    {
        var token = BuildToken();
        var later = Now.AddHours(2);
        const string newAccess = "new-enc-access";
        const string newRefresh = "new-enc-refresh";
        var newExpiry = later.AddHours(1);

        token.Rotate(newAccess, newRefresh, newExpiry, later);

        Assert.Equal(newAccess, token.EncryptedAccessToken);
        Assert.Equal(newRefresh, token.EncryptedRefreshToken);
        Assert.Equal(newExpiry, token.AccessTokenExpiresAtUtc);
        Assert.Equal(later, token.UpdatedAtUtc);
        Assert.Equal(Now, token.CreatedAtUtc); // CreatedAt must NOT change
    }

    private static CanvaUserToken BuildToken() =>
        CanvaUserToken.Create(
            CanvaUserTokenId.New(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "enc-access",
            "enc-refresh",
            Now.AddHours(1),
            "design:content:read",
            Now);
}
