namespace Incoders.Hive.Application.Abstractions.Integrations;

public interface IExternalProviderClientFactory
{
    TClient Create<TClient>(ResolvedProviderCredential credential)
        where TClient : notnull;

    /// <summary>
    /// Whether <paramref name="providerKey"/> has a concrete implementation registered for
    /// <typeparamref name="TClient"/>. Lets callers route by capability — e.g. "can this provider
    /// sync a model catalog?" — without constructing the client or catching creation failures.
    /// </summary>
    bool Supports<TClient>(string providerKey)
        where TClient : notnull;
}