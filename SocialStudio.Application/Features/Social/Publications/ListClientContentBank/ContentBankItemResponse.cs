using SocialStudio.Application.Features.Social.ContentStudio;
using SocialStudio.Domain.Social.ContentStudio;

namespace SocialStudio.Application.Features.Social.Publications.ListClientContentBank;

/// <summary>
/// <see cref="ContentItemResponse"/> fields plus the publication axis (design §5.2) — every
/// <see cref="Publications.PublicationSummaryDto"/> recorded against this item, so the Bank/Planner
/// can render both axes in a single round-trip.
/// </summary>
public sealed record ContentBankItemResponse(
    Guid Id,
    Guid OrganizationId,
    Guid ClientId,
    ContentOriginDto Origin,
    string FormatKey,
    string Medium,
    string? Text,
    string? Caption,
    string? VisualSpecMarkup,
    IReadOnlyList<ContentAssetDto> Assets,
    string TargetNetwork,
    string TargetPlacement,
    string State,
    EditTraceDto EditTrace,
    Guid VariantGroupId,
    bool IsPrimaryVariant,
    PlanContextDto? PlanContext,
    string? RejectionReason,
    Guid? ReviewerUserId,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    IReadOnlyList<PublicationSummaryDto> Publications,
    /// <summary>
    /// The item's OWNING <c>ContentSet</c> id, resolved via <c>Origin.SourceRunId</c> (the same
    /// resolution <c>DuplicateItemCommand</c>/<c>SpawnVariantCommand</c> already use). Bank items are
    /// already materialized — this lets "Open in Studio" load the existing set directly instead of
    /// re-materializing by analysis id, which fails for runs that are not re-materializable that way.
    /// Null when no owning set could be resolved (should not normally happen for a materialized item).
    /// </summary>
    Guid? SetId,
    /// <summary>
    /// Whether this item can be sent to a social network publishing API. False for script/copy
    /// deliverables such as <c>reel-script</c>, which are downloadable only. Mirrors exactly what
    /// <c>PublicationEditorialGate</c> enforces server-side, so the Bank never offers a "Publish"
    /// action the publish command would reject.
    /// </summary>
    bool IsPublishable)
{
    public static ContentBankItemResponse FromMapped(
        ContentItemResponse item, IReadOnlyList<PublicationSummaryDto> publications, Guid? setId) =>
        new(
            item.Id,
            item.OrganizationId,
            item.ClientId,
            item.Origin,
            item.FormatKey,
            item.Medium,
            item.Text,
            item.Caption,
            item.VisualSpecMarkup,
            item.Assets,
            item.TargetNetwork,
            item.TargetPlacement,
            item.State,
            item.EditTrace,
            item.VariantGroupId,
            item.IsPrimaryVariant,
            item.PlanContext,
            item.RejectionReason,
            item.ReviewerUserId,
            item.CreatedAtUtc,
            item.UpdatedAtUtc,
            publications,
            setId,
            Enum.TryParse<ContentMedium>(item.Medium, ignoreCase: true, out var medium)
                && PublicationEditorialGate.IsPublishableMedium(medium));
}
