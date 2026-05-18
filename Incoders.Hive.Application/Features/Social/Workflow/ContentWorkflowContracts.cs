namespace Incoders.Hive.Application.Features.Social.Workflow;

public sealed record ContentWorkflowStartInput(
    Guid ClientId,
    string ClientName,
    string SearchCriterion,
    string StrategyMode,
    IReadOnlyList<string> NetworkSources);

public sealed record ContentWorkflowSessionResponse(
    Guid WorkflowId,
    Guid ClientId,
    string ClientName,
    string SearchCriterion,
    string StrategyMode,
    IReadOnlyList<string> NetworkSources,
    string Status,
    int ProgressPercentage,
    string ProgressMessage,
    int TotalResults,
    int SelectedCount,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record ContentWorkflowStepResponse(
    string Key,
    string Label,
    string Status,
    string Message,
    int ProgressPercentage);

public sealed record ContentWorkflowStatusResponse(
    Guid WorkflowId,
    string Status,
    int ProgressPercentage,
    string ProgressMessage,
    IReadOnlyList<ContentWorkflowStepResponse> Steps,
    DateTime UpdatedAtUtc);

public sealed record ContentWorkflowResultResponse(
    Guid Id,
    string Title,
    string NetworkSource,
    string SearchCriterion,
    string StrategyMode,
    string VideoUrl,
    string ThumbnailUrl,
    string Summary,
    int Views,
    int Likes,
    int Comments,
    bool IsSelected);

public sealed record ContentWorkflowPreviewResponse(
    Guid WorkflowId,
    Guid ResultId,
    string ResultTitle,
    string VideoAnalysis,
    string ScriptDraft);

public sealed record ContentWorkflowSelectionResponse(
    Guid WorkflowId,
    int SelectedCount,
    IReadOnlyList<Guid> SelectedResultIds,
    DateTime UpdatedAtUtc);

public sealed record ContentWorkflowActionResponse(
    Guid ActionId,
    Guid WorkflowId,
    string Action,
    string Status,
    IReadOnlyList<Guid> ResultIds,
    string Message,
    DateTime ExecutedAtUtc);

public sealed record ContentWorkflowPackageResponse(
    Guid WorkflowId,
    string PackageFileName,
    string DownloadUrl,
    string Status,
    int ItemCount,
    DateTime GeneratedAtUtc);

public sealed record ContentWorkflowHistoryItemResponse(
    Guid WorkflowId,
    Guid ClientId,
    string ClientName,
    string SearchCriterion,
    string StrategyMode,
    IReadOnlyList<string> NetworkSources,
    string Status,
    int TotalResults,
    int SelectedCount,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record ContentWorkflowHistoryResponse(
    ContentWorkflowHistoryItemResponse Summary,
    IReadOnlyList<ContentWorkflowActionResponse> Actions);
