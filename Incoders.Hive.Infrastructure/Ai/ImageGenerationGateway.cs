using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Incoders.Hive.Infrastructure.Ai;

/// <summary>
/// Resolves the request route context, executes ordered image provider candidates through the
/// integration execution policy, and delegates provider-specific work to Infrastructure clients.
/// </summary>
public sealed class ImageGenerationGateway : IImageGenerationGateway
{
    private readonly IWorkflowConfigurationResolver _workflowResolver;
    private readonly IIntegrationCredentialResolver _credentialResolver;
    private readonly IExternalProviderClientFactory _clientFactory;
    private readonly IIntegrationExecutionPolicy? _executionPolicy;
    private readonly ILogger<ImageGenerationGateway> _logger;

    public ImageGenerationGateway(
        IWorkflowConfigurationResolver workflowResolver,
        IIntegrationCredentialResolver credentialResolver,
        IExternalProviderClientFactory clientFactory,
        ILogger<ImageGenerationGateway>? logger = null)
        : this(workflowResolver, credentialResolver, clientFactory, executionPolicy: null, logger)
    {
    }

    public ImageGenerationGateway(
        IWorkflowConfigurationResolver workflowResolver,
        IIntegrationCredentialResolver credentialResolver,
        IExternalProviderClientFactory clientFactory,
        IIntegrationExecutionPolicy? executionPolicy,
        ILogger<ImageGenerationGateway>? logger = null)
    {
        _workflowResolver = workflowResolver;
        _credentialResolver = credentialResolver;
        _clientFactory = clientFactory;
        _executionPolicy = executionPolicy;
        _logger = logger ?? NullLogger<ImageGenerationGateway>.Instance;
    }

    public async Task<Result<ImageGenerationResult>> GenerateAsync(ImageGenerationRequest request, CancellationToken ct = default)
    {
        var routeResult = await _workflowResolver.ResolveStepAsync(
            new WorkflowStepResolutionRequest(
                request.OrganizationId,
                request.WorkflowKey,
                request.AnalysisType,
                request.StepKey,
                request.OperationKey,
                request.NetworkKey),
            ct);

        if (routeResult.IsFailure)
        {
            _logger.LogWarning("Image route resolution failed for org {OrgId}: {Code}.", request.OrganizationId, routeResult.Error.Code);
            return Result.Failure<ImageGenerationResult>(routeResult.Error);
        }

        var candidates = routeResult.Value
            .Select(candidate => candidate.ToIntegrationRouteCandidate())
            .OrderBy(candidate => candidate.Priority)
            .ToArray();

        if (candidates.Length == 0)
        {
            _logger.LogWarning("No image generation route configured for org {OrgId}.", request.OrganizationId);
            return Result.Failure<ImageGenerationResult>(IntegrationErrors.AiRouteNotConfigured);
        }

        if (_executionPolicy is null)
        {
            return await ExecuteCandidateAsync(request, candidates[0], ct);
        }

        var execution = await _executionPolicy.ExecuteAsync(
            new IntegrationExecutionRequest(
                request.OrganizationId,
                AnalysisId: null,
                request.WorkflowKey,
                request.CapabilityKey,
                request.NetworkKey),
            candidates,
            (candidate, cancellationToken) => ExecuteCandidateAsync(request, candidate, cancellationToken),
            ct);

        return execution.IsSuccess
            ? Result.Success(execution.Value.Payload)
            : Result.Failure<ImageGenerationResult>(execution.Error);
    }

    private async Task<Result<ImageGenerationResult>> ExecuteCandidateAsync(
        ImageGenerationRequest request,
        IntegrationRouteCandidate candidate,
        CancellationToken ct)
    {
        var credentialResult = await _credentialResolver.ResolveAsync(request.OrganizationId, candidate, ct);
        if (credentialResult.IsFailure)
        {
            _logger.LogWarning("Image credential resolution failed for org {OrgId} provider {ProviderKey}: {Code}.", request.OrganizationId, candidate.ProviderKey, credentialResult.Error.Code);
            return Result.Failure<ImageGenerationResult>(credentialResult.Error);
        }

        var credential = credentialResult.Value;
        var selectedModel = request.PreferredModel ?? candidate.SelectedModel ?? credential.SelectedModel;
        if (!string.IsNullOrWhiteSpace(selectedModel))
        {
            credential = credential with { SelectedModel = selectedModel };
        }

        IImageModelClient client;
        try
        {
            client = _clientFactory.Create<IImageModelClient>(credential);
        }
        catch (InvalidOperationException)
        {
            _logger.LogWarning("No IImageModelClient registered for provider '{ProviderKey}'.", candidate.ProviderKey);
            return Result.Failure<ImageGenerationResult>(IntegrationErrors.ExternalProviderUnsupported);
        }

        return await client.GenerateAsync(request with
        {
            PreferredProviderKey = credential.ProviderKey,
            PreferredModel = credential.SelectedModel,
        }, ct);
    }
}
