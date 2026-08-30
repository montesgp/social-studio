using Incoders.Hive.Domain.Social.ContentStudio;

namespace Incoders.Hive.Application.Features.Social.ContentStudio;

public sealed record ContentOriginDto(
    string GeneratorKind,
    Guid SourceRunId,
    Guid? SourcePackageId,
    Guid FormatResultId,
    string FormatKey)
{
    public static ContentOriginDto FromDomain(ContentOrigin o) =>
        new(o.GeneratorKind.ToString(), o.SourceRunId, o.SourcePackageId, o.FormatResultId, o.FormatKey);
}

public sealed record ContentAssetDto(string Role, string StorageUri, string Mime)
{
    public static ContentAssetDto FromDomain(ContentAssetRef a) => new(a.Role, a.StorageUri, a.Mime);
}

public sealed record EditTraceDto(string LastEditedSource, DateTime? LastEditedAtUtc, string? PromptVersion)
{
    public static EditTraceDto FromDomain(EditTrace t) =>
        new(t.LastEditedSource.ToString(), t.LastEditedAtUtc, t.PromptVersion);
}

public sealed record PlanContextDto(Guid ContentPlanId, short PeriodKind, int Year, int Month, Guid CalendarSlotId)
{
    public static PlanContextDto FromDomain(PlanContext p) =>
        new(p.ContentPlanId, p.Period.Kind, p.Period.Year, p.Period.Month, p.CalendarSlotId);
}

public sealed record ContentItemResponse(
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
    DateTime UpdatedAtUtc)
{
    public static ContentItemResponse FromDomain(ContentItem i) =>
        new(
            i.Id.Value,
            i.OrganizationId,
            i.ClientId,
            ContentOriginDto.FromDomain(i.Origin),
            i.Kind.FormatKey,
            i.Kind.Medium.ToString(),
            i.Draft.Text,
            i.Draft.Caption?.Value,
            i.Draft.VisualSpecMarkup,
            i.Draft.Assets.Select(ContentAssetDto.FromDomain).ToList(),
            i.Draft.TargetChannel.Network,
            i.Draft.TargetChannel.Placement,
            i.State.ToString(),
            EditTraceDto.FromDomain(i.EditTrace),
            i.VariantGroupId,
            i.IsPrimaryVariant,
            i.PlanContext is null ? null : PlanContextDto.FromDomain(i.PlanContext),
            i.RejectionReason,
            i.ReviewerUserId,
            i.CreatedAtUtc,
            i.UpdatedAtUtc);
}

public sealed record ReviewSummaryDto(
    int Total,
    int Draft,
    int InReview,
    int Edited,
    int Approved,
    int Rejected,
    int Discarded,
    int ReadyForPublication,
    int HandedOff)
{
    public static ReviewSummaryDto FromDomain(ReviewSummary s) =>
        new(s.Total, s.Draft, s.InReview, s.Edited, s.Approved, s.Rejected, s.Discarded, s.ReadyForPublication, s.HandedOff);
}

public sealed record ContentSetMemberDto(Guid ItemId, int SortOrder);

public sealed record ContentSetResponse(
    Guid Id,
    Guid OrganizationId,
    Guid ClientId,
    string Kind,
    Guid? SourceRunId,
    ReviewSummaryDto Summary,
    IReadOnlyList<ContentSetMemberDto> Members,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc)
{
    public static ContentSetResponse FromDomain(ContentSet set, ReviewSummary summary) =>
        new(
            set.Id.Value,
            set.OrganizationId,
            set.ClientId,
            set.Kind.ToString(),
            set.Origin.SourceRunId,
            ReviewSummaryDto.FromDomain(summary),
            set.Members.Select(m => new ContentSetMemberDto(m.ItemId.Value, m.SortOrder)).ToList(),
            set.CreatedAtUtc,
            set.UpdatedAtUtc);
}
