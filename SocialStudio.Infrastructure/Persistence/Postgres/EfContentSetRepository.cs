using SocialStudio.Application.Abstractions.Social.ContentStudio;
using SocialStudio.Domain.Social.ContentStudio;
using SocialStudio.Infrastructure.Identity;
using SocialStudio.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace SocialStudio.Infrastructure.Persistence.Postgres;

public sealed class EfContentSetRepository : IContentSetRepository
{
    private readonly AppIdentityDbContext _dbContext;

    public EfContentSetRepository(AppIdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ContentSet?> GetByIdAsync(Guid organizationId, ContentSetId id, CancellationToken ct)
    {
        var row = await _dbContext.ContentStudioSets
            .AsNoTracking()
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == id.Value && x.OrganizationId == organizationId, ct);

        return row is null ? null : ToDomain(row);
    }

    public async Task<ContentSet?> GetByRunAsync(Guid organizationId, Guid sourceRunId, CancellationToken ct)
    {
        var row = await _dbContext.ContentStudioSets
            .AsNoTracking()
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.OrganizationId == organizationId && x.OriginSourceRunId == sourceRunId, ct);

        return row is null ? null : ToDomain(row);
    }

    public async Task<IReadOnlyDictionary<Guid, Guid>> GetSetIdsByItemIdsAsync(
        Guid organizationId,
        IReadOnlyCollection<Guid> itemIds,
        CancellationToken ct)
    {
        if (itemIds.Count == 0)
        {
            return new Dictionary<Guid, Guid>();
        }

        // Join through the membership table and back to the set so the org scope is enforced on the
        // set row (the join table itself carries no organization column).
        var pairs = await _dbContext.ContentStudioSetItems
            .AsNoTracking()
            .Where(link => itemIds.Contains(link.ItemId)
                && _dbContext.ContentStudioSets.Any(set =>
                    set.Id == link.SetId && set.OrganizationId == organizationId))
            .Select(link => new { link.ItemId, link.SetId })
            .ToListAsync(ct);

        var map = new Dictionary<Guid, Guid>(pairs.Count);
        foreach (var pair in pairs)
        {
            // An item should belong to exactly one set; if history ever produced more, the first
            // wins deterministically rather than throwing on a read path.
            map.TryAdd(pair.ItemId, pair.SetId);
        }

        return map;
    }

    public async Task<int> RemoveItemMembershipsAsync(
        Guid organizationId, IReadOnlyCollection<Guid> itemIds, CancellationToken ct)
    {
        if (itemIds.Count == 0)
        {
            return 0;
        }

        // Same org-scoping shape as GetSetIdsByItemIdsAsync: the join table carries no organization
        // column, so the scope is enforced on the owning set row.
        var rows = await _dbContext.ContentStudioSetItems
            .Where(link => itemIds.Contains(link.ItemId)
                && _dbContext.ContentStudioSets.Any(set =>
                    set.Id == link.SetId && set.OrganizationId == organizationId))
            .ToListAsync(ct);

        if (rows.Count == 0)
        {
            return 0;
        }

        _dbContext.ContentStudioSetItems.RemoveRange(rows);
        await _dbContext.SaveChangesAsync(ct);
        return rows.Count;
    }

    public async Task AddAsync(ContentSet set, CancellationToken ct)
    {
        _dbContext.ContentStudioSets.Add(ToData(set));
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(ContentSet set, CancellationToken ct)
    {
        var existing = await _dbContext.ContentStudioSets
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == set.Id.Value && x.OrganizationId == set.OrganizationId, ct);

        if (existing is null)
        {
            return;
        }

        Apply(set, existing);

        try
        {
            await _dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            _dbContext.ChangeTracker.Clear();

            existing = await _dbContext.ContentStudioSets
                .Include(x => x.Items)
                .FirstOrDefaultAsync(x => x.Id == set.Id.Value && x.OrganizationId == set.OrganizationId, ct);

            if (existing is null)
            {
                return;
            }

            Apply(set, existing);
            await _dbContext.SaveChangesAsync(ct);
        }
    }

    public async Task<ReviewSummary> GetReviewSummaryAsync(Guid organizationId, ContentSetId setId, CancellationToken ct)
    {
        var states = await (
            from member in _dbContext.ContentStudioSetItems.AsNoTracking()
            join item in _dbContext.ContentStudioItems.AsNoTracking() on member.ItemId equals item.Id
            where member.SetId == setId.Value && item.OrganizationId == organizationId
            select item.ReviewState).ToListAsync(ct);

        return ReviewSummary.From(states.Select(s => (ReviewState)s));
    }

    // ─── Mapping helpers ──────────────────────────────────────────────────────

    private static ContentSet ToDomain(ContentStudioSetData d)
    {
        var origin = new SetOrigin(
            d.OriginGeneratorKind is short kind ? (GeneratorKind)kind : null,
            d.OriginSourceRunId,
            d.OriginCampaignId,
            d.OriginPeriodKind is short periodKind && d.OriginYear is int year && d.OriginMonth is int month
                ? new PeriodRef(periodKind, year, month)
                : null);

        var members = d.Items
            .OrderBy(i => i.SortOrder)
            .Select(i => new ContentSetMember(new ContentItemId(i.ItemId), i.SortOrder))
            .ToList();

        return ContentSet.Restore(
            new ContentSetId(d.Id),
            d.OrganizationId,
            d.ClientId,
            (SetKind)d.SetKind,
            origin,
            members,
            d.CreatedAtUtc,
            d.UpdatedAtUtc);
    }

    private static ContentStudioSetData ToData(ContentSet set)
    {
        var d = new ContentStudioSetData { Id = set.Id.Value };
        MapScalars(set, d);
        d.CreatedAtUtc = set.CreatedAtUtc;
        d.Items = set.Members
            .Select(m => new ContentStudioSetItemData
            {
                Id = Guid.NewGuid(),
                SetId = set.Id.Value,
                ItemId = m.ItemId.Value,
                SortOrder = m.SortOrder,
            })
            .ToList();
        return d;
    }

    private void Apply(ContentSet set, ContentStudioSetData existing)
    {
        MapScalars(set, existing);

        // Reconcile members in place (additive/idempotent): keep the rows that are still members,
        // remove the ones dropped, add the genuinely new ones. The previous Clear()+reinsert deleted
        // every set_item and re-inserted all of them on each open; two concurrent materialize calls
        // (e.g. React StrictMode double-invoke in dev) then raced — the second call's DELETE of rows
        // the first already removed affected 0 rows and threw DbUpdateConcurrencyException.
        var desired = set.Members.ToDictionary(m => m.ItemId.Value, m => m.SortOrder);

        foreach (var row in existing.Items.Where(i => !desired.ContainsKey(i.ItemId)).ToList())
        {
            _dbContext.ContentStudioSetItems.Remove(row);
            existing.Items.Remove(row);
        }

        foreach (var (itemId, sortOrder) in desired)
        {
            var row = existing.Items.FirstOrDefault(i => i.ItemId == itemId);
            if (row is null)
            {
                row = new ContentStudioSetItemData
                {
                    Id = Guid.NewGuid(),
                    SetId = set.Id.Value,
                    ItemId = itemId,
                    SortOrder = sortOrder,
                };
                existing.Items.Add(row);
                _dbContext.ContentStudioSetItems.Add(row);
            }
            else
            {
                row.SortOrder = sortOrder;
            }
        }
    }

    private static void MapScalars(ContentSet set, ContentStudioSetData d)
    {
        d.OrganizationId = set.OrganizationId;
        d.ClientId = set.ClientId;
        d.SetKind = (short)set.Kind;
        d.OriginGeneratorKind = set.Origin.GeneratorKind is GeneratorKind generatorKind ? (short)generatorKind : null;
        d.OriginSourceRunId = set.Origin.SourceRunId;
        d.OriginCampaignId = set.Origin.CampaignId;
        d.OriginPeriodKind = set.Origin.Period?.Kind;
        d.OriginYear = set.Origin.Period?.Year;
        d.OriginMonth = set.Origin.Period?.Month;
        d.UpdatedAtUtc = set.UpdatedAtUtc;
    }
}
