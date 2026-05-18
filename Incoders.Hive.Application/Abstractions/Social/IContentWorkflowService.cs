using Incoders.Hive.Application.Common.Pagination;
using Incoders.Hive.Application.Features.Social.Workflow;
using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Application.Abstractions.Social;

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

    Task<Result<ContentWorkflowPreviewResponse>> GetPreviewAsync(
        Guid organizationId,
        Guid workflowId,
        Guid resultId,
        CancellationToken cancellationToken = default);

    Task<Result<ContentWorkflowSelectionResponse>> SetSelectionAsync(
        Guid organizationId,
        Guid workflowId,
        IReadOnlyList<Guid> resultIds,
        CancellationToken cancellationToken = default);

    Task<Result<ContentWorkflowActionResponse>> RunActionAsync(
        Guid organizationId,
        Guid workflowId,
        string action,
        IReadOnlyList<Guid> resultIds,
        CancellationToken cancellationToken = default);

    Task<Result<ContentWorkflowPackageResponse>> GeneratePackageAsync(
        Guid organizationId,
        Guid workflowId,
        CancellationToken cancellationToken = default);

    Task<Result<ContentWorkflowHistoryResponse>> GetHistoryAsync(
        Guid organizationId,
        Guid workflowId,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<ContentWorkflowHistoryItemResponse>>> ListHistoryAsync(
        Guid organizationId,
        int limit,
        CancellationToken cancellationToken = default);
}
