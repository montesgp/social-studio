using SocialStudio.Application.Abstractions.Social.ContentStudio;
using SocialStudio.Domain.Social.ContentStudio;
using SocialStudio.Infrastructure.Identity;
using SocialStudio.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace SocialStudio.Infrastructure.Persistence.Postgres;

public sealed class EfContentItemRepository : IContentItemRepository
{
    private readonly AppIdentityDbContext _dbContext;

    public EfContentItemRepository(AppIdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ContentItem?> GetByIdAsync(Guid organizationId, ContentItemId id, CancellationToken ct)
    {
        var row = await _dbContext.ContentStudioItems
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id.Value && x.OrganizationId == organizationId, ct);

        return row is null ? null : ToDomain(row);
    }

    public async Task<IReadOnlyList<ContentItem>> ListByRunAsync(Guid organizationId, Guid sourceRunId, CancellationToken ct)
    {
        var rows = await _dbContext.ContentStudioItems
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.SourceRunId == sourceRunId)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(ct);

        return rows.Select(ToDomain).ToList();
    }

    public async Task<IReadOnlyList<ContentItem>> ListBySetAsync(Guid organizationId, ContentSetId setId, CancellationToken ct)
    {
        var members = await _dbContext.ContentStudioSetItems
            .AsNoTracking()
            .Where(x => x.SetId == setId.Value)
            .OrderBy(x => x.SortOrder)
            .Select(x => x.ItemId)
            .ToListAsync(ct);

        if (members.Count == 0)
        {
            return [];
        }

        var ids = members.ToHashSet();
        var rows = await _dbContext.ContentStudioItems
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && ids.Contains(x.Id))
            .ToListAsync(ct);

        var order = members
            .Select((itemId, index) => (itemId, index))
            .ToDictionary(t => t.itemId, t => t.index);

        return rows
            .OrderBy(r => order.TryGetValue(r.Id, out var index) ? index : int.MaxValue)
            .Select(ToDomain)
            .ToList();
    }

    public async Task<IReadOnlyList<ContentItem>> ListByIdsAsync(
        Guid organizationId, IReadOnlyCollection<Guid> itemIds, CancellationToken ct)
    {
        if (itemIds.Count == 0)
        {
            return [];
        }

        var ids = itemIds.ToHashSet();
        var rows = await _dbContext.ContentStudioItems
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && ids.Contains(x.Id))
            .ToListAsync(ct);

        return rows.Select(ToDomain).ToList();
    }

    public async Task<IReadOnlyList<ContentItem>> ListByClientAsync(
        Guid organizationId, Guid clientId, ContentBankFilter filter, CancellationToken ct)
    {
        var query = _dbContext.ContentStudioItems
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.ClientId == clientId);

        if (filter.FromUtc is { } fromUtc)
        {
            query = query.Where(x => x.CreatedAtUtc >= fromUtc);
        }

        if (filter.ToUtc is { } toUtc)
        {
            query = query.Where(x => x.CreatedAtUtc <= toUtc);
        }

        if (filter.State is { } state)
        {
            var stateValue = (short)state;
            query = query.Where(x => x.ReviewState == stateValue);
        }

        var rows = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(ct);

        return rows.Select(ToDomain).ToList();
    }

    public async Task AddRangeAsync(IReadOnlyList<ContentItem> items, CancellationToken ct)
    {
        foreach (var item in items)
        {
            _dbContext.ContentStudioItems.Add(ToData(item));
        }

        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(ContentItem item, CancellationToken ct)
    {
        var existing = await _dbContext.ContentStudioItems
            .FirstOrDefaultAsync(x => x.Id == item.Id.Value && x.OrganizationId == item.OrganizationId, ct);

        if (existing is null)
        {
            return;
        }

        MapToExisting(item, existing);
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid organizationId, ContentItemId id, CancellationToken ct)
    {
        var existing = await _dbContext.ContentStudioItems
            .FirstOrDefaultAsync(x => x.Id == id.Value && x.OrganizationId == organizationId, ct);

        if (existing is null)
        {
            return;
        }

        _dbContext.ContentStudioItems.Remove(existing);
        await _dbContext.SaveChangesAsync(ct);
    }

    // ─── Mapping helpers ──────────────────────────────────────────────────────

    private static ContentItem ToDomain(ContentStudioItemData d)
    {
        var origin = new ContentOrigin(
            (GeneratorKind)d.GeneratorKind, d.SourceRunId, d.SourcePackageId, d.FormatResultId, d.FormatKey);

        var kind = new ContentKind(d.FormatKey, (ContentMedium)d.ContentMedium);

        var assets = d.DraftAssets
            .Select(a => new ContentAssetRef(a.Role, a.StorageUri, a.Mime))
            .ToList();

        // Defensive: Caption.Create re-validates on read. A row written before this change (or via a
        // path that bypassed validation) never blocks a read — an invariant-violating stored value
        // simply surfaces as no caption rather than throwing.
        var caption = Caption.Create(d.DraftCaption) is { IsSuccess: true } captionResult
            ? captionResult.Value
            : null;

        var draft = new ContentDraft(
            d.DraftText, caption, d.DraftVisualSpecMarkup, assets, new TargetChannel(d.TargetNetwork, d.TargetPlacement));

        var editTrace = new EditTrace((EditSource)d.EditLastSource, d.EditLastAtUtc, d.EditPromptVersion);

        return ContentItem.Restore(
            new ContentItemId(d.Id),
            d.OrganizationId,
            d.ClientId,
            origin,
            kind,
            draft,
            (ReviewState)d.ReviewState,
            editTrace,
            d.VariantGroupId,
            d.IsPrimaryVariant,
            ToPlanContext(d),
            d.RejectionReason,
            d.ReviewerUserId,
            d.CreatedAtUtc,
            d.UpdatedAtUtc);
    }

    private static PlanContext? ToPlanContext(ContentStudioItemData d)
    {
        if (d.PlanContentPlanId is Guid planId
            && d.PlanCalendarSlotId is Guid slotId
            && d.PlanPeriodKind is short kind
            && d.PlanYear is int year
            && d.PlanMonth is int month)
        {
            return new PlanContext(planId, new PeriodRef(kind, year, month), slotId);
        }

        return null;
    }

    private static ContentStudioItemData ToData(ContentItem item)
    {
        var d = new ContentStudioItemData { Id = item.Id.Value };
        MapToExisting(item, d);
        d.CreatedAtUtc = item.CreatedAtUtc;
        return d;
    }

    private static void MapToExisting(ContentItem item, ContentStudioItemData d)
    {
        d.OrganizationId = item.OrganizationId;
        d.ClientId = item.ClientId;
        d.GeneratorKind = (short)item.Origin.GeneratorKind;
        d.SourceRunId = item.Origin.SourceRunId;
        d.SourcePackageId = item.Origin.SourcePackageId;
        d.FormatResultId = item.Origin.FormatResultId;
        d.FormatKey = item.Origin.FormatKey;
        d.ContentMedium = (short)item.Kind.Medium;
        d.DraftText = item.Draft.Text;
        d.DraftCaption = item.Draft.Caption?.Value;
        d.DraftVisualSpecMarkup = item.Draft.VisualSpecMarkup;
        d.DraftAssets = item.Draft.Assets
            .Select(a => new ContentStudioAssetData { Role = a.Role, StorageUri = a.StorageUri, Mime = a.Mime })
            .ToList();
        d.TargetNetwork = item.Draft.TargetChannel.Network;
        d.TargetPlacement = item.Draft.TargetChannel.Placement;
        d.ReviewState = (short)item.State;
        d.RejectionReason = item.RejectionReason;
        d.ReviewerUserId = item.ReviewerUserId;
        d.VariantGroupId = item.VariantGroupId;
        d.IsPrimaryVariant = item.IsPrimaryVariant;
        d.EditLastSource = (short)item.EditTrace.LastEditedSource;
        d.EditLastAtUtc = item.EditTrace.LastEditedAtUtc;
        d.EditPromptVersion = item.EditTrace.PromptVersion;
        d.PlanContentPlanId = item.PlanContext?.ContentPlanId;
        d.PlanPeriodKind = item.PlanContext?.Period.Kind;
        d.PlanYear = item.PlanContext?.Period.Year;
        d.PlanMonth = item.PlanContext?.Period.Month;
        d.PlanCalendarSlotId = item.PlanContext?.CalendarSlotId;
        d.UpdatedAtUtc = item.UpdatedAtUtc;
    }
}
