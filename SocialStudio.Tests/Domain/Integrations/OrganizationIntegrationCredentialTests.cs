using SocialStudio.Domain.Integrations;

namespace SocialStudio.Tests.Domain.Integrations;

public sealed class OrganizationIntegrationCredentialTests
{
    private static readonly DateTime Now = new(2026, 5, 23, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_NormalizesCredentialKeyAndStoresEncryptedValue()
    {
        var result = OrganizationIntegrationCredential.Create(
            OrganizationIntegrationCredentialId.New(),
            OrganizationIntegrationId.New(),
            " API_KEY ",
            "encrypted-value",
            Now);

        Assert.True(result.IsSuccess);
        Assert.Equal("api_key", result.Value.Key);
        Assert.Equal("encrypted-value", result.Value.EncryptedValue);
        Assert.Equal(Now, result.Value.CreatedAtUtc);
        Assert.Equal(Now, result.Value.UpdatedAtUtc);
    }

    [Fact]
    public void Create_RejectsEmptyIntegrationId()
    {
        var result = OrganizationIntegrationCredential.Create(
            OrganizationIntegrationCredentialId.New(),
            new OrganizationIntegrationId(Guid.Empty),
            "api_key",
            "encrypted-value",
            Now);

        Assert.True(result.IsFailure);
        Assert.Equal("integrations.organization_integration_not_found", result.Error.Code);
    }

    [Fact]
    public void Create_RejectsMissingEncryptedValue()
    {
        var result = OrganizationIntegrationCredential.Create(
            OrganizationIntegrationCredentialId.New(),
            OrganizationIntegrationId.New(),
            "api_key",
            " ",
            Now);

        Assert.True(result.IsFailure);
        Assert.Equal("integrations.credential_value_required", result.Error.Code);
    }

    [Fact]
    public void Rotate_ReplacesEncryptedValueAndBumpsUpdatedAt()
    {
        var credential = OrganizationIntegrationCredential.Create(
            OrganizationIntegrationCredentialId.New(),
            OrganizationIntegrationId.New(),
            "api_key",
            "old-encrypted-value",
            Now).Value;
        var later = Now.AddMinutes(5);

        var result = credential.Rotate("new-encrypted-value", later);

        Assert.True(result.IsSuccess);
        Assert.Equal("new-encrypted-value", credential.EncryptedValue);
        Assert.Equal(later, credential.UpdatedAtUtc);
    }
}
