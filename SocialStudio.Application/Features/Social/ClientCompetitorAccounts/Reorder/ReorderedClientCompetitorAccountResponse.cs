using SocialStudio.Domain.Social.ClientCompetitorAccounts;

namespace SocialStudio.Application.Features.Social.ClientCompetitorAccounts.Reorder;

/// <summary>
/// Lightweight response for the Reorder endpoint — returns only identity and sort_order.
/// </summary>
public sealed record ReorderedClientCompetitorAccountResponse(
    Guid Id,
    Guid ClientId,
    int SortOrder,
    DateTime UpdatedAtUtc)
{
    public static ReorderedClientCompetitorAccountResponse FromDomain(ClientCompetitorAccount account) =>
        new(
            account.Id.Value,
            account.ClientId.Value,
            account.SortOrder,
            account.UpdatedAtUtc);
}
