using SocialStudio.Application.Common.Pagination;
using SocialStudio.Domain.Common;
using MediatR;

namespace SocialStudio.Application.Features.Social.Workflow;

public sealed record StartContentWorkflowCommand(
    Guid OrganizationId,
    Guid ClientId,
    string ClientName,
    string SearchCriterion,
    string StrategyMode,
    IReadOnlyList<string> NetworkSources,
    bool ForceRefresh = false,
    string? OriginMode = null,
    int? MaxResults = null) : IRequest<Result<ContentWorkflowSessionResponse>>;

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

/// <summary>
/// The operator's explicit "bring it back" action on a candidate the Generator screen's candidate grid
/// moved into its "Processed" section (see <see cref="ContentWorkflowResultResponse.Status"/>). Only
/// legal from <c>BatchStarted</c> (see <see cref="SocialStudio.Domain.Social.Workflow.WorkflowCandidateTransition"/>);
/// does not undo whatever content generation already happened and does not block re-enqueueing the same
/// candidate afterwards.
/// </summary>
public sealed record RecoverContentWorkflowCandidateCommand(
    Guid OrganizationId,
    Guid WorkflowId,
    Guid CandidateId) : IRequest<Result<ContentWorkflowCandidateStatusResponse>>;
