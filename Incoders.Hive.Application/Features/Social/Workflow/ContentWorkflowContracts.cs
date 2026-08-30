namespace Incoders.Hive.Application.Features.Social.Workflow;

/// <param name="ForceRefresh">
/// The operator's EXPLICIT opt-in to a billed provider run. Discovery serves from the stored candidate
/// bank by default; refresh is manual only. Defaults to <c>false</c> so no caller can cause a paid run
/// by omission.
/// </param>
/// <param name="OriginMode">
/// <c>discovery</c> (default) reaches the market: scraping providers or the stored candidate bank.
/// <c>ai-ideas</c> reaches nothing — the model invents topics from the client's own context, so no
/// actor runs and nothing is billed. Only the RAW MATERIAL differs; the client's aesthetic is applied
/// later, at generation, and is unaffected by this choice. <see cref="NetworkSources"/> and
/// <see cref="ForceRefresh"/> are meaningless under <c>ai-ideas</c> and are ignored.
/// </param>
public sealed record ContentWorkflowStartInput(
    Guid ClientId,
    string ClientName,
    string SearchCriterion,
    string StrategyMode,
    IReadOnlyList<string> NetworkSources,
    bool ForceRefresh = false,
    string? OriginMode = null);

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

/// <param name="Status">
/// One of <c>in_progress</c>, <c>completed</c>, <c>failed</c> or <c>unconfigured</c>. Discovery runs in
/// the background after <c>StartAsync</c> returns, so this is the only place the caller learns the
/// outcome. <c>completed</c> with <see cref="TotalResults"/> at zero is the honest "ran and found
/// nothing" case — deliberately distinct from <c>failed</c> (the provider errored) and from
/// <c>unconfigured</c> (no enabled actor for the requested network).
/// </param>
/// <param name="TotalResults">
/// How many candidates the session holds. Carried here so a poller can tell "completed with
/// candidates" from "completed empty" without a second round trip to the results endpoint.
/// </param>
/// <param name="ResultOrigin">
/// <c>bank</c> when every candidate was served from the stored candidate bank (no provider call, nothing
/// billed), <c>fresh</c> when the run scraped. Empty while the session is still working. Serving cached
/// data silently is how an operator ends up working from stale content without knowing.
/// </param>
/// <param name="BankObservedAtUtc">
/// When the OLDEST banked candidate was observed, so the screen can say how old the stored ones are.
/// <c>null</c> for a fresh run.
/// </param>
public sealed record ContentWorkflowStatusResponse(
    Guid WorkflowId,
    string Status,
    int ProgressPercentage,
    string ProgressMessage,
    IReadOnlyList<ContentWorkflowStepResponse> Steps,
    DateTime UpdatedAtUtc,
    int TotalResults,
    string ResultOrigin = "",
    DateTimeOffset? BankObservedAtUtc = null);

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
