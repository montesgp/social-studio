using Incoders.Hive.ApiService.Endpoints.Social;
using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Tenancy;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Batches;
using Incoders.Hive.Application.Features.Social.Workflow;
using Incoders.Hive.Application.Features.Social.Workflow.Enqueue;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Tenants;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Incoders.Hive.Tests.Api.Social;

/// <summary>
/// Contract tests for the Phase 6 enqueue endpoint and the client-scoped history filter — mirrors the
/// pattern from <c>ContentGenerationEndpointTests</c>: internal static handler methods invoked directly
/// so the mapping contract is tested without standing up a full web host.
/// </summary>
public sealed class ContentWorkflowEndpointTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid WorkflowId = Guid.NewGuid();
    private static readonly Guid ClientId = Guid.NewGuid();

    [Fact]
    public async Task Enqueue_ValidRequest_Returns202WithPerCandidateResults()
    {
        var candidateId = Guid.NewGuid();
        var response = new EnqueueContentWorkflowCandidatesResponse(
            WorkflowId,
            [new EnqueuedCandidateResult(candidateId, true, null)],
            15,
            "/content-generation-jobs");

        var sender = new FakeSender<EnqueueContentWorkflowCandidatesCommand, EnqueueContentWorkflowCandidatesResponse>(
            Result.Success(response));

        var body = new EnqueueSocialContentWorkflowRequest(
            ClientId,
            [new EnqueueCandidateCompositionRequest(candidateId, [new EnqueueFormatCompositionRequest("reel-script", 1)])]);

        var result = await SocialContentWorkflowEndpointsExtensions.EnqueueAsync(
            WorkflowId, body, sender, new FakeTenant(OrgId), new FakeUser(UserId), CancellationToken.None);

        var accepted = Assert.IsType<Accepted<EnqueueContentWorkflowCandidatesResponse>>(result);
        Assert.NotNull(accepted.Value);
        Assert.Equal(15, accepted.Value!.EstimatedMinutes);
        Assert.Single(accepted.Value.Candidates);

        var sentCommand = Assert.IsType<EnqueueContentWorkflowCandidatesCommand>(sender.LastCommand);
        Assert.Equal(OrgId, sentCommand.OrganizationId);
        Assert.Equal(UserId, sentCommand.UserId);
        Assert.Equal(ClientId, sentCommand.ClientId);
        Assert.Equal(candidateId, sentCommand.Candidates[0].CandidateId);
        Assert.Equal("reel-script", sentCommand.Candidates[0].Composition[0].FormatKey);
    }

    [Fact]
    public async Task Enqueue_MissingTenant_ReturnsProblem_AndNeverCallsSender()
    {
        var sender = new FakeSender<EnqueueContentWorkflowCandidatesCommand, EnqueueContentWorkflowCandidatesResponse>(
            Result.Success(new EnqueueContentWorkflowCandidatesResponse(WorkflowId, [], 0, "/")));

        var body = new EnqueueSocialContentWorkflowRequest(ClientId, []);

        var result = await SocialContentWorkflowEndpointsExtensions.EnqueueAsync(
            WorkflowId, body, sender, new FakeTenant(null), new FakeUser(UserId), CancellationToken.None);

        Assert.IsNotType<Accepted<EnqueueContentWorkflowCandidatesResponse>>(result);
        Assert.Null(sender.LastCommand);
    }

    [Fact]
    public async Task Enqueue_MissingUser_ReturnsProblem_AndNeverCallsSender()
    {
        var sender = new FakeSender<EnqueueContentWorkflowCandidatesCommand, EnqueueContentWorkflowCandidatesResponse>(
            Result.Success(new EnqueueContentWorkflowCandidatesResponse(WorkflowId, [], 0, "/")));

        var body = new EnqueueSocialContentWorkflowRequest(ClientId, []);

        var result = await SocialContentWorkflowEndpointsExtensions.EnqueueAsync(
            WorkflowId, body, sender, new FakeTenant(OrgId), new FakeUser(null), CancellationToken.None);

        Assert.IsNotType<Accepted<EnqueueContentWorkflowCandidatesResponse>>(result);
        Assert.Null(sender.LastCommand);
    }

    [Fact]
    public async Task ListHistory_WithoutClientId_PassesNullThrough()
    {
        var sender = new FakeSender<ListContentWorkflowHistoryQuery, IReadOnlyList<ContentWorkflowHistoryItemResponse>>(
            Result.Success<IReadOnlyList<ContentWorkflowHistoryItemResponse>>([]));

        await SocialContentWorkflowEndpointsExtensions.ListHistoryAsync(
            sender, new FakeTenant(OrgId), CancellationToken.None, limit: 20);

        var sentQuery = Assert.IsType<ListContentWorkflowHistoryQuery>(sender.LastCommand);
        Assert.Null(sentQuery.ClientId);
        Assert.Equal(20, sentQuery.Limit);
    }

    [Fact]
    public async Task ListHistory_WithClientId_PassesItThrough()
    {
        var sender = new FakeSender<ListContentWorkflowHistoryQuery, IReadOnlyList<ContentWorkflowHistoryItemResponse>>(
            Result.Success<IReadOnlyList<ContentWorkflowHistoryItemResponse>>([]));

        await SocialContentWorkflowEndpointsExtensions.ListHistoryAsync(
            sender, new FakeTenant(OrgId), CancellationToken.None, limit: 20, clientId: ClientId);

        var sentQuery = Assert.IsType<ListContentWorkflowHistoryQuery>(sender.LastCommand);
        Assert.Equal(ClientId, sentQuery.ClientId);
    }

    // ─── Test doubles ─────────────────────────────────────────────────────────

    private sealed class FakeTenant : ICurrentTenant
    {
        public FakeTenant(Guid? tenantId) => TenantId = tenantId.HasValue ? new TenantId(tenantId.Value) : null;

        public TenantId? TenantId { get; }
        public bool IsResolved => TenantId.HasValue;
    }

    private sealed class FakeUser : ICurrentUser
    {
        public FakeUser(Guid? userId) => UserId = userId;

        public bool IsAuthenticated => UserId.HasValue;
        public Guid? UserId { get; }
        public string? Email => null;
        public bool IsPlatformAdmin => false;
    }

    private sealed class FakeSender<TRequest, TResponse> : ISender
        where TRequest : IRequest<Result<TResponse>>
    {
        private readonly Result<TResponse> _result;
        public object? LastCommand { get; private set; }

        public FakeSender(Result<TResponse> result) => _result = result;

        public Task<TOut> Send<TOut>(IRequest<TOut> request, CancellationToken cancellationToken = default)
        {
            LastCommand = request;
            if (request is TRequest)
            {
                return Task.FromResult((TOut)(object)_result);
            }

            throw new InvalidOperationException($"Unexpected request type: {request.GetType().Name}");
        }

        public Task Send<TReq>(TReq request, CancellationToken cancellationToken = default)
            where TReq : IRequest => Task.CompletedTask;

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            Task.FromResult<object?>(null);

        public IAsyncEnumerable<TOut> CreateStream<TOut>(IStreamRequest<TOut> request, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task Publish(object notification, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;
    }
}
