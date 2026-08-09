using Incoders.Hive.Domain.Social.ContentGeneration.Batches;

namespace Incoders.Hive.Tests.Domain.Social.ContentGeneration.Batches;

/// <summary>
/// Tests for turning an idea into the analysis shape the generation pipeline already understands.
///
/// The pipeline loads a SingleAnalyzerAnalysis as the context every piece is written from, and fails
/// the job outright without one. A Magic Generator batch starts from an IDEA, not from a scraped
/// reference — so the idea is projected into that same shape and the whole proven pipeline runs
/// untouched.
///
/// The synthesis must stay HONEST: it is not a real analysis of real content and must never claim the
/// confidence of one.
/// </summary>
public sealed class IdeaAnalysisSynthesizerTests
{
    [Fact]
    public void Synthesize_PutsTheTopicInTheSummary()
    {
        var analysis = IdeaAnalysisSynthesizer.Synthesize("Cómo migrar de Excel a un CRM", null, null);

        Assert.Contains("Cómo migrar de Excel a un CRM", analysis.Summary);
    }

    [Fact]
    public void Synthesize_ProducesAValidAnalysis()
    {
        var analysis = IdeaAnalysisSynthesizer.Synthesize("Un tema", "Contexto largo", "@referente");

        // The pipeline deserializes and uses this directly; an invalid shape would fail every piece.
        Assert.NotNull(analysis);
    }

    [Fact]
    public void Synthesize_CarriesTheIdeaAsTheDerivedIdea()
    {
        var analysis = IdeaAnalysisSynthesizer.Synthesize("Un tema", "El detalle", null);

        var idea = Assert.Single(analysis.DerivedIdeas);
        Assert.Equal("Un tema", idea.Title);
        Assert.Contains("El detalle", idea.Concept);
    }

    [Fact]
    public void Synthesize_FallsBackToTheTitleWhenThereIsNoSummary()
    {
        var analysis = IdeaAnalysisSynthesizer.Synthesize("Un tema", null, null);

        Assert.NotNull(analysis);
        Assert.Contains("Un tema", analysis.DerivedIdeas[0].Concept);
    }

    [Fact]
    public void Synthesize_ReportsLowConfidence()
    {
        var analysis = IdeaAnalysisSynthesizer.Synthesize("Un tema", null, null);

        // This never analysed a real piece of content. Claiming high confidence would let a synthesized
        // context outrank a genuine analysis anywhere confidence is compared.
        Assert.True(analysis.Confidence < 0.5);
    }

    [Fact]
    public void Synthesize_WarnsThatItIsIdeaDerived()
    {
        var analysis = IdeaAnalysisSynthesizer.Synthesize("Un tema", null, null);

        // Provenance must survive into the record: an operator reading the piece later has to be able
        // to tell it came from an idea rather than from a scraped reference.
        Assert.Contains(analysis.Warnings, w => w.Contains("idea", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Synthesize_KeepsTheReferenceWhenTheIdeaHasOne()
    {
        var analysis = IdeaAnalysisSynthesizer.Synthesize("Un tema", null, "@referente");

        Assert.Contains(analysis.Warnings.Concat([analysis.Summary]), text => text.Contains("@referente"));
    }

    [Fact]
    public void Synthesize_TrimsTheTitle()
    {
        var analysis = IdeaAnalysisSynthesizer.Synthesize("   Un tema   ", null, null);

        Assert.Equal("Un tema", analysis.DerivedIdeas[0].Title);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Synthesize_RejectsABlankTopic(string? title)
    {
        // A piece with no topic has nothing to be about; failing loudly beats generating filler.
        Assert.ThrowsAny<ArgumentException>(() => IdeaAnalysisSynthesizer.Synthesize(title!, null, null));
    }
}
