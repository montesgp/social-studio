using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Domain.Social.ContentPlans;
using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace Incoders.Hive.Infrastructure.Persistence.Postgres;

public sealed class EfContentPlanRepository : IContentPlanRepository
{
    private readonly AppIdentityDbContext _dbContext;

    public EfContentPlanRepository(AppIdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ContentPlan?> GetByClientAndPeriodAsync(
        Guid organizationId,
        Guid clientId,
        Period period,
        CancellationToken ct)
    {
        var kind = (short)period.Kind;

        var row = await _dbContext.ContentPlans
            .AsNoTracking()
            .Include(x => x.Slots)
            .FirstOrDefaultAsync(
                x => x.ClientId == clientId
                     && x.OrganizationId == organizationId
                     && x.PeriodKind == kind
                     && x.Year == period.Year
                     && x.Month == period.Month,
                ct);

        return row is null ? null : ToDomain(row);
    }

    public async Task<IReadOnlyList<ContentPlan>> ListByClientAsync(
        Guid organizationId,
        Guid clientId,
        CancellationToken ct)
    {
        var rows = await _dbContext.ContentPlans
            .AsNoTracking()
            .Include(x => x.Slots)
            .Where(x => x.ClientId == clientId && x.OrganizationId == organizationId)
            .OrderByDescending(x => x.Year)
            .ThenByDescending(x => x.Month)
            .ToListAsync(ct);

        return rows.Select(ToDomain).ToList();
    }

    public async Task UpsertAsync(ContentPlan plan, CancellationToken ct)
    {
        var kind = (short)plan.Period.Kind;

        var existing = await _dbContext.ContentPlans
            .Include(x => x.Slots)
            .FirstOrDefaultAsync(
                x => x.ClientId == plan.ClientId
                     && x.OrganizationId == plan.OrganizationId
                     && x.PeriodKind == kind
                     && x.Year == plan.Period.Year
                     && x.Month == plan.Period.Month,
                ct);

        if (existing is null)
        {
            _dbContext.ContentPlans.Add(ToData(plan));
        }
        else
        {
            // Remove existing slots (cascade will handle DB delete; we replace in-memory)
            existing.Slots.Clear();

            MapPlanToExisting(plan, existing);
        }

        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task LinkSlotPackageAsync(
        Guid planId,
        CalendarSlotId slotId,
        Guid packageId,
        CancellationToken ct)
    {
        var slot = await _dbContext.ContentPlanSlots
            .FirstOrDefaultAsync(
                x => x.ContentPlanId == planId && x.Id == slotId.Value,
                ct);

        if (slot is null)
        {
            return;
        }

        slot.ProducedPackageId = packageId;
        await _dbContext.SaveChangesAsync(ct);
    }

    // ─── Mapping helpers ──────────────────────────────────────────────────────

    private static ContentPlan ToDomain(ContentPlanData d)
    {
        var period = Period.RestoreMonthly(d.Year, d.Month);

        var objective = new ObjectiveNode(
            d.ObjectiveText,
            d.ObjectiveKpis,
            RestoreProvenance(d.ObjectiveProvenanceSource, d.ObjectiveProvenanceStatus,
                d.ObjectiveProvenanceGeneratedAtUtc, d.ObjectiveProvenancePromptVersion));

        var contentStrategy = new ContentStrategyNode(
            d.ContentStrategyText,
            RestoreProvenance(d.ContentStrategyProvenanceSource, d.ContentStrategyProvenanceStatus,
                d.ContentStrategyProvenanceGeneratedAtUtc, d.ContentStrategyProvenancePromptVersion));

        var calendarProvenance = RestoreProvenance(
            d.CalendarProvenanceSource, d.CalendarProvenanceStatus,
            d.CalendarProvenanceGeneratedAtUtc, d.CalendarProvenancePromptVersion);

        var slots = d.Slots
            .OrderBy(s => s.SortOrder)
            .Select(s => CalendarSlot.Restore(
                new CalendarSlotId(s.Id),
                s.Day,
                s.ContentFormat,
                s.SortOrder,
                s.ProducedPackageId,
                RestoreProvenance(s.SlotProvenanceSource, s.SlotProvenanceStatus,
                    s.SlotProvenanceGeneratedAtUtc, s.SlotProvenancePromptVersion)))
            .ToList();

        var calendar = new ContentCalendar(slots, calendarProvenance);

        return ContentPlan.Restore(
            new ContentPlanId(d.Id),
            d.ClientId,
            d.OrganizationId,
            period,
            objective,
            contentStrategy,
            calendar,
            d.CreatedAtUtc,
            d.UpdatedAtUtc);
    }

    private static ContentPlanData ToData(ContentPlan plan) => new()
    {
        Id = plan.Id.Value,
        ClientId = plan.ClientId,
        OrganizationId = plan.OrganizationId,
        PeriodKind = (short)plan.Period.Kind,
        Year = plan.Period.Year,
        Month = plan.Period.Month,
        ObjectiveText = plan.Objective.Objectives,
        ObjectiveKpis = new List<string>(plan.Objective.Kpis),
        ObjectiveProvenanceSource = (short)plan.Objective.Provenance.Source,
        ObjectiveProvenanceStatus = (short)plan.Objective.Provenance.Status,
        ObjectiveProvenanceGeneratedAtUtc = plan.Objective.Provenance.GeneratedAtUtc,
        ObjectiveProvenancePromptVersion = plan.Objective.Provenance.PromptVersion,
        ContentStrategyText = plan.ContentStrategy.StrategyText,
        ContentStrategyProvenanceSource = (short)plan.ContentStrategy.Provenance.Source,
        ContentStrategyProvenanceStatus = (short)plan.ContentStrategy.Provenance.Status,
        ContentStrategyProvenanceGeneratedAtUtc = plan.ContentStrategy.Provenance.GeneratedAtUtc,
        ContentStrategyProvenancePromptVersion = plan.ContentStrategy.Provenance.PromptVersion,
        CalendarProvenanceSource = (short)plan.Calendar.Provenance.Source,
        CalendarProvenanceStatus = (short)plan.Calendar.Provenance.Status,
        CalendarProvenanceGeneratedAtUtc = plan.Calendar.Provenance.GeneratedAtUtc,
        CalendarProvenancePromptVersion = plan.Calendar.Provenance.PromptVersion,
        CreatedAtUtc = plan.CreatedAtUtc,
        UpdatedAtUtc = plan.UpdatedAtUtc,
        Slots = plan.Calendar.Slots.Select(s => ToSlotData(s, plan.Id.Value)).ToList(),
    };

    private static void MapPlanToExisting(ContentPlan plan, ContentPlanData target)
    {
        target.ObjectiveText = plan.Objective.Objectives;
        target.ObjectiveKpis = new List<string>(plan.Objective.Kpis);
        target.ObjectiveProvenanceSource = (short)plan.Objective.Provenance.Source;
        target.ObjectiveProvenanceStatus = (short)plan.Objective.Provenance.Status;
        target.ObjectiveProvenanceGeneratedAtUtc = plan.Objective.Provenance.GeneratedAtUtc;
        target.ObjectiveProvenancePromptVersion = plan.Objective.Provenance.PromptVersion;
        target.ContentStrategyText = plan.ContentStrategy.StrategyText;
        target.ContentStrategyProvenanceSource = (short)plan.ContentStrategy.Provenance.Source;
        target.ContentStrategyProvenanceStatus = (short)plan.ContentStrategy.Provenance.Status;
        target.ContentStrategyProvenanceGeneratedAtUtc = plan.ContentStrategy.Provenance.GeneratedAtUtc;
        target.ContentStrategyProvenancePromptVersion = plan.ContentStrategy.Provenance.PromptVersion;
        target.CalendarProvenanceSource = (short)plan.Calendar.Provenance.Source;
        target.CalendarProvenanceStatus = (short)plan.Calendar.Provenance.Status;
        target.CalendarProvenanceGeneratedAtUtc = plan.Calendar.Provenance.GeneratedAtUtc;
        target.CalendarProvenancePromptVersion = plan.Calendar.Provenance.PromptVersion;
        target.UpdatedAtUtc = plan.UpdatedAtUtc;
        target.Slots = plan.Calendar.Slots.Select(s => ToSlotData(s, target.Id)).ToList();
    }

    private static ContentPlanSlotData ToSlotData(CalendarSlot slot, Guid planId) => new()
    {
        Id = slot.Id.Value,
        ContentPlanId = planId,
        Day = slot.Day,
        ContentFormat = slot.ContentFormat,
        SortOrder = slot.SortOrder,
        ProducedPackageId = slot.ProducedPackageId,
        SlotProvenanceSource = (short)slot.Provenance.Source,
        SlotProvenanceStatus = (short)slot.Provenance.Status,
        SlotProvenanceGeneratedAtUtc = slot.Provenance.GeneratedAtUtc,
        SlotProvenancePromptVersion = slot.Provenance.PromptVersion,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow,
    };

    private static Provenance RestoreProvenance(
        short source, short status, DateTime? generatedAtUtc, string? promptVersion) =>
        new((ProvenanceSource)source, (ProvenanceStatus)status, generatedAtUtc, promptVersion);
}
