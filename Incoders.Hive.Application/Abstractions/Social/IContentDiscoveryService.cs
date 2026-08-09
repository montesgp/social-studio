namespace Incoders.Hive.Application.Abstractions.Social;

/// <summary>
/// A real candidate the operator can choose to generate content from, with the metrics that justify
/// choosing it.
///
/// This is the proposal phase of the Content Generator: the system goes out and finds content that
/// actually performed, shows what it measured, and lets a human decide. The metrics are the whole
/// point — a proposal the operator cannot judge is just a list.
/// </summary>
public sealed record DiscoveredContent(
    string Title,
    string Summary,
    string NetworkSource,
    string ContentUrl,
    string? ThumbnailUrl,
    long Views,
    long Likes,
    long Comments,
    /// <summary>Account the content came from, so the operator can see whose playbook this is.</summary>
    string SourceHandle);

/// <summary>
/// Finds real, high-performing content to propose for generation.
///
/// Implementations must return an EMPTY list rather than throwing when a client has nothing to mine —
/// an empty proposal is a legitimate outcome the UI can explain, while an exception is not.
/// </summary>
public interface IContentDiscoveryService
{
    Task<IReadOnlyList<DiscoveredContent>> DiscoverAsync(
        ContentDiscoveryRequest request,
        CancellationToken ct = default);
}

/// <param name="NetworkSources">Networks the operator ticked; empty means "whatever the client has".</param>
/// <param name="MaxResults">Upper bound on the proposal size. Scraping bills per item.</param>
public sealed record ContentDiscoveryRequest(
    Guid OrganizationId,
    Guid ClientId,
    IReadOnlyList<string> NetworkSources,
    int MaxResults);
