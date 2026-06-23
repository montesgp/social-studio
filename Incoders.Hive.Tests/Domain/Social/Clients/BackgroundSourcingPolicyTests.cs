using Incoders.Hive.Domain.Social.Clients;

namespace Incoders.Hive.Tests.Domain.Social.Clients;

/// <summary>
/// TDD tests for WU-2: BackgroundSourcingPolicy on Client aggregate.
/// Refs: BSP-07, BSP-05, BSP-REG-01; design §3.1, §3.4, §3.5.
/// </summary>
public sealed class BackgroundSourcingPolicyTests
{
    private static readonly DateTime Now = new(2026, 6, 19, 12, 0, 0, DateTimeKind.Utc);

    // -----------------------------------------------------------------------
    // 2.1 RED: Client.SetBackgroundSourcingPolicy mutator
    // -----------------------------------------------------------------------

    [Fact]
    public void SetBackgroundSourcingPolicy_ChangesPolicy_ToClientDefinedSet()
    {
        var client = CreateValidClient();

        client.SetBackgroundSourcingPolicy(BackgroundSourcingPolicy.ClientDefinedSet, Now);

        Assert.Equal(BackgroundSourcingPolicy.ClientDefinedSet, client.BackgroundSourcingPolicy);
    }

    [Fact]
    public void SetBackgroundSourcingPolicy_ChangesPolicy_ToAiPalette()
    {
        var client = CreateValidClient();

        client.SetBackgroundSourcingPolicy(BackgroundSourcingPolicy.AiPalette, Now);

        Assert.Equal(BackgroundSourcingPolicy.AiPalette, client.BackgroundSourcingPolicy);
    }

    [Fact]
    public void SetBackgroundSourcingPolicy_ChangesPolicy_ToClientAesthetic()
    {
        var client = CreateValidClient();

        client.SetBackgroundSourcingPolicy(BackgroundSourcingPolicy.ClientAesthetic, Now);

        Assert.Equal(BackgroundSourcingPolicy.ClientAesthetic, client.BackgroundSourcingPolicy);
    }

    [Fact]
    public void SetBackgroundSourcingPolicy_ToNull_ClearsPolicy()
    {
        var client = CreateValidClient();
        client.SetBackgroundSourcingPolicy(BackgroundSourcingPolicy.AiPalette, Now);

        client.SetBackgroundSourcingPolicy(null, Now);

        Assert.Null(client.BackgroundSourcingPolicy);
    }

    [Fact]
    public void SetBackgroundSourcingPolicy_UpdatesUpdatedAtUtc()
    {
        var client = CreateValidClient();
        var later = Now.AddHours(1);

        client.SetBackgroundSourcingPolicy(BackgroundSourcingPolicy.AiPalette, later);

        Assert.Equal(later, client.UpdatedAtUtc);
    }

    // -----------------------------------------------------------------------
    // Create: null policy starts as null (UNSET = legacy default, BSP-REG-01)
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_WithDefaultPolicy_StartsAsNull()
    {
        var result = Client.Create(ClientId.New(), Guid.NewGuid(), "Acme", null, Now);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.BackgroundSourcingPolicy);
    }

    // -----------------------------------------------------------------------
    // Restore: null policy defaults to legacy (BSP-05)
    // -----------------------------------------------------------------------

    [Fact]
    public void Restore_WithNullPolicy_DefaultsToLegacy()
    {
        var client = Client.Restore(
            ClientId.New(), Guid.NewGuid(), "Acme", null,
            isActive: true,
            createdAtUtc: Now,
            updatedAtUtc: Now,
            policy: null);

        Assert.Null(client.BackgroundSourcingPolicy);
    }

    [Fact]
    public void Restore_WithClientDefinedSet_PreservesPolicy()
    {
        var client = Client.Restore(
            ClientId.New(), Guid.NewGuid(), "Acme", null,
            isActive: true,
            createdAtUtc: Now,
            updatedAtUtc: Now,
            policy: BackgroundSourcingPolicy.ClientDefinedSet);

        Assert.Equal(BackgroundSourcingPolicy.ClientDefinedSet, client.BackgroundSourcingPolicy);
    }

    private static Client CreateValidClient()
    {
        return Client.Create(ClientId.New(), Guid.NewGuid(), "Acme Social", null, Now).Value;
    }
}
