using Incoders.Hive.Domain.Social.Clients;

namespace Incoders.Hive.Tests.Domain.Social;

public sealed class ClientTests
{
    private static readonly DateTime Now = new(2026, 5, 16, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_Succeeds_WithValidOrganizationAndName()
    {
        var organizationId = Guid.NewGuid();

        var result = Client.Create(ClientId.New(), organizationId, "Acme Social", "Primary account", Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(organizationId, result.Value.OrganizationId);
        Assert.Equal("Acme Social", result.Value.Name);
        Assert.Equal("Primary account", result.Value.Description);
        Assert.True(result.Value.IsActive);
    }

    [Fact]
    public void Create_RejectsMissingOrganization()
    {
        var result = Client.Create(ClientId.New(), Guid.Empty, "Acme Social", null, Now);

        Assert.True(result.IsFailure);
        Assert.Equal("social.clients.organization_required", result.Error.Code);
    }

    [Fact]
    public void Create_RejectsEmptyName()
    {
        var result = Client.Create(ClientId.New(), Guid.NewGuid(), "   ", null, Now);

        Assert.True(result.IsFailure);
        Assert.Equal("social.clients.name_required", result.Error.Code);
    }

    // --- Rename ---

    [Fact]
    public void Rename_Succeeds_WithValidNameAndDescription()
    {
        var client = CreateValidClient();

        var result = client.Rename("New Name", "New Description", Now);

        Assert.True(result.IsSuccess);
        Assert.Equal("New Name", client.Name);
        Assert.Equal("New Description", client.Description);
    }

    [Fact]
    public void Rename_TrimsWhitespaceFromName()
    {
        var client = CreateValidClient();

        var result = client.Rename("  Trimmed  ", null, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal("Trimmed", client.Name);
    }

    [Fact]
    public void Rename_NormalizesEmptyDescriptionToNull()
    {
        var client = CreateValidClient();

        var result = client.Rename("Name", "   ", Now);

        Assert.True(result.IsSuccess);
        Assert.Null(client.Description);
    }

    [Fact]
    public void Rename_RejectsEmptyName()
    {
        var client = CreateValidClient();

        var result = client.Rename("   ", null, Now);

        Assert.True(result.IsFailure);
        Assert.Equal("social.clients.name_required", result.Error.Code);
    }

    [Fact]
    public void Rename_RejectsNameTooLong()
    {
        var client = CreateValidClient();

        var result = client.Rename(new string('x', Client.NameMaxLength + 1), null, Now);

        Assert.True(result.IsFailure);
        Assert.Equal("social.clients.name_too_long", result.Error.Code);
    }

    [Fact]
    public void Rename_RejectsDescriptionTooLong()
    {
        var client = CreateValidClient();

        var result = client.Rename("Valid Name", new string('x', Client.DescriptionMaxLength + 1), Now);

        Assert.True(result.IsFailure);
        Assert.Equal("social.clients.description_too_long", result.Error.Code);
    }

    [Fact]
    public void Rename_UpdatesUpdatedAtUtc()
    {
        var client = CreateValidClient();
        var later = Now.AddHours(1);

        client.Rename("Updated", null, later);

        Assert.Equal(later, client.UpdatedAtUtc);
    }

    // --- Deactivate ---

    [Fact]
    public void Deactivate_TogglesIsActiveToFalse_OnActiveClient()
    {
        var client = CreateValidClient();

        var result = client.Deactivate(Now);

        Assert.True(result.IsSuccess);
        Assert.False(client.IsActive);
    }

    [Fact]
    public void Deactivate_UpdatesUpdatedAtUtc()
    {
        var client = CreateValidClient();
        var later = Now.AddHours(1);

        client.Deactivate(later);

        Assert.Equal(later, client.UpdatedAtUtc);
    }

    [Fact]
    public void Deactivate_ReturnsAlreadyInactive_OnInactiveClient()
    {
        var client = CreateValidClient();
        client.Deactivate(Now);

        var result = client.Deactivate(Now);

        Assert.True(result.IsFailure);
        Assert.Equal("social.clients.already_inactive", result.Error.Code);
    }

    // --- Activate ---

    [Fact]
    public void Activate_TogglesIsActiveToTrue_OnInactiveClient()
    {
        var client = CreateValidClient();
        client.Deactivate(Now);

        var result = client.Activate(Now);

        Assert.True(result.IsSuccess);
        Assert.True(client.IsActive);
    }

    [Fact]
    public void Activate_UpdatesUpdatedAtUtc()
    {
        var client = CreateValidClient();
        client.Deactivate(Now);
        var later = Now.AddHours(1);

        client.Activate(later);

        Assert.Equal(later, client.UpdatedAtUtc);
    }

    [Fact]
    public void Activate_ReturnsAlreadyActive_OnActiveClient()
    {
        var client = CreateValidClient();

        var result = client.Activate(Now);

        Assert.True(result.IsFailure);
        Assert.Equal("social.clients.already_active", result.Error.Code);
    }

    private static Client CreateValidClient()
    {
        var result = Client.Create(ClientId.New(), Guid.NewGuid(), "Acme Social", "Primary account", Now);
        return result.Value;
    }
}
