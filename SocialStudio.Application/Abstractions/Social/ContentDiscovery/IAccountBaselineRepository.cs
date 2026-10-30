namespace SocialStudio.Application.Abstractions.Social.ContentDiscovery;

/// <summary>
/// Port over the cached, author-level baseline table (design §5.2). Implemented by
/// <c>EfAccountBaselineRepository</c> in Infrastructure (WU-5). No <c>client_id</c> anywhere in this
/// port's shape — a baseline is a property of the author, not of whichever client discovered them.
/// </summary>
public interface IAccountBaselineRepository
{
    /// <summary>Reads the cached baseline for one author, or null when none has ever been computed.</summary>
    Task<AccountBaseline?> GetAsync(
        Guid organizationId,
        string networkKey,
        string authorHandle,
        CancellationToken ct = default);

    /// <summary>Upserts by the (organization, network, lower(handle)) unique key (design §5.2).</summary>
    Task UpsertAsync(
        Guid organizationId,
        AccountBaseline baseline,
        CancellationToken ct = default);
}
