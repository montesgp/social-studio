using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
// WU-9: alias removed — IImageGenerationClient retired; ImageGenerationRequest is unambiguous now.

namespace Incoders.Hive.Infrastructure.Ai;

/// <summary>
/// Resolves the org's IMAGE route (<c>generate-image</c> step via
/// <see cref="IWorkflowConfigurationResolver"/>), creates an <see cref="IImageModelClient"/>
/// via <see cref="IExternalProviderClientFactory"/>, and delegates
/// <see cref="IImageGenerationGateway.GenerateAsync"/>.
///
/// Mirrors <c>AiCompletionGateway</c> — same route-resolution + factory-creation pattern.
/// Wrapped by <c>ImageGenerationGatewayInstrumentationDecorator</c> at DI composition time.
/// Refs: IMG-01, IMG-06; design §2.3; tasks 5.2.
/// </summary>
public sealed class ImageGenerationGateway : IImageGenerationGateway
{
    private const string WorkflowKey = "single-analyzer";
    private const string AnalysisType = "content-analysis";
    private const string StepKey = "generate-image";

    private readonly IWorkflowConfigurationResolver _workflowResolver;
    private readonly IIntegrationCredentialResolver _credentialResolver;
    private readonly IExternalProviderClientFactory _clientFactory;
    private readonly ILogger<ImageGenerationGateway> _logger;

    public ImageGenerationGateway(
        IWorkflowConfigurationResolver workflowResolver,
        IIntegrationCredentialResolver credentialResolver,
        IExternalProviderClientFactory clientFactory,
        ILogger<ImageGenerationGateway>? logger = null)
    {
        _workflowResolver = workflowResolver;
        _credentialResolver = credentialResolver;
        _clientFactory = clientFactory;
        _logger = logger ?? NullLogger<ImageGenerationGateway>.Instance;
    }

    public async Task<Result<ImageGenerationResult>> GenerateAsync(
        ImageGenerationRequest request,
        CancellationToken ct = default)
    {
        // 1. Resolve the generate-image route for this org.
        var routeResult = await _workflowResolver.ResolveStepAsync(
            new WorkflowStepResolutionRequest(
                request.OrganizationId,
                WorkflowKey,
                AnalysisType,
                StepKey),
            ct);

        if (routeResult.IsFailure)
        {
            _logger.LogWarning(
                "ImageGenerationGateway: route resolution failed for org {OrgId}: {Code}.",
                request.OrganizationId,
                routeResult.Error.Code);

            return Result.Failure<ImageGenerationResult>(routeResult.Error);
        }

        var candidates = routeResult.Value;
        if (candidates.Count == 0)
        {
            _logger.LogWarning(
                "ImageGenerationGateway: no IMAGE route configured for org {OrgId}. " +
                "Creative slides will degrade to CSS.",
                request.OrganizationId);

            return Result.Failure<ImageGenerationResult>(IntegrationErrors.AiRouteNotConfigured);
        }

        // Use the first (highest-priority) candidate.
        var primaryCandidate = candidates[0];

        // 2. Resolve credentials for this candidate.
        var credentialResult = await _credentialResolver.ResolveAsync(
            request.OrganizationId,
            primaryCandidate.ToIntegrationRouteCandidate(),
            ct);

        if (credentialResult.IsFailure)
        {
            _logger.LogWarning(
                "ImageGenerationGateway: credential resolution failed for org {OrgId} provider {ProviderKey}: {Code}.",
                request.OrganizationId,
                primaryCandidate.ProviderKey,
                credentialResult.Error.Code);

            return Result.Failure<ImageGenerationResult>(credentialResult.Error);
        }

        var credential = credentialResult.Value;

        // Override model from request if specified.
        if (!string.IsNullOrWhiteSpace(request.PreferredModel))
        {
            credential = credential with { SelectedModel = request.PreferredModel };
        }

        // 3. Create the provider client via the factory.
        IImageModelClient client;
        try
        {
            client = _clientFactory.Create<IImageModelClient>(credential);
        }
        catch (InvalidOperationException)
        {
            _logger.LogWarning(
                "ImageGenerationGateway: no IImageModelClient registered for provider '{ProviderKey}'.",
                primaryCandidate.ProviderKey);

            return Result.Failure<ImageGenerationResult>(IntegrationErrors.ExternalProviderUnsupported);
        }

        // 4. Forward the generation request with the resolved preferred provider/model.
        var enrichedRequest = request with
        {
            PreferredProviderKey = credential.ProviderKey,
            PreferredModel = credential.SelectedModel,
        };

        return await client.GenerateAsync(enrichedRequest, ct);
    }
}
