using SocialStudio.Application.Common.Pagination;
using SocialStudio.Application.Features.Social.Workflow;
using SocialStudio.Domain.Common;

namespace SocialStudio.Application.Abstractions.Social;

public interface IContentWorkflowService
{
    Task<Result<ContentWorkflowSessionResponse>> StartAsync(
        Guid organizationId,
        ContentWorkflowStartInput input,
        CancellationToken cancellationToken = default);

    Task<Result<ContentWorkflowStatusResponse>> GetStatusAsync(
        Guid organizationId,
        Guid workflowId,
        CancellationToken cancellationToken = default);

    Task<Result<PageResult<ContentWorkflowResultResponse>>> GetResultsAsync(
        Guid organizationId,
        Guid workflowId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<Result<ContentWorkflowSelectionResponse>> SetSelectionAsync(
        Guid organizationId,
        Guid workflowId,
        IReadOnlyList<Guid> resultIds,
        CancellationToken cancellationToken = default);

    Task<Result<ContentWorkflowHistoryResponse>> GetHistoryAsync(
        Guid organizationId,
        Guid workflowId,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<ContentWorkflowHistoryItemResponse>>> ListHistoryAsync(
        Guid organizationId,
        int limit,
        Guid? clientId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Moves a processed candidate (<c>BatchStarted</c>) back to <c>Recovered</c> so it
    /// reappears in the operator's active candidate pool. See <see cref="ContentWorkflowResultResponse.Status"/>.</summary>
    Task<Result<ContentWorkflowCandidateStatusResponse>> RecoverCandidateAsync(
        Guid organizationId,
        Guid workflowId,
        Guid candidateId,
        CancellationToken cancellationToken = default);
}
