using SocialStudio.Application.Abstractions.Ai;
using SocialStudio.Application.Abstractions.Integrations;
using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Infrastructure.Ai.Providers;
using SocialStudio.Infrastructure.Social.Providers;
using Microsoft.Extensions.DependencyInjection;
// WU-9: alias removed — IImageGenerationClient retired; ImageGenerationRequest is unambiguous now.

namespace SocialStudio.Infrastructure.Integrations;

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

    public bool Supports<TClient>(string providerKey)
        where TClient : notnull =>
        !string.IsNullOrWhiteSpace(providerKey)
        && ResolveTargetType(typeof(TClient), providerKey) is not null;

    private static Type? ResolveTargetType(Type clientType, string providerKey)
    {
        var normalized = providerKey.Trim().ToLowerInvariant();

        if (clientType == typeof(IAiModelClient))
        {
            return normalized switch
            {
                "openai" => typeof(OpenAiModelClient),
                "claude-p" => typeof(ClaudePromptCliModelClient),
                "claude" or "anthropic" => typeof(ClaudeModelClient),
                "gemini" or "google-gemini" => typeof(GeminiModelClient),
                _ => null,
            };
        }

        if (clientType == typeof(IImageModelClient))
        {
            return normalized switch
            {
                "openai" => typeof(OpenAiImageModelClient),
                "claude-p" => typeof(ClaudePromptCliImageModelClient),
                "comfyui" => typeof(ComfyUiImageModelClient),
                _ => null,
            };
        }

        if (clientType == typeof(IAiModelCatalogClient))
        {
            return normalized switch
            {
                "claude" or "anthropic" => typeof(ClaudeModelCatalogClient),
                "claude-p" => typeof(ClaudePromptCliModelCatalogClient),
                "openai" => typeof(OpenAiModelCatalogClient),
                "comfyui" => typeof(ComfyUiModelCatalogClient),
                _ => null,
            };
        }

        if (clientType == typeof(IActorCatalogClient))
        {
            return normalized switch
            {
                "apify" => typeof(ApifyActorCatalogClient),
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
