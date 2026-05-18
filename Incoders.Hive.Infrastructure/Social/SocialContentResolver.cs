using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;

namespace Incoders.Hive.Infrastructure.Social;

public sealed class SocialContentResolver : ISocialContentResolver
{
    private const string SingleAnalyzerFlowKey = "single-analyzer";
    private const string SocialExtractionCapability = "social-extraction";

    private readonly IOrganizationIntegrationResolver _routeResolver;
    private readonly IIntegrationCredentialResolver _credentialResolver;
    private readonly IExternalProviderClientFactory _clientFactory;
    private readonly IIntegrationExecutionPolicy _executionPolicy;

    public SocialContentResolver(
        IOrganizationIntegrationResolver routeResolver,
        IIntegrationCredentialResolver credentialResolver,
        IExternalProviderClientFactory clientFactory,
        IIntegrationExecutionPolicy executionPolicy)
    {
        _routeResolver = routeResolver;
        _credentialResolver = credentialResolver;
        _clientFactory = clientFactory;
        _executionPolicy = executionPolicy;
    }

    public async Task<Result<SocialContentResolution>> ResolveAsync(
        SocialContentResolutionRequest request,
        CancellationToken cancellationToken = default)
    {
        var platformKey = NormalizePlatform(request.PlatformHint);
        var normalizedUrl = NormalizeUrl(request.SourceUrl);
        var route = await _routeResolver.ResolveRouteAsync(
            new IntegrationRouteRequest(
                request.OrganizationId,
                SingleAnalyzerFlowKey,
                SocialExtractionCapability,
                platformKey,
                request.PreferredProviderKey,
                PreferredModel: null),
            cancellationToken);

        if (route.IsFailure)
        {
            return Result.Failure<SocialContentResolution>(route.Error);
        }

        var execution = await _executionPolicy.ExecuteAsync(
            new IntegrationExecutionRequest(
                request.OrganizationId,
                AnalysisId: null,
                SingleAnalyzerFlowKey,
                SocialExtractionCapability,
                platformKey),
            route.Value.Candidates,
            async (candidate, token) =>
            {
                var credential = await _credentialResolver.ResolveAsync(request.OrganizationId, candidate, token);
                if (credential.IsFailure)
                {
                    return Result.Failure<SocialContentSnapshot>(credential.Error);
                }

                ISocialContentExtractor extractor;
                try
                {
                    extractor = _clientFactory.Create<ISocialContentExtractor>(credential.Value);
                }
                catch (InvalidOperationException)
                {
                    return Result.Failure<SocialContentSnapshot>(IntegrationErrors.ExternalProviderUnsupported);
                }

                return await extractor.ExtractAsync(
                    new SocialContentRequest(
                        request.OrganizationId,
                        request.SourceUrl,
                        normalizedUrl,
                        platformKey,
                        SocialExtractionCapability),
                    token);
            },
            cancellationToken);

        return execution.IsSuccess
            ? Result.Success(new SocialContentResolution(execution.Value.Payload, execution.Value.Diagnostics))
            : Result.Failure<SocialContentResolution>(execution.Error);
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
