using Incoders.Hive.Domain.Social.ContentGeneration;
using Incoders.Hive.Domain.Social.SingleAnalyzer;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;

internal static class GenerationPromptContext
{
    public const string BrandConstraint =
        "Use the client context as hard constraints. Respect the brand manual, palette, typography, voice, " +
        "strategy, objectives, KPIs, referent accounts, and competitor context. Do not invent colors, fonts, " +
        "tone, audience, goals, or market positioning when explicit client data exists.";

    /// <summary>
    /// Specific-over-general precedence (particular → general). The client's brand-manual notes and any
    /// explicit client fields are AUTHORITATIVE editorial directives: honored literally, and they OVERRIDE
    /// every inferred default below (whether to add a call-to-action or a signature, where to place it, and
    /// its exact text — INCLUDING explicit prohibitions). When the directives are SILENT on a point, the
    /// agent falls back to inferring it from the client's analyzed content/style. This is the single rule
    /// that makes editorial behavior configurable per client without hardcoding it.
    /// </summary>
    public const string ClientDirectivePrecedence =
        "CLIENT DIRECTIVES (authoritative — particular over general): treat the brand manual notes and any " +
        "explicit client fields as EDITORIAL INSTRUCTIONS. Honor them LITERALLY and let them OVERRIDE the " +
        "default rules below — whether to add a call-to-action or a signature, on which slide to place it " +
        "(e.g. first or last), and its exact text. A directive may PROHIBIT something (e.g. 'do not sign the " +
        "slides with the client's @handle'); that prohibition WINS even if the client's own posts do it. When " +
        "the directives are SILENT on a point, infer it from the client's analyzed content/style.";

    /// <summary>
    /// Advisory concision constraint shared by all format generators (CG-CON-01, CG-CON-02).
    /// This is a firm prompt-side nudge only — never a hard validator. The pipeline logs a
    /// warning when generated copy exceeds per-slot soft limits but never rejects or retries.
    /// </summary>
    public const string ConcisionConstraint =
        "CONCISION (advisory, firm): social formats are skim-read. Singles (story, feed) = ONE super-summary " +
        "idea; carousels = ONE concept molded across slides, conceptual not exhaustive. Per text slot: short, " +
        "punchy, scannable; prefer phrases over sentences; avoid paragraphs. Headlines tight; body minimal; " +
        "no walls of text. Quality over length.";

    /// <summary>
    /// WU-4 (CAG-CG-08): additive aesthetic guidance for CASE C (ClientAesthetic) generation.
    ///
    /// <para>
    /// Emits an advisory hint containing the client's voice/tone and/or avg-slides-per-carousel when
    /// the profile is present. When null, returns an empty string so the caller can concatenate
    /// unconditionally without injecting whitespace or changing the prompt for non-CaseC jobs.
    /// </para>
    ///
    /// <para>
    /// ADDITIVE contract: callers MUST append this AFTER <see cref="ConcisionConstraint"/> so the
    /// concision directive takes precedence. This method does NOT modify <see cref="ConcisionConstraint"/>.
    /// The avg-slides value is clamped to [3, 10] ONLY in the rendered hint; the stored
    /// <c>ProfileStats.AverageSlidesPerCarousel</c> retains the raw computed value (design §6, OQ-2).
    /// </para>
    ///
    /// Refs: design §6; CAG-CG-08, CAG-CG-09; tasks 4.6–4.7.
    /// </summary>
    public static string AestheticGuidance(ClientAestheticProfile? profile)
    {
        if (profile is null)
        {
            return string.Empty;
        }

        var parts = new System.Text.StringBuilder("CLIENT AESTHETIC (advisory): ");

        if (profile.VoiceTone is { Length: > 0 } vt)
        {
            parts.Append($"match the client's voice/tone — {vt}. ");
        }

        // Phase 2 (CAG2-CG-05): caption-derived copy voice + visual style descriptors, additive.
        if (profile.CaptionVoice is { Length: > 0 } cv)
        {
            parts.Append($"mirror the client's caption/copy style — {cv}. ");
        }

        if (profile.StyleDescriptors is { Count: > 0 } styleDescriptors)
        {
            parts.Append($"the client's visual style reads as: {string.Join("; ", styleDescriptors)}. ");
        }

        if (profile.AverageSlidesPerCarousel is { } avg)
        {
            var clamped = (int)Math.Round(Math.Clamp(avg, 3.0, 10.0));
            parts.Append($"the client typically posts ~{clamped} slides per carousel; " +
                         "prefer a similar length when it does not harm the message. ");
        }

        parts.Append("These are ADVISORY: the CONCISION constraint above WINS on length and slot count.");
        return parts.ToString();
    }

    public static object FromAnalysis(SingleAnalyzerAnalysis analysis, string? regenerationObjection = null)
    {
        return new
        {
            summary = analysis.Summary,
            detectedHooks = analysis.DetectedHooks,
            likelyAudience = analysis.LikelyAudience,
            narrativeStructure = analysis.NarrativeStructure,
            strengths = analysis.Strengths,
            weaknesses = analysis.Weaknesses,
            derivedIdeas = analysis.DerivedIdeas.Select(idea => new
            {
                idea.Title,
                idea.Concept,
                idea.Rationale,
                score = new
                {
                    idea.Score.Novelty,
                    idea.Score.ClientFit,
                    idea.Score.PlatformFit,
                    idea.Score.HookQuality,
                    idea.Score.Feasibility,
                    idea.Score.ExpectedEngagement
                }
            }),
            primaryIdea = analysis.DerivedIdeas.FirstOrDefault() is { } primary
                ? new
                {
                    primary.Title,
                    primary.Concept,
                    primary.Rationale,
                    score = new
                    {
                        primary.Score.Novelty,
                        primary.Score.ClientFit,
                        primary.Score.PlatformFit,
                        primary.Score.HookQuality,
                        primary.Score.Feasibility,
                        primary.Score.ExpectedEngagement
                    }
                }
                : null,
            clientAdaptationRecommendations = analysis.ClientAdaptationRecommendations,
            confidence = analysis.Confidence,
            warnings = analysis.Warnings,
            patterns = analysis.Patterns is null
                ? null
                : new
                {
                    communication = analysis.Patterns.CommunicationPatterns.Select(p => p.Name),
                    emotional = analysis.Patterns.EmotionalPatterns.Select(p => p.Name),
                    conversion = analysis.Patterns.ConversionPatterns.Select(p => p.Name)
                },
            userCorrection = string.IsNullOrWhiteSpace(regenerationObjection)
                ? null
                : regenerationObjection.Trim()
        };
    }

    public static object FromBrand(BrandSnapshot snapshot)
    {
        return new
        {
            client = snapshot.Client is null
                ? null
                : new
                {
                    snapshot.Client.Name,
                    snapshot.Client.Description,
                    snapshot.Client.SectorOrNiche
                },
            brandManual = new
            {
                colorPalette = snapshot.Brand?.ColorPalette.Select(wc => new
                {
                    hex  = wc.Hex,
                    role = wc.Role.HasValue ? (object?)wc.Role.Value.ToString() : null,
                    rank = wc.Rank
                }),
                logos = snapshot.Brand?.Logos,
                backgroundTemplates = snapshot.Brand?.BackgroundTemplates.Select(background => new
                {
                    background.Name,
                    background.Url,
                    background.UseCase
                }),
                typography = snapshot.Brand?.Typography,
                voiceAndTone = snapshot.Brand?.VoiceAndTone,
                notes = snapshot.Brand?.Notes
            },
            strategy = snapshot.Strategy is null
                ? null
                : new
                {
                    contentPillars = snapshot.Strategy.ContentPillars,
                    targetAudience = snapshot.Strategy.TargetAudience,
                    postingFrequency = snapshot.Strategy.PostingFrequency
                },
            monthlyObjective = snapshot.MonthlyObjective is null
                ? null
                : new
                {
                    objectives = snapshot.MonthlyObjective.Objectives,
                    kpis = snapshot.MonthlyObjective.Kpis
                },
            referentAccounts = snapshot.ReferentAccounts.Select(ToPromptAccount),
            competitorAccounts = snapshot.CompetitorAccounts.Select(ToPromptAccount),
            inferredFields = snapshot.InferredFields,
            // Per-period tactical context (null when not available — AI must not assume)
            contentStrategy = snapshot.ContentStrategy is null
                ? (object?)null
                : new
                {
                    strategyText = snapshot.ContentStrategy.StrategyText,
                    source = snapshot.ContentStrategy.Source,
                    status = snapshot.ContentStrategy.Status
                },
            calendar = snapshot.Calendar is null
                ? (object?)null
                : new
                {
                    slots = snapshot.Calendar.Slots.Select(s => new
                    {
                        day = s.Day,
                        contentFormat = s.ContentFormat,
                        sortOrder = s.SortOrder
                    })
                },
            period = snapshot.Period is null
                ? (object?)null
                : new
                {
                    year = snapshot.Period.Year,
                    month = snapshot.Period.Month,
                    periodKind = snapshot.Period.PeriodKind
                }
        };
    }

    private static object ToPromptAccount(ClientAccountSnapshot account)
    {
        return new
        {
            networkCode = account.NetworkCode,
            networkName = account.NetworkName,
            profileUrl = account.ProfileUrl,
            handle = account.Handle,
            label = account.Label,
            notes = account.Notes
        };
    }
}
