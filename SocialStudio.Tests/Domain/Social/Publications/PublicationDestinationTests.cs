using SocialStudio.Domain.Social.Publications;

namespace SocialStudio.Tests.Domain.Social.Publications;

public sealed class PublicationDestinationTests
{
    [Fact]
    public void Equality_IsValueBased()
    {
        var connectionId = Guid.NewGuid();
        var networkId = Guid.NewGuid();

        var a = new PublicationDestination(connectionId, networkId, "instagram");
        var b = new PublicationDestination(connectionId, networkId, "instagram");

        Assert.Equal(a, b);
    }

    [Fact]
    public void Equality_DiffersOnAnyField()
    {
        var connectionId = Guid.NewGuid();
        var networkId = Guid.NewGuid();

        var a = new PublicationDestination(connectionId, networkId, "instagram");
        var b = new PublicationDestination(Guid.NewGuid(), networkId, "instagram");

        Assert.NotEqual(a, b);
    }
}
