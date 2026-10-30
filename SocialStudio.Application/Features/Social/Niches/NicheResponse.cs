using SocialStudio.Domain.Social.Niches;

namespace SocialStudio.Application.Features.Social.Niches;

public sealed record NicheResponse(
    Guid Id,
    string Code,
    string DisplayName,
    string? IconKey,
    IReadOnlyList<string> SearchHashtags,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc)
{
    public static NicheResponse FromDomain(Niche n) =>
        new(
            n.Id.Value,
            n.Code,
            n.DisplayName,
            n.IconKey,
            n.SearchHashtags.ToList(),
            n.IsActive,
            n.CreatedAtUtc,
            n.UpdatedAtUtc);
}
