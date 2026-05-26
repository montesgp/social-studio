using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Incoders.Hive.Infrastructure.Social;

/// <summary>
/// Resolves social content by calling the org's connected extraction provider directly.
/// The provider (for example, "apify") is resolved from the organization's encrypted
/// integration config.
/// </summary>
public sealed class SocialContentResolver : ISocialContentResolver
{
    private const string SocialExtractionCapability = "social-extraction";
    private const string DefaultExtractionProviderKey = "apify";

    private readonly IIntegrationResolver _integrationResolver;
    private readonly IExternalProviderClientFactory _clientFactory;
    private readonly ILogger<SocialContentResolver> _logger;

    public SocialContentResolver(
        IIntegrationResolver integrationResolver,
        IExternalProviderClientFactory clientFactory,
        ILogger<SocialContentResolver>? logger = null)
    {
        _integrationResolver = integrationResolver;
        _clientFactory = clientFactory;
        _logger = logger ?? NullLogger<SocialContentResolver>.Instance;
    }

    public async Task<Result<SocialContentResolution>> ResolveAsync(
        SocialContentResolutionRequest request,
        CancellationToken cancellationToken = default)
    {
        var platformKey = NormalizePlatform(request.PlatformHint);
        var normalizedUrl = NormalizeUrl(request.SourceUrl);
        var providerKey = string.IsNullOrWhiteSpace(request.PreferredProviderKey)
            ? DefaultExtractionProviderKey
            : request.PreferredProviderKey.Trim();

        _logger.LogInformation(
            "Single Analyzer extraction: resolving provider {ProviderKey} for platform {PlatformKey} (org {OrganizationId}).",
            providerKey,
            platformKey,
            request.OrganizationId);

        var resolved = await _integrationResolver.ResolveProviderAsync(
            request.OrganizationId,
            providerKey,
            cancellationToken);

        if (resolved.IsFailure)
        {
            _logger.LogWarning(
                "Single Analyzer extraction: provider {ProviderKey} unavailable for org {OrganizationId}: {ErrorCode}.",
                providerKey,
                request.OrganizationId,
                resolved.Error.Code);

            return Result.Failure<SocialContentResolution>(resolved.Error);
        }

        var credential = new ResolvedProviderCredential(
            Guid.Empty,
            resolved.Value.ProviderKey,
            resolved.Value.ConnectionMode,
            resolved.Value.Credentials,
            resolved.Value.SelectedAiModel);

        ISocialContentExtractor extractor;
        try
        {
            extractor = _clientFactory.Create<ISocialContentExtractor>(credential);
        }
        catch (InvalidOperationException)
        {
            _logger.LogWarning(
                "Single Analyzer extraction: provider {ProviderKey} has no extractor client registered.",
                providerKey);

            return Result.Failure<SocialContentResolution>(IntegrationErrors.ExternalProviderUnsupported);
        }

        var snapshot = await extractor.ExtractAsync(
            new SocialContentRequest(
                request.OrganizationId,
                request.SourceUrl,
                normalizedUrl,
                platformKey,
                SocialExtractionCapability),
            cancellationToken);

        if (snapshot.IsFailure)
        {
            _logger.LogWarning(
                "Single Analyzer extraction: provider {ProviderKey} failed for platform {PlatformKey}: {ErrorCode}.",
                providerKey,
                platformKey,
                snapshot.Error.Code);

            return Result.Failure<SocialContentResolution>(snapshot.Error);
        }

        _logger.LogInformation(
            "Single Analyzer extraction: provider {ProviderKey} succeeded for platform {PlatformKey} ({WarningCount} warnings).",
            providerKey,
            platformKey,
            snapshot.Value.Warnings.Count);

        return Result.Success(new SocialContentResolution(snapshot.Value, []));
    }

    private static string NormalizePlatform(string? platformHint) =>
        string.IsNullOrWhiteSpace(platformHint) ? "unknown" : platformHint.Trim().ToLowerInvariant();

    private static string NormalizeUrl(string sourceUrl)
    {
        if (Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri))
        {
            return uri.AbsoluteUri;
        }

        return sourceUrl.Trim();
    }
}
