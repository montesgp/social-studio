using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ClientReferentAccounts;
using Incoders.Hive.Domain.Social.Clients;
using Incoders.Hive.Domain.Social.Networks;

namespace Incoders.Hive.Tests.Domain.Social;

public sealed class ClientReferentAccountTests
{
    private static readonly DateTime Now = new(2026, 5, 18, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Later = new(2026, 5, 18, 13, 0, 0, DateTimeKind.Utc);

    private static readonly ClientId ValidClientId = ClientId.New();
    private static readonly Guid ValidOrgId = Guid.NewGuid();
    private static readonly SocialNetworkId ValidNetworkId = SocialNetworkId.New();
    private const string ValidProfileUrl = "https://www.instagram.com/brand";

    // --- Create: happy path ---

    [Fact]
    public void Create_WithValidInputs_ReturnsActiveReferentAccount()
    {
        var result = ClientReferentAccount.Create(
            ClientReferentAccountId.New(),
            ValidClientId,
            ValidOrgId,
            ValidNetworkId,
            ValidProfileUrl,
            "@brand",
            "Main IG",
            "Key competitor",
            Now);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsActive);
        Assert.Equal(Now, result.Value.CreatedAtUtc);
        Assert.Equal(Now, result.Value.UpdatedAtUtc);
        Assert.Equal("https://www.instagram.com/brand", result.Value.ProfileUrl);
        Assert.Equal("@brand", result.Value.Handle);
        Assert.Equal("Main IG", result.Value.Label);
    }

    [Fact]
    public void Create_SetsIsActiveTrue()
    {
        var result = CreateValid();
        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsActive);
    }

    [Fact]
    public void Create_SetsCreatedAndUpdatedAtToNow()
    {
        var result = CreateValid();
        Assert.Equal(Now, result.Value.CreatedAtUtc);
        Assert.Equal(Now, result.Value.UpdatedAtUtc);
    }

    [Fact]
    public void Create_WithNullOptionalFields_Succeeds()
    {
        var result = ClientReferentAccount.Create(
            ClientReferentAccountId.New(),
            ValidClientId,
            ValidOrgId,
            ValidNetworkId,
            ValidProfileUrl,
            null,
            null,
            null,
            Now);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Handle);
        Assert.Null(result.Value.Label);
        Assert.Null(result.Value.Notes);
    }

    [Fact]
    public void Create_TrimsProfileUrl()
    {
        var result = ClientReferentAccount.Create(
            ClientReferentAccountId.New(),
            ValidClientId,
            ValidOrgId,
            ValidNetworkId,
            "  https://www.instagram.com/brand  ",
            null, null, null, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal("https://www.instagram.com/brand", result.Value.ProfileUrl);
    }

    // --- Create: ProfileUrl validation ---

    [Fact]
    public void Create_WithEmptyProfileUrl_ReturnsValidationError()
    {
        var result = ClientReferentAccount.Create(
            ClientReferentAccountId.New(),
            ValidClientId, ValidOrgId, ValidNetworkId,
            "", null, null, null, Now);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    [Fact]
    public void Create_WithRelativeProfileUrl_ReturnsValidationError()
    {
        var result = ClientReferentAccount.Create(
            ClientReferentAccountId.New(),
            ValidClientId, ValidOrgId, ValidNetworkId,
            "/instagram/brand", null, null, null, Now);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    [Fact]
    public void Create_WithNonHttpSchemeProfileUrl_ReturnsValidationError()
    {
        var result = ClientReferentAccount.Create(
            ClientReferentAccountId.New(),
            ValidClientId, ValidOrgId, ValidNetworkId,
            "ftp://example.com/brand", null, null, null, Now);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    [Fact]
    public void Create_WithProfileUrlTooShort_ReturnsValidationError()
    {
        // "https://x" is 9 chars — below minimum 10
        var result = ClientReferentAccount.Create(
            ClientReferentAccountId.New(),
            ValidClientId, ValidOrgId, ValidNetworkId,
            "https://x", null, null, null, Now);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    [Fact]
    public void Create_WithProfileUrlTooLong_ReturnsValidationError()
    {
        var longUrl = "https://example.com/" + new string('a', 2030);
        var result = ClientReferentAccount.Create(
            ClientReferentAccountId.New(),
            ValidClientId, ValidOrgId, ValidNetworkId,
            longUrl, null, null, null, Now);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    // --- Create: Handle validation ---

    [Fact]
    public void Create_WithHandleContainingInternalWhitespace_ReturnsValidationError()
    {
        var result = ClientReferentAccount.Create(
            ClientReferentAccountId.New(),
            ValidClientId, ValidOrgId, ValidNetworkId,
            ValidProfileUrl, "my brand", null, null, Now);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    [Fact]
    public void Create_WithHandleExceedingMaxLength_ReturnsValidationError()
    {
        var longHandle = new string('a', 101);
        var result = ClientReferentAccount.Create(
            ClientReferentAccountId.New(),
            ValidClientId, ValidOrgId, ValidNetworkId,
            ValidProfileUrl, longHandle, null, null, Now);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    // --- Create: Label validation ---

    [Fact]
    public void Create_WithLabelExceedingMaxLength_ReturnsValidationError()
    {
        var longLabel = new string('x', 81);
        var result = ClientReferentAccount.Create(
            ClientReferentAccountId.New(),
            ValidClientId, ValidOrgId, ValidNetworkId,
            ValidProfileUrl, null, longLabel, null, Now);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    // --- Create: Notes validation ---

    [Fact]
    public void Create_WithNotesExceedingMaxLength_ReturnsValidationError()
    {
        var longNotes = new string('x', 501);
        var result = ClientReferentAccount.Create(
            ClientReferentAccountId.New(),
            ValidClientId, ValidOrgId, ValidNetworkId,
            ValidProfileUrl, null, null, longNotes, Now);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    // --- Update ---

    [Fact]
    public void Update_WithValidInputs_UpdatesAllEditableFieldsAndBumpsUpdatedAt()
    {
        var account = CreateValid().Value;

        var result = account.Update(
            "https://www.tiktok.com/brand2",
            "@brand2",
            "New Label",
            "New notes",
            Later);

        Assert.True(result.IsSuccess);
        Assert.Equal("https://www.tiktok.com/brand2", account.ProfileUrl);
        Assert.Equal("@brand2", account.Handle);
        Assert.Equal("New Label", account.Label);
        Assert.Equal("New notes", account.Notes);
        Assert.Equal(Later, account.UpdatedAtUtc);
    }

    [Fact]
    public void Update_DoesNotChangeClientId_OrOrganizationId_OrSocialNetworkId()
    {
        var account = CreateValid().Value;
        var originalClientId = account.ClientId;
        var originalOrgId = account.OrganizationId;
        var originalNetworkId = account.SocialNetworkId;

        account.Update("https://www.tiktok.com/brand2", null, null, null, Later);

        Assert.Equal(originalClientId, account.ClientId);
        Assert.Equal(originalOrgId, account.OrganizationId);
        Assert.Equal(originalNetworkId, account.SocialNetworkId);
    }

    [Fact]
    public void Update_WithInvalidProfileUrl_ReturnsValidationError()
    {
        var account = CreateValid().Value;

        var result = account.Update("not-a-url", null, null, null, Later);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    // --- Deactivate ---

    [Fact]
    public void Deactivate_WhenActive_SetsIsActiveFalse()
    {
        var account = CreateValid().Value;

        var result = account.Deactivate(Later);

        Assert.True(result.IsSuccess);
        Assert.False(account.IsActive);
        Assert.Equal(Later, account.UpdatedAtUtc);
    }

    [Fact]
    public void Deactivate_WhenAlreadyInactive_ReturnsAlreadyInactiveError()
    {
        var account = CreateValid().Value;
        account.Deactivate(Now);

        var result = account.Deactivate(Later);

        Assert.True(result.IsFailure);
        Assert.Equal("social.client_referent_accounts.already_inactive", result.Error.Code);
    }

    // --- Activate ---

    [Fact]
    public void Activate_WhenInactive_SetsIsActiveTrue()
    {
        var account = CreateValid().Value;
        account.Deactivate(Now);

        var result = account.Activate(Later);

        Assert.True(result.IsSuccess);
        Assert.True(account.IsActive);
        Assert.Equal(Later, account.UpdatedAtUtc);
    }

    [Fact]
    public void Activate_WhenAlreadyActive_ReturnsAlreadyActiveError()
    {
        var account = CreateValid().Value;

        var result = account.Activate(Later);

        Assert.True(result.IsFailure);
        Assert.Equal("social.client_referent_accounts.already_active", result.Error.Code);
    }

    // --- Restore ---

    [Fact]
    public void Restore_ReturnsAggregateWithoutValidation()
    {
        var id = ClientReferentAccountId.New();
        // Even an otherwise-invalid profile URL is accepted by Restore
        var account = ClientReferentAccount.Restore(
            id, ValidClientId, ValidOrgId, ValidNetworkId,
            "stored://trusted-data", null, null, null,
            false, Now, Now);

        Assert.Equal(id, account.Id);
        Assert.False(account.IsActive);
        Assert.Equal("stored://trusted-data", account.ProfileUrl);
    }

    // --- Helpers ---

    private static Result<ClientReferentAccount> CreateValid() =>
        ClientReferentAccount.Create(
            ClientReferentAccountId.New(),
            ValidClientId,
            ValidOrgId,
            ValidNetworkId,
            ValidProfileUrl,
            "@brand",
            "Main IG",
            "Key competitor",
            Now);
}
