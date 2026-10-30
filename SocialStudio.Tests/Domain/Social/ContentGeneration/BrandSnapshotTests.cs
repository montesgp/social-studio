using SocialStudio.Domain.Social.ClientBrandManuals;
using SocialStudio.Domain.Social.ContentGeneration;

namespace SocialStudio.Tests.Domain.Social.ContentGeneration;

public sealed class BrandSnapshotTests
{
    // --- Frozen copy semantics (immutability) ---

    [Fact]
    public void BrandSnapshot_IsImmutableRecord()
    {
        var snapshot = BuildFullSnapshot();

        // Record types are structurally immutable — all properties are init-only
        Assert.IsAssignableFrom<BrandSnapshot>(snapshot);
        Assert.NotNull(snapshot.Brand);
        Assert.NotNull(snapshot.Strategy);
        Assert.NotNull(snapshot.MonthlyObjective);
    }

    // --- Full brand: no inferred fields ---

    [Fact]
    public void FullBrandSnapshot_HasNoInferredFields()
    {
        var snapshot = BuildFullSnapshot();

        Assert.DoesNotContain(snapshot.InferredFields, kv => kv.Value);
    }

    [Fact]
    public void FullBrandSnapshot_AllFieldsMarkedNotInferred()
    {
        var snapshot = BuildFullSnapshot();

        // Every field we expose should be present and marked false (not inferred)
        Assert.True(snapshot.InferredFields.ContainsKey("ColorPalette"));
        Assert.False(snapshot.InferredFields["ColorPalette"]);
        Assert.True(snapshot.InferredFields.ContainsKey("VoiceAndTone"));
        Assert.False(snapshot.InferredFields["VoiceAndTone"]);
    }

    // --- Empty / null brand: all fields inferred ---

    [Fact]
    public void EmptyBrandSnapshot_AllFieldsAreInferred()
    {
        var snapshot = BrandSnapshot.FromBrandData(null, null, null);

        Assert.True(snapshot.InferredFields.All(kv => kv.Value));
    }

    [Fact]
    public void EmptyBrandSnapshot_HasNoNullValues_OnlyInferredMarkers()
    {
        var snapshot = BrandSnapshot.FromBrandData(null, null, null);

        // Snapshot must not have null collections — it should have defaults
        Assert.NotNull(snapshot.Brand);
        Assert.Empty(snapshot.Brand!.ColorPalette);
    }

    // --- Partial brand: only missing fields inferred ---

    [Fact]
    public void PartialBrandSnapshot_MissingColorPalette_ColorPaletteIsInferred()
    {
        var brandData = new BrandManualSnapshot(
            ColorPalette: Array.Empty<WeightedColor>(),   // empty = missing
            Logos: Array.Empty<string>(),
            BackgroundTemplates: Array.Empty<BrandBackgroundTemplateSnapshot>(),
            Typography: Array.Empty<string>(),
            VoiceAndTone: "Professional and friendly",
            Notes: null);

        var snapshot = BrandSnapshot.FromBrandData(brandData, null, null);

        Assert.True(snapshot.InferredFields["ColorPalette"]);
        Assert.False(snapshot.InferredFields["VoiceAndTone"]);
    }

    [Fact]
    public void PartialBrandSnapshot_WithVoiceOnly_OnlyNonVoiceFieldsAreInferred()
    {
        var brandData = new BrandManualSnapshot(
            ColorPalette: Array.Empty<WeightedColor>(),
            Logos: Array.Empty<string>(),
            BackgroundTemplates: Array.Empty<BrandBackgroundTemplateSnapshot>(),
            Typography: Array.Empty<string>(),
            VoiceAndTone: "Bold and direct",
            Notes: null);

        var snapshot = BrandSnapshot.FromBrandData(brandData, null, null);

        // VoiceAndTone is present — should NOT be inferred
        Assert.False(snapshot.InferredFields["VoiceAndTone"]);

        // ColorPalette is absent — should be inferred
        Assert.True(snapshot.InferredFields["ColorPalette"]);
    }

    // --- IsInferred map is read-only ---

    [Fact]
    public void BrandSnapshot_InferredFields_IsReadOnly()
    {
        var snapshot = BuildFullSnapshot();

        Assert.IsAssignableFrom<IReadOnlyDictionary<string, bool>>(snapshot.InferredFields);
    }

    // --- BrandSnapshot.Empty ---

    [Fact]
    public void BrandSnapshot_Empty_AllFieldsInferredAndNonNull()
    {
        var empty = BrandSnapshot.Empty;

        Assert.NotNull(empty);
        Assert.True(empty.InferredFields.Any());
        Assert.True(empty.InferredFields.All(kv => kv.Value));
    }

    // --- Helpers ---

    private static BrandSnapshot BuildFullSnapshot()
    {
        var brand = new BrandManualSnapshot(
            ColorPalette: new[] { new WeightedColor("#FF0000", ColorRole.Primary, 0), new WeightedColor("#00FF00", ColorRole.Secondary, 1) },
            Logos: new[] { "https://cdn.example.com/logo.png" },
            BackgroundTemplates:
            [
                new BrandBackgroundTemplateSnapshot(
                    "Story",
                    "/storage/client-assets/org/client/brand/story.png",
                    "story")
            ],
            Typography: new[] { "Inter", "Roboto" },
            VoiceAndTone: "Friendly and professional",
            Notes: "Q1 brand guidelines");

        var strategy = new StrategySnapshot(
            ContentPillars: new[] { "Education", "Entertainment" },
            TargetAudience: "Young professionals 25-35",
            PostingFrequency: "3 posts/week");

        var objective = new ClientMonthlyObjectiveSnapshot(
            Objectives: "Grow engagement by 20%",
            Kpis: new[] { "Reach 10k views", "CTR > 5%" });

        return BrandSnapshot.FromBrandData(
            brand,
            strategy,
            objective,
            client: new ClientProfileSnapshot(
                Name: "Northwind Labs",
                Description: "AI operations platform",
                SectorOrNiche: "B2B SaaS"),
            referentAccounts:
            [
                new ClientAccountSnapshot("linkedin", "LinkedIn", "https://linkedin.com/company/referent", "@referent", "Referent", "Benchmark")
            ],
            competitorAccounts:
            [
                new ClientAccountSnapshot("linkedin", "LinkedIn", "https://linkedin.com/company/competitor", "@competitor", "Competitor", "Avoid similar claims")
            ],
            contentStrategy: new ContentStrategySnapshot(
                StrategyText: "Focus on educational reels and carousel posts this quarter.",
                Source: 1,
                Status: 2),
            calendar: new CalendarSnapshot(Slots:
            [
                new CalendarSlotSnapshot(Day: 5,  ContentFormat: "caption",  SortOrder: 0),
                new CalendarSlotSnapshot(Day: 15, ContentFormat: "carousel", SortOrder: 1),
            ]),
            period: new PeriodSnapshot(Year: 2026, Month: 6, PeriodKind: 0));
    }
}
