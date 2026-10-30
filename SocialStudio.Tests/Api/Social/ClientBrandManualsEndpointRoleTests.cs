using SocialStudio.Application.Features.Social.ClientBrandManuals;
using SocialStudio.Application.Features.Social.ClientBrandManuals.Dtos;
using SocialStudio.Domain.Social.ClientBrandManuals;
using SocialStudio.Domain.Social.Clients;

namespace SocialStudio.Tests.Api.Social;

/// <summary>
/// WU-8 TDD tests — ColorRole + Rank in brand-manual API surface (BSP-08, design §2.4).
///
/// These tests verify that the ClientBrandManualResponse exposes role + rank on each
/// ColorPalette entry, and that ColorEntryDto carries role + rank for create/update requests.
///
/// Since WU-1 already wired ColorRole + Rank into the domain model and the Application
/// response/DTO types, these tests are GREEN immediately after WU-8.4 production changes
/// (no new types needed — just verifying the existing contract is complete for the API surface).
///
/// Tests assert:
///   a) ClientBrandManualResponse.ColorPalette entries include Role and Rank (API GET contract).
///   b) ColorEntryDto carries Role and Rank for create/update input (API POST/PUT contract).
///   c) Round-trip: dto → domain → response preserves role + rank values.
/// </summary>
public sealed class ClientBrandManualsEndpointRoleTests
{
    private static readonly Guid ClientId = Guid.Parse("11111111-1111-1111-1111-000000000001");
    private static readonly Guid OrgId = Guid.Parse("22222222-2222-2222-2222-000000000002");
    private static readonly Guid ManualId = Guid.Parse("33333333-3333-3333-3333-000000000003");

    // ─── GET: response includes role + rank ───────────────────────────────────

    /// <summary>
    /// ClientBrandManualResponse must expose ColorRole and Rank on each ColorPalette entry.
    /// </summary>
    [Fact]
    public void ClientBrandManualResponse_ColorPalette_ExposesRoleAndRank()
    {
        var manual = BuildManualWithPalette(
            ColorEntry.Restore("Brand Blue", "#0033FF", ColorRole.Primary, 0),
            ColorEntry.Restore("Brand Accent", "#FF6600", ColorRole.Accent, 1));

        var response = ClientBrandManualResponse.FromDomain(manual);

        Assert.Equal(2, response.ColorPalette.Count);

        var primary = response.ColorPalette[0];
        Assert.Equal(ColorRole.Primary, primary.Role);
        Assert.Equal(0, primary.Rank);
        Assert.Equal("#0033FF", primary.Hex);

        var accent = response.ColorPalette[1];
        Assert.Equal(ColorRole.Accent, accent.Role);
        Assert.Equal(1, accent.Rank);
        Assert.Equal("#FF6600", accent.Hex);
    }

    [Fact]
    public void ClientBrandManualResponse_ColorPalette_NullRole_IsPreserved()
    {
        var manual = BuildManualWithPalette(
            ColorEntry.Restore("Unclassified", "#CCCCCC", null, 0));

        var response = ClientBrandManualResponse.FromDomain(manual);

        Assert.Single(response.ColorPalette);
        Assert.Null(response.ColorPalette[0].Role);
        Assert.Equal(0, response.ColorPalette[0].Rank);
    }

    // ─── ColorEntryDto: carries role + rank for input ─────────────────────────

    /// <summary>
    /// ColorEntryDto must have Role and Rank for the create/update API inputs.
    /// </summary>
    [Fact]
    public void ColorEntryDto_HasRoleAndRank_Properties()
    {
        var dto = new ColorEntryDto("Brand Blue", "#0033FF", ColorRole.Secondary, Rank: 2);

        Assert.Equal(ColorRole.Secondary, dto.Role);
        Assert.Equal(2, dto.Rank);
    }

    [Fact]
    public void ColorEntryDto_DefaultRank_IsZero()
    {
        var dto = new ColorEntryDto("Blue", "#0000FF", ColorRole.Primary);

        Assert.Equal(0, dto.Rank);
    }

    [Fact]
    public void ColorEntryDto_NullRole_IsSupported()
    {
        var dto = new ColorEntryDto("Mystery", "#AABBCC", Role: null, Rank: 5);

        Assert.Null(dto.Role);
        Assert.Equal(5, dto.Rank);
    }

    // ─── Round-trip: DTO → Domain → Response ─────────────────────────────────

    /// <summary>
    /// Converting a ColorEntryDto with role + rank through the domain and back into the
    /// response must preserve the exact role and rank values.
    /// </summary>
    [Fact]
    public void ColorEntryDto_ToDomain_ToResponse_PreservesRoleAndRank()
    {
        var dto = new ColorEntryDto("Primary Blue", "#001F5B", ColorRole.Primary, Rank: 0);

        // Convert DTO → domain ColorEntry
        var result = ColorEntry.Create(dto.Name, dto.Hex, dto.Role, dto.Rank);
        Assert.True(result.IsSuccess);
        var entry = result.Value;

        // Domain entry carries the role + rank
        Assert.Equal(ColorRole.Primary, entry.Role);
        Assert.Equal(0, entry.Rank);

        // Build a response from a manual that contains this entry
        var manual = BuildManualWithPalette(entry);
        var response = ClientBrandManualResponse.FromDomain(manual);

        Assert.Equal(ColorRole.Primary, response.ColorPalette[0].Role);
        Assert.Equal(0, response.ColorPalette[0].Rank);
        Assert.Equal("#001F5B", response.ColorPalette[0].Hex);
    }

    // ─── All ColorRole values are accepted by the DTO ────────────────────────

    [Theory]
    [InlineData(ColorRole.Primary)]
    [InlineData(ColorRole.Secondary)]
    [InlineData(ColorRole.Accent)]
    [InlineData(ColorRole.Neutral)]
    public void ColorEntryDto_AllColorRoleValues_AreAccepted(ColorRole role)
    {
        var dto = new ColorEntryDto("Color", "#123456", role, 0);
        Assert.Equal(role, dto.Role);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static ClientBrandManual BuildManualWithPalette(params ColorEntry[] palette)
    {
        var result = ClientBrandManual.Create(
            new ClientBrandManualId(ManualId),
            new ClientId(ClientId),
            OrgId,
            "Brand Guide v1",
            palette.ToList(),
            logos: [],
            backgroundTemplates: [],
            typography: [],
            voiceAndTone: null,
            notes: null,
            DateTime.UtcNow);
        return result.Value;
    }
}
