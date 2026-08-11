namespace Incoders.Hive.Application.Abstractions.Social.ContentDiscovery;

/// <summary>
/// A real candidate the operator can choose to generate content from, with the metrics that justify
/// choosing it.
///
/// This is the proposal phase of the Content Generator: the system goes out and finds content that
/// actually performed, shows what it measured, and lets a human decide. The metrics are the whole
/// point — a proposal the operator cannot judge is just a list.
///
/// Metric fields are plain <c>long?</c> (D2, CD-04): <c>null</c> means the network does not report
/// this metric; <c>0</c> means it reported zero. A parallel (value, isAvailable) wrapper would give
/// two ways to say the same thing and would drift.
/// </summary>
public sealed record DiscoveredContent(
    string Title,
    string Summary,
    string NetworkSource,
    string ContentUrl,
    string? ThumbnailUrl,
    long? Views,
    long? Likes,
    long? Comments,
    long? Shares,
    long? Saves,
    DateTimeOffset? PublishedAtUtc,
    /// <summary>Account the content came from, so the operator can see whose playbook this is.</summary>
    string SourceHandle,
    /// <summary>Which source produced it — audit trail and shortfall attribution.</summary>
    string SourceKey);

/// <summary>
/// Finds real, high-performing content to propose for generation.
///
/// Implementations must return an EMPTY plan rather than throwing when a client has nothing to mine —
/// an empty proposal is a legitimate outcome the UI can explain, while an exception is not.
/// </summary>
public interface IContentDiscoveryService
{
    Task<ContentDiscoveryPlan> DiscoverAsync(
        ContentDiscoveryRequest request,
        CancellationToken ct = default);
}

/// <param name="NetworkSources">Networks the operator ticked; empty means "whatever the client has".</param>
/// <param name="MaxResults">Upper bound on the proposal size. Scraping bills per item.</param>
/// <param name="SearchCriterion">Normalized by <c>ContentWorkflowCatalog</c> before it gets here.</param>
public sealed record ContentDiscoveryRequest(
    Guid OrganizationId,
    Guid ClientId,
    IReadOnlyList<string> NetworkSources,
    int MaxResults,
    string SearchCriterion);

/// <summary>
/// What the chain managed to gather, and what it could not.
/// </summary>
/// <param name="Candidates">The candidates found, at most as many as were asked for.</param>
/// <param name="Shortfall">How many the chain could not fill.</param>
/// <param name="FailedSourceKeys">Sources that ran and errored.</param>
/// <param name="UnconfiguredSourceKeys">
/// Sources that never ran because no enabled actor/step configuration exists (D12, CD-13) — distinct
/// from <see cref="FailedSourceKeys"/> so the operator can tell "configure this stage" apart from
/// "the provider is down".
/// </param>
public sealed record ContentDiscoveryPlan(
    IReadOnlyList<DiscoveredContent> Candidates,
    int Shortfall,
    IReadOnlyList<string> FailedSourceKeys,
    IReadOnlyList<string> UnconfiguredSourceKeys);

/// <summary>
/// Three outcomes, not two — "unconfigured" is a first-class one (CD-13, D12), never thrown, never
/// logged as an error, never a code-level fallback.
/// </summary>
public enum SourceOutcome
{
    Ran,
    Unconfigured,
    Failed
}

/// <summary>
/// A single source's contribution to one discovery run. <c>Ran</c> with zero candidates is a
/// SUCCESS: the source is reported nowhere in the plan's failure/unconfigured lists.
/// </summary>
public sealed record SourceDiscoveryResult(
    SourceOutcome Outcome,
    IReadOnlyList<DiscoveredContent> Candidates);
