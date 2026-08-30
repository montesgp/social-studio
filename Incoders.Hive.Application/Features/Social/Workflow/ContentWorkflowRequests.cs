using Incoders.Hive.Application.Common.Pagination;
using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.Workflow;

public sealed record StartContentWorkflowCommand(
    Guid OrganizationId,
    Guid ClientId,
    string ClientName,
    string SearchCriterion,
    string StrategyMode,
    IReadOnlyList<string> NetworkSources,
    bool ForceRefresh = false,
    string? OriginMode = null) : IRequest<Result<ContentWorkflowSessionResponse>>;

public sealed record GetContentWorkflowStatusQuery(
    Guid OrganizationId,
    Guid WorkflowId) : IRequest<Result<ContentWorkflowStatusResponse>>;

public sealed record GetContentWorkflowResultsQuery(
    Guid OrganizationId,
    Guid WorkflowId,
    int Page,
    int PageSize) : IRequest<Result<PageResult<ContentWorkflowResultResponse>>>;

public sealed record SelectContentWorkflowResultsCommand(
    Guid OrganizationId,
    Guid WorkflowId,
    IReadOnlyList<Guid> ResultIds) : IRequest<Result<ContentWorkflowSelectionResponse>>;

public sealed record GetContentWorkflowHistoryQuery(
    Guid OrganizationId,
    Guid WorkflowId) : IRequest<Result<ContentWorkflowHistoryResponse>>;

public sealed record ListContentWorkflowHistoryQuery(
    Guid OrganizationId,
    int Limit,
    Guid? ClientId = null) : IRequest<Result<IReadOnlyList<ContentWorkflowHistoryItemResponse>>>;
