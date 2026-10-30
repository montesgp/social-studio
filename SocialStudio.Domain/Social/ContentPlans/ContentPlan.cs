using SocialStudio.Domain.Common;

namespace SocialStudio.Domain.Social.ContentPlans;

/// <summary>
/// Aggregate root representing the per-period content plan for a client.
/// Keyed by (ClientId, Period); succeeds ClientMonthlyObjective.
/// Sibling aggregate to Client — owns its own repository, keyed by (ClientId, Period) FK.
/// Uniqueness on (client_id, period_kind, year, month) is enforced at DB.
/// References Generated Content Packages via CalendarSlot.ProducedPackageId only —
/// never owns or embeds package internals.
/// </summary>
public sealed class ContentPlan : Entity<ContentPlanId>
{
    public const int ObjectivesMaxLength = 3000;
    public const int KpisMaxCount = 20;

    private List<CalendarSlot> _calendarSlots = new();

    private ContentPlan(
        ContentPlanId id,
        Guid clientId,
        Guid organizationId,
        Period period,
        ObjectiveNode objective,
        ContentStrategyNode contentStrategy,
        ContentCalendar calendar,
        DateTime createdAtUtc,
        DateTime updatedAtUtc)
        : base(id)
    {
        ClientId = clientId;
        OrganizationId = organizationId;
        Period = period;
        Objective = objective;
        ContentStrategy = contentStrategy;
        _calendarSlots = new List<CalendarSlot>(calendar.Slots);
        Calendar = new ContentCalendar(_calendarSlots, calendar.Provenance);
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
    }

    // Parameterless constructor for EF Core
    private ContentPlan() : base(default) { }

    public Guid ClientId { get; private set; }

    public Guid OrganizationId { get; private set; }

    public Period Period { get; private set; }

    public ObjectiveNode Objective { get; private set; } = default!;

    public ContentStrategyNode ContentStrategy { get; private set; } = default!;

    public ContentCalendar Calendar { get; private set; } = default!;

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>
    /// Factory for creating a new ContentPlan. Validates objective text and KPI list.
    /// </summary>
    public static Result<ContentPlan> Create(
        ContentPlanId id,
        Guid clientId,
        Guid organizationId,
        Period period,
        string objectives,
        IReadOnlyList<string>? kpis,
        DateTime nowUtc)
    {
        var objectivesResult = ValidateObjectives(objectives);
        if (objectivesResult.IsFailure)
        {
            return Result.Failure<ContentPlan>(objectivesResult.Error);
        }

        var kpiList = kpis ?? Array.Empty<string>();
        if (kpiList.Count > KpisMaxCount)
        {
            return Result.Failure<ContentPlan>(ContentPlanErrors.Validation(
                $"KPIs must not exceed {KpisMaxCount} items."));
        }

        var objectiveNode = new ObjectiveNode(
            objectivesResult.Value,
            kpiList,
            Provenance.Manual(nowUtc));

        var contentStrategyNode = new ContentStrategyNode(null, Provenance.Empty);
        var emptyCalendar = new ContentCalendar(Array.Empty<CalendarSlot>(), Provenance.Empty);

        return Result.Success(new ContentPlan(
            id,
            clientId,
            organizationId,
            period,
            objectiveNode,
            contentStrategyNode,
            emptyCalendar,
            nowUtc,
            nowUtc));
    }

    /// <summary>
    /// Re-hydration factory used by EF mapping. Bypasses validation — data is trusted from persistence.
    /// </summary>
    public static ContentPlan Restore(
        ContentPlanId id,
        Guid clientId,
        Guid organizationId,
        Period period,
        ObjectiveNode objective,
        ContentStrategyNode contentStrategy,
        ContentCalendar calendar,
        DateTime createdAtUtc,
        DateTime updatedAtUtc)
    {
        return new ContentPlan(
            id,
            clientId,
            organizationId,
            period,
            objective,
            contentStrategy,
            calendar,
            createdAtUtc,
            updatedAtUtc);
    }

    /// <summary>
    /// Replaces the Objective node content. Validates text and KPI count.
    /// </summary>
    public Result UpsertObjective(
        string objectives,
        IReadOnlyList<string>? kpis,
        DateTime nowUtc,
        DateTime updatedAtUtc)
    {
        var objectivesResult = ValidateObjectives(objectives);
        if (objectivesResult.IsFailure)
        {
            return Result.Failure(objectivesResult.Error);
        }

        var kpiList = kpis is null ? new List<string>() : new List<string>(kpis);
        if (kpiList.Count > KpisMaxCount)
        {
            return Result.Failure(ContentPlanErrors.Validation(
                $"KPIs must not exceed {KpisMaxCount} items."));
        }

        Objective = new ObjectiveNode(
            objectivesResult.Value,
            kpiList,
            Provenance.Manual(nowUtc));

        UpdatedAtUtc = updatedAtUtc;
        return Result.Success();
    }

    /// <summary>
    /// Replaces the Content Strategy node content.
    /// </summary>
    public Result UpsertContentStrategy(
        string? strategyText,
        Provenance provenance,
        DateTime updatedAtUtc)
    {
        ContentStrategy = new ContentStrategyNode(strategyText?.Trim(), provenance);
        UpdatedAtUtc = updatedAtUtc;
        return Result.Success();
    }

    /// <summary>
    /// Replaces all calendar slots for this plan.
    /// Validates that each slot's day is within the range for this plan's period.
    /// Slot order is preserved as provided (explicit SortOrder = input index).
    /// </summary>
    public Result ReplaceCalendarSlots(
        IReadOnlyList<CalendarSlotInput> slots,
        DateTime updatedAtUtc)
    {
        var daysInPeriod = Period.DaysInMonth();

        for (var i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            if (slot.Day < 1 || slot.Day > daysInPeriod)
            {
                return Result.Failure(ContentPlanErrors.InvalidDay);
            }
        }

        _calendarSlots = slots
            .Select((s, i) => new CalendarSlot(
                CalendarSlotId.New(),
                s.Day,
                s.ContentFormat,
                i,
                null,
                s.Provenance))
            .ToList();

        Calendar = new ContentCalendar(_calendarSlots, Calendar.Provenance);
        UpdatedAtUtc = updatedAtUtc;
        return Result.Success();
    }

    /// <summary>
    /// Links a Generated Content Package to a specific calendar slot.
    /// The reference is a soft Guid — no ownership or EF FK.
    /// </summary>
    public Result LinkSlotPackage(CalendarSlotId slotId, Guid producedPackageId)
    {
        var slot = _calendarSlots.FirstOrDefault(s => s.Id == slotId);
        if (slot is null)
        {
            return Result.Failure(ContentPlanErrors.SlotNotFound);
        }

        slot.ProducedPackageId = producedPackageId;
        return Result.Success();
    }

    private static Result<string> ValidateObjectives(string objectives)
    {
        if (string.IsNullOrWhiteSpace(objectives))
        {
            return Result.Failure<string>(ContentPlanErrors.ObjectiveRequired);
        }

        var trimmed = objectives.Trim();

        if (trimmed.Length > ObjectivesMaxLength)
        {
            return Result.Failure<string>(ContentPlanErrors.Validation(
                $"Objectives must not exceed {ObjectivesMaxLength} characters."));
        }

        return Result.Success(trimmed);
    }
}
