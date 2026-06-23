using Incoders.Hive.Application.Features.Social.Clients;
using Incoders.Hive.Application.Features.Social.Clients.Create;
using Incoders.Hive.Application.Features.Social.Clients.Update;
using Incoders.Hive.Domain.Social.Clients;

namespace Incoders.Hive.Tests.Api.Social;

/// <summary>
/// WU-8 TDD tests — BackgroundSourcingPolicy on the social-clients API surface (BSP-08a/b/c).
///
/// These tests target the Application layer that backs the endpoint:
///   a) SocialClientResponse.FromDomain exposes BackgroundSourcingPolicy (BSP-08a).
///   b) UpdateSocialClientCommand and CreateSocialClientCommand accept BackgroundSourcingPolicy (BSP-08b).
///   c) Invalid (undefined) policy enum values are detected by Enum.IsDefined (BSP-08c guard).
///
/// Turned GREEN by:
///   - Adding BackgroundSourcingPolicy? to SocialClientResponse + FromDomain mapping
///   - Adding BackgroundSourcingPolicy? to CreateSocialClientCommand / UpdateSocialClientCommand
/// </summary>
public sealed class SocialClientsEndpointPolicyTests
{
    private static readonly Guid OrgId = Guid.Parse("11111111-1111-1111-1111-000000000001");
    private static readonly Guid ClientId = Guid.Parse("22222222-2222-2222-2222-000000000002");

    // ─── BSP-08a: response includes backgroundSourcingPolicy ─────────────────

    /// <summary>
    /// A SocialClientResponse built from a Client with AiPalette policy must expose that policy.
    /// </summary>
    [Fact]
    public void SocialClientResponse_FromDomain_IncludesBackgroundSourcingPolicy()
    {
        var client = BuildClient(BackgroundSourcingPolicy.AiPalette);
        var response = SocialClientResponse.FromDomain(client);

        Assert.Equal(BackgroundSourcingPolicy.AiPalette, response.BackgroundSourcingPolicy);
    }

    [Fact]
    public void SocialClientResponse_FromDomain_ClientDefinedSetPolicy_IsMapped()
    {
        var client = BuildClient(BackgroundSourcingPolicy.ClientDefinedSet);
        var response = SocialClientResponse.FromDomain(client);

        Assert.Equal(BackgroundSourcingPolicy.ClientDefinedSet, response.BackgroundSourcingPolicy);
    }

    [Fact]
    public void SocialClientResponse_FromDomain_NullPolicy_ReturnsNullPolicy()
    {
        var client = BuildClient(policy: null);
        var response = SocialClientResponse.FromDomain(client);

        Assert.Null(response.BackgroundSourcingPolicy);
    }

    [Fact]
    public void SocialClientResponse_FromDomain_UnsetPolicy_IsPreserved()
    {
        var client = BuildClient(BackgroundSourcingPolicy.Unset);
        var response = SocialClientResponse.FromDomain(client);

        Assert.Equal(BackgroundSourcingPolicy.Unset, response.BackgroundSourcingPolicy);
    }

    // ─── BSP-08b: commands carry policy ──────────────────────────────────────

    /// <summary>
    /// UpdateSocialClientCommand must have a BackgroundSourcingPolicy? property (BSP-08b).
    /// </summary>
    [Fact]
    public void UpdateSocialClientCommand_AcceptsBackgroundSourcingPolicy()
    {
        var cmd = new UpdateSocialClientCommand(
            ClientId,
            OrgId,
            "Name",
            Description: null,
            SectorOrNiche: null,
            BackgroundSourcingPolicy: BackgroundSourcingPolicy.AiPalette);

        Assert.Equal(BackgroundSourcingPolicy.AiPalette, cmd.BackgroundSourcingPolicy);
    }

    [Fact]
    public void UpdateSocialClientCommand_AcceptsNullPolicy()
    {
        var cmd = new UpdateSocialClientCommand(
            ClientId,
            OrgId,
            "Name",
            Description: null,
            SectorOrNiche: null,
            BackgroundSourcingPolicy: null);

        Assert.Null(cmd.BackgroundSourcingPolicy);
    }

    /// <summary>
    /// CreateSocialClientCommand must have a BackgroundSourcingPolicy? property (BSP-08b).
    /// </summary>
    [Fact]
    public void CreateSocialClientCommand_AcceptsBackgroundSourcingPolicy()
    {
        var cmd = new CreateSocialClientCommand(
            OrgId,
            "Name",
            Description: null,
            SectorOrNiche: null,
            BackgroundSourcingPolicy: BackgroundSourcingPolicy.ClientAesthetic);

        Assert.Equal(BackgroundSourcingPolicy.ClientAesthetic, cmd.BackgroundSourcingPolicy);
    }

    // ─── BSP-08c: out-of-range policy is rejected ────────────────────────────

    /// <summary>
    /// Enum.IsDefined guards against undefined ordinals — the endpoint validation layer
    /// should use this before mapping a request's policy string to the enum (BSP-08c).
    /// </summary>
    [Fact]
    public void BackgroundSourcingPolicy_OutOfRangeOrdinal_IsNotDefined()
    {
        var invalidPolicy = (BackgroundSourcingPolicy)99;
        Assert.False(Enum.IsDefined(invalidPolicy));
    }

    [Theory]
    [InlineData(BackgroundSourcingPolicy.Unset)]
    [InlineData(BackgroundSourcingPolicy.ClientDefinedSet)]
    [InlineData(BackgroundSourcingPolicy.AiPalette)]
    [InlineData(BackgroundSourcingPolicy.ClientAesthetic)]
    public void BackgroundSourcingPolicy_ValidValues_AreDefined(BackgroundSourcingPolicy policy)
    {
        Assert.True(Enum.IsDefined(policy));
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static Client BuildClient(BackgroundSourcingPolicy? policy)
    {
        var result = Client.Create(
            new ClientId(ClientId),
            OrgId,
            "Test Client",
            description: null,
            DateTime.UtcNow,
            sectorOrNiche: null);
        var client = result.Value;
        if (policy.HasValue)
        {
            client.SetBackgroundSourcingPolicy(policy, DateTime.UtcNow);
        }
        return client;
    }
}
