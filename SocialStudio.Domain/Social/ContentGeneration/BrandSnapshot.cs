namespace SocialStudio.Domain.Social.ContentGeneration;

/// <summary>
/// Frozen copy of all brand context at content-generation job-start time.
/// Immutable for the lifetime of the job.
/// <para>
/// <see cref="InferredFields"/> maps each field name to true when the value was derived
/// from inference (absent explicit client input) rather than from persisted brand data.
/// </para>
/// </summary>
public sealed record BrandSnapshot(
    ClientProfileSnapshot? Client,
    BrandManualSnapshot? Brand,
    StrategySnapshot? Strategy,
    ClientMonthlyObjectiveSnapshot? MonthlyObjective,
    IReadOnlyList<ClientAccountSnapshot> ReferentAccounts,
    IReadOnlyList<ClientAccountSnapshot> CompetitorAccounts,
    IReadOnlyDictionary<string, bool> InferredFields,
    // NEW — optional per-period context (additive, safe defaults = null)
    ContentStrategySnapshot? ContentStrategy = null,
    CalendarSnapshot? Calendar = null,
    PeriodSnapshot? Period = null)
{
    /// <summary>
    /// Fully-inferred snapshot — used when no brand data exists at all.
    /// </summary>
    public static BrandSnapshot Empty => FromBrandData(null, null, null);

    /// <summary>
    /// Builds a brand snapshot from the supplied (nullable) brand data sources.
    /// Missing or empty fields are marked <c>IsInferred = true</c> in <see cref="InferredFields"/>.
    /// This factory is domain-only; actual data loading lives in Application/Infrastructure.
    /// </summary>
    public static BrandSnapshot FromBrandData(
        BrandManualSnapshot? brand,
        StrategySnapshot? strategy,
        ClientMonthlyObjectiveSnapshot? monthlyObjective,
        ClientProfileSnapshot? client = null,
        IReadOnlyList<ClientAccountSnapshot>? referentAccounts = null,
        IReadOnlyList<ClientAccountSnapshot>? competitorAccounts = null,
        ContentStrategySnapshot? contentStrategy = null,
        CalendarSnapshot? calendar = null,
        PeriodSnapshot? period = null)
    {
        var inferred = new Dictionary<string, bool>();

        // Client profile fields
        bool hasClient = client is not null;
        inferred["ClientName"] = !hasClient || string.IsNullOrWhiteSpace(client!.Name);
        inferred["ClientDescription"] = !hasClient || string.IsNullOrWhiteSpace(client!.Description);
        inferred["ClientSectorOrNiche"] = !hasClient || string.IsNullOrWhiteSpace(client!.SectorOrNiche);

        // Brand manual fields
        bool hasBrand = brand is not null;
        inferred["ColorPalette"] = !hasBrand || brand!.ColorPalette.Count == 0;
        inferred["Logos"] = !hasBrand || brand!.Logos.Count == 0;
        inferred["BackgroundTemplates"] = !hasBrand || brand!.BackgroundTemplates.Count == 0;
        inferred["Typography"] = !hasBrand || brand!.Typography.Count == 0;
        inferred["VoiceAndTone"] = !hasBrand || string.IsNullOrWhiteSpace(brand!.VoiceAndTone);
        inferred["Notes"] = !hasBrand || string.IsNullOrWhiteSpace(brand!.Notes);

        // Strategy fields
        bool hasStrategy = strategy is not null;
        inferred["ContentPillars"] = !hasStrategy || strategy!.ContentPillars.Count == 0;
        inferred["TargetAudience"] = !hasStrategy || string.IsNullOrWhiteSpace(strategy!.TargetAudience);
        inferred["PostingFrequency"] = !hasStrategy || string.IsNullOrWhiteSpace(strategy!.PostingFrequency);

        // Monthly objective
        bool hasObjective = monthlyObjective is not null;
        inferred["MonthlyObjectives"] = !hasObjective || string.IsNullOrWhiteSpace(monthlyObjective!.Objectives);
        inferred["MonthlyKpis"] = !hasObjective || monthlyObjective!.Kpis.Count == 0;

        var effectiveReferentAccounts = referentAccounts ?? Array.Empty<ClientAccountSnapshot>();
        var effectiveCompetitorAccounts = competitorAccounts ?? Array.Empty<ClientAccountSnapshot>();
        inferred["ReferentAccounts"] = effectiveReferentAccounts.Count == 0;
        inferred["CompetitorAccounts"] = effectiveCompetitorAccounts.Count == 0;

        // Per-period content strategy (new — optional)
        inferred["ContentStrategy"] = contentStrategy is null ||
                                      string.IsNullOrWhiteSpace(contentStrategy.StrategyText);

        // Calendar slots (new — optional)
        inferred["CalendarSlots"] = calendar is null || calendar.Slots.Count == 0;

        // Provide a default empty brand if none was supplied so consumers don't see nulls
        var effectiveBrand = brand ?? new BrandManualSnapshot(
            ColorPalette: Array.Empty<WeightedColor>(),
            Logos: Array.Empty<string>(),
            BackgroundTemplates: Array.Empty<BrandBackgroundTemplateSnapshot>(),
            Typography: Array.Empty<string>(),
            VoiceAndTone: null,
            Notes: null);

        return new BrandSnapshot(
            client,
            effectiveBrand,
            strategy,
            monthlyObjective,
            effectiveReferentAccounts,
            effectiveCompetitorAccounts,
            inferred,
            ContentStrategy: contentStrategy,
            Calendar: calendar,
            Period: period);
    }
}
