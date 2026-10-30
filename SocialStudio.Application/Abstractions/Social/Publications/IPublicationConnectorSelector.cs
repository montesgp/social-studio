using SocialStudio.Domain.Common;

namespace SocialStudio.Application.Abstractions.Social.Publications;

/// <summary>
/// Resolves the registered <see cref="IPublicationConnector"/> for a given canonical network key
/// (<c>PublicationDestination.NetworkKey</c>). v1 registers exactly one connector ("instagram");
/// additional networks are purely additive registrations.
/// </summary>
public interface IPublicationConnectorSelector
{
    Result<IPublicationConnector> For(string networkKey);
}
