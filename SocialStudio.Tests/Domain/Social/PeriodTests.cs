using SocialStudio.Domain.Common;
using SocialStudio.Domain.Social.ContentPlans;

namespace SocialStudio.Tests.Domain.Social;

public sealed class PeriodTests
{
    // --- CP-01: Valid monthly period accepted ---

    [Fact]
    public void Monthly_ValidYearAndMonth_Succeeds()
    {
        var result = Period.Monthly(2026, 3);

        Assert.True(result.IsSuccess);
        var period = result.Value;
        Assert.Equal(PeriodKind.Monthly, period.Kind);
        Assert.Equal(2026, period.Year);
        Assert.Equal(3, period.Month);
    }

    // --- CP-01: Stable string representation ---

    [Fact]
    public void Monthly_StringRepresentation_IsStableAndDeterministic()
    {
        var p1 = Period.Monthly(2026, 3).Value;
        var p2 = Period.Monthly(2026, 3).Value;

        Assert.Equal(p1.ToString(), p2.ToString());
        Assert.False(string.IsNullOrWhiteSpace(p1.ToString()));
    }

    // --- CP-01: Value equality ---

    [Fact]
    public void Monthly_SameYearAndMonth_AreEqual()
    {
        var p1 = Period.Monthly(2026, 5).Value;
        var p2 = Period.Monthly(2026, 5).Value;

        Assert.Equal(p1, p2);
    }

    [Fact]
    public void Monthly_DifferentMonth_AreNotEqual()
    {
        var p1 = Period.Monthly(2026, 5).Value;
        var p2 = Period.Monthly(2026, 6).Value;

        Assert.NotEqual(p1, p2);
    }

    // --- CP-01: Invalid month rejected ---

    [Fact]
    public void Monthly_Month13_ReturnsError()
    {
        var result = Period.Monthly(2026, 13);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Equal("social.content_plans.invalid_month", result.Error.Code);
    }

    [Fact]
    public void Monthly_Month0_ReturnsError()
    {
        var result = Period.Monthly(2026, 0);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Equal("social.content_plans.invalid_month", result.Error.Code);
    }

    // --- CP-01: Invalid year rejected ---

    [Fact]
    public void Monthly_Year0_ReturnsError()
    {
        var result = Period.Monthly(0, 6);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Equal("social.content_plans.invalid_year", result.Error.Code);
    }

    [Fact]
    public void Monthly_Year10000_ReturnsError()
    {
        var result = Period.Monthly(10000, 6);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Equal("social.content_plans.invalid_year", result.Error.Code);
    }

    // --- CP-01: Boundary values pass ---

    [Theory]
    [InlineData(1, 1)]
    [InlineData(9999, 12)]
    [InlineData(2026, 6)]
    public void Monthly_BoundaryValues_Succeed(int year, int month)
    {
        var result = Period.Monthly(year, month);
        Assert.True(result.IsSuccess);
    }

    // --- CP-01: RestoreMonthly bypasses validation ---

    [Fact]
    public void RestoreMonthly_BypassesValidation()
    {
        // Month 0 would normally fail Monthly() — Restore must not validate
        var period = Period.RestoreMonthly(2026, 0);

        Assert.Equal(PeriodKind.Monthly, period.Kind);
        Assert.Equal(2026, period.Year);
        Assert.Equal(0, period.Month);
    }

    // --- Days-in-month helper ---

    [Fact]
    public void DaysInMonth_February2026_Returns28()
    {
        var period = Period.Monthly(2026, 2).Value;
        Assert.Equal(28, period.DaysInMonth());
    }

    [Fact]
    public void DaysInMonth_January2026_Returns31()
    {
        var period = Period.Monthly(2026, 1).Value;
        Assert.Equal(31, period.DaysInMonth());
    }
}
