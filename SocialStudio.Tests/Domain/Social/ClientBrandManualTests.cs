using SocialStudio.Domain.Common;
using SocialStudio.Domain.Social.ClientBrandManuals;
using SocialStudio.Domain.Social.Clients;

namespace SocialStudio.Tests.Domain.Social;

public sealed class ClientBrandManualTests
{
    private static readonly DateTime Now = new(2026, 5, 18, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Later = new(2026, 5, 18, 13, 0, 0, DateTimeKind.Utc);
    private static readonly ClientId ValidClientId = ClientId.New();
    private static readonly Guid ValidOrgId = Guid.NewGuid();

    private static readonly IReadOnlyList<ColorEntry> ValidPalette =
        new[] { ColorEntry.Create("Primary", "#FF0000", ColorRole.Primary, 0).Value };

    // --- Create: happy path ---

    [Fact]
    public void Create_WithValidFullManual_ReturnsActiveManual()
    {
        var result = ClientBrandManual.Create(
            ClientBrandManualId.New(),
            ValidClientId,
            ValidOrgId,
            "Acme Brand 2026",
            ValidPalette,
            null, null, null,
            "Friendly but professional",
            "Updated Q1",
            Now);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsActive);
        Assert.Equal(Now, result.Value.CreatedAtUtc);
        Assert.Equal(Now, result.Value.UpdatedAtUtc);
        Assert.Equal("Acme Brand 2026", result.Value.Title);
        Assert.Equal("Friendly but professional", result.Value.VoiceAndTone);
        Assert.Equal("Updated Q1", result.Value.Notes);
    }

    [Fact]
    public void Create_WithOnlyVoiceAndTone_Succeeds()
    {
        var result = ClientBrandManual.Create(
            ClientBrandManualId.New(), ValidClientId, ValidOrgId,
            "Voice Only Manual", null, null, null, null,
            "Bold and direct", null, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal("Bold and direct", result.Value.VoiceAndTone);
        Assert.Empty(result.Value.ColorPalette);
    }

    [Fact]
    public void Create_WithOnlyNotes_Succeeds()
    {
        var result = ClientBrandManual.Create(
            ClientBrandManualId.New(), ValidClientId, ValidOrgId,
            "Notes Only Manual", null, null, null, null,
            null, "Internal guidelines only", Now);

        Assert.True(result.IsSuccess);
        Assert.Equal("Internal guidelines only", result.Value.Notes);
    }

    [Fact]
    public void Create_WithAllSignalsEmpty_ReturnsEmptyManualError()
    {
        var result = ClientBrandManual.Create(
            ClientBrandManualId.New(), ValidClientId, ValidOrgId,
            "Empty Manual", null, null, null, null,
            null, null, Now);

        Assert.True(result.IsFailure);
        Assert.Equal("social.client_brand_manuals.empty_manual", result.Error.Code);
    }

    [Fact]
    public void Create_WithWhitespaceOnlyVoiceAndTone_ReturnsEmptyManualError()
    {
        var result = ClientBrandManual.Create(
            ClientBrandManualId.New(), ValidClientId, ValidOrgId,
            "Whitespace Voice", null, null, null, null,
            "   ", null, Now);

        Assert.True(result.IsFailure);
        Assert.Equal("social.client_brand_manuals.empty_manual", result.Error.Code);
    }

    [Fact]
    public void Create_TitleTooLong_ReturnsValidationError()
    {
        var result = ClientBrandManual.Create(
            ClientBrandManualId.New(), ValidClientId, ValidOrgId,
            new string('A', 81), ValidPalette, null, null, null,
            null, null, Now);

        Assert.True(result.IsFailure);
        Assert.Equal("social.client_brand_manuals.validation", result.Error.Code);
    }

    [Fact]
    public void Create_TitleEmpty_ReturnsValidationError()
    {
        var result = ClientBrandManual.Create(
            ClientBrandManualId.New(), ValidClientId, ValidOrgId,
            "", ValidPalette, null, null, null,
            null, null, Now);

        Assert.True(result.IsFailure);
        Assert.Equal("social.client_brand_manuals.validation", result.Error.Code);
    }

    [Fact]
    public void Create_TitleTrimsWhitespace()
    {
        var result = ClientBrandManual.Create(
            ClientBrandManualId.New(), ValidClientId, ValidOrgId,
            "  Trimmed Title  ", ValidPalette, null, null, null,
            null, null, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal("Trimmed Title", result.Value.Title);
    }

    [Fact]
    public void Create_VoiceAndToneTooLong_ReturnsValidationError()
    {
        var result = ClientBrandManual.Create(
            ClientBrandManualId.New(), ValidClientId, ValidOrgId,
            "Manual", ValidPalette, null, null, null,
            new string('A', 2001), null, Now);

        Assert.True(result.IsFailure);
        Assert.Equal("social.client_brand_manuals.validation", result.Error.Code);
    }

    [Fact]
    public void Create_NotesTooLong_ReturnsValidationError()
    {
        var result = ClientBrandManual.Create(
            ClientBrandManualId.New(), ValidClientId, ValidOrgId,
            "Manual", ValidPalette, null, null, null,
            null, new string('A', 4001), Now);

        Assert.True(result.IsFailure);
        Assert.Equal("social.client_brand_manuals.validation", result.Error.Code);
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

    // --- ColorEntry ---

    [Fact]
    public void ColorEntry_Create_InvalidHex_ReturnsValidationError()
    {
        var result = ColorEntry.Create("Blue", "#ZZZZZZ", null, 0);
        Assert.True(result.IsFailure);
        Assert.Equal("social.client_brand_manuals.validation", result.Error.Code);
    }

    [Fact]
    public void ColorEntry_Create_ValidThreeCharHex_Succeeds()
    {
        var result = ColorEntry.Create("Red", "#F00", null, 0);
        Assert.True(result.IsSuccess);
        Assert.Equal("#F00", result.Value.Hex);
    }

    [Fact]
    public void ColorEntry_Create_ValidSixCharHex_Succeeds()
    {
        var result = ColorEntry.Create("Blue", "#0055FF", null, 0);
        Assert.True(result.IsSuccess);
        Assert.Equal("#0055FF", result.Value.Hex);
    }

    [Fact]
    public void ColorEntry_Create_ValidEightCharHex_Succeeds()
    {
        var result = ColorEntry.Create("Transparent Blue", "#0055FF80", null, 0);
        Assert.True(result.IsSuccess);
        Assert.Equal("#0055FF80", result.Value.Hex);
    }

    [Fact]
    public void ColorEntry_Create_NameTooLong_ReturnsValidationError()
    {
        var result = ColorEntry.Create(new string('A', 51), "#FF0000", null, 0);
        Assert.True(result.IsFailure);
        Assert.Equal("social.client_brand_manuals.validation", result.Error.Code);
    }

    // --- LogoEntry ---

    [Fact]
    public void LogoEntry_Create_InvalidScheme_ReturnsValidationError()
    {
        var result = LogoEntry.Create("Logo", "ftp://example.com/logo.png", null);
        Assert.True(result.IsFailure);
        Assert.Equal("social.client_brand_manuals.validation", result.Error.Code);
    }

    [Fact]
    public void LogoEntry_Create_UrlTooLong_ReturnsValidationError()
    {
        var url = "https://example.com/" + new string('a', 2030);
        var result = LogoEntry.Create("Logo", url, null);
        Assert.True(result.IsFailure);
        Assert.Equal("social.client_brand_manuals.validation", result.Error.Code);
    }

    [Fact]
    public void LogoEntry_Create_RelativeUrl_ReturnsValidationError()
    {
        var result = LogoEntry.Create("Logo", "/images/logo.png", null);
        Assert.True(result.IsFailure);
        Assert.Equal("social.client_brand_manuals.validation", result.Error.Code);
    }

    // --- BackgroundEntry ---

    [Fact]
    public void BackgroundEntry_Create_NameTooLong_ReturnsValidationError()
    {
        var result = BackgroundEntry.Create(new string('A', 101), "https://example.com/bg.jpg", null);
        Assert.True(result.IsFailure);
        Assert.Equal("social.client_brand_manuals.validation", result.Error.Code);
    }

    [Fact]
    public void BackgroundEntry_Create_ValidUrl_Succeeds()
    {
        var result = BackgroundEntry.Create("Background", "https://example.com/bg.jpg", null);
        Assert.True(result.IsSuccess);
    }

    // --- TypographyEntry ---

    [Fact]
    public void TypographyEntry_Create_FontUrlInvalidScheme_ReturnsValidationError()
    {
        var result = TypographyEntry.Create("Heading", "Inter", null, "ftp://fonts.com/inter.ttf", null);
        Assert.True(result.IsFailure);
        Assert.Equal("social.client_brand_manuals.validation", result.Error.Code);
    }

    [Fact]
    public void TypographyEntry_Create_FontUrlNull_Succeeds()
    {
        var result = TypographyEntry.Create("Body", "Roboto", "body-copy", null, "400");
        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.FontUrl);
        Assert.Equal("400", result.Value.Weight);
    }

    [Fact]
    public void TypographyEntry_Create_FontFamilyTooLong_ReturnsValidationError()
    {
        var result = TypographyEntry.Create("Body", new string('A', 101), null, null, null);
        Assert.True(result.IsFailure);
        Assert.Equal("social.client_brand_manuals.validation", result.Error.Code);
    }

    // --- Update ---

    [Fact]
    public void Update_WithValidInputs_ReplacesAllFieldsAndBumpsUpdatedAt()
    {
        var manual = CreateValid().Value;
        var newPalette = new[] { ColorEntry.Create("Green", "#00FF00", null, 0).Value };

        var result = manual.Update(
            "New Title", newPalette, null, null, null,
            "New voice", "New notes", Later);

        Assert.True(result.IsSuccess);
        Assert.Equal("New Title", manual.Title);
        Assert.Equal("New voice", manual.VoiceAndTone);
        Assert.Equal("New notes", manual.Notes);
        Assert.Equal(Later, manual.UpdatedAtUtc);
        Assert.Single(manual.ColorPalette);
    }

    [Fact]
    public void Update_DoesNotChangeClientId_OrOrganizationId()
    {
        var manual = CreateValid().Value;
        var originalClientId = manual.ClientId;
        var originalOrgId = manual.OrganizationId;

        manual.Update("Updated", ValidPalette, null, null, null, null, null, Later);

        Assert.Equal(originalClientId, manual.ClientId);
        Assert.Equal(originalOrgId, manual.OrganizationId);
    }

    [Fact]
    public void Update_AllSignalsEmpty_ReturnsEmptyManualError()
    {
        var manual = CreateValid().Value;

        var result = manual.Update("Updated", null, null, null, null, null, null, Later);

        Assert.True(result.IsFailure);
        Assert.Equal("social.client_brand_manuals.empty_manual", result.Error.Code);
    }

    // --- Deactivate ---

    [Fact]
    public void Deactivate_WhenActive_SetsIsActiveFalse()
    {
        var manual = CreateValid().Value;

        var result = manual.Deactivate(Later);

        Assert.True(result.IsSuccess);
        Assert.False(manual.IsActive);
        Assert.Equal(Later, manual.UpdatedAtUtc);
    }

    [Fact]
    public void Deactivate_WhenAlreadyInactive_ReturnsAlreadyInactiveError()
    {
        var manual = CreateValid().Value;
        manual.Deactivate(Now);

        var result = manual.Deactivate(Later);

        Assert.True(result.IsFailure);
        Assert.Equal("social.client_brand_manuals.already_inactive", result.Error.Code);
    }

    // --- Activate ---

    [Fact]
    public void Activate_WhenInactive_SetsIsActiveTrue()
    {
        var manual = CreateValid().Value;
        manual.Deactivate(Now);

        var result = manual.Activate(Later);

        Assert.True(result.IsSuccess);
        Assert.True(manual.IsActive);
        Assert.Equal(Later, manual.UpdatedAtUtc);
    }

    [Fact]
    public void Activate_WhenAlreadyActive_ReturnsAlreadyActiveError()
    {
        var manual = CreateValid().Value;

        var result = manual.Activate(Later);

        Assert.True(result.IsFailure);
        Assert.Equal("social.client_brand_manuals.already_active", result.Error.Code);
    }

    // --- Restore ---

    [Fact]
    public void Restore_ReturnsAggregateWithoutValidation()
    {
        // Restore accepts even a blank title (no validation bypass should throw)
        var id = ClientBrandManualId.New();
        var manual = ClientBrandManual.Restore(
            id, ValidClientId, ValidOrgId,
            "Restored Title", new List<ColorEntry>(), new List<LogoEntry>(),
            new List<BackgroundEntry>(), new List<TypographyEntry>(),
            null, null, false, Now, Now);

        Assert.Equal(id, manual.Id);
        Assert.Equal("Restored Title", manual.Title);
        Assert.False(manual.IsActive);
        Assert.Empty(manual.ColorPalette);
    }

    // --- Helpers ---

    private static Result<ClientBrandManual> CreateValid() =>
        ClientBrandManual.Create(
            ClientBrandManualId.New(),
            ValidClientId,
            ValidOrgId,
            "Valid Manual",
            ValidPalette,
            null, null, null,
            null, null,
            Now);
}
