using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ContentPlans;

namespace Incoders.Hive.Tests.Domain.Social;

public sealed class ContentPlanTests
{
    private static readonly DateTime Now = new(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Later = new(2026, 6, 1, 11, 0, 0, DateTimeKind.Utc);
    private static readonly Guid ValidClientId = Guid.NewGuid();
    private static readonly Guid ValidOrgId = Guid.NewGuid();
    private static readonly Period ValidPeriod = Period.Monthly(2026, 6).Value;
    private const string ValidObjectiveText = "Increase brand awareness by 20% this month.";

    // --- CP-03: Create Content Plan for new period ---

    [Fact]
    public void Create_WithValidObjective_Succeeds()
    {
        var result = ContentPlan.Create(
            ContentPlanId.New(),
            ValidClientId,
            ValidOrgId,
            ValidPeriod,
            ValidObjectiveText,
            Array.Empty<string>(),
            Now);

        Assert.True(result.IsSuccess);
        var plan = result.Value;
        Assert.Equal(ValidClientId, plan.ClientId);
        Assert.Equal(ValidOrgId, plan.OrganizationId);
        Assert.Equal(ValidPeriod, plan.Period);
        Assert.Equal(Now, plan.CreatedAtUtc);
        Assert.Equal(Now, plan.UpdatedAtUtc);
    }

    // --- CP-03: Objective text preserved ---

    [Fact]
    public void Create_ObjectiveTextPreserved()
    {
        var result = ContentPlan.Create(
            ContentPlanId.New(), ValidClientId, ValidOrgId, ValidPeriod,
            ValidObjectiveText, Array.Empty<string>(), Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(ValidObjectiveText, result.Value.Objective.Objectives);
    }

    // --- CP-03: Objective node has Manual provenance ---

    [Fact]
    public void Create_ObjectiveNode_HasManualProvenance()
    {
        var result = ContentPlan.Create(
            ContentPlanId.New(), ValidClientId, ValidOrgId, ValidPeriod,
            ValidObjectiveText, Array.Empty<string>(), Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(ProvenanceSource.Manual, result.Value.Objective.Provenance.Source);
        Assert.Equal(ProvenanceStatus.Draft, result.Value.Objective.Provenance.Status);
    }

    // --- CP-03: Empty Objective rejected ---

    [Fact]
    public void Create_EmptyObjective_ReturnsError()
    {
        var result = ContentPlan.Create(
            ContentPlanId.New(), ValidClientId, ValidOrgId, ValidPeriod,
            "   ", Array.Empty<string>(), Now);

        Assert.True(result.IsFailure);
        Assert.Equal(ContentPlanErrors.ObjectiveRequired.Code, result.Error.Code);
    }

    [Fact]
    public void Create_NullObjective_ReturnsError()
    {
        var result = ContentPlan.Create(
            ContentPlanId.New(), ValidClientId, ValidOrgId, ValidPeriod,
            null!, Array.Empty<string>(), Now);

        Assert.True(result.IsFailure);
        Assert.Equal(ContentPlanErrors.ObjectiveRequired.Code, result.Error.Code);
    }

    // --- CP-03: Objective max length ---

    [Fact]
    public void Create_ObjectiveExceeding3000Chars_ReturnsError()
    {
        var longObjective = new string('x', 3001);

        var result = ContentPlan.Create(
            ContentPlanId.New(), ValidClientId, ValidOrgId, ValidPeriod,
            longObjective, Array.Empty<string>(), Now);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    // --- CP-03: KPIs max count ---

    [Fact]
    public void Create_KPIsExceeding20_ReturnsError()
    {
        var tooManyKpis = Enumerable.Range(1, 21).Select(i => $"KPI{i}").ToList();

        var result = ContentPlan.Create(
            ContentPlanId.New(), ValidClientId, ValidOrgId, ValidPeriod,
            ValidObjectiveText, tooManyKpis, Now);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    // --- CP-03: Restore bypasses validation ---

    [Fact]
    public void Restore_BypassesValidation()
    {
        var id = ContentPlanId.New();
        var emptyPeriod = Period.RestoreMonthly(0, 0); // invalid period that bypasses validation

        var plan = ContentPlan.Restore(
            id,
            ValidClientId,
            ValidOrgId,
            emptyPeriod,
            new ObjectiveNode(string.Empty, Array.Empty<string>(), Provenance.Empty),
            new ContentStrategyNode(null, Provenance.Empty),
            new ContentCalendar(Array.Empty<CalendarSlot>(), Provenance.Empty),
            Now,
            Now);

        Assert.Equal(id, plan.Id);
    }

    // --- CP-03: UpsertObjective ---

    [Fact]
    public void UpsertObjective_UpdatesObjectiveAndTimestamp()
    {
        var plan = CreateValid();
        var newKpis = new[] { "Reach", "Impressions" };

        var result = plan.UpsertObjective("Updated objective text.", newKpis, Now, Later);

        Assert.True(result.IsSuccess);
        Assert.Equal("Updated objective text.", plan.Objective.Objectives);
        Assert.Equal(2, plan.Objective.Kpis.Count);
        Assert.Equal(Later, plan.UpdatedAtUtc);
    }

    // --- CP-03: UpsertObjective rejects empty ---

    [Fact]
    public void UpsertObjective_EmptyText_ReturnsError()
    {
        var plan = CreateValid();

        var result = plan.UpsertObjective("  ", Array.Empty<string>(), Now, Later);

        Assert.True(result.IsFailure);
        Assert.Equal(ContentPlanErrors.ObjectiveRequired.Code, result.Error.Code);
    }

    // --- CP-04: Calendar slots: valid days accepted ---

    [Fact]
    public void ReplaceCalendarSlots_ValidDays_Succeeds()
    {
        var plan = CreateValid();
        var slots = new[]
        {
            new CalendarSlotInput(1, "Reel", Provenance.Empty),
            new CalendarSlotInput(8, "Carousel", Provenance.Empty),
            new CalendarSlotInput(15, "Story", Provenance.Empty),
        };

        var result = plan.ReplaceCalendarSlots(slots, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, plan.Calendar.Slots.Count);
        Assert.Equal(1, plan.Calendar.Slots[0].Day);
        Assert.Equal(8, plan.Calendar.Slots[1].Day);
        Assert.Equal(15, plan.Calendar.Slots[2].Day);
    }

    // --- CP-04: Day out of range for period rejected ---

    [Fact]
    public void ReplaceCalendarSlots_DayOutOfRange_ReturnsError()
    {
        // February 2026 has 28 days; day 30 is invalid
        var febPeriod = Period.Monthly(2026, 2).Value;
        var plan = CreatePlanForPeriod(febPeriod);
        var slots = new[]
        {
            new CalendarSlotInput(30, "Reel", Provenance.Empty),
        };

        var result = plan.ReplaceCalendarSlots(slots, Now);

        Assert.True(result.IsFailure);
        Assert.Equal(ContentPlanErrors.InvalidDay.Code, result.Error.Code);
    }

    // --- CP-04: Slot order preserved ---

    [Fact]
    public void ReplaceCalendarSlots_OrderPreserved()
    {
        var plan = CreateValid();
        var slots = new[]
        {
            new CalendarSlotInput(5, "Reel", Provenance.Empty),
            new CalendarSlotInput(12, "Carousel", Provenance.Empty),
            new CalendarSlotInput(20, "Story", Provenance.Empty),
        };

        plan.ReplaceCalendarSlots(slots, Now);

        Assert.Equal(5, plan.Calendar.Slots[0].Day);
        Assert.Equal(12, plan.Calendar.Slots[1].Day);
        Assert.Equal(20, plan.Calendar.Slots[2].Day);
    }

    // --- CP-03: UpsertContentStrategy ---

    [Fact]
    public void UpsertContentStrategy_SetsTextAndProvenance()
    {
        var plan = CreateValid();

        var result = plan.UpsertContentStrategy("Focus on educational content.", Provenance.Manual(Now), Later);

        Assert.True(result.IsSuccess);
        Assert.Equal("Focus on educational content.", plan.ContentStrategy.StrategyText);
        Assert.Equal(Later, plan.UpdatedAtUtc);
    }

    // --- CP-04: LinkSlotPackage ---

    [Fact]
    public void LinkSlotPackage_ExistingSlot_SetsProducedPackageId()
    {
        var plan = CreateValid();
        var slots = new[] { new CalendarSlotInput(1, "Reel", Provenance.Empty) };
        plan.ReplaceCalendarSlots(slots, Now);
        var slotId = plan.Calendar.Slots[0].Id;
        var packageId = Guid.NewGuid();

        var result = plan.LinkSlotPackage(slotId, packageId);

        Assert.True(result.IsSuccess);
        Assert.Equal(packageId, plan.Calendar.Slots[0].ProducedPackageId);
    }

    [Fact]
    public void LinkSlotPackage_NonExistentSlot_ReturnsError()
    {
        var plan = CreateValid();
        var fakeSlotId = CalendarSlotId.New();
        var packageId = Guid.NewGuid();

        var result = plan.LinkSlotPackage(fakeSlotId, packageId);

        Assert.True(result.IsFailure);
        Assert.Equal(ContentPlanErrors.SlotNotFound.Code, result.Error.Code);
    }

    // --- CP-03: Timestamps ---

    [Fact]
    public void Timestamps_AreUtcDateTime()
    {
        var plan = CreateValid();

        Assert.Equal(DateTimeKind.Utc, plan.CreatedAtUtc.Kind);
        Assert.Equal(DateTimeKind.Utc, plan.UpdatedAtUtc.Kind);
    }

    // --- Helpers ---

    private static ContentPlan CreateValid() =>
        ContentPlan.Create(
            ContentPlanId.New(),
            ValidClientId,
            ValidOrgId,
            ValidPeriod,
            ValidObjectiveText,
            Array.Empty<string>(),
            Now).Value;

    private static ContentPlan CreatePlanForPeriod(Period period) =>
        ContentPlan.Create(
            ContentPlanId.New(),
            ValidClientId,
            ValidOrgId,
            period,
            ValidObjectiveText,
            Array.Empty<string>(),
            Now).Value;
}
