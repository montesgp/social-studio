using SocialStudio.Domain.Common;
using SocialStudio.Domain.Social.GeneralStrategies;
using SocialStudio.Domain.Social.Clients;

namespace SocialStudio.Tests.Domain.Social;

public sealed class GeneralStrategyTests
{
    private static readonly DateTime Now = new(2026, 5, 20, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Later = new(2026, 5, 20, 13, 0, 0, DateTimeKind.Utc);
    private static readonly ClientId ValidClientId = ClientId.New();
    private static readonly Guid ValidOrgId = Guid.NewGuid();

    private static readonly IReadOnlyList<string> ValidPillars = new[] { "Education", "Entertainment" };

    // --- Type name checks ---

    [Fact]
    public void TypeName_IsGeneralStrategy()
    {
        var result = CreateValid();
        Assert.IsType<GeneralStrategy>(result);
        Assert.Equal(nameof(GeneralStrategy), result.GetType().Name);
    }

    [Fact]
    public void IdType_IsGeneralStrategyId()
    {
        var id = GeneralStrategyId.New();
        Assert.IsType<GeneralStrategyId>(id);
    }

    // --- Create: happy path ---

    [Fact]
    public void Create_WithValidData_Succeeds()
    {
        var result = GeneralStrategy.Create(
            GeneralStrategyId.New(),
            ValidClientId.Value,
            ValidOrgId,
            ValidPillars,
            "Young professionals 25-35",
            "3 posts/week",
            "Internal notes",
            Now);

        Assert.True(result.IsSuccess);
        var s = result.Value;
        Assert.Equal(ValidClientId.Value, s.ClientId);
        Assert.Equal(ValidOrgId, s.OrganizationId);
        Assert.Equal(Now, s.CreatedAtUtc);
        Assert.Equal(Now, s.UpdatedAtUtc);
        Assert.Equal(2, s.ContentPillars.Count);
        Assert.Equal("Young professionals 25-35", s.TargetAudience);
        Assert.Equal("3 posts/week", s.PostingFrequency);
        Assert.Equal("Internal notes", s.Notes);
    }

    // --- Create: ContentPillars limit ---

    [Fact]
    public void Create_ContentPillarsExceeding10_ReturnsError()
    {
        var tooMany = Enumerable.Range(1, 11).Select(i => $"Pillar{i}").ToList();

        var result = GeneralStrategy.Create(
            GeneralStrategyId.New(),
            ValidClientId.Value,
            ValidOrgId,
            tooMany,
            null, null, null, Now);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    // --- Create: TargetAudience limit ---

    [Fact]
    public void Create_TargetAudienceExceeding1000Chars_ReturnsError()
    {
        var longText = new string('a', 1001);

        var result = GeneralStrategy.Create(
            GeneralStrategyId.New(),
            ValidClientId.Value,
            ValidOrgId,
            ValidPillars,
            longText, null, null, Now);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    // --- Create: PostingFrequency limit ---

    [Fact]
    public void Create_PostingFrequencyExceeding200Chars_ReturnsError()
    {
        var longText = new string('b', 201);

        var result = GeneralStrategy.Create(
            GeneralStrategyId.New(),
            ValidClientId.Value,
            ValidOrgId,
            ValidPillars,
            null, longText, null, Now);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    // --- Create: Notes limit ---

    [Fact]
    public void Create_NotesExceeding2000Chars_ReturnsError()
    {
        var longText = new string('c', 2001);

        var result = GeneralStrategy.Create(
            GeneralStrategyId.New(),
            ValidClientId.Value,
            ValidOrgId,
            ValidPillars,
            null, null, longText, Now);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    // --- Create: EmptyStrategy guard ---

    [Fact]
    public void Create_AllFieldsEmpty_ReturnsEmptyStrategyError()
    {
        var result = GeneralStrategy.Create(
            GeneralStrategyId.New(),
            ValidClientId.Value,
            ValidOrgId,
            Array.Empty<string>(),
            null, null, null, Now);

        Assert.True(result.IsFailure);
        Assert.Equal("social.general_strategies.empty_strategy", result.Error.Code);
    }

    // --- Errors: NotFound code ---

    [Fact]
    public void GeneralStrategyErrors_NotFound_HasExpectedCode()
    {
        Assert.Equal("social.general_strategies.not_found", GeneralStrategyErrors.NotFound.Code);
    }

    // --- Restore: bypasses validation ---

    [Fact]
    public void Restore_BypassesValidation()
    {
        var id = GeneralStrategyId.New();

        // Eleven pillars would normally fail Create — Restore must not validate
        var manyPillars = Enumerable.Range(1, 11).Select(i => $"P{i}").ToList();
        var strategy = GeneralStrategy.Restore(
            id,
            ValidClientId.Value,
            ValidOrgId,
            manyPillars,
            null, null, null,
            Now, Now);

        Assert.Equal(id, strategy.Id);
        Assert.Equal(11, strategy.ContentPillars.Count);
    }

    // --- Update ---

    [Fact]
    public void Update_AppliesChanges()
    {
        var strategy = CreateValid();
        var newPillars = new[] { "Fitness" };

        var result = strategy.Update(
            newPillars,
            "Athletes 20-40",
            "Daily",
            "Updated notes",
            Later);

        Assert.True(result.IsSuccess);
        Assert.Single(strategy.ContentPillars);
        Assert.Equal("Fitness", strategy.ContentPillars[0]);
        Assert.Equal("Athletes 20-40", strategy.TargetAudience);
        Assert.Equal("Daily", strategy.PostingFrequency);
        Assert.Equal("Updated notes", strategy.Notes);
        Assert.Equal(Later, strategy.UpdatedAtUtc);
    }

    // --- Timestamps type check ---

    [Fact]
    public void Timestamps_AreDateTime_NotDateTimeOffset()
    {
        var strategy = CreateValid();

        Assert.IsType<DateTime>(strategy.CreatedAtUtc);
        Assert.IsType<DateTime>(strategy.UpdatedAtUtc);
        Assert.Equal(DateTimeKind.Utc, strategy.CreatedAtUtc.Kind);
        Assert.Equal(DateTimeKind.Utc, strategy.UpdatedAtUtc.Kind);
    }

    // --- Helpers ---

    private static GeneralStrategy CreateValid() =>
        GeneralStrategy.Create(
            GeneralStrategyId.New(),
            ValidClientId.Value,
            ValidOrgId,
            ValidPillars,
            "Young professionals 25-35",
            "3 posts/week",
            "Internal notes",
            Now).Value;
}
