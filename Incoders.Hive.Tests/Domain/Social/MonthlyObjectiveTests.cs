using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.MonthlyObjectives;
using Incoders.Hive.Domain.Social.Clients;

namespace Incoders.Hive.Tests.Domain.Social;

public sealed class MonthlyObjectiveTests
{
    private static readonly DateTime Now = new(2026, 5, 20, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Later = new(2026, 5, 20, 13, 0, 0, DateTimeKind.Utc);
    private static readonly ClientId ValidClientId = ClientId.New();
    private static readonly Guid ValidOrgId = Guid.NewGuid();
    private const string ValidObjectives = "Increase brand awareness by 20%.";
    private static readonly IReadOnlyList<string> ValidKpis = new[] { "Reach", "Impressions" };

    // --- Create: happy path ---

    [Fact]
    public void Create_WithValidData_Succeeds()
    {
        var result = MonthlyObjective.Create(
            MonthlyObjectiveId.New(),
            ValidClientId.Value,
            ValidOrgId,
            2026,
            5,
            ValidObjectives,
            ValidKpis,
            Now);

        Assert.True(result.IsSuccess);
        var obj = result.Value;
        Assert.Equal(ValidClientId.Value, obj.ClientId);
        Assert.Equal(ValidOrgId, obj.OrganizationId);
        Assert.Equal(2026, obj.Year);
        Assert.Equal(5, obj.Month);
        Assert.Equal(ValidObjectives, obj.Objectives);
        Assert.Equal(2, obj.Kpis.Count);
        Assert.Equal(Now, obj.CreatedAtUtc);
        Assert.Equal(Now, obj.UpdatedAtUtc);
    }

    // --- Create: Month validation ---

    [Fact]
    public void Create_MonthZero_ReturnsError()
    {
        var result = MonthlyObjective.Create(
            MonthlyObjectiveId.New(),
            ValidClientId.Value,
            ValidOrgId,
            2026, 0, ValidObjectives, ValidKpis, Now);

        Assert.True(result.IsFailure);
        Assert.Equal("social.monthly_objectives.invalid_month", result.Error.Code);
    }

    [Fact]
    public void Create_Month13_ReturnsError()
    {
        var result = MonthlyObjective.Create(
            MonthlyObjectiveId.New(),
            ValidClientId.Value,
            ValidOrgId,
            2026, 13, ValidObjectives, ValidKpis, Now);

        Assert.True(result.IsFailure);
        Assert.Equal("social.monthly_objectives.invalid_month", result.Error.Code);
    }

    [Fact]
    public void Create_ValidMonths_Boundaries()
    {
        var result1 = MonthlyObjective.Create(
            MonthlyObjectiveId.New(), ValidClientId.Value, ValidOrgId,
            2026, 1, ValidObjectives, ValidKpis, Now);

        var result12 = MonthlyObjective.Create(
            MonthlyObjectiveId.New(), ValidClientId.Value, ValidOrgId,
            2026, 12, ValidObjectives, ValidKpis, Now);

        Assert.True(result1.IsSuccess);
        Assert.True(result12.IsSuccess);
    }

    // --- Create: Year validation ---

    [Fact]
    public void Create_YearOutOfRange_ReturnsError()
    {
        var result0 = MonthlyObjective.Create(
            MonthlyObjectiveId.New(), ValidClientId.Value, ValidOrgId,
            0, 5, ValidObjectives, ValidKpis, Now);

        var result10000 = MonthlyObjective.Create(
            MonthlyObjectiveId.New(), ValidClientId.Value, ValidOrgId,
            10000, 5, ValidObjectives, ValidKpis, Now);

        Assert.True(result0.IsFailure);
        Assert.Equal("social.monthly_objectives.invalid_year", result0.Error.Code);

        Assert.True(result10000.IsFailure);
        Assert.Equal("social.monthly_objectives.invalid_year", result10000.Error.Code);
    }

    // --- Create: Objectives length ---

    [Fact]
    public void Create_ObjectivesExceeding3000Chars_ReturnsError()
    {
        var longText = new string('x', 3001);

        var result = MonthlyObjective.Create(
            MonthlyObjectiveId.New(), ValidClientId.Value, ValidOrgId,
            2026, 5, longText, ValidKpis, Now);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    // --- Create: KPIs limit ---

    [Fact]
    public void Create_KPIsExceeding20Items_ReturnsError()
    {
        var tooMany = Enumerable.Range(1, 21).Select(i => $"KPI{i}").ToList();

        var result = MonthlyObjective.Create(
            MonthlyObjectiveId.New(), ValidClientId.Value, ValidOrgId,
            2026, 5, ValidObjectives, tooMany, Now);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    // --- Create: Objectives required ---

    [Fact]
    public void Create_EmptyBrief_ReturnsError()
    {
        var resultNull = MonthlyObjective.Create(
            MonthlyObjectiveId.New(), ValidClientId.Value, ValidOrgId,
            2026, 5, null!, ValidKpis, Now);

        var resultEmpty = MonthlyObjective.Create(
            MonthlyObjectiveId.New(), ValidClientId.Value, ValidOrgId,
            2026, 5, "  ", ValidKpis, Now);

        Assert.True(resultNull.IsFailure);
        Assert.Equal("social.monthly_objectives.brief_required", resultNull.Error.Code);

        Assert.True(resultEmpty.IsFailure);
        Assert.Equal("social.monthly_objectives.brief_required", resultEmpty.Error.Code);
    }

    // --- Restore: bypasses validation ---

    [Fact]
    public void Restore_BypassesValidation()
    {
        var id = MonthlyObjectiveId.New();

        // Month 0 would fail Create — Restore must not validate
        var obj = MonthlyObjective.Restore(
            id,
            ValidClientId.Value,
            ValidOrgId,
            0, 0,
            "Stored data",
            Array.Empty<string>(),
            Now, Now);

        Assert.Equal(id, obj.Id);
        Assert.Equal(0, obj.Month);
        Assert.Equal("Stored data", obj.Objectives);
    }

    // --- Update ---

    [Fact]
    public void Update_AppliesChanges()
    {
        var obj = CreateValid();
        var newKpis = new[] { "Conversions" };

        var result = obj.Update(
            "New objective text.",
            newKpis,
            Later);

        Assert.True(result.IsSuccess);
        Assert.Equal("New objective text.", obj.Objectives);
        Assert.Single(obj.Kpis);
        Assert.Equal("Conversions", obj.Kpis[0]);
        Assert.Equal(Later, obj.UpdatedAtUtc);
    }

    // --- Timestamps type check ---

    [Fact]
    public void Timestamps_AreDateTime_NotDateTimeOffset()
    {
        var obj = CreateValid();

        Assert.IsType<DateTime>(obj.CreatedAtUtc);
        Assert.IsType<DateTime>(obj.UpdatedAtUtc);
        Assert.Equal(DateTimeKind.Utc, obj.CreatedAtUtc.Kind);
        Assert.Equal(DateTimeKind.Utc, obj.UpdatedAtUtc.Kind);
    }

    // --- Helpers ---

    private static MonthlyObjective CreateValid() =>
        MonthlyObjective.Create(
            MonthlyObjectiveId.New(),
            ValidClientId.Value,
            ValidOrgId,
            2026, 5,
            ValidObjectives,
            ValidKpis,
            Now).Value;
}
