using Incoders.Hive.Application.Features.Social.Clients;
using Incoders.Hive.Application.Features.Social.Clients.Create;
using Incoders.Hive.Application.Features.Social.Clients.Update;
using Incoders.Hive.Domain.Social.Clients;

namespace Incoders.Hive.Tests.Api.Social;

/// <summary>
/// WU-7 TDD tests — AestheticRefreshThresholdDays on the social-clients API surface (CAG-CS-02).
///
/// Pattern mirrors SocialClientsEndpointPolicyTests: tests target the Application layer
/// (SocialClientResponse, CreateSocialClientCommand, UpdateSocialClientCommand) and
/// validate the contract shape without spinning up an HTTP host.
///
/// Turned GREEN by:
///   - Adding int? AestheticRefreshThresholdDays to SocialClientResponse + FromDomain mapping
///   - Adding int? AestheticRefreshThresholdDays to CreateSocialClientCommand
///   - Adding int? AestheticRefreshThresholdDays to UpdateSocialClientCommand
///   - Handlers calling client.SetAestheticRefreshThresholdDays when the field is present
/// </summary>
public sealed class SocialClientsEndpointThresholdTests
{
    private static readonly Guid OrgId = Guid.Parse("11111111-1111-1111-1111-000000000011");
    private static readonly Guid ClientId = Guid.Parse("22222222-2222-2222-2222-000000000022");

    // ─── GET response includes aestheticRefreshThresholdDays ─────────────────────

    [Fact]
    public void SocialClientResponse_FromDomain_IncludesAestheticRefreshThresholdDays_WhenSet()
    {
        var client = BuildClientWithThreshold(14);
        var response = SocialClientResponse.FromDomain(client);

        Assert.Equal(14, response.AestheticRefreshThresholdDays);
    }

    [Fact]
    public void SocialClientResponse_FromDomain_ReturnsNullThreshold_WhenNotSet()
    {
        var client = BuildClientWithThreshold(null);
        var response = SocialClientResponse.FromDomain(client);

        Assert.Null(response.AestheticRefreshThresholdDays);
    }

    // ─── CREATE command carries threshold ─────────────────────────────────────

    [Fact]
    public void CreateSocialClientCommand_AcceptsAestheticRefreshThresholdDays()
    {
        var cmd = new CreateSocialClientCommand(
            OrgId,
            "Test Client",
            Description: null,
            SectorOrNiche: null,
            AestheticRefreshThresholdDays: 14);

        Assert.Equal(14, cmd.AestheticRefreshThresholdDays);
    }

    [Fact]
    public void CreateSocialClientCommand_AcceptsNullThreshold()
    {
        var cmd = new CreateSocialClientCommand(
            OrgId,
            "Test Client",
            Description: null,
            SectorOrNiche: null,
            AestheticRefreshThresholdDays: null);

        Assert.Null(cmd.AestheticRefreshThresholdDays);
    }

    // ─── UPDATE command carries threshold; null means leave unchanged ──────────

    [Fact]
    public void UpdateSocialClientCommand_AcceptsAestheticRefreshThresholdDays()
    {
        var cmd = new UpdateSocialClientCommand(
            ClientId,
            OrgId,
            "Updated Name",
            Description: null,
            SectorOrNiche: null,
            AestheticRefreshThresholdDays: 7);

        Assert.Equal(7, cmd.AestheticRefreshThresholdDays);
    }

    [Fact]
    public void UpdateSocialClientCommand_AcceptsNullThreshold_MeansLeaveUnchanged()
    {
        var cmd = new UpdateSocialClientCommand(
            ClientId,
            OrgId,
            "Updated Name",
            Description: null,
            SectorOrNiche: null,
            AestheticRefreshThresholdDays: null);

        // null = "leave current threshold unchanged" (not "clear to null")
        Assert.Null(cmd.AestheticRefreshThresholdDays);
    }

    // ─── Domain validation rejects out-of-range values ────────────────────────

    [Fact]
    public void Client_SetAestheticRefreshThresholdDays_ValueZero_ReturnsFailure()
    {
        var client = BuildClientWithThreshold(null);
        var result = client.SetAestheticRefreshThresholdDays(0, DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(ClientErrors.AestheticRefreshThresholdTooLow.Code, result.Error.Code);
    }

    [Fact]
    public void Client_SetAestheticRefreshThresholdDays_Value366_ReturnsFailure()
    {
        var client = BuildClientWithThreshold(null);
        var result = client.SetAestheticRefreshThresholdDays(366, DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(ClientErrors.AestheticRefreshThresholdTooHigh.Code, result.Error.Code);
    }

    [Fact]
    public void Client_SetAestheticRefreshThresholdDays_ValueNull_ClearsToNull()
    {
        var client = BuildClientWithThreshold(14);
        var result = client.SetAestheticRefreshThresholdDays(null, DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Null(client.AestheticRefreshThresholdDays);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static Client BuildClientWithThreshold(int? thresholdDays)
    {
        var created = Client.Create(
            new ClientId(ClientId),
            OrgId,
            "Test Client",
            description: null,
            DateTime.UtcNow,
            sectorOrNiche: null);
        var client = created.Value;
        if (thresholdDays.HasValue)
        {
            client.SetAestheticRefreshThresholdDays(thresholdDays.Value, DateTime.UtcNow);
        }
        return client;
    }
}
