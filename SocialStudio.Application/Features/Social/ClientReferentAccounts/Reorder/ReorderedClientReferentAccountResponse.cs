using SocialStudio.Domain.Social.ClientReferentAccounts;

namespace SocialStudio.Application.Features.Social.ClientReferentAccounts.Reorder;

/// <summary>
/// Lightweight response for the Reorder endpoint — returns only identity and sort_order.
/// Full denormalization (network fields) is not needed for a reorder confirmation.
/// </summary>
public sealed record ReorderedClientReferentAccountResponse(
    Guid Id,
    Guid ClientId,
    int SortOrder,
    DateTime UpdatedAtUtc)
{
    public static ReorderedClientReferentAccountResponse FromDomain(ClientReferentAccount account) =>
        new(
            account.Id.Value,
            account.ClientId.Value,
            account.SortOrder,
            account.UpdatedAtUtc);
}
