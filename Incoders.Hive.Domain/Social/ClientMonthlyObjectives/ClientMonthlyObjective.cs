using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Domain.Social.ClientMonthlyObjectives;

/// <summary>
/// Aggregate root representing the monthly content objectives for a client.
/// Sibling aggregate to Client — owns its own repository, keyed by (ClientId, Year, Month).
/// Uniqueness on (ClientId, Year, Month) is enforced at DB and pre-checked in the handler.
/// </summary>
public sealed class ClientMonthlyObjective : Entity<ClientMonthlyObjectiveId>
{
    public const int YearMin = 1;
    public const int YearMax = 9999;
    public const int MonthMin = 1;
    public const int MonthMax = 12;
    public const int ObjectivesMaxLength = 3000;
    public const int KpisMaxCount = 20;

    private ClientMonthlyObjective(
        ClientMonthlyObjectiveId id,
        Guid clientId,
        Guid organizationId,
        int year,
        int month,
        string objectives,
        IReadOnlyList<string> kpis,
        DateTime createdAtUtc,
        DateTime updatedAtUtc)
        : base(id)
    {
        ClientId = clientId;
        OrganizationId = organizationId;
        Year = year;
        Month = month;
        Objectives = objectives;
        Kpis = kpis;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
    }

    // Parameterless constructor for EF Core
    private ClientMonthlyObjective() : base(default) { }

    public Guid ClientId { get; private set; }

    public Guid OrganizationId { get; private set; }

    public int Year { get; private set; }

    public int Month { get; private set; }

    public string Objectives { get; private set; } = default!;

    public IReadOnlyList<string> Kpis { get; private set; } = Array.Empty<string>();

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>
    /// Factory for creating a new ClientMonthlyObjective. Validates all fields.
    /// </summary>
    public static Result<ClientMonthlyObjective> Create(
        ClientMonthlyObjectiveId id,
        Guid clientId,
        Guid organizationId,
        int year,
        int month,
        string objectives,
        IReadOnlyList<string>? kpis,
        DateTime nowUtc)
    {
        if (year < YearMin || year > YearMax)
        {
            return Result.Failure<ClientMonthlyObjective>(ClientMonthlyObjectiveErrors.InvalidYear);
        }

        if (month < MonthMin || month > MonthMax)
        {
            return Result.Failure<ClientMonthlyObjective>(ClientMonthlyObjectiveErrors.InvalidMonth);
        }

        var objectivesResult = ValidateObjectives(objectives);
        if (objectivesResult.IsFailure)
        {
            return Result.Failure<ClientMonthlyObjective>(objectivesResult.Error);
        }

        var kpiList = kpis ?? Array.Empty<string>();

        if (kpiList.Count > KpisMaxCount)
        {
            return Result.Failure<ClientMonthlyObjective>(ClientMonthlyObjectiveErrors.Validation(
                $"KPIs must not exceed {KpisMaxCount} items."));
        }

        return Result.Success(new ClientMonthlyObjective(
            id,
            clientId,
            organizationId,
            year,
            month,
            objectivesResult.Value,
            kpiList,
            nowUtc,
            nowUtc));
    }

    /// <summary>
    /// Re-hydration factory used by EF mapping. Bypasses validation — data is trusted from persistence.
    /// </summary>
    public static ClientMonthlyObjective Restore(
        ClientMonthlyObjectiveId id,
        Guid clientId,
        Guid organizationId,
        int year,
        int month,
        string objectives,
        IReadOnlyList<string> kpis,
        DateTime createdAtUtc,
        DateTime updatedAtUtc)
    {
        return new ClientMonthlyObjective(
            id,
            clientId,
            organizationId,
            year,
            month,
            objectives,
            kpis,
            createdAtUtc,
            updatedAtUtc);
    }

    /// <summary>
    /// Updates the objectives text and KPIs. Year, Month, ClientId, OrganizationId are immutable.
    /// Resolution 6: KPI list ref replaced with new List instance for EF ValueComparer.
    /// </summary>
    public Result<ClientMonthlyObjective> Update(
        string objectives,
        IReadOnlyList<string>? kpis,
        DateTime nowUtc)
    {
        var objectivesResult = ValidateObjectives(objectives);
        if (objectivesResult.IsFailure)
        {
            return Result.Failure<ClientMonthlyObjective>(objectivesResult.Error);
        }

        var kpiList = kpis is null ? new List<string>() : new List<string>(kpis);

        if (kpiList.Count > KpisMaxCount)
        {
            return Result.Failure<ClientMonthlyObjective>(ClientMonthlyObjectiveErrors.Validation(
                $"KPIs must not exceed {KpisMaxCount} items."));
        }

        Objectives = objectivesResult.Value;
        Kpis = kpiList;
        UpdatedAtUtc = nowUtc;

        return Result.Success(this);
    }

    private static Result<string> ValidateObjectives(string objectives)
    {
        if (string.IsNullOrWhiteSpace(objectives))
        {
            return Result.Failure<string>(ClientMonthlyObjectiveErrors.BriefRequired);
        }

        var trimmed = objectives.Trim();

        if (trimmed.Length > ObjectivesMaxLength)
        {
            return Result.Failure<string>(ClientMonthlyObjectiveErrors.Validation(
                $"Objectives must not exceed {ObjectivesMaxLength} characters."));
        }

        return Result.Success(trimmed);
    }
}
