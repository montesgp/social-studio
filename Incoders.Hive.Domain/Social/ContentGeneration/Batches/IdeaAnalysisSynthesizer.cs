using Incoders.Hive.Domain.Social.SingleAnalyzer;

namespace Incoders.Hive.Domain.Social.ContentGeneration.Batches;

/// <summary>
/// Projects a content idea into the analysis shape the generation pipeline already consumes.
///
/// WHY THIS EXISTS: every piece is written from a <see cref="SingleAnalyzerAnalysis"/>, and the
/// processor fails a job outright when one is missing. A Magic Generator batch starts from an idea
/// rather than from a scraped reference, so instead of teaching a 2500-line, heavily-tested pipeline a
/// second notion of context, the idea is expressed in the vocabulary that pipeline already speaks.
///
/// WHAT THIS IS NOT: a real analysis. Nothing here was observed from actual content, so the result
/// carries low confidence and an explicit warning. Dressing it up as a genuine analysis would let a
/// guess outrank real evidence wherever confidence is compared, and would leave an operator unable to
/// tell later which pieces came from a reference and which from an idea.
/// </summary>
public static class IdeaAnalysisSynthesizer
{
    /// <summary>
    /// Deliberately low. This context was never observed from real content, and callers that weigh
    /// confidence must be able to tell the difference.
    /// </summary>
    public const double SynthesizedConfidence = 0.3;

    /// <summary>Neutral mid-scale score: an unproven idea has neither evidence for nor against it.</summary>
    private const int NeutralScore = 3;

    public static SingleAnalyzerAnalysis Synthesize(string topic, string? detail, string? reference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);

        var title = topic.Trim();
        var concept = string.IsNullOrWhiteSpace(detail) ? title : detail.Trim();

        var provenance = string.IsNullOrWhiteSpace(reference)
            ? "Contexto sintetizado a partir de una idea, sin contenido de referencia analizado."
            : $"Contexto sintetizado a partir de una idea inspirada en {reference.Trim()}.";

        var idea = new SingleAnalyzerIdea(
            Title: title,
            Concept: concept,
            Rationale: provenance,
            Score: new SingleAnalyzerScore(
                Novelty: NeutralScore,
                ClientFit: NeutralScore,
                PlatformFit: NeutralScore,
                HookQuality: NeutralScore,
                Feasibility: NeutralScore,
                ExpectedEngagement: NeutralScore));

        // Built through the domain factory rather than the constructor, so the synthesized shape is
        // held to exactly the same validation as a real analysis. Every list field below must be
        // non-empty to pass; the values state what is actually known rather than inventing
        // observations about content nobody looked at.
        var result = SingleAnalyzerAnalysis.Create(
            summary: $"{title}. {provenance}",
            detectedHooks: [title],
            likelyAudience: "Audiencia objetivo del cliente según su perfil y estrategia.",
            narrativeStructure: "A definir por el generador a partir del formato solicitado.",
            strengths: ["Tema alineado al contexto del cliente."],
            weaknesses: ["Sin métricas de rendimiento observadas para este tema."],
            derivedIdeas: [idea],
            clientAdaptationRecommendations: ["Adaptar el tema a la voz y los objetivos del cliente."],
            confidence: SynthesizedConfidence,
            warnings: [provenance]);

        // A failure here is a programming error in this factory, never bad input: the topic is the
        // only caller-supplied value and it is validated above.
        return result.IsSuccess
            ? result.Value
            : throw new InvalidOperationException(
                $"Synthesized idea analysis was rejected by the domain: {result.Error.Code}");
    }
}
