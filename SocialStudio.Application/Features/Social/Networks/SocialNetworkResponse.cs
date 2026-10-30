using SocialStudio.Domain.Social.Networks;

namespace SocialStudio.Application.Features.Social.Networks;

public sealed record SocialNetworkResponse(
    Guid Id,
    string Code,
    string DisplayName,
    string IconKey,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc)
{
    public static SocialNetworkResponse FromDomain(SocialNetwork n) =>
        new(
            n.Id.Value,
            n.Code,
            n.DisplayName,
            n.IconKey,
            n.IsActive,
            n.CreatedAtUtc,
            n.UpdatedAtUtc);
}
