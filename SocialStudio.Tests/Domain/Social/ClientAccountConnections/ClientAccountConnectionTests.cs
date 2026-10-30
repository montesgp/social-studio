using SocialStudio.Domain.Integrations;
using SocialStudio.Domain.Social.ClientAccountConnections;
using SocialStudio.Domain.Social.Clients;
using SocialStudio.Domain.Social.Networks;

namespace SocialStudio.Tests.Domain.Social.ClientAccountConnections;

public sealed class ClientAccountConnectionTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly ClientId Client = ClientId.New();
    private static readonly SocialNetworkId Network = SocialNetworkId.New();
    private static readonly OrganizationIntegrationId Integration = OrganizationIntegrationId.New();
    private static readonly DateTime Now = new(2026, 7, 20, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_RejectsEmptyExternalAccountId()
    {
        var result = ClientAccountConnection.Create(
            ClientAccountConnectionId.New(), Org, Client, Network, Integration,
            string.Empty, "cipher-text", null, Now);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Create_RejectsEmptyCiphertext()
    {
        var result = ClientAccountConnection.Create(
            ClientAccountConnectionId.New(), Org, Client, Network, Integration,
            "ig-business-123", string.Empty, null, Now);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Create_RejectsEmptyOrganizationIntegrationId()
    {
        var result = ClientAccountConnection.Create(
            ClientAccountConnectionId.New(), Org, Client, Network, default,
            "ig-business-123", "cipher-text", null, Now);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Create_Succeeds_WithValidInputs()
    {
        var result = ClientAccountConnection.Create(
            ClientAccountConnectionId.New(), Org, Client, Network, Integration,
            "ig-business-123", "cipher-text", Now.AddDays(60), Now);

        Assert.True(result.IsSuccess);
        var connection = result.Value;
        Assert.Equal("ig-business-123", connection.ExternalAccountId);
        Assert.Equal("cipher-text", connection.EncryptedAccessToken);
        Assert.Equal(ClientAccountConnectionStatus.Connected, connection.Status);
    }

    [Fact]
    public void RotateToken_ReplacesCiphertextAndExpiry()
    {
        var connection = ClientAccountConnection.Create(
            ClientAccountConnectionId.New(), Org, Client, Network, Integration,
            "ig-business-123", "old-cipher", Now.AddDays(10), Now).Value;

        var result = connection.RotateToken("new-cipher", Now.AddDays(90), Now.AddDays(1));

        Assert.True(result.IsSuccess);
        Assert.Equal("new-cipher", connection.EncryptedAccessToken);
        Assert.Equal(Now.AddDays(90), connection.TokenExpiresAtUtc);
    }

    [Fact]
    public void RotateToken_RejectsEmptyCiphertext()
    {
        var connection = ClientAccountConnection.Create(
            ClientAccountConnectionId.New(), Org, Client, Network, Integration,
            "ig-business-123", "old-cipher", null, Now).Value;

        var result = connection.RotateToken(string.Empty, null, Now.AddDays(1));

        Assert.True(result.IsFailure);
        Assert.Equal("old-cipher", connection.EncryptedAccessToken);
    }

    [Fact]
    public void MarkTestResult_SetsConnected_OnSuccess()
    {
        var connection = ClientAccountConnection.Create(
            ClientAccountConnectionId.New(), Org, Client, Network, Integration,
            "ig-business-123", "cipher", null, Now).Value;

        connection.MarkTestResult(true, null, Now.AddMinutes(5));

        Assert.Equal(ClientAccountConnectionStatus.Connected, connection.Status);
        Assert.Null(connection.LastError);
        Assert.Equal(Now.AddMinutes(5), connection.LastTestedAtUtc);
    }

    [Fact]
    public void MarkTestResult_SetsFailed_OnFailure_WithLastError()
    {
        var connection = ClientAccountConnection.Create(
            ClientAccountConnectionId.New(), Org, Client, Network, Integration,
            "ig-business-123", "cipher", null, Now).Value;

        connection.MarkTestResult(false, "token expired", Now.AddMinutes(5));

        Assert.Equal(ClientAccountConnectionStatus.Failed, connection.Status);
        Assert.Equal("token expired", connection.LastError);
    }

    [Fact]
    public void Disable_RejectsFurtherTokenRotation()
    {
        var connection = ClientAccountConnection.Create(
            ClientAccountConnectionId.New(), Org, Client, Network, Integration,
            "ig-business-123", "cipher", null, Now).Value;

        var disable = connection.Disable(Now.AddMinutes(5));
        Assert.True(disable.IsSuccess);
        Assert.Equal(ClientAccountConnectionStatus.Disabled, connection.Status);

        var rotate = connection.RotateToken("new-cipher", null, Now.AddMinutes(6));
        Assert.True(rotate.IsFailure);
    }
}
