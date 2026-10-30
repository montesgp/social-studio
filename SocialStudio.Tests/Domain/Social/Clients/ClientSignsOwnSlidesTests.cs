using SocialStudio.Domain.Social.Clients;

namespace SocialStudio.Tests.Domain.Social.Clients;

/// <summary>
/// Issue #70: plain boolean client field gating the conditional @handle signature.
/// </summary>
public sealed class ClientSignsOwnSlidesTests
{
    private static readonly DateTime Now = new(2026, 7, 11, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_DefaultsSignsOwnSlidesToFalse()
    {
        var created = Client.Create(ClientId.New(), Guid.NewGuid(), "Acme", null, Now);

        Assert.True(created.IsSuccess);
        Assert.False(created.Value.SignsOwnSlides);
    }

    [Fact]
    public void SetSignsOwnSlides_True_UpdatesFlagAndTimestamp()
    {
        var created = Client.Create(ClientId.New(), Guid.NewGuid(), "Acme", null, Now);
        var later = Now.AddMinutes(5);

        created.Value.SetSignsOwnSlides(true, later);

        Assert.True(created.Value.SignsOwnSlides);
        Assert.Equal(later, created.Value.UpdatedAtUtc);
    }

    [Fact]
    public void SetSignsOwnSlides_False_AfterTrue_RevertsFlag()
    {
        var created = Client.Create(ClientId.New(), Guid.NewGuid(), "Acme", null, Now);
        created.Value.SetSignsOwnSlides(true, Now.AddMinutes(1));

        created.Value.SetSignsOwnSlides(false, Now.AddMinutes(2));

        Assert.False(created.Value.SignsOwnSlides);
    }

    [Fact]
    public void Restore_RoundTripsSignsOwnSlides()
    {
        var restored = Client.Restore(
            ClientId.New(),
            Guid.NewGuid(),
            "Acme",
            null,
            isActive: true,
            createdAtUtc: Now,
            updatedAtUtc: Now,
            signsOwnSlides: true);

        Assert.True(restored.SignsOwnSlides);
    }
}
