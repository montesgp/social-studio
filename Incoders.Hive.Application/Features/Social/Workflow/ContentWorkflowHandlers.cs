using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Common.Pagination;
using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.Workflow;

public sealed class StartContentWorkflowHandler
    : IRequestHandler<StartContentWorkflowCommand, Result<ContentWorkflowSessionResponse>>
{
    private readonly IContentWorkflowService _service;

    public StartContentWorkflowHandler(IContentWorkflowService service)
    {
        _service = service;
    }

    public Task<Result<ContentWorkflowSessionResponse>> Handle(
        StartContentWorkflowCommand request,
        CancellationToken cancellationToken)
    {
        return _service.StartAsync(
            request.OrganizationId,
            new ContentWorkflowStartInput(
                request.ClientId,
                request.ClientName,
                request.SearchCriterion,
                request.StrategyMode,
                request.NetworkSources,
                request.ForceRefresh,
                request.OriginMode,
                request.MaxResults),
            cancellationToken);
    }
}

public sealed class GetContentWorkflowStatusHandler
    : IRequestHandler<GetContentWorkflowStatusQuery, Result<ContentWorkflowStatusResponse>>
{
    private readonly IContentWorkflowService _service;

    public GetContentWorkflowStatusHandler(IContentWorkflowService service)
    {
        _service = service;
    }

    public Task<Result<ContentWorkflowStatusResponse>> Handle(
        GetContentWorkflowStatusQuery request,
        CancellationToken cancellationToken) =>
        _service.GetStatusAsync(request.OrganizationId, request.WorkflowId, cancellationToken);
}

public sealed class GetContentWorkflowResultsHandler
    : IRequestHandler<GetContentWorkflowResultsQuery, Result<PageResult<ContentWorkflowResultResponse>>>
{
    private readonly IContentWorkflowService _service;

    public GetContentWorkflowResultsHandler(IContentWorkflowService service)
    {
        _service = service;
    }

    public Task<Result<PageResult<ContentWorkflowResultResponse>>> Handle(
        GetContentWorkflowResultsQuery request,
        CancellationToken cancellationToken) =>
        _service.GetResultsAsync(
            request.OrganizationId,
            request.WorkflowId,
            request.Page,
            request.PageSize,
            cancellationToken);
}

public sealed class SelectContentWorkflowResultsHandler
    : IRequestHandler<SelectContentWorkflowResultsCommand, Result<ContentWorkflowSelectionResponse>>
{
    private readonly IContentWorkflowService _service;

    public SelectContentWorkflowResultsHandler(IContentWorkflowService service)
    {
        _service = service;
    }

    public Task<Result<ContentWorkflowSelectionResponse>> Handle(
        SelectContentWorkflowResultsCommand request,
        CancellationToken cancellationToken) =>
        _service.SetSelectionAsync(
            request.OrganizationId,
            request.WorkflowId,
            request.ResultIds,
            cancellationToken);
}

public sealed class GetContentWorkflowHistoryHandler
    : IRequestHandler<GetContentWorkflowHistoryQuery, Result<ContentWorkflowHistoryResponse>>
{
    private readonly IContentWorkflowService _service;

    public GetContentWorkflowHistoryHandler(IContentWorkflowService service)
    {
        _service = service;
    }

    public Task<Result<ContentWorkflowHistoryResponse>> Handle(
        GetContentWorkflowHistoryQuery request,
        CancellationToken cancellationToken) =>
        _service.GetHistoryAsync(request.OrganizationId, request.WorkflowId, cancellationToken);
}

public sealed class ListContentWorkflowHistoryHandler
    : IRequestHandler<ListContentWorkflowHistoryQuery, Result<IReadOnlyList<ContentWorkflowHistoryItemResponse>>>
{
    private readonly IContentWorkflowService _service;

    public ListContentWorkflowHistoryHandler(IContentWorkflowService service)
    {
        _service = service;
    }

    public Task<Result<IReadOnlyList<ContentWorkflowHistoryItemResponse>>> Handle(
        ListContentWorkflowHistoryQuery request,
        CancellationToken cancellationToken) =>
        _service.ListHistoryAsync(request.OrganizationId, request.Limit, request.ClientId, cancellationToken);
}

public sealed class RecoverContentWorkflowCandidateHandler
    : IRequestHandler<RecoverContentWorkflowCandidateCommand, Result<ContentWorkflowCandidateStatusResponse>>
{
    private readonly IContentWorkflowService _service;

    public RecoverContentWorkflowCandidateHandler(IContentWorkflowService service)
    {
        _service = service;
    }

    public Task<Result<ContentWorkflowCandidateStatusResponse>> Handle(
        RecoverContentWorkflowCandidateCommand request,
        CancellationToken cancellationToken) =>
        _service.RecoverCandidateAsync(request.OrganizationId, request.WorkflowId, request.CandidateId, cancellationToken);
}
