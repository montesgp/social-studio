namespace SocialStudio.Domain.Social.ContentStudio;

/// <summary>
/// An ordered, soft membership of a <see cref="ContentItem"/> in a <see cref="ContentSet"/>.
/// References the item by id only — removing a set never deletes its items.
/// </summary>
public sealed record ContentSetMember(
    ContentItemId ItemId,
    int SortOrder);
