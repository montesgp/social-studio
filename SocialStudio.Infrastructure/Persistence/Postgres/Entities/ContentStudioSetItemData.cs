namespace SocialStudio.Infrastructure.Persistence.Postgres.Entities;

/// <summary>
/// EF Core persistence entity for the join table <c>social.content_studio_set_items</c>.
/// <c>ItemId</c> is a SOFT reference (no FK to content_studio_items) so removing a set never
/// deletes items and orphan rows are tolerated and resolved at read time.
/// </summary>
public sealed class ContentStudioSetItemData
{
    public Guid Id { get; set; }

    public Guid SetId { get; set; }

    public Guid ItemId { get; set; }

    public int SortOrder { get; set; }

    // Navigation back to the owning set (FK lives here; configured on the set side).
    public ContentStudioSetData? Set { get; set; }
}
