using Incoders.Hive.Domain.Social.ContentGeneration;
using Incoders.Hive.Domain.Social.SingleAnalyzer;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;

internal static class GenerationPromptContext
{
    public const string BrandConstraint =
        "Use the client context as hard constraints. Respect the brand manual, palette, typography, voice, " +
        "strategy, objectives, KPIs, referent accounts, and competitor context. Do not invent colors, fonts, " +
        "tone, audience, goals, or market positioning when explicit client data exists.";

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
                colorPalette = snapshot.Brand?.ColorPalette,
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
                    toneKeywords = snapshot.Strategy.ToneKeywords,
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
            inferredFields = snapshot.InferredFields
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
