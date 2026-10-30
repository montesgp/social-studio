using SocialStudio.Application.Abstractions.Social.Publications;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Social.Publications;

namespace SocialStudio.Application.Features.Social.Publications;

/// <summary>
/// Resolves a registered <see cref="IPublicationConnector"/> by <see cref="PublicationDestination.NetworkKey"/>.
/// Adding a new network is purely additive — register a new <see cref="IPublicationConnector"/>
/// implementation under its own key; this selector needs no change.
/// </summary>
public sealed class PublicationConnectorSelector : IPublicationConnectorSelector
{
    private readonly IReadOnlyDictionary<string, IPublicationConnector> _connectors;

    public PublicationConnectorSelector(IEnumerable<IPublicationConnector> connectors)
    {
        _connectors = connectors.ToDictionary(c => c.NetworkKey, StringComparer.OrdinalIgnoreCase);
    }

    public Result<IPublicationConnector> For(string networkKey) =>
        _connectors.TryGetValue(networkKey, out var connector)
            ? Result.Success(connector)
            : Result.Failure<IPublicationConnector>(PublicationErrors.ConnectorNotFound);
}
