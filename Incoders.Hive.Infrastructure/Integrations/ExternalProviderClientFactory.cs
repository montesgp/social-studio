using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Infrastructure.Ai.Providers;
using Incoders.Hive.Infrastructure.Social.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace Incoders.Hive.Infrastructure.Integrations;

public sealed class ExternalProviderClientFactory : IExternalProviderClientFactory
{
    private readonly IServiceProvider _serviceProvider;

    public ExternalProviderClientFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public TClient Create<TClient>(ResolvedProviderCredential credential)
        where TClient : notnull
    {
        ArgumentNullException.ThrowIfNull(credential);

        var targetType = ResolveTargetType(typeof(TClient), credential.ProviderKey);
        if (targetType is null)
        {
            throw new InvalidOperationException($"Provider '{credential.ProviderKey}' does not support client '{typeof(TClient).Name}'.");
        }

        return (TClient)ActivatorUtilities.CreateInstance(_serviceProvider, targetType, credential);
    }

    private static Type? ResolveTargetType(Type clientType, string providerKey)
    {
        var normalized = providerKey.Trim().ToLowerInvariant();

        if (clientType == typeof(IAiModelClient))
        {
            return normalized switch
            {
                "openai" => typeof(OpenAiModelClient),
                "claude" or "anthropic" => typeof(ClaudeModelClient),
                "gemini" or "google-gemini" => typeof(GeminiModelClient),
                _ => null,
            };
        }

        if (clientType == typeof(IAiModelCatalogClient))
        {
            return normalized switch
            {
                "claude" or "anthropic" => typeof(ClaudeModelCatalogClient),
                _ => null,
            };
        }

        if (clientType == typeof(ISocialContentExtractor))
        {
            return normalized switch
            {
                "apify" => typeof(ApifySocialContentExtractor),
                "data365" => typeof(Data365SocialContentExtractor),
                _ => null,
            };
        }

        return null;
    }
}
