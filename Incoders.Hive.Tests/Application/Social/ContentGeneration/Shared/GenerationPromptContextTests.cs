using System.Text.Json;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Incoders.Hive.Domain.Social.ContentGeneration;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Shared;

/// <summary>
/// Tests that GenerationPromptContext.FromBrand serializes the new per-period sections
/// (contentStrategy, calendar, period) into the prompt JSON when present,
/// and omits / nulls them when absent.
/// </summary>
public sealed class GenerationPromptContextTests
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    // Helper — deserialize FromBrand result to a JsonElement for loose assertions
    private static JsonElement Serialize(BrandSnapshot snapshot)
    {
        var obj = GenerationPromptContext.FromBrand(snapshot);
        var json = JsonSerializer.Serialize(obj, JsonOpts);
        return JsonSerializer.Deserialize<JsonElement>(json, JsonOpts);
    }

    [Fact]
    public void FromBrand_WithContentStrategyAndCalendarAndPeriod_SerializesAllThreeSections()
    {
        // Arrange
        var snapshot = BrandSnapshot.FromBrandData(
            brand: null,
            strategy: null,
            monthlyObjective: null,
            contentStrategy: new ContentStrategySnapshot(
                StrategyText: "Go heavy on educational reels this month.",
                Source: 1,
                Status: 2),
            calendar: new CalendarSnapshot(Slots:
            [
                new CalendarSlotSnapshot(Day: 5,  ContentFormat: "caption",   SortOrder: 0),
                new CalendarSlotSnapshot(Day: 12, ContentFormat: "carousel",  SortOrder: 1),
                new CalendarSlotSnapshot(Day: 25, ContentFormat: "reel",      SortOrder: 2),
            ]),
            period: new PeriodSnapshot(Year: 2026, Month: 6, PeriodKind: 0));

        // Act
        var doc = Serialize(snapshot);

        // Assert — contentStrategy section
        Assert.True(doc.TryGetProperty("contentStrategy", out var cs));
        Assert.Equal("Go heavy on educational reels this month.", cs.GetProperty("strategyText").GetString());
        Assert.Equal(1, cs.GetProperty("source").GetInt32());
        Assert.Equal(2, cs.GetProperty("status").GetInt32());

        // Assert — calendar section
        Assert.True(doc.TryGetProperty("calendar", out var cal));
        var slots = cal.GetProperty("slots");
        Assert.Equal(3, slots.GetArrayLength());
        Assert.Equal(5,          slots[0].GetProperty("day").GetInt32());
        Assert.Equal("caption",  slots[0].GetProperty("contentFormat").GetString());
        Assert.Equal(0,          slots[0].GetProperty("sortOrder").GetInt32());
        Assert.Equal(12,         slots[1].GetProperty("day").GetInt32());
        Assert.Equal("carousel", slots[1].GetProperty("contentFormat").GetString());
        Assert.Equal(25,         slots[2].GetProperty("day").GetInt32());
        Assert.Equal("reel",     slots[2].GetProperty("contentFormat").GetString());

        // Assert — period section
        Assert.True(doc.TryGetProperty("period", out var period));
        Assert.Equal(2026, period.GetProperty("year").GetInt32());
        Assert.Equal(6,    period.GetProperty("month").GetInt32());
        Assert.Equal(0,    period.GetProperty("periodKind").GetInt32());
    }

    [Fact]
    public void FromBrand_WithNullContentStrategyAndCalendarAndPeriod_SectionsAreNullOrAbsent()
    {
        // Arrange — all new fields absent (default)
        var snapshot = BrandSnapshot.FromBrandData(null, null, null);

        // Act
        var doc = Serialize(snapshot);

        // Assert — sections are present but null (camelCase JSON with null values)
        // null sections must serialize as JSON null so the AI can detect missing context
        if (doc.TryGetProperty("contentStrategy", out var cs))
            Assert.Equal(JsonValueKind.Null, cs.ValueKind);

        if (doc.TryGetProperty("calendar", out var cal))
            Assert.Equal(JsonValueKind.Null, cal.ValueKind);

        if (doc.TryGetProperty("period", out var per))
            Assert.Equal(JsonValueKind.Null, per.ValueKind);
    }

    [Fact]
    public void FromBrand_WithContentStrategyButNullText_SerializesNullStrategyText()
    {
        // Arrange — strategy node present but StrategyText not yet authored
        var snapshot = BrandSnapshot.FromBrandData(
            brand: null,
            strategy: null,
            monthlyObjective: null,
            contentStrategy: new ContentStrategySnapshot(
                StrategyText: null,
                Source: 0,
                Status: 0));

        // Act
        var doc = Serialize(snapshot);

        // Assert — contentStrategy section present, strategyText is null
        Assert.True(doc.TryGetProperty("contentStrategy", out var cs));
        Assert.Equal(JsonValueKind.Null, cs.GetProperty("strategyText").ValueKind);
    }

    [Fact]
    public void FromBrand_WithEmptyCalendarSlots_SerializesEmptyArray()
    {
        // Arrange — calendar node present but no slots
        var snapshot = BrandSnapshot.FromBrandData(
            brand: null,
            strategy: null,
            monthlyObjective: null,
            calendar: new CalendarSnapshot(Slots: []));

        // Act
        var doc = Serialize(snapshot);

        // Assert — calendar.slots is an empty array
        Assert.True(doc.TryGetProperty("calendar", out var cal));
        var slots = cal.GetProperty("slots");
        Assert.Equal(JsonValueKind.Array, slots.ValueKind);
        Assert.Equal(0, slots.GetArrayLength());
    }

    [Fact]
    public void FromBrand_ExistingFields_AreUnchanged()
    {
        // Regression: existing fields must not be broken by the new sections
        var strategy = new StrategySnapshot(
            ContentPillars: new[] { "Education" },
            TargetAudience: "Professionals",
            PostingFrequency: "Daily");

        var objective = new ClientMonthlyObjectiveSnapshot(
            Objectives: "Grow 20%",
            Kpis: new[] { "10k views" });

        var snapshot = BrandSnapshot.FromBrandData(null, strategy, objective);

        var doc = Serialize(snapshot);

        // Existing fields still present
        Assert.True(doc.TryGetProperty("strategy", out var strat));
        Assert.Equal("Professionals", strat.GetProperty("targetAudience").GetString());

        Assert.True(doc.TryGetProperty("monthlyObjective", out var mo));
        Assert.Equal("Grow 20%", mo.GetProperty("objectives").GetString());
    }
}
