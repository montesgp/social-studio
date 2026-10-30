using SocialStudio.Domain.Social.ClientOwnAccounts;

namespace SocialStudio.Application.Features.Social.ClientOwnAccounts.Reorder;

/// <summary>
/// Lightweight response for the Reorder endpoint — returns only identity and sort_order.
/// </summary>
public sealed record ReorderedClientOwnAccountResponse(
    Guid Id,
    Guid ClientId,
    int SortOrder,
    DateTime UpdatedAtUtc)
{
    public static ReorderedClientOwnAccountResponse FromDomain(ClientOwnAccount account) =>
        new(
            account.Id.Value,
            account.ClientId.Value,
            account.SortOrder,
            account.UpdatedAtUtc);
}
