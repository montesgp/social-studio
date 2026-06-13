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

        var actorIds = request.PreferredActorIds?
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var diagnostics = new List<ProviderFailureDiagnostic>();
        List<string?> attempts = actorIds is { Count: > 0 }
            ? actorIds.Cast<string?>().ToList()
            : [null];

        for (var index = 0; index < attempts.Count; index++)
        {
            var actorId = attempts[index];
            var isFallback = index > 0;

            _logger.LogInformation(
                "WorkflowProviderAttemptStart OrganizationId={OrganizationId} WorkflowKey={WorkflowKey} AnalysisType={AnalysisType} StepKey={StepKey} ProviderKey={ProviderKey} NetworkKey={NetworkKey} ActorId={ActorId} Priority={Priority} IsFallback={IsFallback}",
                request.OrganizationId,
                "single-analyzer",
                "content-analysis",
                "scrape-video",
                providerKey,
                platformKey,
                actorId ?? "(legacy-mapping)",
                index + 1,
                isFallback);

            var snapshot = await extractor.ExtractAsync(
                new SocialContentRequest(
                    request.OrganizationId,
                    request.SourceUrl,
                    normalizedUrl,
                    platformKey,
                    SocialExtractionCapability,
                    actorId),
                cancellationToken);

            if (snapshot.IsSuccess)
            {
                _logger.LogInformation(
                    "Single Analyzer extraction: provider {ProviderKey} succeeded for platform {PlatformKey} actor {ActorId} ({WarningCount} warnings).",
                    providerKey,
                    platformKey,
                    actorId ?? "(legacy-mapping)",
                    snapshot.Value.Warnings.Count);

                return Result.Success(new SocialContentResolution(snapshot.Value, diagnostics));
            }

            _logger.LogWarning(
                "WorkflowProviderAttemptFailed OrganizationId={OrganizationId} WorkflowKey={WorkflowKey} AnalysisType={AnalysisType} StepKey={StepKey} ProviderKey={ProviderKey} NetworkKey={NetworkKey} ActorId={ActorId} Priority={Priority} IsFallback={IsFallback} ErrorCode={ErrorCode}",
                request.OrganizationId,
                "single-analyzer",
                "content-analysis",
                "scrape-video",
                providerKey,
                platformKey,
                actorId ?? "(legacy-mapping)",
                index + 1,
                isFallback,
                snapshot.Error.Code);

            if (!string.IsNullOrWhiteSpace(actorId))
            {
                var diagnostic = ProviderFailureDiagnostic.Create(
                    SocialExtractionCapability,
                    providerKey,
                    index + 1,
                    snapshot.Error.Code,
                    snapshot.Error.MessageKey,
                    isRetryable: false,
                    fallbackAllowed: index + 1 < attempts.Count,
                    elapsedMs: 0,
                    DateTime.UtcNow);

                if (diagnostic.IsSuccess)
                {
                    diagnostics.Add(diagnostic.Value);
                }
            }
        }

        var finalError = diagnostics.Count > 0
            ? Error.Failure(diagnostics[^1].FailureCode, diagnostics[^1].MessageKey)
            : IntegrationErrors.ExternalProviderUnavailable;

        return Result.Failure<SocialContentResolution>(finalError);
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
