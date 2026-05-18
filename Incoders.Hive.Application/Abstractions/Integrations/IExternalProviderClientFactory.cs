namespace Incoders.Hive.Application.Abstractions.Integrations;

public interface IExternalProviderClientFactory
{
    TClient Create<TClient>(ResolvedProviderCredential credential)
        where TClient : notnull;
}