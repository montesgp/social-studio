namespace SocialStudio.Infrastructure.Persistence.Postgres.Entities;

/// <summary>
/// EF Core persistence entity for <c>social.content_studio_sets</c>.
/// Members live in <see cref="ContentStudioSetItemData"/> (join table) and reference items softly.
/// </summary>
public sealed class ContentStudioSetData
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid ClientId { get; set; }

    public short SetKind { get; set; }

    // Origin (soft, nullable)
    public short? OriginGeneratorKind { get; set; }
    public Guid? OriginSourceRunId { get; set; }
    public Guid? OriginCampaignId { get; set; }
    public short? OriginPeriodKind { get; set; }
    public int? OriginYear { get; set; }
    public int? OriginMonth { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public List<ContentStudioSetItemData> Items { get; set; } = new();
}
