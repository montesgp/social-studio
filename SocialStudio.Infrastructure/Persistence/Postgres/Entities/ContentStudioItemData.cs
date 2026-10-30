namespace SocialStudio.Infrastructure.Persistence.Postgres.Entities;

/// <summary>
/// EF Core persistence entity for <c>social.content_studio_items</c>.
/// No annotations — all configuration is done via fluent API in AppIdentityDbContext.
/// No navigation to generation/package/plan rows — those are soft Guids (orphan-tolerant).
/// Enums are stored as smallint; the domain↔short conversion lives in the repository mapping.
/// </summary>
public sealed class ContentStudioItemData
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid ClientId { get; set; }

    // Origin (soft reference to the generator output)
    public short GeneratorKind { get; set; }
    public Guid SourceRunId { get; set; }
    public Guid? SourcePackageId { get; set; }
    public Guid FormatResultId { get; set; }
    public string FormatKey { get; set; } = string.Empty;

    // Kind
    public short ContentMedium { get; set; }

    // Draft (editable working payload)
    public string? DraftText { get; set; }
    public string? DraftCaption { get; set; }
    public string? DraftVisualSpecMarkup { get; set; }
    public List<ContentStudioAssetData> DraftAssets { get; set; } = new();
    public string TargetNetwork { get; set; } = string.Empty;
    public string TargetPlacement { get; set; } = string.Empty;

    // Lifecycle
    public short ReviewState { get; set; }
    public string? RejectionReason { get; set; }
    public Guid? ReviewerUserId { get; set; }

    // Variants
    public Guid VariantGroupId { get; set; }
    public bool IsPrimaryVariant { get; set; }

    // Edit trace (owned, prefixed edit_*)
    public short EditLastSource { get; set; }
    public DateTime? EditLastAtUtc { get; set; }
    public string? EditPromptVersion { get; set; }

    // Plan context (owned, nullable, prefixed plan_*)
    public Guid? PlanContentPlanId { get; set; }
    public short? PlanPeriodKind { get; set; }
    public int? PlanYear { get; set; }
    public int? PlanMonth { get; set; }
    public Guid? PlanCalendarSlotId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
