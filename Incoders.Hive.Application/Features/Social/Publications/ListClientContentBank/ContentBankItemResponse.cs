using Incoders.Hive.Application.Features.Social.ContentStudio;

namespace Incoders.Hive.Application.Features.Social.Publications.ListClientContentBank;

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
    IReadOnlyList<PublicationSummaryDto> Publications)
{
    public static ContentBankItemResponse FromMapped(
        ContentItemResponse item, IReadOnlyList<PublicationSummaryDto> publications) =>
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
            publications);
}
