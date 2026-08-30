using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Domain.Social.ContentPlans;

/// <summary>
/// Cadence-open Value Object representing a content planning period.
/// Only the Monthly variant is implemented. Year/Month validation migrated
/// from ClientMonthlyObjective; corresponding errors now live in ContentPlanErrors.
/// </summary>
public readonly record struct Period
{
    public const int YearMin = 1;
    public const int YearMax = 9999;
    public const int MonthMin = 1;
    public const int MonthMax = 12;

    private Period(PeriodKind kind, int year, int month)
    {
        Kind = kind;
        Year = year;
        Month = month;
    }

    public PeriodKind Kind { get; }

    /// <summary>Year component (1–9999 for the monthly variant).</summary>
    public int Year { get; }

    /// <summary>Month component (1–12 for the monthly variant).</summary>
    public int Month { get; }

    /// <summary>
    /// Validated factory for a monthly period. Returns a failure result when
    /// year or month are outside their valid ranges.
    /// </summary>
    public static Result<Period> Monthly(int year, int month)
    {
        if (year < YearMin || year > YearMax)
        {
            return Result.Failure<Period>(ContentPlanErrors.InvalidYear);
        }

        if (month < MonthMin || month > MonthMax)
        {
            return Result.Failure<Period>(ContentPlanErrors.InvalidMonth);
        }

        return Result.Success(new Period(PeriodKind.Monthly, year, month));
    }

    /// <summary>
    /// Trusted re-hydration factory used by EF mapping. Bypasses validation —
    /// data is trusted from persistence.
    /// </summary>
    public static Period RestoreMonthly(int year, int month) =>
        new(PeriodKind.Monthly, year, month);

    /// <summary>
    /// Returns the number of days in this period's month, accounting for leap years.
    /// Valid only for the Monthly variant.
    /// </summary>
    public int DaysInMonth() => DateTime.DaysInMonth(Year, Month);

    /// <summary>
    /// Stable, deterministic string representation: "Monthly:2026-05".
    /// </summary>
    public override string ToString() => Kind switch
    {
        PeriodKind.Monthly => $"Monthly:{Year:D4}-{Month:D2}",
        _ => $"{Kind}:{Year}-{Month}",
    };
}
