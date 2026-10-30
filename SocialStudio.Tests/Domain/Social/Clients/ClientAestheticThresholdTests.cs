using SocialStudio.Domain.Social.Clients;

namespace SocialStudio.Tests.Domain.Social.Clients;

/// <summary>
/// TDD tests for WU-1 task 1.5/1.6: Client.AestheticRefreshThresholdDays (CAG-CS-01, design §2.1).
/// Mirrors the BackgroundSourcingPolicyTests pattern.
/// </summary>
public sealed class ClientAestheticThresholdTests
{
    private static readonly DateTime Now = new(2026, 6, 20, 12, 0, 0, DateTimeKind.Utc);

    // -----------------------------------------------------------------------
    // Field existence: nullable int?
    // -----------------------------------------------------------------------

    [Fact]
    public void Client_ExposesAestheticRefreshThresholdDays_AsNullableInt()
    {
        var client = CreateValidClient();

        int? threshold = client.AestheticRefreshThresholdDays;

        Assert.Null(threshold);
    }

    // -----------------------------------------------------------------------
    // Create: starts as null
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_AestheticRefreshThresholdDays_StartsAsNull()
    {
        var result = Client.Create(ClientId.New(), Guid.NewGuid(), "Acme", null, Now);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.AestheticRefreshThresholdDays);
    }

    // -----------------------------------------------------------------------
    // SetAestheticRefreshThresholdDays: set value
    // -----------------------------------------------------------------------

    [Fact]
    public void SetAestheticRefreshThresholdDays_SetsValue_WhenValid()
    {
        var client = CreateValidClient();

        client.SetAestheticRefreshThresholdDays(14, Now);

        Assert.Equal(14, client.AestheticRefreshThresholdDays);
    }

    [Fact]
    public void SetAestheticRefreshThresholdDays_ToNull_ClearsValue()
    {
        var client = CreateValidClient();
        client.SetAestheticRefreshThresholdDays(14, Now);

        client.SetAestheticRefreshThresholdDays(null, Now);

        Assert.Null(client.AestheticRefreshThresholdDays);
    }

    [Fact]
    public void SetAestheticRefreshThresholdDays_UpdatesUpdatedAtUtc()
    {
        var client = CreateValidClient();
        var later = Now.AddHours(1);

        client.SetAestheticRefreshThresholdDays(30, later);

        Assert.Equal(later, client.UpdatedAtUtc);
    }

    [Fact]
    public void SetAestheticRefreshThresholdDays_ToNull_UpdatesUpdatedAtUtc()
    {
        var client = CreateValidClient();
        client.SetAestheticRefreshThresholdDays(30, Now);
        var later = Now.AddHours(2);

        client.SetAestheticRefreshThresholdDays(null, later);

        Assert.Equal(later, client.UpdatedAtUtc);
    }

    // -----------------------------------------------------------------------
    // Boundary validation: 0 and 366 must fail domain validation
    // -----------------------------------------------------------------------

    [Fact]
    public void SetAestheticRefreshThresholdDays_WithZero_FailsDomainValidation()
    {
        var client = CreateValidClient();

        var result = client.SetAestheticRefreshThresholdDays(0, Now);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void SetAestheticRefreshThresholdDays_With366_FailsDomainValidation()
    {
        var client = CreateValidClient();

        var result = client.SetAestheticRefreshThresholdDays(366, Now);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void SetAestheticRefreshThresholdDays_With1_Succeeds()
    {
        var client = CreateValidClient();

        var result = client.SetAestheticRefreshThresholdDays(1, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, client.AestheticRefreshThresholdDays);
    }

    [Fact]
    public void SetAestheticRefreshThresholdDays_With365_Succeeds()
    {
        var client = CreateValidClient();

        var result = client.SetAestheticRefreshThresholdDays(365, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(365, client.AestheticRefreshThresholdDays);
    }

    // -----------------------------------------------------------------------
    // Restore: back-compat with null threshold
    // -----------------------------------------------------------------------

    [Fact]
    public void Restore_WithNullThreshold_WorksCorrectly()
    {
        var client = Client.Restore(
            ClientId.New(), Guid.NewGuid(), "Acme", null,
            isActive: true,
            createdAtUtc: Now,
            updatedAtUtc: Now,
            aestheticRefreshThresholdDays: null);

        Assert.Null(client.AestheticRefreshThresholdDays);
    }

    [Fact]
    public void Restore_WithThresholdValue_PreservesValue()
    {
        var client = Client.Restore(
            ClientId.New(), Guid.NewGuid(), "Acme", null,
            isActive: true,
            createdAtUtc: Now,
            updatedAtUtc: Now,
            aestheticRefreshThresholdDays: 14);

        Assert.Equal(14, client.AestheticRefreshThresholdDays);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static Client CreateValidClient() =>
        Client.Create(ClientId.New(), Guid.NewGuid(), "Acme Social", null, Now).Value;
}
