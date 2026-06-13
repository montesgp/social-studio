using System.Text.Json;
using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Features.Social.ClientBrandManuals.Converters;
using Incoders.Hive.Application.Features.Social.ClientBrandManuals.Dtos;
using Incoders.Hive.Application.Features.Social.ProfileAnalyzer.Analyze;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ClientBrandManuals;
using Incoders.Hive.Domain.Social.Clients;
using Incoders.Hive.Domain.Social.ContentPlans;
using Incoders.Hive.Domain.Social.ProfileAnalysis;
using MediatR;
using DomainGeneralStrategy = Incoders.Hive.Domain.Social.GeneralStrategies.GeneralStrategy;
using DomainGeneralStrategyId = Incoders.Hive.Domain.Social.GeneralStrategies.GeneralStrategyId;

namespace Incoders.Hive.Application.Features.Social.ProfileAnalyzer.Impute;

/// <summary>
/// Handles <see cref="ImputeProfileAnalysisCommand"/>.
///
/// Per-field impute flow (PA-20, PA-21, PA-22):
/// 1. Load the analysis; guard Status == Analyzed (PA-22).
/// 2. Guard ClientId != null (PA-20).
/// 3. Deserialize the stored AiAnalysis JSON to extract field values.
/// 4. For each selected target group:
///    - BrandManual: load current active manual → merge selected fields → Update
///    - GeneralStrategy: load current strategy → merge selected fields → Update (or Create if absent)
///    - ContentPlan: load current-month plan → merge selected fields → Upsert (creates if absent)
/// 5. Each operation is idempotent: re-applying the same values yields the same state (PA-21).
///
/// IMPUTE TARGETS (10 total — Logos, Slots, and ToneKeywords are NOT AI-derivable or removed):
///   BrandManual:     ColorPalette(1), Typography(2), BackgroundTemplates(3), VoiceAndTone(4)
///   ContentPlan:     ObjectiveText(5), ObjectiveKpis(6), ContentStrategyText(7)
///   GeneralStrategy: ContentPillars(8), TargetAudience(9), PostingFrequency(10)
///
/// Load-merge-write invariant:
///   Each write passes ALL existing fields with only the selected targets overwritten.
///   Unselected fields are carried forward unchanged so a partial impute never blanks data.
/// </summary>
public sealed class ImputeProfileAnalysisHandler
    : IRequestHandler<ImputeProfileAnalysisCommand, Result<Unit>>
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly IProfileAnalysisRepository _analysisRepo;
    private readonly IClientBrandManualRepository _brandManualRepo;
    private readonly IContentPlanRepository _contentPlanRepo;
    private readonly IGeneralStrategyRepository _generalStrategyRepo;
    private readonly IClock _clock;

    public ImputeProfileAnalysisHandler(
        IProfileAnalysisRepository analysisRepo,
        IClientBrandManualRepository brandManualRepo,
        IContentPlanRepository contentPlanRepo,
        IGeneralStrategyRepository generalStrategyRepo,
        IClock clock)
    {
        _analysisRepo = analysisRepo;
        _brandManualRepo = brandManualRepo;
        _contentPlanRepo = contentPlanRepo;
        _generalStrategyRepo = generalStrategyRepo;
        _clock = clock;
    }

    public async Task<Result<Unit>> Handle(
        ImputeProfileAnalysisCommand request,
        CancellationToken cancellationToken)
    {
        // ── Load analysis ─────────────────────────────────────────────────────
        var analysis = await _analysisRepo.GetByIdAsync(
            request.AnalysisId,
            request.OrganizationId,
            cancellationToken);

        if (analysis is null)
        {
            return Result.Failure<Unit>(ProfileAnalysisErrors.NotFound);
        }

        // ── PA-22: Guard status == Analyzed ───────────────────────────────────
        if (analysis.Status != ProfileAnalysisStatus.Analyzed)
        {
            return Result.Failure<Unit>(ProfileAnalysisErrors.NotAnalyzed);
        }

        // ── PA-20: Guard client is linked ─────────────────────────────────────
        if (!analysis.ClientId.HasValue)
        {
            return Result.Failure<Unit>(ProfileAnalysisErrors.ClientRequired);
        }

        var clientId = analysis.ClientId.Value;
        var orgId = analysis.OrganizationId;
        var now = _clock.UtcNow;

        // ── Deserialize AI analysis ────────────────────────────────────────────
        var ai = DeserializeAiAnalysis(analysis.AiAnalysisJson);

        var targets = new HashSet<ProfileImputeTarget>(request.Targets);

        // ── BrandManual group ─────────────────────────────────────────────────
        var brandManualTargets = new[]
        {
            ProfileImputeTarget.ColorPalette,
            ProfileImputeTarget.Typography,
            ProfileImputeTarget.BackgroundTemplates,
            ProfileImputeTarget.VoiceAndTone
        };

        if (targets.Any(t => brandManualTargets.Contains(t)) && ai is not null)
        {
            var result = await ImputeBrandManualAsync(
                clientId, orgId, targets, ai, now, cancellationToken);
            if (result.IsFailure) return Result.Failure<Unit>(result.Error);
        }

        // ── ContentPlan group ──────────────────────────────────────────────────
        var contentPlanTargets = new[]
        {
            ProfileImputeTarget.ObjectiveText,
            ProfileImputeTarget.ObjectiveKpis,
            ProfileImputeTarget.ContentStrategyText
        };

        if (targets.Any(t => contentPlanTargets.Contains(t)) && ai is not null)
        {
            var result = await ImputeContentPlanAsync(
                clientId, orgId, targets, ai, now, cancellationToken);
            if (result.IsFailure) return Result.Failure<Unit>(result.Error);
        }

        // ── GeneralStrategy group ──────────────────────────────────────────────
        var generalStrategyTargets = new[]
        {
            ProfileImputeTarget.ContentPillars,
            ProfileImputeTarget.TargetAudience,
            ProfileImputeTarget.PostingFrequency,
        };

        if (targets.Any(t => generalStrategyTargets.Contains(t)) && ai is not null)
        {
            var result = await ImputeGeneralStrategyAsync(
                clientId, orgId, targets, ai, now, cancellationToken);
            if (result.IsFailure) return Result.Failure<Unit>(result.Error);
        }

        return Result.Success(Unit.Value);
    }

    // ── BrandManual impute ────────────────────────────────────────────────────

    private async Task<Result<Unit>> ImputeBrandManualAsync(
        Guid clientId,
        Guid orgId,
        HashSet<ProfileImputeTarget> targets,
        AiAnalysisDto ai,
        DateTime now,
        CancellationToken ct)
    {
        var manual = await _brandManualRepo.GetActiveByClientAsync(
            new ClientId(clientId), orgId, ct);

        if (manual is null) return Result.Success(Unit.Value); // No active manual → skip gracefully

        // Load-merge-write: start from existing state, override only selected targets
        var palette = targets.Contains(ProfileImputeTarget.ColorPalette)
            ? MapColorPalette(ai.DetectedBrandColors)
            : [.. manual.ColorPalette.Select(c => new ColorEntryDto(c.Name, c.Hex, c.Role))];

        var typography = targets.Contains(ProfileImputeTarget.Typography)
            ? MapTypography(ai.DetectedFonts)
            : [.. manual.Typography.Select(t => new TypographyEntryDto(t.Name, t.FontFamily, t.Role, t.FontUrl, t.Weight))];

        var backgrounds = targets.Contains(ProfileImputeTarget.BackgroundTemplates)
            ? MapBackgrounds(ai.DetectedBackgroundImages)
            : [.. manual.BackgroundTemplates.Select(b => new BackgroundEntryDto(b.Name, b.Url, b.UseCase))];

        var voiceAndTone = targets.Contains(ProfileImputeTarget.VoiceAndTone)
            ? ai.VoiceTone
            : manual.VoiceAndTone;

        // Logos are NOT an impute target (not AI-derivable per WU-01 spike) — always preserved
        IReadOnlyList<LogoEntryDto> logos = manual.Logos.Select(l => new LogoEntryDto(l.Name, l.Url, l.Variant)).ToList();

        var paletteResult = BrandManualDtoConverters.ToColorEntries(palette);
        if (paletteResult.IsFailure) return Result.Failure<Unit>(paletteResult.Error);

        var logosResult = BrandManualDtoConverters.ToLogoEntries(logos);
        if (logosResult.IsFailure) return Result.Failure<Unit>(logosResult.Error);

        var bgResult = BrandManualDtoConverters.ToBackgroundEntries(backgrounds);
        if (bgResult.IsFailure) return Result.Failure<Unit>(bgResult.Error);

        var typoResult = BrandManualDtoConverters.ToTypographyEntries(typography);
        if (typoResult.IsFailure) return Result.Failure<Unit>(typoResult.Error);

        var updateResult = manual.Update(
            manual.Title,
            paletteResult.Value,
            logosResult.Value,
            bgResult.Value,
            typoResult.Value,
            voiceAndTone,
            manual.Notes,
            now);

        if (updateResult.IsFailure) return Result.Failure<Unit>(updateResult.Error);

        _brandManualRepo.Update(manual);
        await _brandManualRepo.SaveChangesAsync(ct);

        return Result.Success(Unit.Value);
    }

    // ── ContentPlan impute ────────────────────────────────────────────────────

    private async Task<Result<Unit>> ImputeContentPlanAsync(
        Guid clientId,
        Guid orgId,
        HashSet<ProfileImputeTarget> targets,
        AiAnalysisDto ai,
        DateTime now,
        CancellationToken ct)
    {
        // Target period = current calendar month (PA-20 spike finding)
        var periodResult = Period.Monthly(now.Year, now.Month);
        if (periodResult.IsFailure) return Result.Failure<Unit>(periodResult.Error);
        var period = periodResult.Value;

        var existing = await _contentPlanRepo.GetByClientAndPeriodAsync(
            orgId, clientId, period, ct);

        // Load-merge-write: start from existing state, override only selected targets
        var objectiveText = targets.Contains(ProfileImputeTarget.ObjectiveText)
            ? (ai.MonthlyObjectiveSuggestion ?? string.Empty)
            : (existing?.Objective.Objectives ?? ai.MonthlyObjectiveSuggestion ?? string.Empty);

        var kpis = targets.Contains(ProfileImputeTarget.ObjectiveKpis)
            ? (IReadOnlyList<string>)(ai.RecommendedKpis ?? [])
            : existing?.Objective.Kpis ?? [];

        // ContentStrategyText is stored in the ContentStrategy node — impute via domain update
        ContentPlan plan;
        if (existing is null)
        {
            // CREATE path: upsert creates the plan for the current month
            var createResult = ContentPlan.Create(
                ContentPlanId.New(), clientId, orgId, period,
                objectiveText, kpis, now);
            if (createResult.IsFailure) return Result.Failure<Unit>(createResult.Error);
            plan = createResult.Value;
        }
        else
        {
            // UPDATE path: update objective fields if selected
            plan = existing;
            if (targets.Contains(ProfileImputeTarget.ObjectiveText) ||
                targets.Contains(ProfileImputeTarget.ObjectiveKpis))
            {
                var updateResult = plan.UpsertObjective(objectiveText, kpis, now, now);
                if (updateResult.IsFailure) return Result.Failure<Unit>(updateResult.Error);
            }

            if (targets.Contains(ProfileImputeTarget.ContentStrategyText))
            {
                var strategyResult = plan.UpsertContentStrategy(
                    ai.ContentStrategySuggestion,
                    Provenance.Manual(now),
                    now);
                if (strategyResult.IsFailure) return Result.Failure<Unit>(strategyResult.Error);
            }
        }

        await _contentPlanRepo.UpsertAsync(plan, ct);
        return Result.Success(Unit.Value);
    }

    // ── GeneralStrategy impute ────────────────────────────────────────────────

    private async Task<Result<Unit>> ImputeGeneralStrategyAsync(
        Guid clientId,
        Guid orgId,
        HashSet<ProfileImputeTarget> targets,
        AiAnalysisDto ai,
        DateTime now,
        CancellationToken ct)
    {
        var existing = await _generalStrategyRepo.GetByClientIdAsync(clientId, orgId, ct);

        // Load-merge-write: start from existing state, override only selected targets
        var pillars = targets.Contains(ProfileImputeTarget.ContentPillars)
            ? (IReadOnlyList<string>)(ai.ContentPillars ?? [])
            : existing?.ContentPillars ?? [];

        var audience = targets.Contains(ProfileImputeTarget.TargetAudience)
            ? ai.AudienceProfile
            : existing?.TargetAudience;

        // PostingDays[] → descriptive PostingFrequency string (e.g. "Mon, Wed, Fri")
        var postingFrequency = targets.Contains(ProfileImputeTarget.PostingFrequency)
            ? MapPostingFrequency(ai.PostingDays)
            : existing?.PostingFrequency;

        if (existing is null)
        {
            // CREATE path: must have at least one signal
            var ensuredPillars = pillars.Count > 0 ? pillars : ["General"];
            var createResult = DomainGeneralStrategy.Create(
                DomainGeneralStrategyId.New(), clientId, orgId,
                ensuredPillars, audience, postingFrequency, notes: null, now);
            if (createResult.IsFailure) return Result.Failure<Unit>(createResult.Error);

            _generalStrategyRepo.Add(createResult.Value);
        }
        else
        {
            // UPDATE path
            var updateResult = existing.Update(
                pillars, audience, postingFrequency, existing.Notes, now);
            if (updateResult.IsFailure) return Result.Failure<Unit>(updateResult.Error);

            _generalStrategyRepo.Update(existing);
        }

        await _generalStrategyRepo.SaveChangesAsync(ct);
        return Result.Success(Unit.Value);
    }

    // ── Mapping helpers ───────────────────────────────────────────────────────

    private static IReadOnlyList<ColorEntryDto> MapColorPalette(
        IReadOnlyList<AiColorDto>? colors)
    {
        if (colors is null || colors.Count == 0) return [];
        return colors.Select(c => new ColorEntryDto(c.Name, c.Hex, null)).ToList();
    }

    private static IReadOnlyList<TypographyEntryDto> MapTypography(
        IReadOnlyList<string>? fonts)
    {
        if (fonts is null || fonts.Count == 0) return [];
        return fonts.Select(f => new TypographyEntryDto(f, f, null, null, null)).ToList();
    }

    private static IReadOnlyList<BackgroundEntryDto> MapBackgrounds(
        IReadOnlyList<string>? urls)
    {
        if (urls is null || urls.Count == 0) return [];
        return urls.Select((url, i) =>
            new BackgroundEntryDto($"Background {i + 1}", url, null)).ToList();
    }

    /// <summary>
    /// Maps postingDays string array (e.g. ["Mon","Wed","Fri"]) to a descriptive
    /// PostingFrequency string (e.g. "Mon, Wed, Fri"). This is the AI posting-days
    /// → GeneralStrategy.PostingFrequency mapping per WU-01 spike findings.
    /// </summary>
    private static string? MapPostingFrequency(IReadOnlyList<string>? days)
    {
        if (days is null || days.Count == 0) return null;
        return string.Join(", ", days);
    }

    // ── AI Analysis deserialization ───────────────────────────────────────────

    /// <summary>
    /// Deserializes stored AiAnalysisJson to a projection for impute field extraction.
    /// Returns null when no AI analysis is stored (graceful degradation).
    /// </summary>
    private AiAnalysisDto? DeserializeAiAnalysis(string? json)
    {
        if (json is null) return null;
        try
        {
            return JsonSerializer.Deserialize<AiAnalysisDto>(json, JsonOpts);
        }
        catch
        {
            return null;
        }
    }

    // ── Internal DTO records for AI JSON deserialization ──────────────────────

    private sealed record AiAnalysisDto(
        string? Summary = null,
        string? AudienceProfile = null,
        IReadOnlyList<string>? ContentPillars = null,
        IReadOnlyList<string>? TopContentTypes = null,
        IReadOnlyList<string>? PostingDays = null,
        IReadOnlyList<AiColorDto>? DetectedBrandColors = null,
        IReadOnlyList<string>? DetectedFonts = null,
        IReadOnlyList<string>? DetectedBackgroundImages = null,
        string? VoiceTone = null,
        IReadOnlyList<string>? RecommendedKpis = null,
        string? MonthlyObjectiveSuggestion = null,
        string? ContentStrategySuggestion = null,
        IReadOnlyList<string>? Strengths = null,
        IReadOnlyList<string>? Weaknesses = null,
        IReadOnlyList<string>? Opportunities = null,
        double Confidence = 0.0,
        IReadOnlyList<string>? Warnings = null);

    private sealed record AiColorDto(string Name, string Hex);
}
