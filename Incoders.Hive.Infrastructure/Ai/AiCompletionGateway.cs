using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;

namespace Incoders.Hive.Infrastructure.Ai;

public sealed class AiCompletionGateway : IAiCompletionGateway
{
    private const string SingleAnalyzerFlowKey = "single-analyzer";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IOrganizationIntegrationResolver _routeResolver;
    private readonly IIntegrationCredentialResolver _credentialResolver;
    private readonly IExternalProviderClientFactory _clientFactory;
    private readonly IIntegrationExecutionPolicy _executionPolicy;

    public AiCompletionGateway(
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

    public async Task<Result<AiJsonCompletionResponse<TResponse>>> CompleteJsonAsync<TResponse>(
        AiJsonCompletionRequest request,
        CancellationToken cancellationToken = default)
        where TResponse : notnull
    {
        var route = await _routeResolver.ResolveRouteAsync(
            new IntegrationRouteRequest(
                request.OrganizationId,
                SingleAnalyzerFlowKey,
                request.CapabilityKey,
                PlatformKey: null,
                request.PreferredProviderKey,
                request.PreferredModel),
            cancellationToken);

        if (route.IsFailure)
        {
            return Result.Failure<AiJsonCompletionResponse<TResponse>>(route.Error);
        }

        var execution = await _executionPolicy.ExecuteAsync(
            new IntegrationExecutionRequest(
                request.OrganizationId,
                request.AnalysisId,
                SingleAnalyzerFlowKey,
                request.CapabilityKey,
                PlatformKey: null),
            route.Value.Candidates,
            async (candidate, token) => await CompleteJsonForCandidateAsync<TResponse>(request, candidate, token),
            cancellationToken);

        if (execution.IsFailure)
        {
            return Result.Failure<AiJsonCompletionResponse<TResponse>>(execution.Error);
        }

        return Result.Success(new AiJsonCompletionResponse<TResponse>(
            execution.Value.Payload.Payload,
            execution.Value.Candidate.ProviderKey,
            execution.Value.Payload.Model,
            execution.Value.Diagnostics));
    }

    private async Task<Result<AiJsonCandidateResponse<TResponse>>> CompleteJsonForCandidateAsync<TResponse>(
        AiJsonCompletionRequest request,
        IntegrationRouteCandidate candidate,
        CancellationToken cancellationToken)
        where TResponse : notnull
    {
        var credential = await _credentialResolver.ResolveAsync(request.OrganizationId, candidate, cancellationToken);
        if (credential.IsFailure)
        {
            return Result.Failure<AiJsonCandidateResponse<TResponse>>(credential.Error);
        }

        IAiModelClient client;
        try
        {
            client = _clientFactory.Create<IAiModelClient>(credential.Value);
        }
        catch (InvalidOperationException)
        {
            return Result.Failure<AiJsonCandidateResponse<TResponse>>(IntegrationErrors.ExternalProviderUnsupported);
        }

        var completion = await client.CompleteAsync(
            new AiCompletionRequest(
                request.OrganizationId,
                request.CapabilityKey,
                request.PromptVersionKey,
                request.InputJson,
                request.PreferredProviderKey,
                request.PreferredModel ?? credential.Value.SelectedModel),
            cancellationToken);

        if (completion.IsFailure)
        {
            return Result.Failure<AiJsonCandidateResponse<TResponse>>(completion.Error);
        }

        var parsed = TryParse<TResponse>(completion.Value.Output);
        if (parsed.IsSuccess)
        {
            return Result.Success(new AiJsonCandidateResponse<TResponse>(
                parsed.Value,
                completion.Value.Model));
        }

        var repair = await client.CompleteAsync(
            new AiCompletionRequest(
                request.OrganizationId,
                request.CapabilityKey,
                $"{request.PromptVersionKey}:json-repair",
                $"Return only valid JSON for this schema. Invalid output: {completion.Value.Output}",
                request.PreferredProviderKey,
                request.PreferredModel ?? credential.Value.SelectedModel),
            cancellationToken);

        if (repair.IsFailure)
        {
            return Result.Failure<AiJsonCandidateResponse<TResponse>>(repair.Error);
        }

        parsed = TryParse<TResponse>(repair.Value.Output);
        return parsed.IsSuccess
            ? Result.Success(new AiJsonCandidateResponse<TResponse>(parsed.Value, repair.Value.Model))
            : Result.Failure<AiJsonCandidateResponse<TResponse>>(IntegrationErrors.AiJsonInvalid);
    }

    private static Result<TResponse> TryParse<TResponse>(string output)
        where TResponse : notnull
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<TResponse>(output, JsonOptions);
            return parsed is null
                ? Result.Failure<TResponse>(IntegrationErrors.AiJsonInvalid)
                : Result.Success(parsed);
        }
        catch (JsonException)
        {
            return Result.Failure<TResponse>(IntegrationErrors.AiJsonInvalid);
        }
    }

    private sealed record AiJsonCandidateResponse<TResponse>(
        TResponse Payload,
        string? Model)
        where TResponse : notnull;
}
