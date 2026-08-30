using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Domain.Social.ContentStudio;

/// <summary>
/// Aggregate root for a named collection of content items (a generation run now; campaign / plan-period /
/// client-presentation / ad-hoc later). Members are soft, ordered references — removing a set never deletes
/// its items, and an item may belong to more than one set.
/// </summary>
public sealed class ContentSet : Entity<ContentSetId>
{
    private readonly List<ContentSetMember> _members = new();

    private ContentSet(
        ContentSetId id,
        Guid organizationId,
        Guid clientId,
        SetKind kind,
        SetOrigin origin,
        IEnumerable<ContentSetMember> members,
        DateTime createdAtUtc,
        DateTime updatedAtUtc)
        : base(id)
    {
        OrganizationId = organizationId;
        ClientId = clientId;
        Kind = kind;
        Origin = origin;
        _members = members.ToList();
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
    }

    // Parameterless constructor for EF Core
    private ContentSet() : base(default) { }

    public Guid OrganizationId { get; private set; }

    public Guid ClientId { get; private set; }

    public SetKind Kind { get; private set; }

    public SetOrigin Origin { get; private set; } = default!;

    /// <summary>Members ordered by their sort order.</summary>
    public IReadOnlyList<ContentSetMember> Members =>
        _members.OrderBy(m => m.SortOrder).ToList();

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>Creates an empty set for a single generation run.</summary>
    public static Result<ContentSet> CreateForRun(
        ContentSetId id,
        Guid organizationId,
        Guid clientId,
        SetOrigin origin,
        DateTime nowUtc)
    {
        if (origin.SourceRunId is null)
        {
            return Result.Failure<ContentSet>(
                ContentStudioErrors.Validation("A generation-run set requires a source run id."));
        }

        return Result.Success(new ContentSet(
            id,
            organizationId,
            clientId,
            SetKind.GenerationRun,
            origin,
            Array.Empty<ContentSetMember>(),
            nowUtc,
            nowUtc));
    }

    /// <summary>Re-hydration factory used by EF mapping. Bypasses validation — data is trusted.</summary>
    public static ContentSet Restore(
        ContentSetId id,
        Guid organizationId,
        Guid clientId,
        SetKind kind,
        SetOrigin origin,
        IEnumerable<ContentSetMember> members,
        DateTime createdAtUtc,
        DateTime updatedAtUtc) =>
        new(id, organizationId, clientId, kind, origin, members, createdAtUtc, updatedAtUtc);

    /// <summary>Adds a soft member reference. Rejects a duplicate item id.</summary>
    public Result AddMember(ContentItemId itemId, int sortOrder)
    {
        if (_members.Any(m => m.ItemId == itemId))
        {
            return Result.Failure(ContentStudioErrors.MemberAlreadyExists);
        }

        _members.Add(new ContentSetMember(itemId, sortOrder));
        return Result.Success();
    }

    /// <summary>Removes a member reference. Does not delete the item itself.</summary>
    public Result RemoveMember(ContentItemId itemId)
    {
        var existing = _members.FirstOrDefault(m => m.ItemId == itemId);
        if (existing is null)
        {
            return Result.Failure(ContentStudioErrors.MemberNotFound);
        }

        _members.Remove(existing);
        return Result.Success();
    }
}
